using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>button</c> widget: a text label with a raised 3D border
/// that sinks while pressed, highlights while the pointer is over it
/// (<c>-activebackground</c>/<c>-activeforeground</c>), greys when
/// <c>-state disabled</c>, and fires its command on release. The Tcl-facing
/// <c>-command</c> is stored as text and surfaced through
/// <see cref="Invoked"/>, which the Phase-C command bridge evaluates; headless
/// callers can invoke it directly with <see cref="Invoke"/>.
/// </summary>
public sealed class ButtonWidget : WidgetBase
{
    private bool _pressed;
    private bool _active;

    /// <summary>Creates a button on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public ButtonWidget(TkWindow window)
        : base(window, "Button")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>Raised when the button is invoked (released over it while enabled, or <see cref="Invoke"/>).</summary>
    public event Action Invoked;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Button"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 2; }
    }

    private protected override string DefaultRelief
    {
        get { return "raised"; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    /// <summary>Whether the pointer is currently over the button (the active state).</summary>
    public bool IsActive
    {
        get { return _active; }
    }

    /// <summary>Whether the button is currently pressed.</summary>
    public bool IsPressed
    {
        get { return _pressed; }
    }

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
        get { int v; return TclString.TryParsePixels(Options.Get("-padx", "4"), out v) ? v : 4; }
    }

    private int PadY
    {
        get { int v; return TclString.TryParsePixels(Options.Get("-pady", "2"), out v) ? v : 2; }
    }

    private bool Disabled
    {
        get { return Options.Get("-state", "normal") == "disabled"; }
    }

    /// <summary>Invokes the button — fires <see cref="Invoked"/> unless disabled.</summary>
    public void Invoke()
    {
        if (Disabled) { return; }
        Action handler = Invoked;
        if (handler != null) { handler(); }
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
        SKColor background = BackgroundColor;
        if (_active && !Disabled)
        {
            SKColor active;
            if (TkColor.TryParse(ResolveOption("-activebackground", Theme.ActiveBackground), out active))
            {
                background = active;
            }
        }

        // The pressed button sinks; otherwise its configured (raised) relief.
        int highlight = HighlightThickness;
        var rect = new SKRect(0, 0, Window.Width, Window.Height);
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(rect, paint);
        }
        var borderRect = new SKRect(highlight, highlight,
                Window.Width - highlight, Window.Height - highlight);
        Relief relief = _pressed ? Rendering.Relief.Sunken : Relief;
        ReliefPainter.DrawBorder(canvas, borderRect, BorderWidth, relief, background);

        int inset = Inset;
        var content = new SKRect(
                inset + PadX, inset + PadY,
                Window.Width - inset - PadX, Window.Height - inset - PadY);
        // The pressed button nudges its label down-right by one pixel, like Tk.
        if (_pressed) { content.Offset(1, 1); }
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
        if (Disabled)
        {
            spec = ResolveOption("-disabledforeground", Theme.DisabledForeground);
        }
        else if (_active)
        {
            spec = ResolveOption("-activeforeground",
                    Options.IsSet("-fg") ? Options.Get("-fg") : ResolveOption("-foreground", Theme.ActiveForeground));
        }
        else if (Options.IsSet("-fg") && !Options.IsSet("-foreground"))
        {
            spec = Options.Get("-fg");
        }
        else
        {
            spec = ResolveOption("-foreground", Theme.ButtonForeground);
        }
        SKColor color;
        return TkColor.TryParse(spec, out color) ? color : SKColors.Black;
    }

    /// <inheritdoc/>
    private protected override string DefaultBackground
    {
        get { return Theme.ButtonBackground; }
    }

    /// <inheritdoc/>
    private protected override IReadOnlyCollection<string> StyleStates
    {
        get
        {
            var states = new List<string>(3);
            if (Disabled) { states.Add("disabled"); }
            if (_pressed) { states.Add("pressed"); }
            if (_active) { states.Add("active"); }
            if (Window.Tree.FocusWindow == Window) { states.Add("focus"); }
            return states;
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    // ------------------------------------------------------------------
    // Class bindings (shared by every button; resolve the instance from the
    // event's target window).
    // ------------------------------------------------------------------

    private static void EnsureClassBindings(BindingTable bindings)
    {
        // Static handlers → binding is idempotent; safe to (re)register per
        // instance, and correct across multiple window trees.
        bindings.Bind("Button", "<Enter>", OnEnter);
        bindings.Bind("Button", "<Leave>", OnLeave);
        bindings.Bind("Button", "<ButtonPress-1>", OnPress);
        bindings.Bind("Button", "<ButtonRelease-1>", OnRelease);
    }

    private static ButtonWidget From(TkEvent tkEvent)
    {
        return (tkEvent.Window != null) ? tkEvent.Window.Widget as ButtonWidget : null;
    }

    private static DispatchResult OnEnter(TkEvent tkEvent)
    {
        ButtonWidget b = From(tkEvent);
        if (b != null && !b._active) { b._active = true; b.Repaint(); }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnLeave(TkEvent tkEvent)
    {
        ButtonWidget b = From(tkEvent);
        if (b != null && (b._active || b._pressed)) { b._active = false; b._pressed = false; b.Repaint(); }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnPress(TkEvent tkEvent)
    {
        ButtonWidget b = From(tkEvent);
        if (b != null && !b.Disabled && !b._pressed) { b._pressed = true; b.Repaint(); }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnRelease(TkEvent tkEvent)
    {
        ButtonWidget b = From(tkEvent);
        if (b == null) { return DispatchResult.Continue; }
        bool wasPressed = b._pressed;
        if (b._pressed) { b._pressed = false; b.Repaint(); }
        // Fire only when released over the button (the active state) — Tk's rule.
        if (wasPressed && b._active) { b.Invoke(); }
        return DispatchResult.Continue;
    }
}
