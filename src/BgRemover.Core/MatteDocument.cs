namespace BgRemover.Core;

public sealed class RenderOutput
{
    public RenderOutput(Bitmap32 image, byte[] alpha) { Image = image; Alpha = alpha; }
    public Bitmap32 Image { get; }
    public byte[] Alpha { get; }
}

/// <summary>An opened image, its model matte, and the user's manual keep/remove edits.</summary>
public sealed class MatteDocument
{
    private readonly LinkedList<byte[]> _undo = new();
    private readonly int _undoLimit;

    public Bitmap32 Source { get; }
    public byte[] BaseAlpha { get; }

    /// <summary>One byte per pixel: 128 untouched, 255 keep, 0 remove (soft values in between).</summary>
    public byte[] Edits { get; }

    public MatteDocument(Bitmap32 source, byte[] baseAlpha)
    {
        if (baseAlpha.Length != source.Width * source.Height) throw new ArgumentException("Alpha size mismatch.");
        Source = source;
        BaseAlpha = baseAlpha;
        Edits = new byte[baseAlpha.Length];
        Array.Fill(Edits, (byte)128);
        _undoLimit = (int)Math.Clamp(300_000_000L / Math.Max(1, baseAlpha.Length), 2, 20);
    }

    public bool HasEdits => Edits.Any(e => e != 128);

    public void BeginStroke()
    {
        _undo.AddLast((byte[])Edits.Clone());
        while (_undo.Count > _undoLimit) _undo.RemoveFirst();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var last = _undo.Last!.Value;
        _undo.RemoveLast();
        Buffer.BlockCopy(last, 0, Edits, 0, last.Length);
        return true;
    }

    /// <summary>Paints a soft round dab. Returns the affected pixel rectangle (x0, y0, x1, y1 inclusive).</summary>
    public (int x0, int y0, int x1, int y1) Stamp(double cx, double cy, double radius, double hardness, bool keep)
    {
        int w = Source.Width, h = Source.Height;
        radius = Math.Max(radius, 0.5);
        hardness = Math.Clamp(hardness, 0, 1);
        int x0 = Math.Max(0, (int)Math.Floor(cx - radius)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + radius));
        int y0 = Math.Max(0, (int)Math.Floor(cy - radius)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + radius));
        float target = keep ? 255f : 0f;
        double inner = radius * hardness;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > radius) continue;
                double s = d <= inner ? 1.0 : 1.0 - (d - inner) / Math.Max(radius - inner, 1e-6);
                s = s * s * (3 - 2 * s);
                int i = y * w + x;
                float cur = Edits[i];
                Edits[i] = (byte)Math.Clamp((int)MathF.Round(cur + (target - cur) * (float)s), 0, 255);
            }
        }
        return (x0, y0, x1, y1);
    }

    public byte[] ComputeAlpha(EditSettings s)
        => AlphaOps.Finalize(BaseAlpha, Edits, Source.Width, Source.Height, s.EdgeShift, s.Feather);

    public RenderOutput Render(EditSettings s)
    {
        var alpha = ComputeAlpha(s);
        var fg = s.Decontaminate ? ForegroundEstimate.Estimate(Source, alpha) : null;
        return new RenderOutput(Compositor.Compose(Source, alpha, fg, s.Background), alpha);
    }
}
