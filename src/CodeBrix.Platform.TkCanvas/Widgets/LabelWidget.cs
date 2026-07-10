using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>label</c> widget: a possibly multi-line text display
/// with a background, an optional 3D border, and internal padding. Its
/// natural size is the measured text (through the toolkit font seam) plus
/// <c>-padx</c>/<c>-pady</c> and the border/highlight inset, or an explicit
/// <c>-width</c> (characters) / <c>-height</c> (lines). The text is placed by
/// <c>-anchor</c> and aligned per <c>-justify</c>, greyed when
/// <c>-state disabled</c>. (Text metrics are font-stack dependent, so the
/// natural size is not byte-compatible with a specific X server's Tk; the
/// formula — text + padding + inset — is.)
/// </summary>
public sealed class LabelWidget : WidgetBase
{
    /// <summary>Creates a label on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public LabelWidget(TkWindow window)
        : base(window, "Label")
    {
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Label"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 1; }
    }

    /// <summary>The resolved font (<c>-font</c>, or TkDefaultFont).</summary>
    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkDefaultFont");
        }
    }

    private int PadX
    {
        get { int v; return TclString.TryParsePixels(Options.Get("-padx", "1"), out v) ? v : 1; }
    }

    private int PadY
    {
        get { int v; return TclString.TryParsePixels(Options.Get("-pady", "1"), out v) ? v : 1; }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int textWidth;
        int textHeight;
        WidgetText.MeasureBlock(Fonts, font, Options.Get("-text", ""), out textWidth, out textHeight);

        int contentWidth = textWidth;
        int contentHeight = textHeight;

        int chars = Options.GetInt("-width", 0);
        if (chars > 0)
        {
            int charWidth = Fonts.Measure(font, "0");
            if (charWidth < 1) { charWidth = 1; }
            contentWidth = chars * charWidth;
        }
        int lines = Options.GetInt("-height", 0);
        if (lines > 0)
        {
            contentHeight = lines * Fonts.Metrics(font).LineSpace;
        }

        int inset = Inset;
        int reqW = contentWidth + 2 * PadX + 2 * inset;
        int reqH = contentHeight + 2 * PadY + 2 * inset;
        Window.SetRequestedSize(reqW > 1 ? reqW : 1, reqH > 1 ? reqH : 1);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        string text = Options.Get("-text", "");
        if (text.Length == 0) { return; }

        int inset = Inset;
        int padX = PadX;
        int padY = PadY;
        var content = new SKRect(
                inset + padX, inset + padY,
                Window.Width - inset - padX, Window.Height - inset - padY);

        SKColor color = ForegroundColor();
        CanvasAnchor anchor = CanvasAnchorMath.Parse(Options.Get("-anchor", "center"));
        string justify = Options.Get("-justify", "center");
        WidgetText.DrawBlock(canvas, Fonts, Font, text, content, anchor, justify, color);
    }

    private SKColor ForegroundColor()
    {
        string spec;
        if (Options.Get("-state", "normal") == "disabled")
        {
            spec = Options.Get("-disabledforeground", "#a3a3a3");
        }
        else
        {
            spec = Options.Get("-foreground", Options.Get("-fg", "black"));
        }
        SKColor color;
        return TkColor.TryParse(spec, out color) ? color : SKColors.Black;
    }
}
