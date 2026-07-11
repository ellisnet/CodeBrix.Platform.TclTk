using System;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>scrollbar</c> widget: a sunken trough with a raised
/// draggable slider and an arrow button at each end. <c>set first last</c>
/// positions the slider from a scrolled widget; dragging the slider or
/// clicking the arrows fires <see cref="Command"/> with the same argument
/// words Tk passes to <c>-command</c> (<c>moveto FRACTION</c> or
/// <c>scroll N units|pages</c>), which the scrolled widget (listbox, text,
/// canvas, …) acts on. <c>-orient vertical</c> is the default.
/// </summary>
public sealed class ScrollbarWidget : WidgetBase
{
    private const int Thickness = 15;
    private const int ArrowSize = 15;

    private double _first;
    private double _last = 1.0;
    private int _dragOffset;
    private bool _dragging;

    /// <summary>Creates a scrollbar on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public ScrollbarWidget(TkWindow window)
        : base(window, "Scrollbar")
    {
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>
    /// Raised with the words Tk would append to <c>-command</c>:
    /// <c>["moveto", frac]</c> or <c>["scroll", n, "units"|"pages"]</c>.
    /// </summary>
    public event Action<string[]> Command;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Scrollbar"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 0; }
    }

    /// <summary>Whether the scrollbar is vertical (the default).</summary>
    public bool IsVertical
    {
        get { return Options.Get("-orient", "vertical") != "horizontal"; }
    }

    /// <summary>The first (top/left) visible fraction the slider shows.</summary>
    public double First
    {
        get { return _first; }
    }

    /// <summary>The last (bottom/right) visible fraction the slider shows.</summary>
    public double Last
    {
        get { return _last; }
    }

    /// <summary>Positions the slider from a scrolled widget — <c>$sb set first last</c>.</summary>
    /// <param name="first">The fraction at the top/left of the view.</param>
    /// <param name="last">The fraction at the bottom/right of the view.</param>
    public void Set(double first, double last)
    {
        _first = Clamp01(first);
        _last = Clamp01(last);
        if (_last < _first) { _last = _first; }
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        if (IsVertical) { Window.SetRequestedSize(Thickness, 1); }
        else { Window.SetRequestedSize(1, Thickness); }
    }

    /// <inheritdoc/>
    private protected override string DefaultBackground
    {
        get { return Theme.ScrollbarBackground; }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        SKColor trough;
        if (!TkColor.TryParse(ResolveOption("-troughcolor", Theme.TroughColor), out trough))
        {
            trough = new SKColor(0xB3, 0xB3, 0xB3);
        }
        SKColor slider = BackgroundColor;

        var whole = new SKRect(0, 0, Window.Width, Window.Height);
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = trough;
            canvas.DrawRect(whole, paint);
        }
        ReliefPainter.DrawBorder(canvas, whole, 1, Relief.Sunken, BackgroundColor);

        // Arrow buttons.
        SKRect arrow1 = FirstArrowRect();
        SKRect arrow2 = SecondArrowRect();
        PaintArrow(canvas, arrow1, slider, IsVertical ? '^' : '<');
        PaintArrow(canvas, arrow2, slider, IsVertical ? 'v' : '>');

