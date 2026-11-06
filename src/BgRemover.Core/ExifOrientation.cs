namespace BgRemover.Core;

public static class ExifOrientation
{
    /// <summary>Applies an EXIF orientation value (1-8) so the returned image is displayed upright.</summary>
    public static Bitmap32 Apply(Bitmap32 src, int orientation)
    {
        if (orientation < 2 || orientation > 8) return src;

        int w = src.Width, h = src.Height;
        bool swap = orientation >= 5;
        int dw = swap ? h : w;
        int dh = swap ? w : h;
        var dst = new Bitmap32(dw, dh);
        var s = src.Bgra;
        var d = dst.Bgra;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int dx, dy;
                switch (orientation)
                {
                    case 2: dx = w - 1 - x; dy = y; break;
                    case 3: dx = w - 1 - x; dy = h - 1 - y; break;
                    case 4: dx = x; dy = h - 1 - y; break;
                    case 5: dx = y; dy = x; break;
                    case 6: dx = h - 1 - y; dy = x; break;
                    case 7: dx = h - 1 - y; dy = w - 1 - x; break;
                    default: dx = y; dy = w - 1 - x; break;
                }
                int si = (y * w + x) * 4;
                int di = (dy * dw + dx) * 4;
                d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = s[si + 3];
            }
        });
        return dst;
    }
}
