using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>image</c> item (Tk 8.6.16 tkCanvImage.c): a photo image
/// positioned by <c>-anchor</c> at a single point. The item type is
/// registered and its <c>-image</c>/<c>-anchor</c> options are accepted and
/// stored now; actual painting and content sizing wait on the photo-image
/// system (the <c>CodeBrix.Imaging</c>-backed sub-phase). Until an image is
/// resolvable the item occupies its anchor point and paints nothing — the
/// deferral discipline, never throwing.
/// </summary>
public sealed class ImageItem : AnchoredCanvasItem
{
    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "image"; }
    }

    // No photo-image backing yet → no content size (arrives with the photo
    // system).
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
        // Deferred: image rendering waits on the photo-image system.
    }
}
