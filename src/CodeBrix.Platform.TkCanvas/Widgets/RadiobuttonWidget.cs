using System;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>radiobutton</c> widget: a labelled option with a diamond
/// indicator that is filled when this button's <c>-value</c> equals the shared
/// <see cref="ToggleVariable"/> it is bound to. Selecting one radiobutton sets
/// the variable, which deselects the others in the group. Clicking (or
/// <see cref="Invoke"/>) selects this button and fires its command.
/// </summary>
public sealed class RadiobuttonWidget : WidgetBase
{
    private const int IndicatorSize = 13;
    private const int Gap = 6;

    private ToggleVariable _variable;

    /// <summary>Creates a radiobutton on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public RadiobuttonWidget(TkWindow window)
        : base(window, "Radiobutton")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>Raised when the radiobutton is invoked (selected).</summary>
    public event Action Invoked;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Radiobutton"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 0; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    /// <summary>The command fired on selection (also surfaced through <see cref="Invoked"/>).</summary>
    public Action Command { get; set; }

    /// <summary>The shared group variable (radiobuttons sharing it are mutually exclusive).</summary>
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

    private string MyValue
    {
        get { return Options.Get("-value", ""); }
    }

    /// <summary>Whether this radiobutton is the selected one in its group.</summary>
    public bool IsSelected
    {
        get { return _variable != null && _variable.Value == MyValue; }
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

    /// <summary>Selects this radiobutton (sets the group variable to its value).</summary>
    public void Select()
    {
        if (_variable != null) { _variable.Set(MyValue); }
        Repaint();
    }

    /// <summary>Selects and fires the command — <c>invoke</c>.</summary>
    public void Invoke()
    {
        if (Disabled) { return; }
        Select();
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
        int contentHeight = Math.Max(IndicatorSize, textHeight);
        Window.SetRequestedSize(
                IndicatorSize + Gap + textWidth + 2 * inset + 4,
                contentHeight + 2 * inset + 4);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        int inset = Inset;
        float cx = inset + 2 + IndicatorSize / 2f;
        float cy = Window.Height / 2f;
        float rad = IndicatorSize / 2f;

        // Diamond indicator (classic Tk radiobutton shape).
        var diamond = new SKPathBuilder();
        diamond.MoveTo(cx, cy - rad);
        diamond.LineTo(cx + rad, cy);
        diamond.LineTo(cx, cy + rad);
        diamond.LineTo(cx - rad, cy);
        diamond.Close();
        using (SKPath path = diamond.Detach())
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColors.White;
            canvas.DrawPath(path, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            paint.Color = ReliefPainter.DarkShadow(BackgroundColor);
            canvas.DrawPath(path, paint);
        }

        if (IsSelected)
        {
            var inner = new SKPathBuilder();
            float ir = rad - 4;
            inner.MoveTo(cx, cy - ir);
            inner.LineTo(cx + ir, cy);
            inner.LineTo(cx, cy + ir);
            inner.LineTo(cx - ir, cy);
            inner.Close();
            using (SKPath path = inner.Detach())
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(0x1a, 0x1a, 0x1a);
                canvas.DrawPath(path, paint);
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
            canvas.DrawText(text, inset + 2 + IndicatorSize + Gap, baseline, SKTextAlign.Left, skFont, paint);
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Radiobutton", "<ButtonRelease-1>", OnRelease);
    }

    private static DispatchResult OnRelease(TkEvent e)
    {
        var rb = (e.Window != null) ? e.Window.Widget as RadiobuttonWidget : null;
        if (rb != null) { rb.Invoke(); }
        return DispatchResult.Continue;
    }
}
