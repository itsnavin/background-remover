using System.Diagnostics;
using System.Text;
using BgRemover.Core;

// Evaluation harness: composites RGBA cut-outs (alpha = reference matte) over synthetic backgrounds,
// runs the real model, and reports agreement with the reference alpha.
//
// usage: BgRemover.Eval --model <onnx> --images <dir> [--out <dir>] [--cpu]

string Arg(string name, string? def = null)
{
    int i = Array.IndexOf(args, name);
    if (i >= 0 && i + 1 < args.Length) return args[i + 1];
    return def ?? throw new ArgumentException("missing " + name);
}

string modelPath = Arg("--model");
string imageDir = Arg("--images");
string outDir = Arg("--out", Path.Combine(AppContext.BaseDirectory, "eval-out"));
bool gpu = !args.Contains("--cpu");
Directory.CreateDirectory(outDir);

var files = Directory.GetFiles(imageDir, "*.png").OrderBy(f => f).ToArray();
if (files.Length == 0) { Console.Error.WriteLine("no images"); return 2; }

using var model = new OnnxMatteModel(modelPath, gpu);
Console.WriteLine("Execution provider: " + model.Description);

var variants = new List<(string Name, RefineOptions Opt, bool Flip)>
{
    ("raw-mask", new RefineOptions { Enabled = false }, false),
};
foreach (int div in new[] { 512, 256, 96 })
    foreach (float eps in new[] { 1e-4f, 1e-3f, 1e-2f, 5e-2f })
        variants.Add(($"guided r=1/{div} eps={eps}", new RefineOptions { Enabled = true, RadiusDivisor = div, Eps = eps }, false));
variants.Add(("raw-mask+flip", new RefineOptions { Enabled = false }, true));

var rows = new List<Row>();
foreach (var file in files)
{
    var rgba = ImageIO.Load(file);
    int w = rgba.Width, h = rgba.Height;
    var gt = new byte[w * h];
    for (int i = 0; i < gt.Length; i++) gt[i] = rgba.Bgra[i * 4 + 3];
    var unknown = UnknownBand(gt, w, h);

    foreach (var bgName in new[] { "flat-gray", "texture", "camouflage" })
    {
        var comp = Composite(rgba, gt, bgName);
        foreach (var flip in new[] { false, true })
        {
            model.FlipAveraging = flip;
            var sw = Stopwatch.StartNew();
            var mask = model.Predict(comp, out int mw, out int mh);
            double predSec = sw.Elapsed.TotalSeconds;
            foreach (var v in variants.Where(v => v.Flip == flip))
            {
                sw.Restart();
                var alpha = Matting.Refine(comp, mask, mw, mh, v.Opt);
                rows.Add(Score(Path.GetFileName(file), bgName, v.Name, alpha, gt, unknown, predSec + sw.Elapsed.TotalSeconds));
                if (v.Name == "raw-mask" && bgName == "texture")
                    ImageIO.Save(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + "_cutout.png"),
                        Compositor.Compose(comp, alpha, null, new BackgroundSpec()), ExportFormat.Png);
            }
        }
    }
}

var sb = new StringBuilder();
sb.AppendLine($"Execution provider: {model.Description}  ");
sb.AppendLine();
sb.AppendLine("Mean over all images and backgrounds (lower MAE / higher IoU is better):");
sb.AppendLine();
sb.AppendLine("| variant | MAE x1000 | boundary MAE x1000 | IoU@0.5 | mean sec/img |");
sb.AppendLine("|---|---|---|---|---|");
foreach (var g in rows.GroupBy(r => r.Variant))
    sb.AppendLine($"| {g.Key} | {g.Average(r => r.Mae) * 1000:F2} | {g.Average(r => r.BoundaryMae) * 1000:F1} | {g.Average(r => r.Iou):F4} | {g.Average(r => r.Seconds):F2} |");
sb.AppendLine();
sb.AppendLine("Per image and background (raw-mask):");
sb.AppendLine();
sb.AppendLine("| image | background | MAE x1000 | boundary MAE x1000 | IoU@0.5 |");
sb.AppendLine("|---|---|---|---|---|");
foreach (var r in rows.Where(r => r.Variant == "raw-mask"))
    sb.AppendLine($"| {r.Image} | {r.Background} | {r.Mae * 1000:F2} | {r.BoundaryMae * 1000:F1} | {r.Iou:F4} |");

