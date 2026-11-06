namespace BgRemover.Core;

public sealed class RefineOptions
{
    /// <summary>Refinement can be disabled entirely (raw model mask, upsampled).</summary>
    // Off by default: measured worse than the raw upsampled mask (see eval/README).
    public bool Enabled { get; set; } = false;

    /// <summary>Guided-filter window radius = longest working side / RadiusDivisor.</summary>
    public int RadiusDivisor { get; set; } = 256;

    public float Eps { get; set; } = 1e-4f;

    /// <summary>0 = keep the model mask, 1 = fully use the guided-filter result.</summary>
    public float Strength { get; set; } = 1f;

    public static RefineOptions Default { get; } = new();
}

public static class Matting
{
    /// <summary>Longest side used for edge refinement; the result is upsampled to full resolution.</summary>
    public const int WorkLongSide = 2048;

    /// <summary>
    /// Runs the model and refines the coarse mask against the image with a guided filter,
    /// producing a soft alpha matte at the original resolution.
    /// </summary>
    public static byte[] ComputeAlpha(Bitmap32 img, IMatteModel model, bool refine = true, CancellationToken ct = default)
        => ComputeAlpha(img, model, new RefineOptions { Enabled = refine }, ct);

    public static byte[] ComputeAlpha(Bitmap32 img, IMatteModel model, RefineOptions options, CancellationToken ct = default)
    {
        var mask = model.Predict(img, out int mw, out int mh);
        ct.ThrowIfCancellationRequested();
        return Refine(img, mask, mw, mh, options, ct);
    }

    public static byte[] Refine(Bitmap32 img, float[] mask, int mw, int mh, RefineOptions options, CancellationToken ct = default)
    {
        int w = img.Width, h = img.Height;
        double s = Math.Min(1.0, (double)WorkLongSide / Math.Max(w, h));
        int ww = Math.Max(1, (int)Math.Round(w * s));
        int wh = Math.Max(1, (int)Math.Round(h * s));

        var p = Resample.Resize(mask, mw, mh, 1, ww, wh);
        if (options.Enabled && options.Strength > 0f)
        {
            var rgb = Resample.ResizeBgraToRgb(img, ww, wh);
            var guide = new float[ww * wh];
            for (int i = 0; i < guide.Length; i++)
                guide[i] = (0.299f * rgb[i * 3] + 0.587f * rgb[i * 3 + 1] + 0.114f * rgb[i * 3 + 2]) / 255f;
            ct.ThrowIfCancellationRequested();
            int r = Math.Max(2, Math.Max(ww, wh) / options.RadiusDivisor);
            var q = GuidedFilter.Filter(guide, p, ww, wh, r, options.Eps);
            float k = Math.Clamp(options.Strength, 0f, 1f);
            for (int i = 0; i < p.Length; i++) p[i] = p[i] + (Math.Clamp(q[i], 0f, 1f) - p[i]) * k;
        }
        ct.ThrowIfCancellationRequested();

        var full = (ww == w && wh == h) ? p : Resample.Resize(p, ww, wh, 1, w, h);
        var alpha = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            int o = y * w;
            for (int x = 0; x < w; x++)
                alpha[o + x] = (byte)Math.Clamp((int)MathF.Round(full[o + x] * 255f), 0, 255);
        });
        return alpha;
    }
}

