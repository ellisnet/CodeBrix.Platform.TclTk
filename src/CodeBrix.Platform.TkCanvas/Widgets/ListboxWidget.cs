using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>listbox</c> widget: a vertically scrolled list of text
/// lines with a selection, a sunken white field, and the yview/scrollcommand
/// protocol. Supports the DRAKON-relevant surface — <c>insert</c>,
/// <c>delete</c>, <c>get</c>, <c>size</c>, <c>curselection</c>,
/// <c>selection</c>, <c>see</c>, <c>activate</c>, <c>nearest</c>,
/// <c>yview</c> — with the <c>&lt;&lt;ListboxSelect&gt;&gt;</c> virtual event
/// fired on selection change and the <c>-yscrollcommand</c> surfaced through
/// <see cref="YScrollChanged"/> so a scrollbar can track it.
/// </summary>
public sealed class ListboxWidget : WidgetBase
{
    private readonly List<string> _items = new List<string>();
    private readonly HashSet<int> _selection = new HashSet<int>();
    private int _top;      // first visible row
    private int _active;   // active (keyboard) index

    /// <summary>Creates a listbox on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public ListboxWidget(TkWindow window)
        : base(window, "Listbox")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>Raised with the vertical scroll fractions (<c>-yscrollcommand</c>).</summary>
    public event Action<double, double> YScrollChanged;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Listbox"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 2; }
    }

    private protected override string DefaultRelief
    {
        get { return "sunken"; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    private protected override string DefaultBackground
    {
        get { return Theme.FieldBackground; }
    }

    /// <summary>The number of items — <c>size</c>.</summary>
    public int Size
    {
        get { return _items.Count; }
    }

    /// <summary>The items in order.</summary>
    public IReadOnlyList<string> Items
    {
        get { return _items; }
    }

    /// <summary>The active (underlined) index — <c>index active</c>.</summary>
    public int Active
    {
        get { return _active; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkDefaultFont");
        }
    }

    private int RowHeight
    {
        get { return Fonts.Metrics(Font).LineSpace + 2; }
    }

    private bool SingleSelect
    {
        get
        {
            string mode = Options.Get("-selectmode", "browse");
            return mode == "browse" || mode == "single";
        }
    }

    /// <summary>Inserts items before <paramref name="index"/> (end when negative) — <c>insert</c>.</summary>
    /// <param name="index">The insertion index, or -1 for the end.</param>
    /// <param name="values">The items to insert.</param>
    public void Insert(int index, params string[] values)
    {
        if (index < 0 || index > _items.Count) { index = _items.Count; }
        _items.InsertRange(index, values);
        Repaint();
    }

    /// <summary>Deletes items in <c>[first, last]</c> (inclusive) — <c>delete</c>.</summary>
    /// <param name="first">The first index.</param>
    /// <param name="last">The last index (defaults to first).</param>
    public void Delete(int first, int last = -1)
    {
        if (last < 0) { last = first; }
        first = Math.Max(0, first);
        last = Math.Min(_items.Count - 1, last);
        if (last < first) { return; }
        _items.RemoveRange(first, last - first + 1);
        _selection.Clear();
        Repaint();
    }

    /// <summary>The item text at an index — <c>get index</c>.</summary>
    /// <param name="index">The item index.</param>
    /// <returns>The text, or empty when out of range.</returns>
    public string Get(int index)
    {
        return (index >= 0 && index < _items.Count) ? _items[index] : "";
    }

    /// <summary>The selected indices in order — <c>curselection</c>.</summary>
    /// <returns>The sorted selected indices.</returns>
    public IReadOnlyList<int> CurSelection()
    {
        var list = new List<int>(_selection);
        list.Sort();
        return list;
    }

    /// <summary>Selects an index (clearing others in single-select modes) — <c>selection set</c>.</summary>
    /// <param name="index">The index to select.</param>
    public void SelectionSet(int index)
    {
        if (index < 0 || index >= _items.Count) { return; }
        if (SingleSelect) { _selection.Clear(); }
        _selection.Add(index);
        _active = index;
        FireSelect();
        Repaint();
    }

    /// <summary>Clears the selection in <c>[first, last]</c> — <c>selection clear</c>.</summary>
    /// <param name="first">The first index.</param>
    /// <param name="last">The last index (defaults to first).</param>
    public void SelectionClear(int first, int last = -1)
    {
        if (last < 0) { last = first; }
        for (int i = first; i <= last; i++) { _selection.Remove(i); }
        Repaint();
    }

    /// <summary>Whether an index is selected — <c>selection includes</c>.</summary>
    /// <param name="index">The index.</param>
    /// <returns>True when selected.</returns>
    public bool SelectionIncludes(int index)
    {
        return _selection.Contains(index);
    }

    /// <summary>The nearest item index to a y coordinate — <c>nearest y</c>.</summary>
    /// <param name="y">The window-relative y.</param>
    /// <returns>The item index (clamped).</returns>
    public int Nearest(int y)
    {
        int row = _top + (y - Inset) / RowHeight;
        if (row < 0) { row = 0; }
        if (row >= _items.Count) { row = _items.Count - 1; }
        return row;
    }

    /// <summary>Scrolls so an item is visible — <c>see</c>.</summary>
    /// <param name="index">The item index.</param>
    public void See(int index)
    {
        int rows = VisibleRows();
        if (index < _top) { _top = index; }
        else if (index >= _top + rows) { _top = index - rows + 1; }
        ClampTop();
        NotifyScroll();
        Repaint();
    }

    /// <summary>Scrolls to a fraction of the list — <c>yview moveto</c>.</summary>
    /// <param name="fraction">The target fraction.</param>
    public void YViewMoveTo(double fraction)
    {
        _top = (int)(fraction * _items.Count + 0.5);
        ClampTop();
        NotifyScroll();
        Repaint();
    }

    /// <summary>Scrolls by units or pages — <c>yview scroll</c>.</summary>
    /// <param name="count">The signed count.</param>
    /// <param name="pages">True for pages, false for units (rows).</param>
    public void YViewScroll(int count, bool pages)
    {
        int step = pages ? VisibleRows() : 1;
        _top += count * step;
        ClampTop();
        NotifyScroll();
        Repaint();
    }

    private int VisibleRows()
    {
        int h = Window.Height - 2 * Inset;
        int rh = RowHeight;
        return (rh > 0) ? Math.Max(1, h / rh) : 1;
    }

    private void ClampTop()
    {
        int maxTop = Math.Max(0, _items.Count - VisibleRows());
        if (_top > maxTop) { _top = maxTop; }
        if (_top < 0) { _top = 0; }
    }

    private void NotifyScroll()
    {
        Action<double, double> handler = YScrollChanged;
        if (handler == null) { return; }
        int n = _items.Count;
        if (n == 0) { handler(0, 1); return; }
        double first = (double)_top / n;
        double last = (double)Math.Min(n, _top + VisibleRows()) / n;
        handler(first, last);
    }

    private void FireSelect()
    {
        if (!Window.IsDestroyed)
        {
            Window.Tree.DispatchEvent(Window, new TkEvent
            {
                Type = TkEventType.Virtual,
                VirtualName = "ListboxSelect",
                KeySym = string.Empty,
                Character = string.Empty,
            });
        }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int chars = Options.GetInt("-width", 20);
        int lines = Options.GetInt("-height", 10);
        int charWidth = Fonts.Measure(font, "0");
        if (charWidth < 1) { charWidth = 1; }
        int inset = Inset;
        int reqW = (chars > 0 ? chars * charWidth : 100) + 2 * inset + 2;
        int reqH = (lines > 0 ? lines : 10) * RowHeight + 2 * inset;
        Window.SetRequestedSize(reqW, reqH);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        TkFont font = Font;
        FontMetrics metrics = Fonts.Metrics(font);
        int inset = Inset;
        int rowHeight = RowHeight;
        int rows = VisibleRows();

        SKColor selBg;
        if (!TkColor.TryParse(ResolveOption("-selectbackground", Theme.ListSelectBackground), out selBg))
        {
            selBg = new SKColor(0x4A, 0x69, 0x84);
        }
        SKColor selFg;
        if (!TkColor.TryParse(ResolveOption("-selectforeground", Theme.ListSelectForeground), out selFg))
        {
            selFg = SKColors.White;
        }
        SKColor fg;
        if (!TkColor.TryParse(ResolveOption("-foreground", Theme.FieldForeground), out fg)) { fg = SKColors.Black; }

        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            for (int r = 0; r < rows; r++)
            {
                int index = _top + r;
                if (index >= _items.Count) { break; }
                float top = inset + r * rowHeight;
                bool selected = _selection.Contains(index);
                if (selected)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = selBg;
                    paint.IsAntialias = false;
                    canvas.DrawRect(new SKRect(inset, top, Window.Width - inset, top + rowHeight), paint);
                }
                paint.Color = selected ? selFg : fg;
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawText(_items[index], inset + 3, top + 1 + metrics.Ascent,
                        SKTextAlign.Left, skFont, paint);
            }
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Listbox", "<ButtonPress-1>", OnPress);
        bindings.Bind("Listbox", "<B1-Motion>", OnDrag);
        bindings.Bind("Listbox", "<MouseWheel>", OnWheel);
    }

    private static ListboxWidget From(TkEvent e)
    {
        return (e.Window != null) ? e.Window.Widget as ListboxWidget : null;
    }

    private static DispatchResult OnPress(TkEvent e)
    {
        ListboxWidget lb = From(e);
        if (lb != null && lb._items.Count > 0)
        {
            lb.Window.Tree.SetFocus(lb.Window);
            int index = lb.Nearest(e.Y);
            lb.SelectionClear(0, lb._items.Count - 1);
            lb.SelectionSet(index);
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnDrag(TkEvent e)
    {
        ListboxWidget lb = From(e);
        if (lb != null && lb._items.Count > 0 && lb.SingleSelect)
        {
            int index = lb.Nearest(e.Y);
            lb.SelectionClear(0, lb._items.Count - 1);
            lb.SelectionSet(index);
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnWheel(TkEvent e)
    {
        ListboxWidget lb = From(e);
        if (lb != null) { lb.YViewScroll(e.Delta > 0 ? -1 : 1, false); }
        return DispatchResult.Continue;
    }
}
