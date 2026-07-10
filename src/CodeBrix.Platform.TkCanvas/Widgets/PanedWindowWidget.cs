using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>panedwindow</c> widget: a container that stacks its pane
/// children along <c>-orient</c> (horizontal by default) with a draggable
/// raised sash between each pair. It arranges its panes itself (through the
/// window layout's content-arrange hook), sizing them proportionally to the
/// space and letting a sash drag shift the split between its two neighbours.
/// Panes are added with <see cref="Add"/>; the sash geometry surfaces through
/// <see cref="SashCoord"/>/<see cref="MoveSash"/>.
/// </summary>
public sealed class PanedWindowWidget : WidgetBase
{
    private readonly List<TkWindow> _panes = new List<TkWindow>();
    private readonly List<double> _weights = new List<double>();
    private int _dragSash = -1;
    private int _dragStart;

    /// <summary>Creates a panedwindow on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public PanedWindowWidget(TkWindow window)
        : base(window, "Panedwindow")
    {
        window.ArrangeContent = ArrangePanes;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Panedwindow"; }
    }

    /// <summary>Whether panes stack left-to-right (the default) rather than top-to-bottom.</summary>
    public bool IsHorizontal
    {
        get { return Options.Get("-orient", "horizontal") != "vertical"; }
    }

    private int SashWidth
    {
        get { int v; return TclString.TryParsePixels(Options.Get("-sashwidth", "6"), out v) ? v : 6; }
    }

    /// <summary>The panes in order.</summary>
    public IReadOnlyList<TkWindow> Panes
    {
        get { return _panes; }
    }

    /// <summary>Adds a pane child window — <c>$pw add window</c>.</summary>
    /// <param name="pane">The child window to manage as a pane.</param>
    public void Add(TkWindow pane)
    {
        if (pane == null || _panes.Contains(pane)) { return; }
        _panes.Add(pane);
        _weights.Add(PaneNaturalSize(pane));
        Measure();
        if (!Window.IsDestroyed) { Window.Tree.NotifyGeometryChanged(); }
    }

    /// <summary>Removes a pane — <c>$pw forget window</c>.</summary>
    /// <param name="pane">The pane to remove.</param>
    public void Forget(TkWindow pane)
    {
        int index = _panes.IndexOf(pane);
        if (index < 0) { return; }
        _panes.RemoveAt(index);
        _weights.RemoveAt(index);
        pane.IsDisplayed = false;
        Measure();
        if (!Window.IsDestroyed) { Window.Tree.NotifyGeometryChanged(); }
    }

    private double PaneNaturalSize(TkWindow pane)
    {
        int v = IsHorizontal ? pane.RequestedWidth : pane.RequestedHeight;
        return (v > 0) ? v : 1;
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        int inset = Inset;
        int sash = SashWidth;
        int along = 0;
        int cross = 0;
        foreach (TkWindow pane in _panes)
        {
            if (IsHorizontal)
            {
                along += pane.RequestedWidth;
                if (pane.RequestedHeight > cross) { cross = pane.RequestedHeight; }
            }
            else
            {
                along += pane.RequestedHeight;
                if (pane.RequestedWidth > cross) { cross = pane.RequestedWidth; }
            }
        }
        if (_panes.Count > 1) { along += (_panes.Count - 1) * sash; }

        int reqAlong = along + 2 * inset;
        int reqCross = cross + 2 * inset;
        int reqW = IsHorizontal ? reqAlong : reqCross;
        int reqH = IsHorizontal ? reqCross : reqAlong;

        int forced;
        if (TclString.TryParsePixels(Options.Get("-width", "0"), out forced) && forced > 0) { reqW = forced; }
        if (TclString.TryParsePixels(Options.Get("-height", "0"), out forced) && forced > 0) { reqH = forced; }

        Window.SetRequestedSize(reqW > 1 ? reqW : 1, reqH > 1 ? reqH : 1);
        Window.SetInternalBorder(inset);
    }