        // Slider.
        SKRect s = SliderRect();
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = slider;
            canvas.DrawRect(s, paint);
        }
        ReliefPainter.DrawBorder(canvas, s, 2, Relief.Raised, slider);
    }

    private int TroughStart
    {
        get { return ArrowSize; }
    }

    private int TroughLength
    {
        get
        {
            int total = IsVertical ? Window.Height : Window.Width;
            int length = total - 2 * ArrowSize;
            return (length > 0) ? length : 0;
        }
    }

    private SKRect SliderRect()
    {
        int length = TroughLength;
        int start = TroughStart + (int)(_first * length);
        int end = TroughStart + (int)(_last * length);
        if (end - start < 8) { end = start + 8; } // minimum grip
        if (IsVertical)
        {
            return new SKRect(1, start, Window.Width - 1, end);
        }
        return new SKRect(start, 1, end, Window.Height - 1);
    }

    private SKRect FirstArrowRect()
    {
        return IsVertical
                ? new SKRect(0, 0, Window.Width, ArrowSize)
                : new SKRect(0, 0, ArrowSize, Window.Height);
    }

    private SKRect SecondArrowRect()
    {
        return IsVertical
                ? new SKRect(0, Window.Height - ArrowSize, Window.Width, Window.Height)
                : new SKRect(Window.Width - ArrowSize, 0, Window.Width, Window.Height);
    }

    private void PaintArrow(SKCanvas canvas, SKRect rect, SKColor background, char direction)
    {
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(rect, paint);
        }
        ReliefPainter.DrawBorder(canvas, rect, 2, Relief.Raised, background);

        var builder = new SKPathBuilder();
        float cx = rect.MidX;
        float cy = rect.MidY;
        float d = 3f;
        switch (direction)
        {
            case '^': builder.MoveTo(cx, cy - d); builder.LineTo(cx - d, cy + d); builder.LineTo(cx + d, cy + d); break;
            case 'v': builder.MoveTo(cx, cy + d); builder.LineTo(cx - d, cy - d); builder.LineTo(cx + d, cy - d); break;
            case '<': builder.MoveTo(cx - d, cy); builder.LineTo(cx + d, cy - d); builder.LineTo(cx + d, cy + d); break;
            default: builder.MoveTo(cx + d, cy); builder.LineTo(cx - d, cy - d); builder.LineTo(cx - d, cy + d); break;
        }
        builder.Close();
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = TkTheme.Color(Theme.Foreground);
            using (SKPath path = builder.Detach()) { canvas.DrawPath(path, paint); }
        }
    }

    private void FireCommand(params string[] words)
    {
        Action<string[]> handler = Command;
        if (handler != null) { handler(words); }
    }

    private int PointerAlong(TkEvent tkEvent)
    {
        return IsVertical ? tkEvent.Y : tkEvent.X;
    }

    private void BeginDrag(TkEvent tkEvent)
    {
        SKRect s = SliderRect();
        int pos = PointerAlong(tkEvent);
        int sliderStart = (int)(IsVertical ? s.Top : s.Left);
        int sliderEnd = (int)(IsVertical ? s.Bottom : s.Right);

        if (pos < TroughStart)
        {
            FireCommand("scroll", "-1", "units"); // first arrow
        }
        else if (pos >= (IsVertical ? Window.Height : Window.Width) - ArrowSize)
        {
            FireCommand("scroll", "1", "units"); // second arrow
        }
        else if (pos < sliderStart)
        {
            FireCommand("scroll", "-1", "pages"); // trough above/left of slider
        }
        else if (pos > sliderEnd)
        {
            FireCommand("scroll", "1", "pages"); // trough below/right of slider
        }
        else
        {
            _dragging = true;
            _dragOffset = pos - sliderStart;
        }
    }

    private void ContinueDrag(TkEvent tkEvent)
    {
        if (!_dragging) { return; }
        int length = TroughLength;
        if (length <= 0) { return; }
        int pos = PointerAlong(tkEvent) - _dragOffset - TroughStart;
        double fraction = (double)pos / length;
        fraction = Clamp01(fraction);
        FireCommand("moveto", fraction.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static double Clamp01(double v)
    {
        if (v < 0) { return 0; }
        if (v > 1) { return 1; }
        return v;
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Scrollbar", "<ButtonPress-1>", OnPress);
        bindings.Bind("Scrollbar", "<B1-Motion>", OnDrag);
        bindings.Bind("Scrollbar", "<ButtonRelease-1>", OnReleaseDrag);
    }

    private static ScrollbarWidget From(TkEvent tkEvent)
    {
        return (tkEvent.Window != null) ? tkEvent.Window.Widget as ScrollbarWidget : null;
    }

    private static DispatchResult OnPress(TkEvent tkEvent)
    {
        ScrollbarWidget sb = From(tkEvent);
        if (sb != null) { sb.BeginDrag(tkEvent); }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnDrag(TkEvent tkEvent)
    {
        ScrollbarWidget sb = From(tkEvent);
        if (sb != null) { sb.ContinueDrag(tkEvent); }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnReleaseDrag(TkEvent tkEvent)
    {
        ScrollbarWidget sb = From(tkEvent);
        if (sb != null) { sb._dragging = false; }
        return DispatchResult.Continue;
    }
}
