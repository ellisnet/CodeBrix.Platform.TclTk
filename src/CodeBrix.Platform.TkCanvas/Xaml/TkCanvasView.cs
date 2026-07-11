using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>canvas</c>. The canvas ITEMS (lines, rectangles, text,
/// images, ...) are a dynamic scene graph — create them in code through
/// <see cref="CanvasWidget"/> (typed API or the Tcl-shaped
/// <c>Execute(...)</c> layer), typically via a view-model bridge.
/// </summary>
public sealed class TkCanvasView : TkElement
{
    /// <summary>The canvas width in pixels (<c>-width</c>; negative = default).</summary>
    public int PixelWidth
    {
        get { return (int)GetValue(PixelWidthProperty); }
        set { SetValue(PixelWidthProperty, value); }
    }

    /// <summary>Identifies the <see cref="PixelWidth"/> property.</summary>
    public static readonly DependencyProperty PixelWidthProperty =
            RegisterOption(nameof(PixelWidth), "-width", typeof(TkCanvasView), typeof(int), -1);

    /// <summary>The canvas height in pixels (<c>-height</c>; negative = default).</summary>
    public int PixelHeight
    {
        get { return (int)GetValue(PixelHeightProperty); }
        set { SetValue(PixelHeightProperty, value); }
    }

    /// <summary>Identifies the <see cref="PixelHeight"/> property.</summary>
    public static readonly DependencyProperty PixelHeightProperty =
            RegisterOption(nameof(PixelHeight), "-height", typeof(TkCanvasView), typeof(int), -1);

    /// <summary>The scrollable region (<c>-scrollregion</c>: "x1 y1 x2 y2").</summary>
    public string ScrollRegion
    {
        get { return (string)GetValue(ScrollRegionProperty); }
        set { SetValue(ScrollRegionProperty, value); }
    }

    /// <summary>Identifies the <see cref="ScrollRegion"/> property.</summary>
    public static readonly DependencyProperty ScrollRegionProperty =
            RegisterOption(nameof(ScrollRegion), "-scrollregion", typeof(TkCanvasView));

    /// <summary>The materialized canvas widget, or null before the host loads.</summary>
    public CanvasWidget CanvasWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        CanvasWidget = new CanvasWidget(window);
        return CanvasWidget;
    }
}