    private bool ArrangePanes()
    {
        if (_panes.Count == 0) { return false; }

        int inset = Inset;
        int sash = SashWidth;
        int alongTotal = (IsHorizontal ? Window.Width : Window.Height) - 2 * inset;
        int crossTotal = (IsHorizontal ? Window.Height : Window.Width) - 2 * inset;
        if (alongTotal <= 0 || crossTotal <= 0) { return false; }

        int avail = alongTotal - (_panes.Count - 1) * sash;
        if (avail < _panes.Count) { avail = _panes.Count; }

        double weightSum = 0;
        foreach (double w in _weights) { weightSum += w; }
        if (weightSum <= 0) { weightSum = _panes.Count; }

        bool changed = false;
        int pos = inset;
        for (int i = 0; i < _panes.Count; i++)
        {
            int size = (i == _panes.Count - 1)
                    ? (inset + avail + (_panes.Count - 1) * sash) - pos - (_panes.Count - 1 - i) * sash
                    : (int)(avail * (_weights[i] / weightSum));
            if (size < 1) { size = 1; }

            TkWindow pane = _panes[i];
            int nx, ny, nw, nh;
            if (IsHorizontal)
            {
                nx = pos; ny = inset; nw = size; nh = crossTotal;
            }
            else
            {
                nx = inset; ny = pos; nw = crossTotal; nh = size;
            }
            if (pane.X != nx || pane.Y != ny || pane.Width != nw || pane.Height != nh
                    || !pane.IsDisplayed)
            {
                pane.X = nx; pane.Y = ny; pane.Width = nw; pane.Height = nh;
                pane.IsDisplayed = true;
                changed = true;
            }
            pos += size + sash;
        }
        return changed;
    }

    /// <summary>The along-axis coordinate of a sash (its left/top edge), or -1 if none.</summary>
    /// <param name="index">The sash index (0 = between pane 0 and 1).</param>
    /// <returns>The window coordinate, or -1.</returns>
    public int SashCoord(int index)
    {
        if (index < 0 || index >= _panes.Count - 1) { return -1; }
        TkWindow after = _panes[index + 1];
        return IsHorizontal ? after.X - SashWidth : after.Y - SashWidth;
    }

    /// <summary>
    /// Shifts a sash by a delta along the orient axis, moving space between
    /// the two panes it divides (clamped so neither shrinks below 1px).
    /// </summary>
    /// <param name="index">The sash index.</param>
    /// <param name="delta">The pixels to move (positive grows the earlier pane).</param>
    public void MoveSash(int index, int delta)
    {
        if (index < 0 || index >= _panes.Count - 1) { return; }
        double moved = delta;
        double left = _weights[index] + moved;
        double right = _weights[index + 1] - moved;
        if (left < 1 || right < 1) { return; }
        _weights[index] = left;
        _weights[index + 1] = right;
        if (!Window.IsDestroyed) { Window.Tree.NotifyGeometryChanged(); }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        // Sashes between panes.
        int sash = SashWidth;
        for (int i = 0; i < _panes.Count - 1; i++)
        {
            int coord = SashCoord(i);
            if (coord < 0) { continue; }
            SKRect rect = IsHorizontal
                    ? new SKRect(coord, Inset, coord + sash, Window.Height - Inset)
                    : new SKRect(Inset, coord, Window.Width - Inset, coord + sash);
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = false;
                paint.Style = SKPaintStyle.Fill;
                paint.Color = BackgroundColor;
                canvas.DrawRect(rect, paint);
            }
            ReliefPainter.DrawBorder(canvas, rect, 2, Relief.Raised, BackgroundColor);
        }
    }

    private int PointerAlong(TkEvent e)
    {
        return IsHorizontal ? e.X : e.Y;
    }

    private int SashAt(int alongCoord)
    {
        int sash = SashWidth;
        for (int i = 0; i < _panes.Count - 1; i++)
        {
            int coord = SashCoord(i);
            if (coord >= 0 && alongCoord >= coord && alongCoord < coord + sash) { return i; }
        }
        return -1;
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Panedwindow", "<ButtonPress-1>", OnPress);
        bindings.Bind("Panedwindow", "<B1-Motion>", OnDrag);
        bindings.Bind("Panedwindow", "<ButtonRelease-1>", OnRelease);
    }

    private static PanedWindowWidget From(TkEvent e)
    {
        return (e.Window != null) ? e.Window.Widget as PanedWindowWidget : null;
    }

    private static DispatchResult OnPress(TkEvent e)
    {
        PanedWindowWidget pw = From(e);
        if (pw != null)
        {
            int along = pw.PointerAlong(e);
            pw._dragSash = pw.SashAt(along);
            pw._dragStart = along;
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnDrag(TkEvent e)
    {
        PanedWindowWidget pw = From(e);
        if (pw != null && pw._dragSash >= 0)
        {
            int along = pw.PointerAlong(e);
            pw.MoveSash(pw._dragSash, along - pw._dragStart);
            pw._dragStart = along;
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnRelease(TkEvent e)
    {
        PanedWindowWidget pw = From(e);
        if (pw != null) { pw._dragSash = -1; }
        return DispatchResult.Continue;
    }
}
