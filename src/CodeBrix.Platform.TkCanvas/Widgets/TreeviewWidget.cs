using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// A node of a <see cref="TreeviewWidget"/> — its stable id, parent, tree
/// text, per-column values, and open/closed state.
/// </summary>
public sealed class TreeItem
{
    /// <summary>The stable item id (Tk's opaque item name).</summary>
    public string Id { get; internal set; } = "";

    /// <summary>The tree-column text (<c>-text</c>).</summary>
    public string Text { get; set; } = "";

    /// <summary>The per-column values (<c>-values</c>), aligned to the display columns.</summary>
    public List<string> Values { get; } = new List<string>();

    /// <summary>Whether the node is expanded (<c>-open</c>).</summary>
    public bool Open { get; set; }

    /// <summary>The parent item id (empty string for a top-level item).</summary>
    public string Parent { get; internal set; } = "";

    /// <summary>The child item ids in order.</summary>
    public List<string> Children { get; } = new List<string>();
}

/// <summary>
/// The Tk <c>ttk::treeview</c> widget on Skia: a hierarchical, optionally
/// multi-column list with expand/collapse triangles, column headings, a
/// selection, and vertical scrolling. Implements the DRAKON-relevant surface
/// — <c>insert</c>, <c>delete</c>, <c>item</c>, <c>heading</c>,
/// <c>column</c>, <c>selection</c>, <c>see</c>, <c>children</c>,
/// <c>parent</c>, open/close via the triangle — and fires
/// <c>&lt;&lt;TreeviewSelect&gt;&gt;</c> on selection change with the
/// yview/scrollcommand protocol.
/// </summary>
public sealed class TreeviewWidget : WidgetBase
{
    private readonly Dictionary<string, TreeItem> _items =
            new Dictionary<string, TreeItem>(StringComparer.Ordinal);
    private readonly TreeItem _root = new TreeItem { Id = "" };
    private readonly List<string> _columns = new List<string>();
    private readonly Dictionary<string, string> _headings =
            new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _selection = new List<string>();
    private int _serial;
    private int _top;

