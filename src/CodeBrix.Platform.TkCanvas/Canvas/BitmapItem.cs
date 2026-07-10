using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>bitmap</c> item (Tk 8.6.16 tkCanvBmap.c): a two-colour
/// bitmap positioned by <c>-anchor</c> at a single point, with
/// <c>-foreground</c>/<c>-background</c>. The bitmap-resource subsystem is not
/// yet wired (that arrives with the photo/image system in a later sub-phase),
/// so no bitmap has a size and the item currently occupies its anchor point
/// and paints nothing — accept-and-store per the deferral discipline, never
/// throwing. Options and geometry are otherwise complete.
/// </summary>
public sealed class BitmapItem : AnchoredCanvasItem
{
    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "bitmap"; }
    }

    // No bitmap-resource backing yet → no content size.
    private protected override int ContentWidth
    {
        get { return 0; }
    }

    private protected override int ContentHeight
    {
        get { return 0; }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        // Deferred: bitmap rendering waits on the bitmap-resource subsystem.
    }
}
