namespace BgRemover.Core;

public static class BoxFilter
{
    /// <summary>Mean over a (2r+1) square window, clamped at the borders.</summary>
    public static float[] Mean(float[] src, int w, int h, int r)
    {
        if (r <= 0) return (float[])src.Clone();
        var t = Pass(src, w, h, r);
        var tt = Transpose(t, w, h);
        var u = Pass(tt, h, w, r);
        return Transpose(u, h, w);
    }

    private static float[] Pass(float[] s, int w, int h, int r)
    {
        var d = new float[s.Length];
        Parallel.For(0, h, y =>
        {
            int o = y * w;
            var p = new double[w + 1];
            for (int x = 0; x < w; x++) p[x + 1] = p[x] + s[o + x];
            for (int x = 0; x < w; x++)
            {
                int lo = Math.Max(0, x - r), hi = Math.Min(w - 1, x + r);
                d[o + x] = (float)((p[hi + 1] - p[lo]) / (hi - lo + 1));
            }
        });
        return d;
    }

    private static float[] Transpose(float[] s, int w, int h)
    {
        var d = new float[s.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++) d[x * h + y] = s[y * w + x];
        });
        return d;
    }
}

/// <summary>Guided filter (He et al.) with a single-channel guide.</summary>
public static class GuidedFilter
{
    public static float[] Filter(float[] guide, float[] p, int w, int h, int r, float eps)
    {
        int n = w * h;
        var ip = new float[n];
        var ii = new float[n];
        for (int i = 0; i < n; i++) { ip[i] = guide[i] * p[i]; ii[i] = guide[i] * guide[i]; }

        var mI = BoxFilter.Mean(guide, w, h, r);
        var mP = BoxFilter.Mean(p, w, h, r);
        var mIP = BoxFilter.Mean(ip, w, h, r);
        var mII = BoxFilter.Mean(ii, w, h, r);

        var a = ip; // reuse buffers
        var b = ii;
        for (int i = 0; i < n; i++)
        {
            float varI = mII[i] - mI[i] * mI[i];
            float cov = mIP[i] - mI[i] * mP[i];
            float ai = cov / (varI + eps);
            a[i] = ai;
            b[i] = mP[i] - ai * mI[i];
        }

        var mA = BoxFilter.Mean(a, w, h, r);
        var mB = BoxFilter.Mean(b, w, h, r);
        var q = new float[n];
        for (int i = 0; i < n; i++) q[i] = mA[i] * guide[i] + mB[i];
        return q;
    }
}
