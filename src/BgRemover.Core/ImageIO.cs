using System.Runtime.InteropServices;
using SkiaSharp;

namespace BgRemover.Core;

public enum ExportFormat
{
    Png,
    WebpLossless,
    WebpLossy,
    Jpeg,
}

public static class ImageIO
{
    /// <summary>Refuse to decode anything larger than this to avoid running out of memory.</summary>
    public const long MaxPixels = 100_000_000;

    public static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Jpeg => ".jpg",
        _ => ".webp",
    };

    public static Bitmap32 Load(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Unsupported or corrupt image: " + path);
        var size = codec.Info;
        if ((long)size.Width * size.Height > MaxPixels)
            throw new InvalidDataException($"Image is too large ({size.Width}x{size.Height}); the limit is {MaxPixels / 1_000_000} megapixels.");

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidDataException("Could not decode image: " + result);

        var bytes = new byte[info.Width * info.Height * 4];
        Marshal.Copy(bmp.GetPixels(), bytes, 0, bytes.Length);
        var image = new Bitmap32(info.Width, info.Height, bytes);
        return ExifOrientation.Apply(image, (int)codec.EncodedOrigin);
    }

    public static void Save(string path, Bitmap32 image, ExportFormat format)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(image.Bgra, 0, bmp.GetPixels(), image.Bgra.Length);

        SKData? data;
        if (format == ExportFormat.WebpLossless || format == ExportFormat.WebpLossy)
        {
            using var pixmap = bmp.PeekPixels();
            var opts = format == ExportFormat.WebpLossless
                ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100)
                : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, 92);
            data = pixmap.Encode(opts);
        }
        else
        {
            using var img = SKImage.FromBitmap(bmp);
            data = format == ExportFormat.Jpeg
                ? img.Encode(SKEncodedImageFormat.Jpeg, 95)
                : img.Encode(SKEncodedImageFormat.Png, 100);
        }

        if (data == null) throw new IOException("Encoding failed.");
        using (data)
        using (var fs = File.Create(path))
        {
            data.SaveTo(fs);
        }
    }
}
