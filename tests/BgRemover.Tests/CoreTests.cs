using BgRemover.Core;
using Xunit;

namespace BgRemover.Tests;

public class CoreTests
{
    private static Bitmap32 Solid(int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        var bmp = new Bitmap32(w, h);
        for (int i = 0; i < w * h; i++)
        {
            bmp.Bgra[i * 4] = b; bmp.Bgra[i * 4 + 1] = g; bmp.Bgra[i * 4 + 2] = r; bmp.Bgra[i * 4 + 3] = a;
        }
        return bmp;
    }

    [Fact]
    public void ExifOrientation6_RotatesClockwise()
    {
        // 3x2 image, pixel (x,y) carries value x + 10*y in the blue channel.
        var src = new Bitmap32(3, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++) { src.Bgra[(y * 3 + x) * 4] = (byte)(x + 10 * y); src.Bgra[(y * 3 + x) * 4 + 3] = 255; }

        var r = ExifOrientation.Apply(src, 6);
        Assert.Equal(2, r.Width);
        Assert.Equal(3, r.Height);
        // Top-left of source ends up at top-right after a 90 degree clockwise rotation.
        Assert.Equal(0, r.Bgra[(0 * 2 + 1) * 4]);
        // Bottom-left of source (0,1) ends up at bottom... top-left.
        Assert.Equal(10, r.Bgra[(0 * 2 + 0) * 4]);
        // Top-right of source (2,0) ends up bottom-right.
        Assert.Equal(2, r.Bgra[(2 * 2 + 1) * 4]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ExifOrientation_PreservesPixelMultiset(int o)
    {
        var src = new Bitmap32(5, 3);
        for (int i = 0; i < 15; i++) { src.Bgra[i * 4] = (byte)i; src.Bgra[i * 4 + 3] = 255; }
        var r = ExifOrientation.Apply(src, o);
        var vals = Enumerable.Range(0, 15).Select(i => r.Bgra[i * 4]).OrderBy(v => v).ToArray();
        Assert.Equal(Enumerable.Range(0, 15).Select(i => (byte)i).ToArray(), vals);
    }

    [Fact]
    public void Resample_AreaAverageOfConstantAndGradient()
    {
        var src = new float[4 * 4];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) src[y * 4 + x] = x;
        var r = Resample.Resize(src, 4, 4, 1, 2, 2);
        Assert.Equal(0.5f, r[0], 4);
        Assert.Equal(2.5f, r[1], 4);
        var c = Resample.Resize(Enumerable.Repeat(0.7f, 9).ToArray(), 3, 3, 1, 7, 5);
        Assert.All(c, v => Assert.Equal(0.7f, v, 4));
    }

    [Fact]
    public void BoxFilter_ConstantStaysConstant_AndImpulseSpreads()
    {
        var c = BoxFilter.Mean(Enumerable.Repeat(2f, 100).ToArray(), 10, 10, 3);
        Assert.All(c, v => Assert.Equal(2f, v, 4));

        var imp = new float[11 * 11];
        imp[5 * 11 + 5] = 9f;
        var m = BoxFilter.Mean(imp, 11, 11, 1);
        Assert.Equal(1f, m[5 * 11 + 5], 4);
        Assert.Equal(1f, m[4 * 11 + 4], 4);
        Assert.Equal(0f, m[2 * 11 + 2], 4);
    }

    [Fact]
    public void GuidedFilter_PreservesConstantMaskAndRange()
    {
        const int w = 32, h = 32;
        var rnd = new Random(1);
        var guide = Enumerable.Range(0, w * h).Select(_ => (float)rnd.NextDouble()).ToArray();
        var q = GuidedFilter.Filter(guide, Enumerable.Repeat(0.7f, w * h).ToArray(), w, h, 4, 1e-3f);
        Assert.All(q, v => Assert.Equal(0.7f, v, 3));
    }

    [Fact]
    public void AlphaOps_EditsOverrideAndShiftErodes()
    {
        const int w = 4, h = 1;
        var alpha = new byte[] { 0, 100, 200, 255 };
        var edits = new byte[] { 255, 128, 128, 0 };
        var r = AlphaOps.Finalize(alpha, edits, w, h, 0f, 0f);
        Assert.Equal(255, r[0]);
        Assert.Equal(100, r[1]);
        Assert.Equal(0, r[3]);

        var eroded = AlphaOps.Finalize(alpha, null, w, h, 0.3f, 0f);
        Assert.Equal(34, eroded[1]);         // (100/255 - 0.3) / 0.7 ~ 0.13
        Assert.Equal(255, eroded[3]);
        Assert.Equal(0, eroded[0]);
    }

    [Fact]
    public void Compositor_SolidBackgroundBlendsByAlpha()
    {
        var src = Solid(2, 2, 0, 0, 255);              // red
        var alpha = new byte[] { 255, 0, 128, 128 };
        var bg = new BackgroundSpec { Kind = BackgroundKind.Solid, R = 0, G = 0, B = 255 };
        var r = Compositor.Compose(src, alpha, null, bg);
        Assert.Equal((0, 0, 255, 255), (r.Bgra[0], r.Bgra[1], r.Bgra[2], r.Bgra[3]));
        Assert.Equal((255, 0, 0, 255), (r.Bgra[4], r.Bgra[5], r.Bgra[6], r.Bgra[7]));
        Assert.InRange(r.Bgra[8], 126, 129);           // blue
        Assert.InRange(r.Bgra[10], 126, 129);          // red
    }

    [Fact]
    public void Compositor_TransparentKeepsAlpha()
    {
        var src = Solid(1, 2, 10, 20, 30);
        var r = Compositor.Compose(src, new byte[] { 0, 77 }, null, new BackgroundSpec());
        Assert.Equal(0, r.Bgra[3]);
        Assert.Equal(77, r.Bgra[7]);
        Assert.Equal(10, r.Bgra[4]);
    }

    [Fact]
    public void Decontamination_ReplacesBackgroundColourAtEdge()
    {
        // Red object on the left, green background on the right, one blended column.
        const int w = 40, h = 8;
        var src = new Bitmap32(w, h);
        var alpha = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte r, g, a;
                if (x < 20) { r = 255; g = 0; a = 255; }
                else if (x == 20) { r = 128; g = 128; a = 128; }
                else { r = 0; g = 255; a = 0; }
                src.Bgra[i * 4] = 0; src.Bgra[i * 4 + 1] = g; src.Bgra[i * 4 + 2] = r; src.Bgra[i * 4 + 3] = 255;
                alpha[i] = a;
            }
        var fg = ForegroundEstimate.Estimate(src, alpha);
        var plain = Compositor.Compose(src, alpha, null, new BackgroundSpec());
        var clean = Compositor.Compose(src, alpha, fg, new BackgroundSpec());
        int idx = (4 * w + 20) * 4;
        Assert.True(clean.Bgra[idx + 1] < plain.Bgra[idx + 1], "green spill should be reduced");
        Assert.True(clean.Bgra[idx + 2] > plain.Bgra[idx + 2], "red should increase");
    }