File.WriteAllText(Path.Combine(outDir, "results.md"), sb.ToString());
Console.WriteLine(sb.ToString());
return 0;

static Row Score(string image, string bg, string variant, byte[] alpha, byte[] gt, bool[] unknown, double sec)
{
    double abs = 0, bAbs = 0; long bN = 0, inter = 0, uni = 0;
    for (int i = 0; i < gt.Length; i++)
    {
        double d = Math.Abs(alpha[i] - gt[i]) / 255.0;
        abs += d;
        if (unknown[i]) { bAbs += d; bN++; }
        bool p = alpha[i] >= 128, g = gt[i] >= 128;
        if (p && g) inter++;
        if (p || g) uni++;
    }
    return new Row(image, bg, variant, abs / gt.Length, bN > 0 ? bAbs / bN : 0, uni > 0 ? (double)inter / uni : 1, sec);
}

// Pixels within ~7px of a non-binary / edge region of the reference matte.
static bool[] UnknownBand(byte[] gt, int w, int h)
{
    var f = new float[gt.Length];
    for (int i = 0; i < f.Length; i++) f[i] = gt[i] / 255f;
    var m = BoxFilter.Mean(f, w, h, 7);
    var res = new bool[f.Length];
    for (int i = 0; i < f.Length; i++) res[i] = m[i] > 0.01f && m[i] < 0.99f;
    return res;
}

static Bitmap32 Composite(Bitmap32 fg, byte[] alpha, string kind)
{
    int w = fg.Width, h = fg.Height;
    var rng = new Random(1234);
    float[] Noise(int cells)
    {
        int cw = Math.Max(2, w / cells), ch = Math.Max(2, h / cells);
        var n = new float[cw * ch * 3];
        for (int i = 0; i < n.Length; i++) n[i] = (float)rng.NextDouble();
        return Resample.Resize(n, cw, ch, 3, w, h);
    }

    double mr = 0, mg = 0, mb = 0, ms = 0;
    for (int i = 0; i < alpha.Length; i++)
    {
        double a = alpha[i] / 255.0;
        mb += fg.Bgra[i * 4] * a; mg += fg.Bgra[i * 4 + 1] * a; mr += fg.Bgra[i * 4 + 2] * a; ms += a;
    }
    if (ms > 0) { mr /= ms; mg /= ms; mb /= ms; }

    float[]? n1 = null, n2 = null;
    if (kind != "flat-gray") { n1 = Noise(48); n2 = Noise(10); }

    var res = new Bitmap32(w, h);
    for (int i = 0; i < alpha.Length; i++)
    {
        double r, g, b;
        switch (kind)
        {
            case "flat-gray": r = g = b = 128; break;
            case "texture":
                r = 255 * (0.6 * n1![i * 3] + 0.4 * n2![i * 3]);
                g = 255 * (0.6 * n1[i * 3 + 1] + 0.4 * n2[i * 3 + 1]);
                b = 255 * (0.6 * n1[i * 3 + 2] + 0.4 * n2[i * 3 + 2]);
                break;
            default: // colours close to the object's own mean colour
                r = mr + 70 * (n1![i * 3] - 0.5) + 40 * (n2![i * 3] - 0.5);
                g = mg + 70 * (n1[i * 3 + 1] - 0.5) + 40 * (n2[i * 3 + 1] - 0.5);
                b = mb + 70 * (n1[i * 3 + 2] - 0.5) + 40 * (n2[i * 3 + 2] - 0.5);
                break;
        }
        double a = alpha[i] / 255.0;
        res.Bgra[i * 4] = (byte)Math.Clamp(fg.Bgra[i * 4] * a + b * (1 - a), 0, 255);
        res.Bgra[i * 4 + 1] = (byte)Math.Clamp(fg.Bgra[i * 4 + 1] * a + g * (1 - a), 0, 255);
        res.Bgra[i * 4 + 2] = (byte)Math.Clamp(fg.Bgra[i * 4 + 2] * a + r * (1 - a), 0, 255);
        res.Bgra[i * 4 + 3] = 255;
    }
    return res;
}

internal record Row(string Image, string Background, string Variant, double Mae, double BoundaryMae, double Iou, double Seconds);
