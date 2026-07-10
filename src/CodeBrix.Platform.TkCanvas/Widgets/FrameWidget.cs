using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>frame</c> widget: a rectangular container with a
/// background and an optional 3D border. It holds no content of its own —
/// children are arranged inside it by <c>pack</c>/<c>grid</c> — so its size
/// comes from its content (or from an explicit <c>-width</c>/<c>-height</c>)
/// and its paint is just the background and border through the shared relief
/// primitive.
/// </summary>
public sealed class FrameWidget : WidgetBase
{
    /// <summary>Creates a frame on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public FrameWidget(TkWindow window)
        : base(window, "Frame")
    {
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Frame"; }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        Window.SetInternalBorder(Inset);

        int width;
        int height;
        bool hasWidth = TclString.TryParsePixels(Options.Get("-width", "0"), out width) && width > 0;
        bool hasHeight = TclString.TryParsePixels(Options.Get("-height", "0"), out height) && height > 0;
        if (hasWidth || hasHeight)
        {
            Window.SetRequestedSize(
                    hasWidth ? width : Window.RequestedWidth,
                    hasHeight ? height : Window.RequestedHeight);
        }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);
    }
}
