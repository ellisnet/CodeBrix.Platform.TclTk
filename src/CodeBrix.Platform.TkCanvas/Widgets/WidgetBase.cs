using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The shared base of the classic (Skia-drawn) widgets: it wires the widget
/// to its <see cref="TkWindow"/>, owns the <see cref="WidgetOptions"/> bag,
/// funnels <c>configure</c> through <see cref="OnConfigured"/> + a
/// re-<see cref="Measure"/> + a repaint, exposes the toolkit font seam, and
/// provides the one Tk 3D-border/background painting helper every widget
/// shares (through <see cref="ReliefPainter"/>). Concrete widgets supply
/// their class name, their default border/relief, their natural size, and
/// their content painting.
/// </summary>
public abstract class WidgetBase : IWidget
{
    /// <summary>Wires the widget onto its window and sets the class bind tag.</summary>
    /// <param name="window">The window the widget owns.</param>
    /// <param name="className">The Tk class name (also the class bind tag).</param>
    private protected WidgetBase(TkWindow window, string className)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        Window = window;
        window.ClassName = className;
        window.Widget = this;
    }

    /// <inheritdoc/>
    public TkWindow Window { get; }

    /// <inheritdoc/>
    public abstract string ClassName { get; }

    /// <inheritdoc/>
    public WidgetOptions Options { get; } = new WidgetOptions();

    /// <summary>The toolkit font-measurement seam (the plan's R2).</summary>
    private protected FontManager Fonts
    {
        get { return Window.Tree.Fonts; }
    }

    /// <summary>The default <c>-borderwidth</c> when unset (widget-specific).</summary>
    private protected virtual int DefaultBorderWidth
    {
        get { return 0; }
    }

    /// <summary>The default <c>-relief</c> when unset (widget-specific).</summary>
    private protected virtual string DefaultRelief
    {
        get { return "flat"; }
    }

    /// <summary>The default <c>-highlightthickness</c> when unset (widget-specific).</summary>
    private protected virtual int DefaultHighlightThickness
    {
        get { return 0; }
    }

    /// <summary>The default <c>-background</c> when unset — Tk's classic gray.</summary>
    private protected virtual string DefaultBackground
    {
        get { return "#d9d9d9"; }
    }

    /// <summary>The configured border width (<c>-borderwidth</c>/<c>-bd</c>).</summary>
    private protected int BorderWidth
    {
        get
        {
            int value;
            if (TclString.TryParsePixels(Options.Get("-borderwidth", ""), out value)) { return value; }
            if (TclString.TryParsePixels(Options.Get("-bd", ""), out value)) { return value; }
            return DefaultBorderWidth;
        }
    }

    /// <summary>The configured highlight-ring thickness (<c>-highlightthickness</c>).</summary>
    private protected int HighlightThickness
    {
        get
        {
            int value;
            return TclString.TryParsePixels(Options.Get("-highlightthickness", ""), out value)
                    ? value : DefaultHighlightThickness;
        }
    }

    /// <summary>The total inset each edge reserves: border + highlight ring.</summary>
    private protected int Inset
    {
        get { return BorderWidth + HighlightThickness; }
    }

    /// <summary>The configured relief style (<c>-relief</c>).</summary>
    private protected Relief Relief
    {
        get { return ReliefPainter.Parse(Options.Get("-relief", DefaultRelief)); }
    }

    /// <summary>The configured background color, or the widget default.</summary>
    private protected SKColor BackgroundColor
    {
        get
        {
            SKColor color;
            string spec = Options.Get("-background", Options.Get("-bg", DefaultBackground));
            return TkColor.TryParse(spec, out color) ? color : new SKColor(0xD9, 0xD9, 0xD9);
        }
    }

    /// <inheritdoc/>
    public virtual bool HitTest(SKPoint point)
    {
        return true;
    }

    /// <inheritdoc/>
    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options != null)
        {
            foreach (KeyValuePair<string, string> option in options)
            {
                Options.Set(option.Key, option.Value);
            }
        }
        OnConfigured();
        Measure();
        if (!Window.IsDestroyed)
        {
            Window.Tree.Scheduler.ScheduleRepaint();
        }
    }

    /// <summary>Interprets the known options after a configure stored them (optional override).</summary>
    private protected virtual void OnConfigured()
    {
    }

    /// <inheritdoc/>
    public abstract void Measure();

    /// <inheritdoc/>
    public abstract void Paint(SKCanvas canvas);

    /// <summary>
    /// Fills the widget's background over its whole window rectangle and draws
    /// its Tk 3D border (border + highlight ring) — the shared frame every
    /// widget starts its paint with. The canvas is already translated so
    /// (0,0) is the window's top-left corner.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="background">The background fill to use (often <see cref="BackgroundColor"/>).</param>
    private protected void PaintBackgroundAndBorder(SKCanvas canvas, SKColor background)
    {
        var rect = new SKRect(0, 0, Window.Width, Window.Height);
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(rect, paint);
        }

        int highlight = HighlightThickness;
        if (highlight > 0)
        {
            SKColor ring;
            string spec = Options.Get("-highlightbackground", DefaultBackground);
            if (!TkColor.TryParse(spec, out ring)) { ring = background; }
            DrawHighlightRing(canvas, rect, highlight, ring);
        }

        var borderRect = new SKRect(highlight, highlight,
                Window.Width - highlight, Window.Height - highlight);
        ReliefPainter.DrawBorder(canvas, borderRect, BorderWidth, Relief, background);
    }

    private static void DrawHighlightRing(SKCanvas canvas, SKRect rect, int width, SKColor color)
    {
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = color;
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + width), paint);
            canvas.DrawRect(new SKRect(rect.Left, rect.Bottom - width, rect.Right, rect.Bottom), paint);
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + width, rect.Bottom), paint);
            canvas.DrawRect(new SKRect(rect.Right - width, rect.Top, rect.Right, rect.Bottom), paint);
        }
    }
}
