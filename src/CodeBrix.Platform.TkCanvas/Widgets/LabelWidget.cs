using System.Collections.Generic;

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
        int contentWidth;
        int contentHeight;

        Images.PhotoImage image = ResolveImage();
        if (image != null)
        {
            // An image replaces the text, and -width/-height (when given)
            // are pixel sizes rather than character/line counts — Tk's rule.
            contentWidth = image.Width;
            contentHeight = image.Height;
            int pixels;
            if (TclString.TryParsePixels(Options.Get("-width", ""), out pixels) && pixels > 0)
            {
                contentWidth = pixels;
            }
            if (TclString.TryParsePixels(Options.Get("-height", ""), out pixels) && pixels > 0)
            {
                contentHeight = pixels;
            }
        }
        else
        {
            TkFont font = Font;
            WidgetText.MeasureBlock(Fonts, font, Options.Get("-text", ""),
                    out contentWidth, out contentHeight);

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

        int inset = Inset;
        int padX = PadX;
        int padY = PadY;
        var content = new SKRect(
                inset + padX, inset + padY,
                Window.Width - inset - padX, Window.Height - inset - padY);
        CanvasAnchor anchor = CanvasAnchorMath.Parse(Options.Get("-anchor", "center"));

        Images.PhotoImage image = ResolveImage();
        if (image != null)
        {
            float left, top;
            PlaceAnchored(anchor, content, image.Width, image.Height, out left, out top);
            image.Draw(canvas, left, top);
            return;
        }

        string text = Options.Get("-text", "");
        if (text.Length == 0) { return; }

        SKColor color = ForegroundColor();
        string justify = Options.Get("-justify", "center");
        WidgetText.DrawBlock(canvas, Fonts, Font, text, content, anchor, justify, color);
    }

    private SKColor ForegroundColor()
    {
        string spec;
        if (Options.Get("-state", "normal") == "disabled")
        {
            spec = ResolveOption("-disabledforeground", Theme.DisabledForeground);
        }
        else if (Options.IsSet("-fg") && !Options.IsSet("-foreground"))
        {
            spec = Options.Get("-fg");
        }
        else
        {
            spec = ResolveOption("-foreground", Theme.Foreground);
        }
        SKColor color;
        return TkColor.TryParse(spec, out color) ? color : SKColors.Black;
    }

    /// <inheritdoc/>
    private protected override IReadOnlyCollection<string> StyleStates
    {
        get
        {
            return Options.Get("-state", "normal") == "disabled" ? new[] { "disabled" } : null;
        }
    }
}
