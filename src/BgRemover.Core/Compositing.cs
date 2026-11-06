namespace BgRemover.Core;

public enum BackgroundKind
{
    Transparent,
    Solid,
    Image,
}

public sealed class BackgroundSpec
{
    public BackgroundKind Kind { get; set; } = BackgroundKind.Transparent;
    public byte R { get; set; } = 255;
    public byte G { get; set; } = 255;
    public byte B { get; set; } = 255;
    public Bitmap32? Image { get; set; }

    private Bitmap32? _prepared;
    private int _pw, _ph;
    private Bitmap32? _preparedSource;

    public BackgroundSpec Clone() => (BackgroundSpec)MemberwiseClone();

    /// <summary>Background image scaled to cover w x h and centre-cropped.</summary>
    internal Bitmap32 Prepare(int w, int h)
    {
        var img = Image ?? throw new InvalidOperationException("No background image set.");
        lock (this)
        {
            if (_prepared != null && _pw == w && _ph == h && ReferenceEquals(_preparedSource, img)) return _prepared;
            double s = Math.Max((double)w / img.Width, (double)h / img.Height);
            int sw = Math.Max(w, (int)Math.Ceiling(img.Width * s));
            int sh = Math.Max(h, (int)Math.Ceiling(img.Height * s));
            var scaled = Resample.ResizeBgra(img, sw, sh);
            var res = new Bitmap32(w, h);
            int ox = (sw - w) / 2, oy = (sh - h) / 2;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(scaled.Bgra, ((y + oy) * sw + ox) * 4, res.Bgra, y * w * 4, w * 4);
            _prepared = res; _pw = w; _ph = h; _preparedSource = img;
            return res;
        }
    }
}

public sealed class EditSettings
{
    /// <summary>Gaussian-like softening of the matte edge, in pixels.</summary>
    public float Feather { get; set; }

    /// <summary>-0.5 (grow) .. +0.5 (shrink) matte edge shift.</summary>
    public float EdgeShift { get; set; }

    /// <summary>Replace edge colours that bled in from the old background.</summary>
    public bool Decontaminate { get; set; } = true;

    public BackgroundSpec Background { get; set; } = new();

    public EditSettings Clone() => new()
    {
        Feather = Feather,
        EdgeShift = EdgeShift,
        Decontaminate = Decontaminate,
        Background = Background.Clone(),
    };
}

public static class AlphaOps
{
    /// <summary>
    /// Applies manual edits (128 = untouched, towards 255 = keep, towards 0 = remove),
    /// then edge shift and feather.
    /// </summary>
    public static byte[] Finalize(byte[] baseAlpha, byte[]? edits, int w, int h, float shift, float feather)
    {
        int n = w * h;
        var f = new float[n];
        shift = Math.Clamp(shift, -0.5f, 0.5f);
        float lo = Math.Max(0f, shift);
        float hi = 1f + Math.Min(0f, shift);
        float inv = 1f / Math.Max(hi - lo, 1e-3f);

        Parallel.For(0, h, y =>
        {
            int o = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = o + x;
                float a = baseAlpha[i] / 255f;
                if (edits != null)
                {
                    int e = edits[i];
                    if (e > 128) a += (1f - a) * ((e - 128) / 127f);
                    else if (e < 128) a *= 1f - (128 - e) / 128f;
                }
                if (shift != 0f) a = Math.Clamp((a - lo) * inv, 0f, 1f);
                f[i] = a;
            }
        });

        int r = (int)MathF.Round(feather);
        if (r >= 1)
        {
            for (int pass = 0; pass < 3; pass++) f = BoxFilter.Mean(f, w, h, r);
        }

        var res = new byte[n];
        Parallel.For(0, h, y =>
        {
            int o = y * w;
            for (int x = 0; x < w; x++)
                res[o + x] = (byte)Math.Clamp((int)MathF.Round(f[o + x] * 255f), 0, 255);
        });
        return res;
    }
}

/// <summary>Low-resolution estimate of the true foreground colour, used to remove background colour spill at edges.</summary>
public sealed class ForegroundEstimate
{
    public int Width { get; }
    public int Height { get; }
    public float[] Rgb { get; }

    private ForegroundEstimate(int w, int h, float[] rgb) { Width = w; Height = h; Rgb = rgb; }

