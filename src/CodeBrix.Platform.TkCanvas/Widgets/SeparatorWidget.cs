using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The Tk <c>ttk::separator</c> widget: a thin sunken divider line, drawn as
/// a 2-pixel 3D groove along its length. <c>-orient horizontal</c> (the
/// default) makes a 2-pixel-tall bar that fills the width it is given;
/// <c>-orient vertical</c> makes a 2-pixel-wide bar that fills its height.
/// </summary>
public sealed class SeparatorWidget : WidgetBase
{
    /// <summary>Creates a separator on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public SeparatorWidget(TkWindow window)
        : base(window, "TSeparator")
    {
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "TSeparator"; }
    }

    private bool IsVertical
    {
        get { return Options.Get("-orient", "horizontal") == "vertical"; }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        // The bar is 2px on its thin axis; the long axis is filled by pack/grid.
        if (IsVertical) { Window.SetRequestedSize(2, 1); }
        else { Window.SetRequestedSize(1, 2); }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        SKColor background = BackgroundColor;
        SKColor dark = ReliefPainter.DarkShadow(background);
        SKColor light = ReliefPainter.LightShadow(background);

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            if (IsVertical)
            {
                float x = Window.Width / 2f - 1;
                paint.Color = dark;
                canvas.DrawRect(new SKRect(x, 0, x + 1, Window.Height), paint);
                paint.Color = light;
                canvas.DrawRect(new SKRect(x + 1, 0, x + 2, Window.Height), paint);
            }
            else
            {
                float y = Window.Height / 2f - 1;
                paint.Color = dark;
                canvas.DrawRect(new SKRect(0, y, Window.Width, y + 1), paint);
                paint.Color = light;
                canvas.DrawRect(new SKRect(0, y + 1, Window.Width, y + 2), paint);
            }
        }
    }
}
