using System;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>checkbutton</c> widget: a labelled toggle with a small
/// indicator box that shows a check when on. Its state can be backed by a
/// shared <see cref="ToggleVariable"/> (the <c>-variable</c>, mapping
/// <c>-onvalue</c>/<c>-offvalue</c>) or, with none set, an internal flag.
/// Clicking (or <see cref="Invoke"/>) toggles the state and fires the
/// command.
/// </summary>
public sealed class CheckbuttonWidget : WidgetBase
{
    private const int IndicatorSize = 13;
    private const int Gap = 6;

    private bool _internalSelected;
    private ToggleVariable _variable;

    /// <summary>Creates a checkbutton on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public CheckbuttonWidget(TkWindow window)
        : base(window, "Checkbutton")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>Raised when the checkbutton is invoked (toggled).</summary>
    public event Action Invoked;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Checkbutton"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 0; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    /// <summary>The command fired on toggle (also surfaced through <see cref="Invoked"/>).</summary>
    public Action Command { get; set; }

    /// <summary>The bound shared variable, or null for internal state.</summary>
    public ToggleVariable Variable
    {
        get { return _variable; }
        set
        {
            if (_variable != null) { _variable.Changed -= OnVariableChanged; }
            _variable = value;
            if (_variable != null) { _variable.Changed += OnVariableChanged; }
            Repaint();
        }
    }

    private string OnValue
    {
        get { return Options.Get("-onvalue", "1"); }
    }

    private string OffValue
    {
        get { return Options.Get("-offvalue", "0"); }
    }

    /// <summary>Whether the checkbutton is currently on.</summary>
    public bool IsSelected
    {
        get { return (_variable != null) ? (_variable.Value == OnValue) : _internalSelected; }
    }

    private bool Disabled
    {
        get { return Options.Get("-state", "normal") == "disabled"; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkDefaultFont");
        }
    }

    /// <summary>Turns the checkbutton on.</summary>
    public void Select()
    {
        if (_variable != null) { _variable.Set(OnValue); }
        else { _internalSelected = true; }
        Repaint();
    }

    /// <summary>Turns the checkbutton off.</summary>
    public void Deselect()
    {
        if (_variable != null) { _variable.Set(OffValue); }
        else { _internalSelected = false; }
        Repaint();
    }

    /// <summary>Toggles and fires the command — <c>invoke</c>.</summary>
    public void Invoke()
    {
        if (Disabled) { return; }
        if (IsSelected) { Deselect(); } else { Select(); }
        Action command = Command;
        if (command != null) { command(); }
        Action handler = Invoked;
        if (handler != null) { handler(); }
    }

    private void OnVariableChanged()
    {
        Repaint();
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int textWidth;
        int textHeight;
        WidgetText.MeasureBlock(Fonts, font, Options.Get("-text", ""), out textWidth, out textHeight);
        int inset = Inset;
        int lineHeight = Fonts.Metrics(font).LineSpace;
        int contentHeight = Math.Max(IndicatorSize, textHeight);
        int reqW = IndicatorSize + Gap + textWidth + 2 * inset + 4;
        int reqH = contentHeight + 2 * inset + 4;
        Window.SetRequestedSize(reqW, reqH);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        int inset = Inset;
        float indTop = (Window.Height - IndicatorSize) / 2f;
        var box = new SKRect(inset + 2, indTop, inset + 2 + IndicatorSize, indTop + IndicatorSize);

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColors.White;
            canvas.DrawRect(box, paint);
        }
        ReliefPainter.DrawBorder(canvas, box, 2, Relief.Sunken, BackgroundColor);

        if (IsSelected)
        {
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 2;
                paint.Color = new SKColor(0x1a, 0x1a, 0x1a);
                canvas.DrawLine(box.Left + 3, box.MidY, box.MidX - 1, box.Bottom - 3, paint);
                canvas.DrawLine(box.MidX - 1, box.Bottom - 3, box.Right - 2, box.Top + 2, paint);
            }
        }

        string text = Options.Get("-text", "");
        if (text.Length == 0) { return; }
        TkFont font = Font;
        FontMetrics metrics = Fonts.Metrics(font);
        SKColor fg;
        string fgSpec = Disabled ? Options.Get("-disabledforeground", "#a3a3a3")
                : Options.Get("-foreground", Options.Get("-fg", "black"));
        if (!TkColor.TryParse(fgSpec, out fg)) { fg = SKColors.Black; }
        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            paint.Color = fg;
            paint.IsAntialias = true;
            float baseline = (Window.Height - metrics.LineSpace) / 2f + metrics.Ascent;
            canvas.DrawText(text, box.Right + Gap, baseline, SKTextAlign.Left, skFont, paint);
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Checkbutton", "<ButtonRelease-1>", OnRelease);
    }

    private static DispatchResult OnRelease(TkEvent e)
    {
        var cb = (e.Window != null) ? e.Window.Widget as CheckbuttonWidget : null;
        if (cb != null) { cb.Invoke(); }
        return DispatchResult.Continue;
    }
}
