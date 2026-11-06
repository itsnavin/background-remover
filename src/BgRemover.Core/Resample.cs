namespace BgRemover.Core;

/// <summary>Separable resampling: area averaging when shrinking, bilinear when enlarging.</summary>
public static class Resample
{
    private sealed class Axis
    {
        public int[] Start = Array.Empty<int>();
        public int[] Count = Array.Empty<int>();
        public int[] Off = Array.Empty<int>();
        public float[] W = Array.Empty<float>();
    }

    private interface ISampler
    {
        float Get(int pixel, int channel);
    }

    private readonly struct FloatSampler : ISampler
    {
        private readonly float[] _a;
        private readonly int _ch;
        public FloatSampler(float[] a, int ch) { _a = a; _ch = ch; }
        public float Get(int p, int c) => _a[p * _ch + c];
    }

    // BGRA bytes exposed as RGB channels.
    private readonly struct BgraRgbSampler : ISampler
    {
        private readonly byte[] _a;
        public BgraRgbSampler(byte[] a) { _a = a; }
        public float Get(int p, int c) => _a[p * 4 + 2 - c];
    }

    private readonly struct BgraSampler : ISampler
    {
        private readonly byte[] _a;
        public BgraSampler(byte[] a) { _a = a; }
        public float Get(int p, int c) => _a[p * 4 + c];
    }

    private readonly struct ByteSampler : ISampler
    {
        private readonly byte[] _a;
        public ByteSampler(byte[] a) { _a = a; }
        public float Get(int p, int c) => _a[p];
    }

    private static Axis Build(int sn, int dn)
    {
        var a = new Axis { Start = new int[dn], Count = new int[dn], Off = new int[dn] };
        var ws = new List<float>(dn * 2);
        double scale = (double)sn / dn;
        for (int d = 0; d < dn; d++)
        {
            a.Off[d] = ws.Count;
            if (scale > 1.0)
            {
                double lo = d * scale, hi = (d + 1) * scale;
                int s0 = Math.Clamp((int)Math.Floor(lo), 0, sn - 1);
                int s1 = Math.Clamp((int)Math.Ceiling(hi) - 1, s0, sn - 1);
                double tot = 0;
                var tmp = new List<double>();
                for (int s = s0; s <= s1; s++)
                {
                    double w = Math.Min(hi, s + 1) - Math.Max(lo, s);
                    if (w < 0) w = 0;
                    tmp.Add(w);
                    tot += w;
                }
                if (tot <= 0) { tmp.Clear(); tmp.Add(1); tot = 1; s1 = s0; }
                a.Start[d] = s0;
                a.Count[d] = tmp.Count;
                foreach (var w in tmp) ws.Add((float)(w / tot));
            }
            else
            {
                double c = (d + 0.5) * scale - 0.5;
                int s0 = (int)Math.Floor(c);
                double f = c - s0;
                int a0 = Math.Clamp(s0, 0, sn - 1);
                int a1 = Math.Clamp(s0 + 1, 0, sn - 1);
                a.Start[d] = a0;
                if (a0 == a1) { a.Count[d] = 1; ws.Add(1f); }
                else { a.Count[d] = 2; ws.Add((float)(1 - f)); ws.Add((float)f); }
            }
        }
        a.W = ws.ToArray();
        return a;
    }

    private static float[] Core<T>(T src, int sw, int sh, int ch, int dw, int dh) where T : struct, ISampler
    {
        var ax = Build(sw, dw);
        var ay = Build(sh, dh);
        var tmp = new float[checked(dw * sh * ch)];

        Parallel.For(0, sh, y =>
        {
            for (int x = 0; x < dw; x++)
            {
                int st = ax.Start[x], cnt = ax.Count[x], off = ax.Off[x];
                for (int c = 0; c < ch; c++)
                {
                    float sum = 0;
                    for (int k = 0; k < cnt; k++)
                        sum += ax.W[off + k] * src.Get(y * sw + st + k, c);
                    tmp[(y * dw + x) * ch + c] = sum;
                }
            }
        });

        var dst = new float[checked(dw * dh * ch)];
        Parallel.For(0, dh, y =>
        {
            int st = ay.Start[y], cnt = ay.Count[y], off = ay.Off[y];
            for (int x = 0; x < dw; x++)
            {
                for (int c = 0; c < ch; c++)
                {
                    float sum = 0;
                    for (int k = 0; k < cnt; k++)
                        sum += ay.W[off + k] * tmp[((st + k) * dw + x) * ch + c];
                    dst[(y * dw + x) * ch + c] = sum;
                }
            }
        });
        return dst;
    }

    public static float[] Resize(float[] src, int sw, int sh, int channels, int dw, int dh)
        => Core(new FloatSampler(src, channels), sw, sh, channels, dw, dh);

    /// <summary>Resizes a BGRA image to interleaved RGB floats in 0..255.</summary>
    public static float[] ResizeBgraToRgb(Bitmap32 src, int dw, int dh)
        => Core(new BgraRgbSampler(src.Bgra), src.Width, src.Height, 3, dw, dh);

    public static float[] ByteMaskToFloat(byte[] mask, int w, int h, int dw, int dh)
        => Core(new ByteSampler(mask), w, h, 1, dw, dh);

    public static Bitmap32 ResizeBgra(Bitmap32 src, int dw, int dh)
    {
        var f = Core(new BgraSampler(src.Bgra), src.Width, src.Height, 4, dw, dh);
        var b = new byte[f.Length];
        for (int i = 0; i < f.Length; i++) b[i] = (byte)Math.Clamp((int)MathF.Round(f[i]), 0, 255);
        return new Bitmap32(dw, dh, b);
    }
}
