using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>labelframe</c> widget: a frame whose 3D border carries a
/// text label set into its top edge (<c>-text</c>, positioned by
/// <c>-labelanchor</c>). The border runs behind the vertical centre of the
/// label, with the background broken out around the text — the classic
/// grouped-controls look. Its top internal border reserves the label height
/// so packed/gridded content sits below the caption.
/// </summary>
public sealed class LabelframeWidget : WidgetBase
{
    private const int LabelIndent = 8;

    /// <summary>Creates a labelframe on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public LabelframeWidget(TkWindow window)
        : base(window, "Labelframe")
    {
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Labelframe"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 2; }
    }

    private protected override string DefaultRelief
    {
        get { return "groove"; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkDefaultFont");
        }
    }

    private int LabelHeight
    {
        get
        {
            return (Options.Get("-text", "").Length > 0) ? Fonts.Metrics(Font).LineSpace : 0;
        }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        int inset = Inset;
        int labelH = LabelHeight;
        Window.SetInternalBorders(inset, inset + labelH, inset, inset);

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
        SKColor background = BackgroundColor;
        int labelH = LabelHeight;
        int highlight = HighlightThickness;
        int borderTop = highlight + labelH / 2;

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(new SKRect(0, 0, Window.Width, Window.Height), paint);
        }

        var borderRect = new SKRect(highlight, borderTop,
                Window.Width - highlight, Window.Height - highlight);
        Rendering.ReliefPainter.DrawBorder(canvas, borderRect, BorderWidth, Relief, background);

        string text = Options.Get("-text", "");
        if (text.Length == 0) { return; }

        // Break the border out behind the label and draw the caption.
        TkFont font = Font;
        int textWidth = Fonts.Measure(font, text);
        FontMetrics metrics = Fonts.Metrics(font);
        float x = LabelX(textWidth);
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(new SKRect(x - 2, highlight, x + textWidth + 2, highlight + labelH), paint);
        }

        SKColor fg;
        if (!TkColor.TryParse(Options.Get("-foreground", Options.Get("-fg", "black")), out fg))
        {
            fg = SKColors.Black;
        }
        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            paint.Color = fg;
            paint.IsAntialias = true;
            canvas.DrawText(text, x, highlight + metrics.Ascent, SKTextAlign.Left, skFont, paint);
        }
    }

    private float LabelX(int textWidth)
    {
        int highlight = HighlightThickness;
        switch (Options.Get("-labelanchor", "nw"))
        {
            case "n": case "ne":
            case "en": case "es":
                return (Window.Width - textWidth) / 2f;
            default:
                return highlight + LabelIndent;
        }
    }
}
