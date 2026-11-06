using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BgRemover.Core;

public interface IMatteModel : IDisposable
{
    /// <summary>Returns a foreground-probability map (0..1) of size width*height.</summary>
    float[] Predict(Bitmap32 image, out int width, out int height);

    string Description { get; }
}

/// <summary>ISNet / DIS style salient-object segmentation model run through ONNX Runtime.</summary>
public sealed class OnnxMatteModel : IMatteModel
{
    public const int InputSize = 1024;

    private readonly string _path;
    private InferenceSession _session;
    private string _inputName;
    private bool _gpu;

    /// <summary>Average the prediction with a horizontally flipped pass (about 2x slower, slightly more stable).</summary>
    public bool FlipAveraging { get; set; }

    public string Description => _gpu ? "DirectML (GPU)" : "CPU";

    public OnnxMatteModel(string modelPath, bool preferGpu)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file not found: " + modelPath, modelPath);
        _path = modelPath;
        (_session, _gpu) = CreateSession(preferGpu);
        _inputName = _session.InputMetadata.Keys.First();
    }

    private (InferenceSession, bool) CreateSession(bool gpu)
    {
        if (gpu)
        {
            try
            {
                var o = new SessionOptions();
                o.EnableMemoryPattern = false;
                o.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                o.AppendExecutionProvider_DML(0);
                return (new InferenceSession(_path, o), true);
            }
            catch
            {
                // fall through to CPU
            }
        }
        var cpu = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return (new InferenceSession(_path, cpu), false);
    }

    public float[] Predict(Bitmap32 image, out int width, out int height)
    {
        width = InputSize;
        height = InputSize;
        var rgb = Resample.ResizeBgraToRgb(image, InputSize, InputSize);
        var input = Preprocess(rgb, false);

        float[] pred;
        try
        {
            pred = Run(input);
            if (FlipAveraging)
            {
                var flipped = Run(Preprocess(rgb, true));
                for (int y = 0; y < InputSize; y++)
                    for (int x = 0; x < InputSize; x++)
                        pred[y * InputSize + x] = 0.5f * (pred[y * InputSize + x] + flipped[y * InputSize + (InputSize - 1 - x)]);
            }
        }
        catch when (_gpu)
        {
            // GPU runtime failure: retry on CPU for the rest of the session.
            _session.Dispose();
            (_session, _gpu) = CreateSession(false);
            _inputName = _session.InputMetadata.Keys.First();
            return Predict(image, out width, out height);
        }
        return pred;
    }

    // Matches the reference preprocessing: scale by max value, subtract 0.5, NCHW RGB.
    private static float[] Preprocess(float[] rgb, bool flip)
    {
        int n = InputSize * InputSize;
        float max = 1e-6f;
        for (int i = 0; i < rgb.Length; i++) if (rgb[i] > max) max = rgb[i];
        var t = new float[3 * n];
        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                int sx = flip ? InputSize - 1 - x : x;
                int si = (y * InputSize + sx) * 3;
                int di = y * InputSize + x;
                t[di] = rgb[si] / max - 0.5f;
                t[n + di] = rgb[si + 1] / max - 0.5f;
                t[2 * n + di] = rgb[si + 2] / max - 0.5f;
            }
        }
        return t;
    }

    private float[] Run(float[] input)
    {
        var tensor = new DenseTensor<float>(input, new[] { 1, 3, InputSize, InputSize });
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var output = results.First().AsTensor<float>().ToArray();
        if (output.Length != InputSize * InputSize)
            throw new InvalidOperationException($"Unexpected model output size {output.Length}.");

        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in output) { if (v < min) min = v; if (v > max) max = v; }
        float range = Math.Max(max - min, 1e-6f);
        for (int i = 0; i < output.Length; i++) output[i] = (output[i] - min) / range;
        return output;
    }

    public void Dispose() => _session.Dispose();
}
