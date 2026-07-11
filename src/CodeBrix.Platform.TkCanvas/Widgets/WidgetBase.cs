using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Theming;
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

        // The option database applies at widget CREATION, for options not
        // explicitly configured — adding entries later does not restyle
        // existing widgets (Tk's behavior, see §B.12b).
        OptionDatabase database = window.Tree.OptionDatabaseIfCreated;
        if (database != null && !database.IsEmpty)
        {
            database.ApplyTo(Options, window);
        }
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

    /// <summary>The tree's color scheme — every default color reads through it.</summary>
    private protected TkTheme Theme
    {
        get { return Window.Tree.Theme; }
    }

    /// <summary>
    /// The widget's ttk style class (<c>TButton</c>, <c>TLabel</c>, ...) used
    /// when no <c>-style</c> is configured. Classes with no ttk counterpart
    /// (Listbox, Menu, Canvas, Text) resolve under their own name.
    /// </summary>
    private protected virtual string TtkClassName
    {
        get
        {
            switch (ClassName)
            {
                case "Listbox":
                case "Treeview":
                case "Menu":
                case "Canvas":
                case "Text":
                case "Toplevel":
                    return ClassName;
                default:
                    // Classes already carrying a ttk-style name (TCombobox,
                    // TSeparator) resolve under themselves.
                    if (ClassName.Length > 1 && ClassName[0] == 'T' && char.IsUpper(ClassName[1]))
                    {
                        return ClassName;
                    }
                    return "T" + ClassName;
            }
        }
    }

    /// <summary>The style this widget resolves under (<c>-style</c> or the class style).</summary>
    private protected string StyleName
    {
        get
        {
            string style = Options.Get("-style", "");
            return (style.Length != 0) ? style : TtkClassName;
        }
    }

    /// <summary>
    /// The widget's current ttk state names for style-map matching
    /// (<c>active</c>, <c>pressed</c>, <c>disabled</c>, <c>focus</c>, ...) —
    /// null means the normal state.
    /// </summary>
    private protected virtual IReadOnlyCollection<string> StyleStates
    {
        get { return null; }
    }

    /// <summary>
    /// Resolves one option through the full styling stack: an explicitly
    /// configured value (which includes creation-time option-database values)
    /// wins; otherwise a <c>ttk::style</c> map entry matching the current
    /// states, then a style configure value, then the theme default.
    /// </summary>
    /// <param name="option">The option name (with its dash).</param>
    /// <param name="themeDefault">The theme's default for this option.</param>
    /// <returns>The resolved value.</returns>
    private protected string ResolveOption(string option, string themeDefault)
    {
        if (Options.IsSet(option)) { return Options.Get(option); }
        TtkStyleEngine styles = Window.Tree.StylesIfCreated;
        if (styles != null)
        {
            string value = styles.Lookup(StyleName, option, StyleStates);
            if (value != null) { return value; }
        }
        return themeDefault;
    }

    /// <summary>
    /// Resolves a color option through <see cref="ResolveOption"/> and parses
    /// it (unparseable text paints black, like <see cref="TkColor"/>).
    /// </summary>
    /// <param name="option">The option name (with its dash).</param>
    /// <param name="themeDefault">The theme's default for this option.</param>
    /// <returns>The resolved color.</returns>
    private protected SKColor ResolveColor(string option, string themeDefault)
    {
        SKColor color;
        TkColor.TryParse(ResolveOption(option, themeDefault), out color);
        return color;
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

    /// <summary>The default <c>-background</c> when unset — the theme's widget background.</summary>
    private protected virtual string DefaultBackground
    {
        get { return Theme.Background; }
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

    /// <summary>The configured background color, or the styled/theme default.</summary>
    private protected SKColor BackgroundColor
    {
        get
        {
            SKColor color;
            string spec = Options.IsSet("-bg") && !Options.IsSet("-background")
                    ? Options.Get("-bg")
                    : ResolveOption("-background", DefaultBackground);
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

    /// <summary>
    /// Resolves the widget's <c>-image</c> option against the tree's image
    /// registry. Null when no image is set or the name does not resolve —
    /// callers then fall back to their text content (accept-and-no-op).
    /// </summary>
    /// <returns>The photo image, or null.</returns>
    private protected Images.PhotoImage ResolveImage()
    {
        string name = Options.Get("-image", "");
        if (name.Length == 0) { return null; }
        Images.ImageManager images = Window.Tree.ImagesIfCreated;
        return (images != null) ? images.Find(name) : null;
    }

    /// <summary>
    /// Computes the top-left corner that places fixed-size content inside a
    /// rectangle per a Tk anchor (<c>nw</c> = top-left, <c>center</c> =
    /// centred, ...) — the widget-content analogue of the canvas anchor math.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="content">The rectangle to place into.</param>
    /// <param name="width">The content width.</param>
    /// <param name="height">The content height.</param>
    /// <param name="left">The computed left edge.</param>
    /// <param name="top">The computed top edge.</param>
    private protected static void PlaceAnchored(CanvasAnchor anchor, SKRect content,
            int width, int height, out float left, out float top)
    {
        switch (anchor)
        {
            case CanvasAnchor.NW:
            case CanvasAnchor.W:
            case CanvasAnchor.SW:
                left = content.Left;
                break;
            case CanvasAnchor.NE:
            case CanvasAnchor.E:
            case CanvasAnchor.SE:
                left = content.Right - width;
                break;
            default:
                left = content.Left + (content.Width - width) / 2f;
                break;
        }
        switch (anchor)
        {
            case CanvasAnchor.NW:
            case CanvasAnchor.N:
            case CanvasAnchor.NE:
                top = content.Top;
                break;
            case CanvasAnchor.SW:
            case CanvasAnchor.S:
            case CanvasAnchor.SE:
                top = content.Bottom - height;
                break;
            default:
                top = content.Top + (content.Height - height) / 2f;
                break;
        }
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
            string spec = Options.Get("-highlightbackground", Theme.HighlightBackground);
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