    public static ForegroundEstimate Estimate(Bitmap32 src, byte[] alpha)
    {
        int w = src.Width, h = src.Height;
        double s = Math.Min(1.0, 1536.0 / Math.Max(w, h));
        int fw = Math.Max(1, (int)Math.Round(w * s));
        int fh = Math.Max(1, (int)Math.Round(h * s));

        var rgb = Resample.ResizeBgraToRgb(src, fw, fh);
        var a = Resample.ByteMaskToFloat(alpha, w, h, fw, fh);
        int n = fw * fh;

        var weight = new float[n];
        var c0 = new float[n]; var c1 = new float[n]; var c2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            float wt = Math.Clamp((a[i] / 255f - 0.8f) / 0.2f, 0f, 1f);
            weight[i] = wt;
            c0[i] = rgb[i * 3] * wt;
            c1[i] = rgb[i * 3 + 1] * wt;
            c2[i] = rgb[i * 3 + 2] * wt;
        }

        int r = Math.Max(2, Math.Max(fw, fh) / 100);
        var den = BoxFilter.Mean(weight, fw, fh, r);
        var n0 = BoxFilter.Mean(c0, fw, fh, r);
        var n1 = BoxFilter.Mean(c1, fw, fh, r);
        var n2 = BoxFilter.Mean(c2, fw, fh, r);

        var res = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            if (den[i] > 0.02f)
            {
                res[i * 3] = n0[i] / den[i];
                res[i * 3 + 1] = n1[i] / den[i];
                res[i * 3 + 2] = n2[i] / den[i];
            }
            else
            {
                res[i * 3] = rgb[i * 3]; res[i * 3 + 1] = rgb[i * 3 + 1]; res[i * 3 + 2] = rgb[i * 3 + 2];
            }
        }
        return new ForegroundEstimate(fw, fh, res);
    }

    internal void Sample(double fx, double fy, out float r, out float g, out float b)
    {
        double x = fx * Width - 0.5, y = fy * Height - 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        float tx = (float)(x - x0), ty = (float)(y - y0);
        int xa = Math.Clamp(x0, 0, Width - 1), xb = Math.Clamp(x0 + 1, 0, Width - 1);
        int ya = Math.Clamp(y0, 0, Height - 1), yb = Math.Clamp(y0 + 1, 0, Height - 1);
        r = Lerp(0, xa, xb, ya, yb, tx, ty);
        g = Lerp(1, xa, xb, ya, yb, tx, ty);
        b = Lerp(2, xa, xb, ya, yb, tx, ty);
    }

    private float Lerp(int c, int xa, int xb, int ya, int yb, float tx, float ty)
    {
        float p00 = Rgb[(ya * Width + xa) * 3 + c], p10 = Rgb[(ya * Width + xb) * 3 + c];
        float p01 = Rgb[(yb * Width + xa) * 3 + c], p11 = Rgb[(yb * Width + xb) * 3 + c];
        float top = p00 + (p10 - p00) * tx, bot = p01 + (p11 - p01) * tx;
        return top + (bot - top) * ty;
    }
}

public static class Compositor
{
    public static Bitmap32 Compose(Bitmap32 src, byte[] alpha, ForegroundEstimate? fg, BackgroundSpec bg)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap32(w, h);
        var s = src.Bgra;
        var d = dst.Bgra;
        Bitmap32? bgImg = bg.Kind == BackgroundKind.Image && bg.Image != null ? bg.Prepare(w, h) : null;
        bool transparent = bg.Kind == BackgroundKind.Transparent;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int o = i * 4;
                int a = alpha[i];

                if (transparent && a == 0) continue;

                float cb = s[o], cg = s[o + 1], cr = s[o + 2];
                if (fg != null && a > 0 && a < 255)
                {
                    float t = Math.Min(1f, (1f - a / 255f) * 2f);
                    fg.Sample((x + 0.5) / w, (y + 0.5) / h, out float fr, out float fgg, out float fb);
                    cr += (fr - cr) * t; cg += (fgg - cg) * t; cb += (fb - cb) * t;
                }

                if (transparent)
                {
                    d[o] = Clamp(cb); d[o + 1] = Clamp(cg); d[o + 2] = Clamp(cr); d[o + 3] = (byte)a;
                    continue;
                }

                float bb, bgG, br;
                if (bgImg != null)
                {
                    bb = bgImg.Bgra[o]; bgG = bgImg.Bgra[o + 1]; br = bgImg.Bgra[o + 2];
                }
                else
                {
                    bb = bg.B; bgG = bg.G; br = bg.R;
                }
                float k = a / 255f;
                d[o] = Clamp(cb * k + bb * (1f - k));
                d[o + 1] = Clamp(cg * k + bgG * (1f - k));
                d[o + 2] = Clamp(cr * k + br * (1f - k));
                d[o + 3] = 255;
            }
        });
        return dst;
    }

    private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
}
