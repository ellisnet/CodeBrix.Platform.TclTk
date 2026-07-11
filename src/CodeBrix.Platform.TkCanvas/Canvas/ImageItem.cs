using CodeBrix.Platform.TkCanvas.Images;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>image</c> item (Tk 8.6.16 tkCanvImage.c): a photo image
/// positioned by <c>-anchor</c> at a single point. The <c>-image</c> option
/// names a photo in the tree's <see cref="ImageManager"/>; while the name
/// does not resolve (or no image was set) the item occupies its anchor point
/// and paints nothing — the deferral discipline, never throwing.
/// </summary>
public sealed class ImageItem : AnchoredCanvasItem
{
    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "image"; }
    }

    private PhotoImage Photo
    {
        get
        {
            string name = Options.Get("-image", "");
            if (name.Length == 0 || Canvas == null) { return null; }
            ImageManager images = Canvas.Window.Tree.ImagesIfCreated;
            return (images != null) ? images.Find(name) : null;
        }
    }

    private protected override int ContentWidth
    {
        get
        {
            PhotoImage photo = Photo;
            return (photo != null) ? photo.Width : 0;
        }
    }

    private protected override int ContentHeight
    {
        get
        {
            PhotoImage photo = Photo;
            return (photo != null) ? photo.Height : 0;
        }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }

        PhotoImage photo = Photo;
        if (photo == null) { return; }
        photo.Draw(canvas, (float)X1, (float)Y1);
    }
}