    [Fact]
    public void MatteDocument_StampKeepAndUndo()
    {
        var doc = new MatteDocument(Solid(20, 20, 1, 2, 3), new byte[400]);
        doc.BeginStroke();
        doc.Stamp(10, 10, 4, 1.0, true);
        var a = doc.ComputeAlpha(new EditSettings());
        Assert.Equal(255, a[10 * 20 + 10]);
        Assert.Equal(0, a[0]);
        Assert.True(doc.Undo());
        Assert.All(doc.ComputeAlpha(new EditSettings()), v => Assert.Equal(0, v));
    }

    [Fact]
    public void PngRoundTrip_PreservesAlphaAndOpaqueColour()
    {
        var img = new Bitmap32(3, 1);
        byte[] px = { 10, 20, 30, 255, 40, 50, 60, 128, 0, 0, 0, 0 };
        Array.Copy(px, img.Bgra, 12);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            ImageIO.Save(path, img, ExportFormat.Png);
            var back = ImageIO.Load(path);
            Assert.Equal(3, back.Width);
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, back.Bgra[..4]);
            Assert.Equal(128, back.Bgra[7]);
            Assert.Equal(0, back.Bgra[11]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WebpLossless_RoundTripKeepsAlpha()
    {
        var img = Solid(8, 8, 200, 100, 50, 255);
        for (int i = 0; i < 8; i++) img.Bgra[i * 4 + 3] = 0;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".webp");
        try
        {
            ImageIO.Save(path, img, ExportFormat.WebpLossless);
            var back = ImageIO.Load(path);
            Assert.Equal(8, back.Width);
            Assert.Equal(0, back.Bgra[3]);
            Assert.Equal(255, back.Bgra[8 * 4 + 3]);
            Assert.Equal(200, back.Bgra[8 * 4]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_RejectsGarbage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        File.WriteAllText(path, "not an image");
        try { Assert.ThrowsAny<Exception>(() => ImageIO.Load(path)); }
        finally { File.Delete(path); }
    }
}