    /// <summary>Creates a treeview on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public TreeviewWidget(TkWindow window)
        : base(window, "Treeview")
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
        get { return "Treeview"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 1; }
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
        get { return "white"; }
    }

    /// <summary>The data (non-tree) column ids in display order (<c>-columns</c>).</summary>
    public IReadOnlyList<string> Columns
    {
        get { return _columns; }
    }

    /// <summary>The currently selected item ids — <c>selection</c>.</summary>
    public IReadOnlyList<string> Selection
    {
        get { return _selection; }
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
        get { return Fonts.Metrics(Font).LineSpace + 4; }
    }

    private int HeadingHeight
    {
        get { return _headings.Count > 0 ? RowHeight : 0; }
    }

    private protected override void OnConfigured()
    {
        string cols = Options.Get("-columns", "");
        if (cols.Length > 0)
        {
            _columns.Clear();
            foreach (string c in TclString.SplitList(cols)) { _columns.Add(c); }
        }
    }

    /// <summary>Defines the data columns — <c>$tv configure -columns {...}</c>.</summary>
    /// <param name="columns">The column ids.</param>
    public void SetColumns(params string[] columns)
    {
        _columns.Clear();
        _columns.AddRange(columns);
        Repaint();
    }

    /// <summary>Sets a column heading text — <c>heading col -text ...</c>.</summary>
    /// <param name="column">The column id (or <c>#0</c> for the tree column).</param>
    /// <param name="text">The heading text.</param>
    public void SetHeading(string column, string text)
    {
        _headings[column] = text;
        Repaint();
    }

    /// <summary>
    /// Inserts an item under a parent — <c>insert parent index -text ... -values ...</c>.
    /// </summary>
    /// <param name="parent">The parent id (empty for top level).</param>
    /// <param name="index">The child index, or -1 for the end.</param>
    /// <param name="text">The tree-column text.</param>
    /// <param name="values">The column values.</param>
    /// <param name="id">An explicit id, or null to auto-generate.</param>
    /// <returns>The inserted item's id.</returns>
    public string Insert(string parent, int index, string text, string[] values = null, string id = null)
    {
        TreeItem parentItem = (parent.Length == 0) ? _root : Find(parent);
        if (parentItem == null) { throw new InvalidOperationException("Item " + parent + " not found"); }

        if (string.IsNullOrEmpty(id)) { id = "I" + (++_serial).ToString("D3", CultureInfo.InvariantCulture); }
        var item = new TreeItem { Id = id, Text = text ?? "", Parent = parent };
        if (values != null) { item.Values.AddRange(values); }
        _items[id] = item;

        if (index < 0 || index > parentItem.Children.Count) { index = parentItem.Children.Count; }
        parentItem.Children.Insert(index, id);
        Repaint();
        return id;
    }

    /// <summary>Deletes items and their subtrees — <c>delete</c>.</summary>
    /// <param name="ids">The item ids to delete.</param>
    public void Delete(params string[] ids)
    {
        foreach (string id in ids) { DeleteOne(id); }
        Repaint();
    }

    private void DeleteOne(string id)
    {
        TreeItem item = Find(id);
        if (item == null) { return; }
        foreach (string child in new List<string>(item.Children)) { DeleteOne(child); }
        TreeItem parent = (item.Parent.Length == 0) ? _root : Find(item.Parent);
        if (parent != null) { parent.Children.Remove(id); }
        _items.Remove(id);
        _selection.Remove(id);
    }

    /// <summary>The item for an id, or null.</summary>
    /// <param name="id">The item id.</param>
    /// <returns>The item, or null.</returns>
    public TreeItem Item(string id)
    {
        return Find(id);
    }

    /// <summary>The child ids of an item — <c>children</c>.</summary>
    /// <param name="id">The parent id (empty for top level).</param>
    /// <returns>The child ids.</returns>
    public IReadOnlyList<string> ChildrenOf(string id)
    {
        TreeItem item = (id.Length == 0) ? _root : Find(id);
        return (item != null) ? item.Children : (IReadOnlyList<string>)new List<string>();
    }

    /// <summary>Sets the selection — <c>selection set</c>, firing <c>&lt;&lt;TreeviewSelect&gt;&gt;</c>.</summary>
    /// <param name="ids">The item ids to select.</param>
    public void SelectionSet(params string[] ids)
    {
        _selection.Clear();
        foreach (string id in ids)
        {
            if (_items.ContainsKey(id)) { _selection.Add(id); }
        }
        FireSelect();
        Repaint();
    }

    /// <summary>Opens or closes an item's expander — <c>item id -open bool</c>.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="open">Whether to open.</param>
    public void SetOpen(string id, bool open)
    {
        TreeItem item = Find(id);
        if (item != null && item.Open != open)
        {
            item.Open = open;
            NotifyScroll();
            Repaint();
        }
    }

    private TreeItem Find(string id)
    {
        TreeItem item;
        return _items.TryGetValue(id, out item) ? item : null;
    }

    /// <summary>The flattened list of currently visible item ids (respecting open state).</summary>
    /// <returns>The visible ids top-to-bottom.</returns>
    public IReadOnlyList<string> VisibleItems()
    {
        var result = new List<string>();
        Flatten(_root, result);
        return result;
    }

    private void Flatten(TreeItem parent, List<string> into)
    {
        foreach (string childId in parent.Children)
        {
            TreeItem child = Find(childId);
            if (child == null) { continue; }
            into.Add(childId);
            if (child.Open && child.Children.Count > 0) { Flatten(child, into); }
        }
    }

    private int Depth(string id)
    {
        int depth = 0;
        TreeItem item = Find(id);
        while (item != null && item.Parent.Length > 0)
        {
            depth++;
            item = Find(item.Parent);
        }
        return depth;
    }

    private int VisibleRows()
    {
        int h = Window.Height - 2 * Inset - HeadingHeight;
        int rh = RowHeight;
        return (rh > 0) ? Math.Max(1, h / rh) : 1;
    }

    private void NotifyScroll()
    {
        Action<double, double> handler = YScrollChanged;
        if (handler == null) { return; }
        int n = VisibleItems().Count;
        if (n == 0) { handler(0, 1); return; }
        double first = (double)_top / n;
        double last = (double)Math.Min(n, _top + VisibleRows()) / n;
        handler(first, last);
    }

    /// <summary>Scrolls to a fraction — <c>yview moveto</c>.</summary>
    /// <param name="fraction">The target fraction.</param>
    public void YViewMoveTo(double fraction)
    {
        _top = (int)(fraction * VisibleItems().Count + 0.5);
        ClampTop();
        NotifyScroll();
        Repaint();
    }

    /// <summary>Scrolls by units or pages — <c>yview scroll</c>.</summary>
    /// <param name="count">The signed count.</param>
    /// <param name="pages">True for pages, false for rows.</param>
    public void YViewScroll(int count, bool pages)
    {
        _top += count * (pages ? VisibleRows() : 1);
        ClampTop();
        NotifyScroll();
        Repaint();
    }

    private void ClampTop()
    {
        int maxTop = Math.Max(0, VisibleItems().Count - VisibleRows());
        _top = Math.Min(Math.Max(0, _top), maxTop);
    }

    private void FireSelect()
    {
        if (!Window.IsDestroyed)
        {
            Window.Tree.DispatchEvent(Window, new TkEvent
            {
                Type = TkEventType.Virtual,
                VirtualName = "TreeviewSelect",
                KeySym = string.Empty,
                Character = string.Empty,
            });
        }
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        int inset = Inset;
        int lines = Options.GetInt("-height", 10);
        int treeWidth = Fonts.Measure(Font, "0") * 20;
        int colsWidth = _columns.Count * 100;
        Window.SetRequestedSize(treeWidth + colsWidth + 2 * inset,
                (lines > 0 ? lines : 10) * RowHeight + HeadingHeight + 2 * inset);
        Window.SetInternalBorder(inset);
    }

    private int TreeColumnWidth
    {
        get
        {
            int inset = Inset;
            int total = Window.Width - 2 * inset;
            int cols = _columns.Count;
            return (cols == 0) ? total : Math.Max(80, total - cols * ColumnWidth);
        }
    }

    private int ColumnWidth
    {
        get { return 100; }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        TkFont font = Font;
        FontMetrics metrics = Fonts.Metrics(font);
        int inset = Inset;
        int rowHeight = RowHeight;
        int treeWidth = TreeColumnWidth;

        SKColor selBg;
        if (!TkColor.TryParse("#4a6984", out selBg)) { selBg = new SKColor(0x4A, 0x69, 0x84); }
        SKColor fg;
        if (!TkColor.TryParse(Options.Get("-foreground", "black"), out fg)) { fg = SKColors.Black; }

        int headingH = HeadingHeight;
        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            // Headings row.
            if (headingH > 0)
            {
                var headRect = new SKRect(inset, inset, Window.Width - inset, inset + headingH);
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(0xE0, 0xE0, 0xE0);
                paint.IsAntialias = false;
                canvas.DrawRect(headRect, paint);
                ReliefPainter.DrawBorder(canvas, headRect, 1, Relief.Raised, new SKColor(0xE0, 0xE0, 0xE0));
                paint.Color = SKColors.Black;
                paint.IsAntialias = true;
                string treeHead;
                _headings.TryGetValue("#0", out treeHead);
                canvas.DrawText(treeHead ?? "", inset + 4, inset + 2 + metrics.Ascent, SKTextAlign.Left, skFont, paint);
                for (int c = 0; c < _columns.Count; c++)
                {
                    string ht;
                    _headings.TryGetValue(_columns[c], out ht);
                    float cx = inset + treeWidth + c * ColumnWidth + 4;
                    canvas.DrawText(ht ?? "", cx, inset + 2 + metrics.Ascent, SKTextAlign.Left, skFont, paint);
                }
            }

            IReadOnlyList<string> visible = VisibleItems();
            int rows = VisibleRows();
            for (int r = 0; r < rows; r++)
            {
                int vi = _top + r;
                if (vi >= visible.Count) { break; }
                string id = visible[vi];
                TreeItem item = Find(id);
                if (item == null) { continue; }
                float top = inset + headingH + r * rowHeight;
                bool selected = _selection.Contains(id);
                if (selected)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = selBg;
                    paint.IsAntialias = false;
                    canvas.DrawRect(new SKRect(inset, top, Window.Width - inset, top + rowHeight), paint);
                }

                int depth = Depth(id);
                float indent = inset + 4 + depth * 16;
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Fill;
                paint.Color = selected ? SKColors.White : fg;

                // Expander triangle for parents.
                if (item.Children.Count > 0)
                {
                    canvas.DrawText(item.Open ? "▾" : "▸", indent - 14, top + 2 + metrics.Ascent,
                            SKTextAlign.Left, skFont, paint);
                }
                canvas.DrawText(item.Text, indent, top + 2 + metrics.Ascent, SKTextAlign.Left, skFont, paint);

                for (int c = 0; c < _columns.Count && c < item.Values.Count; c++)
                {
                    float cx = inset + treeWidth + c * ColumnWidth + 4;
                    canvas.DrawText(item.Values[c], cx, top + 2 + metrics.Ascent, SKTextAlign.Left, skFont, paint);
                }
            }
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    /// <summary>The visible item id at a window y coordinate, or null.</summary>
    /// <param name="y">The window-relative y.</param>
    /// <returns>The item id, or null.</returns>
    public string ItemAt(int y)
    {
        int row = (y - Inset - HeadingHeight) / RowHeight + _top;
        IReadOnlyList<string> visible = VisibleItems();
        return (row >= 0 && row < visible.Count) ? visible[row] : null;
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Treeview", "<ButtonPress-1>", OnPress);
        bindings.Bind("Treeview", "<MouseWheel>", OnWheel);
    }

    private static TreeviewWidget From(TkEvent e)
    {
        return (e.Window != null) ? e.Window.Widget as TreeviewWidget : null;
    }

    private static DispatchResult OnPress(TkEvent e)
    {
        TreeviewWidget tv = From(e);
        if (tv == null) { return DispatchResult.Continue; }
        tv.Window.Tree.SetFocus(tv.Window);
        string id = tv.ItemAt(e.Y);
        if (id == null) { return DispatchResult.Continue; }
        TreeItem item = tv.Find(id);

        // A click on the expander triangle toggles open/closed.
        int depth = tv.Depth(id);
        int triangleX = tv.Inset + 4 + depth * 16 - 14;
        if (item != null && item.Children.Count > 0 && e.X >= triangleX - 2 && e.X < triangleX + 14)
        {
            tv.SetOpen(id, !item.Open);
        }
        else
        {
            tv.SelectionSet(id);
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnWheel(TkEvent e)
    {
        TreeviewWidget tv = From(e);
        if (tv != null) { tv.YViewScroll(e.Delta > 0 ? -1 : 1, false); }
        return DispatchResult.Continue;
    }
}
