namespace BgRemover.Core;

/// <summary>Straight (non-premultiplied) BGRA, 8 bits per channel, top-down.</summary>
public sealed class Bitmap32
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public Bitmap32(int width, int height, byte[]? data = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Image dimensions must be positive.");
        long n = (long)width * height * 4;
        if (n > int.MaxValue) throw new ArgumentException("Image is too large.");
        Width = width;
        Height = height;
        Bgra = data ?? new byte[n];
        if (Bgra.Length != n) throw new ArgumentException("Pixel buffer has the wrong length.");
    }

    /// <summary>Premultiplied copy, as expected by WPF's Pbgra32.</summary>
    public byte[] ToPremultiplied()
    {
        var src = Bgra;
        var dst = new byte[src.Length];
        int w = Width;
        Parallel.For(0, Height, y =>
        {
            int i = y * w * 4;
            for (int x = 0; x < w; x++, i += 4)
            {
                int a = src[i + 3];
                if (a == 255)
                {
                    dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2]; dst[i + 3] = 255;
                }
                else if (a != 0)
                {
                    dst[i] = (byte)((src[i] * a + 127) / 255);
                    dst[i + 1] = (byte)((src[i + 1] * a + 127) / 255);
                    dst[i + 2] = (byte)((src[i + 2] * a + 127) / 255);
                    dst[i + 3] = (byte)a;
                }
            }
        });
        return dst;
    }

    /// <summary>Opaque grayscale visualisation of an 8-bit mask.</summary>
    public static byte[] MaskToBgra(byte[] mask)
    {
        var dst = new byte[mask.Length * 4];
        for (int i = 0; i < mask.Length; i++)
        {
            byte v = mask[i];
            int o = i * 4;
            dst[o] = v; dst[o + 1] = v; dst[o + 2] = v; dst[o + 3] = 255;
        }
        return dst;
    }
}
