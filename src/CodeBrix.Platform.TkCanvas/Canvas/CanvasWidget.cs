using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The Tk <c>canvas</c> widget on Skia — a port of the widget engine in Tk
/// 8.6.16 tkCanvas.c over a retained display list of
/// <see cref="ICanvasItem"/>s. Provides the full consumer-relevant subcommand
/// surface (<c>create coords itemconfigure itemcget delete raise lower bbox
/// addtag gettags dtag find type move moveto scale canvasx canvasy xview
/// yview scan bind focus</c>), the view transform with
/// <c>-scrollregion</c>/<c>-confine</c>/scroll-increment semantics,
/// current-item picking with the <c>-closeenough</c> halo, and item-level
/// event bindings dispatched in Tk's order (<c>all</c>, then the item's tags
/// in order, then the item id). Deferred corners (<c>postscript</c>, canvas
/// text-item editing) accept and no-op per the toolkit's deferral
/// discipline. Subcommands are reachable both as typed methods and through
/// <see cref="Execute(IReadOnlyList{string})"/>, whose argument/result
/// shapes mirror the Tcl command byte-for-byte (the canvas oracle replays
/// real-wish scripts through it).
/// </summary>
public sealed class CanvasWidget : IWidget
{
    private static readonly Dictionary<string, Func<CanvasItem>> TypeRegistry = BuildTypeRegistry();

    private readonly List<CanvasItem> _items = new List<CanvasItem>();
    private readonly Dictionary<int, CanvasItem> _byId = new Dictionary<int, CanvasItem>();
    private int _nextId = 1;

    private int _xOrigin;
    private int _yOrigin;
    private int _scrollX1;
    private int _scrollY1;
    private int _scrollX2;
    private int _scrollY2;
    private bool _hasScrollRegion;
    private bool _confine = true;
    private double _closeEnough = 1.0;
    private int _xScrollIncrement;
    private int _yScrollIncrement;
    private int _scanX;
    private int _scanXOrigin;
    private int _scanY;
    private int _scanYOrigin;

    private CanvasItem _currentItem;
    private CanvasItem _newCurrentItem;
    private CanvasItem _focusItem;
    private bool _leftGrabbedItem;
    private bool _repickInProgress;
    private int _pickX;
    private int _pickY;
    private EventModifiers _pickState;

    /// <summary>
    /// Creates a canvas widget on <paramref name="window"/>: sets the window
    /// class to <c>Canvas</c>, hooks the widget-internal event handler used
    /// for item picking, and requests the default Tk canvas size.
    /// </summary>
    /// <param name="window">The window the widget owns.</param>
    public CanvasWidget(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        Window = window;
        window.ClassName = "Canvas";
        window.Widget = this;
        window.ClassEventHandler = HandleWindowEvent;

        Theming.OptionDatabase database = window.Tree.OptionDatabaseIfCreated;
        if (database != null && !database.IsEmpty)
        {
            database.ApplyTo(Options, window);
        }
        Configure(null);
    }

    /// <inheritdoc/>
    public TkWindow Window { get; }

    /// <inheritdoc/>
    public string ClassName
    {
        get { return "Canvas"; }
    }

    /// <inheritdoc/>
    public WidgetOptions Options { get; } = new WidgetOptions();

    /// <summary>The item currently under the pointer (the <c>current</c> tag holder), or null.</summary>
    public ICanvasItem CurrentItem
    {
        get { return _currentItem; }
    }

    /// <summary>The item holding the canvas keyboard focus (<c>focus</c> subcommand), or null.</summary>
    public ICanvasItem FocusItem
    {
        get { return _focusItem; }
    }

    /// <summary>The items in display order (bottom first).</summary>
    public IReadOnlyList<ICanvasItem> Items
    {
        get { return _items; }
    }

    /// <summary>The canvas-wide <c>-state</c> (items with no own state follow it).</summary>
    public CanvasItemState CanvasState { get; private set; } = CanvasItemState.Normal;

    /// <summary>The canvas x view origin: the canvas coordinate at the window's left edge.</summary>
    public int XOrigin
    {
        get { return _xOrigin; }
    }

    /// <summary>The canvas y view origin: the canvas coordinate at the window's top edge.</summary>
    public int YOrigin
    {
        get { return _yOrigin; }
    }

    /// <summary>
    /// The item-level binding table: bind tags here are canvas item tags and
    /// item ids (as decimal strings), plus <c>all</c> — NOT window path
    /// names. Populated via <see cref="BindItem"/>.
    /// </summary>
    public BindingTable ItemBindings { get; } = new BindingTable();

    /// <summary>
    /// Raised when the horizontal scroll fractions change — the toolkit
    /// surface behind <c>-xscrollcommand</c> (first and last fraction).
    /// </summary>
    public event Action<double, double> XScrollChanged;

    /// <summary>The vertical counterpart of <see cref="XScrollChanged"/> (<c>-yscrollcommand</c>).</summary>
    public event Action<double, double> YScrollChanged;

    /// <summary>
    /// Registers an additional canvas item type (the seam the remaining Tk
    /// item types slot into). The factory must return a fresh item instance.
    /// </summary>
    /// <param name="name">The Tk item type name, e.g. <c>oval</c>.</param>
    /// <param name="factory">The item factory.</param>
    public static void RegisterItemType(string name, Func<CanvasItem> factory)
    {
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("empty item type name", nameof(name)); }
        if (factory == null) { throw new ArgumentNullException(nameof(factory)); }
        TypeRegistry[name] = factory;
    }

    private static Dictionary<string, Func<CanvasItem>> BuildTypeRegistry()
    {
        return new Dictionary<string, Func<CanvasItem>>(StringComparer.Ordinal)
        {
            { "line", () => new LineItem() },
            { "rectangle", () => new RectangleItem() },
            { "polygon", () => new PolygonItem() },
            { "text", () => new TextItem() },
            { "oval", () => new OvalItem() },
            { "arc", () => new ArcItem() },
            { "bitmap", () => new BitmapItem() },
            { "image", () => new ImageItem() },
            { "window", () => new WindowItem() },
        };
    }

    private int Inset
    {
        get { return Options.GetInt("-borderwidth", 0) + Options.GetInt("-highlightthickness", 1); }
    }

    /// <inheritdoc/>
    public void Measure()
    {
        int width, height;
        if (!TclString.TryParsePixels(Options.Get("-width", "10c"), out width)) { width = 378; }
        if (!TclString.TryParsePixels(Options.Get("-height", "7c"), out height)) { height = 265; }
        Window.SetRequestedSize(width + 2 * Inset, height + 2 * Inset);
        Window.SetInternalBorder(Inset);
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

        _closeEnough = Options.GetDouble("-closeenough", 1.0);
        _confine = Options.GetBool("-confine", true);
        CanvasState = CanvasItem.ParseState(Options.Get("-state", "normal"));
        if (CanvasState == CanvasItemState.Default) { CanvasState = CanvasItemState.Normal; }

        int increment;
        _xScrollIncrement = TclString.TryParsePixels(Options.Get("-xscrollincrement", "0"), out increment) ? increment : 0;
        _yScrollIncrement = TclString.TryParsePixels(Options.Get("-yscrollincrement", "0"), out increment) ? increment : 0;

        // Recompute the scroll region (tkCanvas.c ConfigureCanvas).
        _scrollX1 = _scrollY1 = _scrollX2 = _scrollY2 = 0;
        _hasScrollRegion = false;
        string region = Options.Get("-scrollregion", "");
        if (region.Length > 0)
        {
            List<string> words = TclString.SplitList(region);
            int x1, y1, x2, y2;
            if (words.Count == 4
                    && TclString.TryParsePixels(words[0], out x1)
                    && TclString.TryParsePixels(words[1], out y1)
                    && TclString.TryParsePixels(words[2], out x2)
                    && TclString.TryParsePixels(words[3], out y2))
            {
                _scrollX1 = x1;
                _scrollY1 = y1;
                _scrollX2 = x2;
                _scrollY2 = y2;
                _hasScrollRegion = true;
            }
        }

        Measure();

        // Re-confine the origin (a no-op unless confine/scrollregion changed).
        SetOrigin(_xOrigin, _yOrigin);
        NotifyItemChanged();
    }

    /// <inheritdoc/>
    public bool HitTest(SKPoint point)
    {
        return true;
    }

    /// <inheritdoc/>
    public void Paint(SKCanvas canvas)
    {
#if PERFORMANCE_DIAGNOSIS
        long __probe = CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Now;
        try {
#endif
        SKColor background;
        if (!TkColor.TryParse(Options.Get("-background", Window.Tree.Theme.CanvasBackground), out background))
        {
            background = new SKColor(0xD9, 0xD9, 0xD9);
        }
        canvas.Clear(background);

        canvas.Save();
        canvas.Translate(-_xOrigin, -_yOrigin);
        foreach (CanvasItem item in _items)
        {
            if (item.EffectiveState == CanvasItemState.Hidden) { continue; }
            item.Paint(canvas);
        }
        canvas.Restore();
#if PERFORMANCE_DIAGNOSIS
        } finally { CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Add("canvas.PAINT", __probe); }
#endif
    }

    /// <summary>Schedules a repaint after item/view changes (coalesced by the scheduler).</summary>
    internal void NotifyItemChanged()
    {
        if (!Window.IsDestroyed)
        {
            Window.Tree.Scheduler.ScheduleRepaint();
        }
    }

    // ------------------------------------------------------------------
    // Tag searching
    // ------------------------------------------------------------------

    /// <summary>
    /// Enumerates the items matching a tag-or-id in display order: a decimal
    /// spec finds the one item with that id, <c>all</c> finds everything,
    /// anything else finds items carrying the tag.
    /// </summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <returns>The matching items, bottom-most first.</returns>
    public IEnumerable<ICanvasItem> FindWithTag(string tagOrId)
    {
        return Matching(tagOrId);
    }

    private IEnumerable<CanvasItem> Matching(string tagOrId)
    {
        int id;
        if (IsAllDigits(tagOrId) && int.TryParse(tagOrId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            CanvasItem byId;
            if (_byId.TryGetValue(id, out byId))
            {
                yield return byId;
            }
            yield break;
        }

        // A spec that uses tag-expression operators (&& || ^ ! ()) is a
        // boolean tag expression (tkCanvas.c TagSearchEvalExpr); a plain word
        // is a literal tag, and "all" matches everything.
        if (CanvasTagExpression.IsExpression(tagOrId))
        {
            CanvasTagExpression expr = CanvasTagExpression.Parse(tagOrId);
            foreach (CanvasItem item in _items.ToArray())
            {
                if (item.Canvas != this) { continue; }
                if (expr.Evaluate(item.HasTag))
                {
                    yield return item;
                }
            }
            yield break;
        }

        bool all = tagOrId == "all";
        // Snapshot: callers may delete items while iterating.
        foreach (CanvasItem item in _items.ToArray())
        {
            if (item.Canvas != this) { continue; } // deleted mid-iteration
            if (all || item.HasTag(tagOrId))
            {
                yield return item;
            }
        }
    }

    private CanvasItem FirstMatching(string tagOrId)
    {
        foreach (CanvasItem item in Matching(tagOrId))
        {
            return item;
        }
        return null;
    }

    private static bool IsAllDigits(string text)
    {
        if (string.IsNullOrEmpty(text)) { return false; }
        foreach (char c in text)
        {
            if (c < '0' || c > '9') { return false; }
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Item lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates an item — the <c>create</c> subcommand. The type name may be
    /// any unambiguous prefix (<c>rect</c>), like Tk.
    /// </summary>
    /// <param name="type">The item type name or unique prefix.</param>
    /// <param name="coords">The coordinates, x/y interleaved.</param>
    /// <param name="options">Initial options, or null.</param>
    /// <returns>The new item id.</returns>
    public int Create(string type, IReadOnlyList<double> coords,
            IReadOnlyDictionary<string, string> options = null)
    {
        Func<CanvasItem> factory = ResolveType(type);
        CanvasItem item = factory();
        item.Id = _nextId++;
        item.Canvas = this;

        // Configure BEFORE coords so options that shape geometry (width,
        // arrows, smoothing) are in place, then apply coordinates; mirrors
        // Tk's create path, where the item create proc handles both.
        item.Configure(options ?? new Dictionary<string, string>());
        item.SetCoords(coords);

        _items.Add(item);
        _byId[item.Id] = item;
        NotifyItemChanged();
        return item.Id;
    }

    private static Func<CanvasItem> ResolveType(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            throw new InvalidOperationException("unknown or ambiguous item type \"\"");
        }

        Func<CanvasItem> exact;
        if (TypeRegistry.TryGetValue(type, out exact)) { return exact; }

        Func<CanvasItem> match = null;
        foreach (KeyValuePair<string, Func<CanvasItem>> entry in TypeRegistry)
        {
            if (entry.Key.StartsWith(type, StringComparison.Ordinal))
            {
                if (match != null)
                {
                    throw new InvalidOperationException(
                            "unknown or ambiguous item type \"" + type + "\"");
                }
                match = entry.Value;
            }
        }
        if (match == null)
        {
            throw new InvalidOperationException(
                    "unknown or ambiguous item type \"" + type + "\"");
        }
        return match;
    }

    /// <summary>Deletes every item matching the specification — <c>delete</c>.</summary>
    /// <param name="tagOrId">The search specification.</param>
    public void Delete(string tagOrId)
    {
        foreach (CanvasItem item in Matching(tagOrId))
        {
            ItemBindings.RemoveTag(item.Id.ToString(CultureInfo.InvariantCulture));
            _byId.Remove(item.Id);
            _items.Remove(item);
            item.Canvas = null;
            if (ReferenceEquals(item, _currentItem)) { _currentItem = null; }
            if (ReferenceEquals(item, _newCurrentItem)) { _newCurrentItem = null; }
            if (ReferenceEquals(item, _focusItem)) { _focusItem = null; }
        }
        NotifyItemChanged();
    }

    // ------------------------------------------------------------------
    // Tags
    // ------------------------------------------------------------------

    /// <summary>The tags of the first matching item — <c>gettags</c>.</summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <returns>The tags in order, or an empty list.</returns>
    public IReadOnlyList<string> GetTags(string tagOrId)
    {
        CanvasItem item = FirstMatching(tagOrId);
        return (item != null) ? item.Tags : (IReadOnlyList<string>)new string[0];
    }

    /// <summary>Removes a tag from every matching item — <c>dtag</c>.</summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <param name="tagToDelete">The tag to delete (defaults to the search tag).</param>
    public void DeleteTag(string tagOrId, string tagToDelete = null)
    {
        string tag = tagToDelete ?? tagOrId;
        foreach (CanvasItem item in Matching(tagOrId))
        {
            item.RemoveTag(tag);
        }
    }

    // ------------------------------------------------------------------
    // Find / addtag (the search commands share their engine, like Tk)
    // ------------------------------------------------------------------

    /// <summary>All item ids in display order — <c>find all</c>.</summary>
    public IReadOnlyList<int> FindAll()
    {
        var result = new List<int>();
        foreach (CanvasItem item in _items) { result.Add(item.Id); }
        return result;
    }

    /// <summary>
    /// The item just above (after) the topmost item matching the
    /// specification — <c>find above</c>.
    /// </summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <returns>The item, or null.</returns>
    public ICanvasItem FindAbove(string tagOrId)
    {
        CanvasItem last = null;
        foreach (CanvasItem item in Matching(tagOrId)) { last = item; }
        if (last == null) { return null; }
        int index = _items.IndexOf(last);
        return (index >= 0 && index + 1 < _items.Count) ? _items[index + 1] : null;
    }

    /// <summary>
    /// The item just below (before) the bottom-most item matching the
    /// specification — <c>find below</c>.
    /// </summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <returns>The item, or null.</returns>
    public ICanvasItem FindBelow(string tagOrId)
    {
        CanvasItem first = FirstMatching(tagOrId);
        if (first == null) { return null; }
        int index = _items.IndexOf(first);
        return (index > 0) ? _items[index - 1] : null;
    }

    /// <summary>
    /// The item closest to a point — <c>find closest</c>, the exact circular
    /// search of tkCanvas.c: ties go to the topmost item, the halo defaults
    /// to 0, and <paramref name="startTagOrId"/> starts the search after a
    /// given item for cycling through overlapping items.
    /// </summary>
    /// <param name="x">The point x (canvas coordinates).</param>
    /// <param name="y">The point y.</param>
    /// <param name="halo">The distance tolerance (0 = exact).</param>
    /// <param name="startTagOrId">The item to start after, or null.</param>
    /// <returns>The closest item, or null when the canvas is empty.</returns>
    public ICanvasItem FindClosest(double x, double y, double halo = 0.0, string startTagOrId = null)
    {
        if (_items.Count == 0) { return null; }

        int startIndex = 0;
        if (startTagOrId != null)
        {
            CanvasItem startItem = FirstMatching(startTagOrId);
            if (startItem != null)
            {
                int index = _items.IndexOf(startItem);
                if (index >= 0) { startIndex = index; }
            }
        }

        // Skip leading hidden items, like the C loop.
        int firstVisible = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            CanvasItem candidate = _items[(startIndex + i) % _items.Count];
            if (i > 0 && (startIndex + i) % _items.Count == startIndex) { break; }
            if (!IsHiddenForSearch(candidate))
            {
                firstVisible = (startIndex + i) % _items.Count;
                break;
            }
        }
        if (firstVisible < 0) { return null; }

        CanvasItem closest = _items[firstVisible];
        double closestDist = PointDistance(closest, x, y, halo);
        int cursor = firstVisible;

        while (true)
        {
            double x1 = x - closestDist - halo - 1;
            double y1 = y - closestDist - halo - 1;
            double x2 = x + closestDist + halo + 1;
            double y2 = y + closestDist + halo + 1;

            bool improved = false;
            while (true)
            {
                cursor = (cursor + 1) % _items.Count;
                if (cursor == startIndex)
                {
                    return closest;
                }
                CanvasItem item = _items[cursor];
                if (IsHiddenForSearch(item)) { continue; }
                if ((item.X1 >= x2) || (item.X2 <= x1) || (item.Y1 >= y2) || (item.Y2 <= y1))
                {
                    continue;
                }
                double newDist = PointDistance(item, x, y, halo);
                if (newDist <= closestDist)
                {
                    closestDist = newDist;
                    closest = item;
                    improved = true;
                    break;
                }
            }
            if (!improved) { return closest; }
        }
    }

    private static double PointDistance(CanvasItem item, double x, double y, double halo)
    {
        double dist = item.DistanceTo(x, y) - halo;
        return (dist < 0.0) ? 0.0 : dist;
    }

    private bool IsHiddenForSearch(CanvasItem item)
    {
        return item.EffectiveState == CanvasItemState.Hidden;
    }

    /// <summary>
    /// The items overlapping (or enclosed by) a rectangle —
    /// <c>find overlapping</c>/<c>find enclosed</c>.
    /// </summary>
    /// <param name="x1">One corner x.</param>
    /// <param name="y1">One corner y.</param>
    /// <param name="x2">The opposite corner x.</param>
    /// <param name="y2">The opposite corner y.</param>
    /// <param name="enclosedOnly">True for <c>enclosed</c> semantics.</param>
    /// <returns>The matching item ids in display order.</returns>
    public IReadOnlyList<int> FindArea(double x1, double y1, double x2, double y2, bool enclosedOnly)
    {
        var rect = new double[4];
        rect[0] = Math.Min(x1, x2);
        rect[1] = Math.Min(y1, y2);
        rect[2] = Math.Max(x1, x2);
        rect[3] = Math.Max(y1, y2);

        int qx1 = (int)(rect[0] - 1.0);
        int qy1 = (int)(rect[1] - 1.0);
        int qx2 = (int)(rect[2] + 1.0);
        int qy2 = (int)(rect[3] + 1.0);

        int threshold = enclosedOnly ? 1 : 0;
        var result = new List<int>();
        foreach (CanvasItem item in _items)
        {
            if (IsHiddenForSearch(item)) { continue; }
            if ((item.X1 >= qx2) || (item.X2 <= qx1) || (item.Y1 >= qy2) || (item.Y2 <= qy1))
            {
                continue;
            }
            if (item.AreaTest(rect) >= threshold)
            {
                result.Add(item.Id);
            }
        }
        return result;
    }

    /// <summary>Adds a tag to every item a find-style search selects — <c>addtag</c>.</summary>
    /// <param name="tag">The tag to add.</param>
    /// <param name="items">The items the search selected.</param>
    public void AddTagToItems(string tag, IEnumerable<ICanvasItem> items)
    {
        foreach (ICanvasItem item in items)
        {
            ((CanvasItem)item).AddTag(tag);
        }
    }

    // ------------------------------------------------------------------
    // Display order
    // ------------------------------------------------------------------

    /// <summary>
    /// Raises the matching items to the top, or just above a reference item
    /// — <c>raise</c>. Relative order among the moved items is preserved.
    /// </summary>
    /// <param name="tagOrId">The items to move.</param>
    /// <param name="aboveThis">The reference item spec, or null for the very top.</param>
    public void RaiseItems(string tagOrId, string aboveThis = null)
    {
        CanvasItem reference;
        if (aboveThis == null)
        {
            reference = (_items.Count > 0) ? _items[_items.Count - 1] : null;
        }
        else
        {
            reference = null;
            foreach (CanvasItem item in Matching(aboveThis)) { reference = item; }
            if (reference == null)
            {
                throw new InvalidOperationException(
                        "tagOrId \"" + aboveThis + "\" doesn't match any items");
            }
        }
        Relink(tagOrId, reference);
    }

    /// <summary>
    /// Lowers the matching items to the bottom, or just below a reference
    /// item — <c>lower</c>.
    /// </summary>
    /// <param name="tagOrId">The items to move.</param>
    /// <param name="belowThis">The reference item spec, or null for the very bottom.</param>
    public void LowerItems(string tagOrId, string belowThis = null)
    {
        CanvasItem reference = null;
        if (belowThis != null)
        {
            CanvasItem below = FirstMatching(belowThis);
            if (below == null)
            {
                throw new InvalidOperationException(
                        "tagOrId \"" + belowThis + "\" doesn't match any items");
            }
            int index = _items.IndexOf(below);
            reference = (index > 0) ? _items[index - 1] : null;
        }
        Relink(tagOrId, reference);
    }

    /// <summary>
    /// Moves the matching items (in their current relative order) so they
    /// sit just after <paramref name="reference"/> (null = the very bottom)
    /// — the exact port of Tk's RelinkItems, including the guard that
    /// switches the insertion point to its predecessor when the reference
    /// item is itself being moved.
    /// </summary>
    private void Relink(string tagOrId, CanvasItem reference)
    {
        var moving = new List<CanvasItem>();
        foreach (CanvasItem item in Matching(tagOrId))
        {
            if (ReferenceEquals(item, reference))
            {
                int index = _items.IndexOf(reference);
                reference = (index > 0) ? _items[index - 1] : null;
            }
            _items.Remove(item);
            moving.Add(item);
        }
        if (moving.Count == 0) { return; }

        int insertAt = (reference == null) ? 0 : _items.IndexOf(reference) + 1;
        _items.InsertRange(insertAt, moving);
        NotifyItemChanged();
    }

    // ------------------------------------------------------------------
    // Geometry commands
    // ------------------------------------------------------------------

    /// <summary>The union of the matching items' header boxes — <c>bbox</c>.</summary>
    /// <param name="tagOrIds">One or more search specifications.</param>
    /// <returns>The box, or null when nothing matched (or all empty).</returns>
    public SKRectI? BBox(params string[] tagOrIds)
    {
        bool gotAny = false;
        int x1 = 0, y1 = 0, x2 = 0, y2 = 0;
        foreach (string spec in tagOrIds)
        {
            foreach (CanvasItem item in Matching(spec))
            {
                if ((item.X1 >= item.X2) || (item.Y1 >= item.Y2)) { continue; }
                if (!gotAny)
                {
                    x1 = item.X1;
                    y1 = item.Y1;
                    x2 = item.X2;
                    y2 = item.Y2;
                    gotAny = true;
                }
                else
                {
                    if (item.X1 < x1) { x1 = item.X1; }
                    if (item.Y1 < y1) { y1 = item.Y1; }
                    if (item.X2 > x2) { x2 = item.X2; }
                    if (item.Y2 > y2) { y2 = item.Y2; }
                }
            }
        }
        if (!gotAny) { return null; }
        return new SKRectI(x1, y1, x2, y2);
    }

    /// <summary>Moves every matching item by a delta — <c>move</c>.</summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <param name="dx">The x delta.</param>
    /// <param name="dy">The y delta.</param>
    public void Move(string tagOrId, double dx, double dy)
    {
        foreach (CanvasItem item in Matching(tagOrId))
        {
            item.Translate(dx, dy);
        }
        NotifyItemChanged();
    }

    /// <summary>
    /// Moves the matching items so the FIRST matching item's header box
    /// corner lands at the given position — <c>moveto</c>. A null coordinate
    /// leaves that axis unchanged (Tk's empty-string argument).
    /// </summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <param name="x">The target x, or null.</param>
    /// <param name="y">The target y, or null.</param>
    public void MoveTo(string tagOrId, double? x, double? y)
    {
        CanvasItem first = FirstMatching(tagOrId);
        if (first == null) { return; }

        double dx = x.HasValue ? x.Value - first.X1 : 0;
        double dy = y.HasValue ? y.Value - first.Y1 : 0;
        Move(tagOrId, dx, dy);
    }

    /// <summary>Scales every matching item about an origin — <c>scale</c>.</summary>
    /// <param name="tagOrId">The search specification.</param>
    /// <param name="originX">The origin x.</param>
    /// <param name="originY">The origin y.</param>
    /// <param name="scaleX">The x factor (must not be 0).</param>
    /// <param name="scaleY">The y factor (must not be 0).</param>
    public void ScaleItems(string tagOrId, double originX, double originY, double scaleX, double scaleY)
    {
        if (scaleX == 0.0 || scaleY == 0.0)
        {
            throw new InvalidOperationException("scale factor cannot be zero");
        }
        foreach (CanvasItem item in Matching(tagOrId))
        {
            item.Scale(originX, originY, scaleX, scaleY);
        }
        NotifyItemChanged();
    }

    // ------------------------------------------------------------------
    // View transform / scrolling
    // ------------------------------------------------------------------

    /// <summary>Converts a window x coordinate to a canvas coordinate — <c>canvasx</c>.</summary>
    /// <param name="screenX">The window-relative x in pixels.</param>
    /// <param name="gridSpacing">Optional grid to round to (≤ 0 = none).</param>
    /// <returns>The canvas coordinate.</returns>
    public double CanvasX(int screenX, double gridSpacing = 0.0)
    {
        return GridAlign(screenX + _xOrigin, gridSpacing);
    }

    /// <summary>Converts a window y coordinate to a canvas coordinate — <c>canvasy</c>.</summary>
    /// <param name="screenY">The window-relative y in pixels.</param>
    /// <param name="gridSpacing">Optional grid to round to (≤ 0 = none).</param>
    /// <returns>The canvas coordinate.</returns>
    public double CanvasY(int screenY, double gridSpacing = 0.0)
    {
        return GridAlign(screenY + _yOrigin, gridSpacing);
    }

    private static double GridAlign(double coord, double spacing)
    {
        if (spacing <= 0.0) { return coord; }
        if (coord < 0)
        {
            return -((int)((-coord) / spacing + 0.5)) * spacing;
        }
        return ((int)(coord / spacing + 0.5)) * spacing;
    }

    /// <summary>The horizontal scroll fractions — <c>xview</c> with no arguments.</summary>
    /// <param name="first">The fraction of the region left of the view.</param>
    /// <param name="last">The fraction at the right edge of the view.</param>
    public void XViewFractions(out double first, out double last)
    {
        ScrollFractions(_xOrigin + Inset, _xOrigin + Window.Width - Inset,
                _scrollX1, _scrollX2, out first, out last);
    }

    /// <summary>The vertical scroll fractions — <c>yview</c> with no arguments.</summary>
    /// <param name="first">The fraction of the region above the view.</param>
    /// <param name="last">The fraction at the bottom edge of the view.</param>
    public void YViewFractions(out double first, out double last)
    {
        ScrollFractions(_yOrigin + Inset, _yOrigin + Window.Height - Inset,
                _scrollY1, _scrollY2, out first, out last);
    }

    private static void ScrollFractions(int screen1, int screen2, int object1, int object2,
            out double f1, out double f2)
    {
        double range = object2 - object1;
        if (range <= 0)
        {
            f1 = 0;
            f2 = 1.0;
        }
        else
        {
            f1 = (screen1 - object1) / range;
            if (f1 < 0) { f1 = 0.0; }
            f2 = (screen2 - object1) / range;
            if (f2 > 1.0) { f2 = 1.0; }
            if (f2 < f1) { f2 = f1; }
        }
    }

    /// <summary>Scrolls so the given fraction of the region is left of the view — <c>xview moveto</c>.</summary>
    /// <param name="fraction">The target fraction.</param>
    public void XViewMoveTo(double fraction)
    {
        int newX = _scrollX1 - Inset + (int)(fraction * (_scrollX2 - _scrollX1) + 0.5);
        SetOrigin(newX, _yOrigin);
    }

    /// <summary>Scrolls horizontally by units or pages — <c>xview scroll</c>.</summary>
    /// <param name="count">The signed count.</param>
    /// <param name="pages">True for pages (90% of a window), false for units.</param>
    public void XViewScroll(int count, bool pages)
    {
        int newX;
        if (pages)
        {
            newX = (int)(_xOrigin + count * 0.9 * (Window.Width - 2 * Inset));
        }
        else if (_xScrollIncrement > 0)
        {
            newX = _xOrigin + count * _xScrollIncrement;
        }
        else
        {
            newX = (int)(_xOrigin + count * 0.1 * (Window.Width - 2 * Inset));
        }
        SetOrigin(newX, _yOrigin);
    }

    /// <summary>The vertical counterpart of <see cref="XViewMoveTo"/> — <c>yview moveto</c>.</summary>
    /// <param name="fraction">The target fraction.</param>
    public void YViewMoveTo(double fraction)
    {
        int newY = _scrollY1 - Inset + (int)(fraction * (_scrollY2 - _scrollY1) + 0.5);
        SetOrigin(_xOrigin, newY);
    }

    /// <summary>The vertical counterpart of <see cref="XViewScroll"/> — <c>yview scroll</c>.</summary>
    /// <param name="count">The signed count.</param>
    /// <param name="pages">True for pages, false for units.</param>
    public void YViewScroll(int count, bool pages)
    {
        int newY;
        if (pages)
        {
            newY = (int)(_yOrigin + count * 0.9 * (Window.Height - 2 * Inset));
        }
        else if (_yScrollIncrement > 0)
        {
            newY = _yOrigin + count * _yScrollIncrement;
        }
        else
        {
            newY = (int)(_yOrigin + count * 0.1 * (Window.Height - 2 * Inset));
        }
        SetOrigin(_xOrigin, newY);
    }

    /// <summary>Records the start of a middle-drag pan — <c>scan mark</c>.</summary>
    /// <param name="x">The pointer x.</param>
    /// <param name="y">The pointer y.</param>
    public void ScanMark(int x, int y)
    {
        _scanX = x;
        _scanXOrigin = _xOrigin;
        _scanY = y;
        _scanYOrigin = _yOrigin;
    }

    /// <summary>Pans relative to the last mark, amplified by the gain — <c>scan dragto</c>.</summary>
    /// <param name="x">The pointer x.</param>
    /// <param name="y">The pointer y.</param>
    /// <param name="gain">The amplification (Tk defaults to 10).</param>
    public void ScanDragTo(int x, int y, int gain = 10)
    {
        int tmp = _scanXOrigin - gain * (x - _scanX) - _scrollX1;
        int newXOrigin = _scrollX1 + tmp;
        tmp = _scanYOrigin - gain * (y - _scanY) - _scrollY1;
        int newYOrigin = _scrollY1 + tmp;
        SetOrigin(newXOrigin, newYOrigin);
    }

    /// <summary>
    /// Sets the view origin with Tk's scroll-increment rounding and
    /// confine-to-scrollregion clamping (tkCanvas.c CanvasSetOrigin).
    /// </summary>
    /// <param name="xOrigin">The new x origin.</param>
    /// <param name="yOrigin">The new y origin.</param>
    public void SetOrigin(int xOrigin, int yOrigin)
    {
        int inset = Inset;

        if (_xScrollIncrement > 0)
        {
            if (xOrigin >= 0)
            {
                xOrigin += _xScrollIncrement / 2;
                xOrigin -= (xOrigin + inset) % _xScrollIncrement;
            }
            else
            {
                xOrigin = (-xOrigin) + _xScrollIncrement / 2;
                xOrigin = -(xOrigin - (xOrigin - inset) % _xScrollIncrement);
            }
        }
        if (_yScrollIncrement > 0)
        {
            if (yOrigin >= 0)
            {
                yOrigin += _yScrollIncrement / 2;
                yOrigin -= (yOrigin + inset) % _yScrollIncrement;
            }
            else
            {
                yOrigin = (-yOrigin) + _yScrollIncrement / 2;
                yOrigin = -(yOrigin - (yOrigin - inset) % _yScrollIncrement);
            }
        }

        if (_confine && _hasScrollRegion)
        {
            int left = xOrigin + inset - _scrollX1;
            int right = _scrollX2 - (xOrigin + Window.Width - inset);
            int top = yOrigin + inset - _scrollY1;
            int bottom = _scrollY2 - (yOrigin + Window.Height - inset);
            int delta;

            if ((left < 0) && (right > 0))
            {
                delta = (right > -left) ? -left : right;
                if (_xScrollIncrement > 0) { delta -= delta % _xScrollIncrement; }
                xOrigin += delta;
            }
            else if ((right < 0) && (left > 0))
            {
                delta = (left > -right) ? -right : left;
                if (_xScrollIncrement > 0) { delta -= delta % _xScrollIncrement; }
                xOrigin -= delta;
            }
            if ((top < 0) && (bottom > 0))
            {
                delta = (bottom > -top) ? -top : bottom;
                if (_yScrollIncrement > 0) { delta -= delta % _yScrollIncrement; }
                yOrigin += delta;
            }
            else if ((bottom < 0) && (top > 0))
            {
                delta = (top > -bottom) ? -bottom : top;
                if (_yScrollIncrement > 0) { delta -= delta % _yScrollIncrement; }
                yOrigin -= delta;
            }
        }

        if ((xOrigin == _xOrigin) && (yOrigin == _yOrigin)) { return; }

        _xOrigin = xOrigin;
        _yOrigin = yOrigin;
        NotifyItemChanged();
        UpdateScrollNotifications();
    }

    private void UpdateScrollNotifications()
    {
        Action<double, double> xHandler = XScrollChanged;
        if (xHandler != null)
        {
            double first, last;
            XViewFractions(out first, out last);
            xHandler(first, last);
        }
        Action<double, double> yHandler = YScrollChanged;
        if (yHandler != null)
        {
            double first, last;
            YViewFractions(out first, out last);
            yHandler(first, last);
        }
    }

    // ------------------------------------------------------------------
    // Item bindings + current-item picking
    // ------------------------------------------------------------------

    /// <summary>
    /// Binds a handler to an event pattern on an item tag or id —
    /// <c>$canvas bind tagOrId sequence</c>. Only pointer, key, Enter/Leave,
    /// and virtual patterns are legal on items, like Tk.
    /// </summary>
    /// <param name="tagOrId">The item tag or decimal id.</param>
    /// <param name="pattern">The event pattern, e.g. <c>&lt;ButtonPress-1&gt;</c>.</param>
    /// <param name="handler">The handler (null removes the binding).</param>
    public void BindItem(string tagOrId, string pattern, TkEventHandler handler)
    {
        if (handler == null)
        {
            ItemBindings.Unbind(tagOrId, pattern);
            return;
        }
        ItemBindings.Bind(tagOrId, pattern, handler);
    }

    /// <summary>Sets or clears the canvas focus item — the <c>focus</c> subcommand.</summary>
    /// <param name="tagOrId">The item spec, or null/empty to clear.</param>
    public void SetFocusItem(string tagOrId)
    {
        if (string.IsNullOrEmpty(tagOrId))
        {
            _focusItem = null;
            return;
        }
        CanvasItem item = FirstMatching(tagOrId);
        if (item != null) { _focusItem = item; }
    }

    /// <summary>
    /// The widget-internal event hook (the CanvasBindProc port): tracks the
    /// button state, repicks the current item at the right moment relative
    /// to the event, and dispatches item bindings.
    /// </summary>
    private DispatchResult HandleWindowEvent(TkEvent tkEvent)
    {
        switch (tkEvent.Type)
        {
            case TkEventType.ButtonPress:
            {
                // Repick with the pre-press state, then deliver the press.
                PickCurrentItem(tkEvent, tkEvent.State & ~ButtonFlag(tkEvent.Button));
                DoItemEvent(tkEvent);
                break;
            }
            case TkEventType.ButtonRelease:
            {
                // Deliver with the button still down, then repick as if up.
                DoItemEvent(tkEvent);
                PickCurrentItem(tkEvent, tkEvent.State & ~ButtonFlag(tkEvent.Button));
                break;
            }
            case TkEventType.Enter:
            case TkEventType.Leave:
            {
                PickCurrentItem(tkEvent, tkEvent.State);
                break;
            }
            case TkEventType.Motion:
            {
                PickCurrentItem(tkEvent, tkEvent.State);
                DoItemEvent(tkEvent);
                break;
            }
            default:
            {
                DoItemEvent(tkEvent);
                break;
            }
        }
        return DispatchResult.Continue;
    }

    private static EventModifiers ButtonFlag(int button)
    {
        switch (button)
        {
            case 1: return EventModifiers.Button1;
            case 2: return EventModifiers.Button2;
            case 3: return EventModifiers.Button3;
            case 4: return EventModifiers.Button4;
            case 5: return EventModifiers.Button5;
            default: return EventModifiers.None;
        }
    }

    private const EventModifiers AllButtons =
            EventModifiers.Button1 | EventModifiers.Button2 | EventModifiers.Button3
            | EventModifiers.Button4 | EventModifiers.Button5;

    /// <summary>
    /// The PickCurrentItem port: finds the topmost item within
    /// <c>-closeenough</c> of the pointer, defers repicks while a button is
    /// held (Tk's LEFT_GRABBED_ITEM), synthesizes item Enter/Leave events,
    /// and moves the <c>current</c> tag.
    /// </summary>
    private void PickCurrentItem(TkEvent tkEvent, EventModifiers effectiveState)
    {
        bool buttonDown = (effectiveState & AllButtons) != 0;

        if (tkEvent.Type != TkEventType.Leave)
        {
            _pickX = tkEvent.X;
            _pickY = tkEvent.Y;
            _pickState = effectiveState;
        }

        if (_repickInProgress) { return; }

        if (tkEvent.Type != TkEventType.Leave)
        {
            double cx = _pickX + _xOrigin;
            double cy = _pickY + _yOrigin;
            _newCurrentItem = FindClosestForPick(cx, cy);
        }
        else
        {
            _newCurrentItem = null;
        }

        if (ReferenceEquals(_newCurrentItem, _currentItem) && !_leftGrabbedItem)
        {
            return;
        }

        if (!buttonDown) { _leftGrabbedItem = false; }

        if (!ReferenceEquals(_newCurrentItem, _currentItem) && _currentItem != null
                && !_leftGrabbedItem)
        {
            CanvasItem leaving = _currentItem;
            var leaveEvent = new TkEvent
            {
                Type = TkEventType.Leave,
                Window = Window,
                X = _pickX,
                Y = _pickY,
                State = _pickState,
                KeySym = string.Empty,
                Character = string.Empty,
            };
            _repickInProgress = true;
            try
            {
                DoItemEvent(leaveEvent, leaving);
            }
            finally
            {
                _repickInProgress = false;
            }

            if (ReferenceEquals(leaving, _currentItem) && !buttonDown)
            {
                leaving.RemoveTag("current");
            }
        }

        if (!ReferenceEquals(_newCurrentItem, _currentItem) && buttonDown)
        {
            _leftGrabbedItem = true;
            return;
        }

        _leftGrabbedItem = false;
        _currentItem = _newCurrentItem;

        if (_currentItem != null)
        {
            _currentItem.AddTag("current");
            var enterEvent = new TkEvent
            {
                Type = TkEventType.Enter,
                Window = Window,
                X = _pickX,
                Y = _pickY,
                State = _pickState,
                KeySym = string.Empty,
                Character = string.Empty,
            };
            DoItemEvent(enterEvent, _currentItem);
        }
    }

    /// <summary>
    /// The CanvasFindClosest port used by picking: the TOPMOST item whose
    /// distance is within <c>-closeenough</c> (hidden and disabled items are
    /// skipped), or null.
    /// </summary>
    private CanvasItem FindClosestForPick(double x, double y)
    {
        int x1 = (int)(x - _closeEnough);
        int y1 = (int)(y - _closeEnough);
        int x2 = (int)(x + _closeEnough);
        int y2 = (int)(y + _closeEnough);

        CanvasItem best = null;
        foreach (CanvasItem item in _items)
        {
            CanvasItemState state = item.EffectiveState;
            if (state == CanvasItemState.Hidden || state == CanvasItemState.Disabled)
            {
                continue;
            }
            if ((item.X1 > x2) || (item.X2 < x1) || (item.Y1 > y2) || (item.Y2 < y1))
            {
                continue;
            }
            if (item.DistanceTo(x, y) <= _closeEnough)
            {
                best = item;
            }
        }
        return best;
    }

    /// <summary>
    /// The CanvasDoEvent port: dispatches an event to an item's bindings in
    /// Tk's order — the <c>all</c> tag first, then the item's tags in order,
    /// then the item id — firing at most one (most specific) binding per
    /// tag, stopping on <see cref="DispatchResult.Break"/>.
    /// </summary>
    private void DoItemEvent(TkEvent tkEvent, CanvasItem explicitItem = null)
    {
        CanvasItem item = explicitItem;
        if (item == null)
        {
            item = (tkEvent.Type == TkEventType.KeyPress || tkEvent.Type == TkEventType.KeyRelease)
                    ? _focusItem : _currentItem;
        }
        if (item == null) { return; }

        var bindTags = new List<string> { "all" };
        foreach (string tag in item.Tags) { bindTags.Add(tag); }
        bindTags.Add(item.Id.ToString(CultureInfo.InvariantCulture));

        foreach (string tag in bindTags)
        {
            TkEventHandler handler = ItemBindings.FindBest(tag, tkEvent);
            if (handler == null) { continue; }
            if (handler(tkEvent) == DispatchResult.Break) { break; }
        }
    }

    // ------------------------------------------------------------------
    // The string command surface (the Tcl-facing shape; oracle-replayable)
    // ------------------------------------------------------------------

    /// <summary>
    /// Executes one canvas widget command given its words (the Tcl command's
    /// arguments after the widget name) and returns the Tcl result text.
    /// Argument and result shapes mirror <c>tkCanvas.c</c> so a script
    /// transcript captured from real wish replays identically. Errors throw
    /// <see cref="InvalidOperationException"/> carrying the Tk message.
    /// </summary>
    /// <param name="words">The subcommand and its arguments.</param>
    /// <returns>The command result text (empty for action commands).</returns>
    public string Execute(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            throw new InvalidOperationException("wrong # args: should be \"canvas option ?arg ...?\"");
        }

        string subcommand = words[0];
#if PERFORMANCE_DIAGNOSIS
        long __probe = CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Now;
        try
        {
#endif
        switch (subcommand)
        {
            case "create":
            {
                if (words.Count < 3)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas create type coords ?arg ...?\"");
                }

                // Coordinates run until the first word that looks like an
                // option (a dash followed by a letter) — Tk's create scan.
                // A single list argument may also carry all coordinates.
                var coordWords = new List<string>();
                int index = 2;
                while (index < words.Count && !LooksLikeOption(words[index]))
                {
                    coordWords.Add(words[index]);
                    index++;
                }
                if (coordWords.Count == 1 && coordWords[0].IndexOf(' ') >= 0)
                {
                    coordWords = TclString.SplitList(coordWords[0]);
                }

                var coords = new List<double>();
                foreach (string word in coordWords)
                {
                    double value;
                    if (!TclString.TryParseCoord(word, out value))
                    {
                        throw new InvalidOperationException("bad coordinate \"" + word + "\"");
                    }
                    coords.Add(value);
                }

                Dictionary<string, string> options = ParseOptionWords(words, index);
                int id;
                try
                {
                    id = Create(words[1], coords, options);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidOperationException(e.Message);
                }
                return id.ToString(CultureInfo.InvariantCulture);
            }
            case "coords":
            {
                if (words.Count < 2)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas coords tagOrId ?x y x y ...?\"");
                }
                CanvasItem item = FirstMatching(words[1]);
                if (item == null) { return string.Empty; }

                if (words.Count == 2)
                {
                    IReadOnlyList<double> coords = item.GetCoords();
                    var parts = new List<string>();
                    foreach (double value in coords) { parts.Add(TclString.FormatDouble(value)); }
                    return string.Join(" ", parts);
                }

                var coordWords = new List<string>();
                for (int i = 2; i < words.Count; i++) { coordWords.Add(words[i]); }
                if (coordWords.Count == 1)
                {
                    coordWords = TclString.SplitList(coordWords[0]);
                }
                var newCoords = new List<double>();
                foreach (string word in coordWords)
                {
                    double value;
                    if (!TclString.TryParseCoord(word, out value))
                    {
                        throw new InvalidOperationException("bad coordinate \"" + word + "\"");
                    }
                    newCoords.Add(value);
                }
                try
                {
                    item.SetCoords(newCoords);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidOperationException(e.Message);
                }
                return string.Empty;
            }
            case "itemconfigure":
            {
                if (words.Count < 2)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas itemconfigure tagOrId ?-option value ...?\"");
                }
                Dictionary<string, string> options = ParseOptionWords(words, 2);
                foreach (CanvasItem item in Matching(words[1]))
                {
                    item.Configure(options);
                }
                return string.Empty;
            }
            case "itemcget":
            {
                if (words.Count != 3)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas itemcget tagOrId option\"");
                }
                CanvasItem item = FirstMatching(words[1]);
                if (item == null)
                {
                    throw new InvalidOperationException("tagOrId \"" + words[1] + "\" doesn't match any items");
                }
                return item.Options.Get(words[2], DefaultOptionValue(item, words[2]));
            }
            case "delete":
            {
                for (int i = 1; i < words.Count; i++) { Delete(words[i]); }
                return string.Empty;
            }
            case "raise":
            {
                RaiseItems(words[1], (words.Count > 2) ? words[2] : null);
                return string.Empty;
            }
            case "lower":
            {
                LowerItems(words[1], (words.Count > 2) ? words[2] : null);
                return string.Empty;
            }
            case "bbox":
            {
                if (words.Count < 2)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas bbox tagOrId ?tagOrId ...?\"");
                }
                var specs = new string[words.Count - 1];
                for (int i = 1; i < words.Count; i++) { specs[i - 1] = words[i]; }
                SKRectI? box = BBox(specs);
                if (!box.HasValue) { return string.Empty; }
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}",
                        box.Value.Left, box.Value.Top, box.Value.Right, box.Value.Bottom);
            }
            case "gettags":
            {
                return TclString.JoinList(GetTags(words[1]));
            }
            case "dtag":
            {
                DeleteTag(words[1], (words.Count > 2) ? words[2] : null);
                return string.Empty;
            }
            case "addtag":
            {
                if (words.Count < 3)
                {
                    throw new InvalidOperationException("wrong # args: should be \"canvas addtag tag searchCommand ?arg ...?\"");
                }
                string tag = words[1];
                var searchWords = new List<string>();
                for (int i = 2; i < words.Count; i++) { searchWords.Add(words[i]); }
                foreach (int id in RunSearch(searchWords))
                {
                    CanvasItem item;
                    if (_byId.TryGetValue(id, out item)) { item.AddTag(tag); }
                }
                return string.Empty;
            }
            case "find":
            {
                var searchWords = new List<string>();
                for (int i = 1; i < words.Count; i++) { searchWords.Add(words[i]); }
                var parts = new List<string>();
                foreach (int id in RunSearch(searchWords))
                {
                    parts.Add(id.ToString(CultureInfo.InvariantCulture));
                }
                return string.Join(" ", parts);
            }
            case "type":
            {
                CanvasItem item = FirstMatching(words[1]);
                return (item != null) ? item.TypeName : string.Empty;
            }
            case "move":
            {
                Move(words[1], ParseCoordWord(words[2]), ParseCoordWord(words[3]));
                return string.Empty;
            }
            case "moveto":
            {
                double? x = (words[2].Length == 0) ? (double?)null : ParseCoordWord(words[2]);
                double? y = (words[3].Length == 0) ? (double?)null : ParseCoordWord(words[3]);
                MoveTo(words[1], x, y);
                return string.Empty;
            }
            case "scale":
            {
                ScaleItems(words[1], ParseCoordWord(words[2]), ParseCoordWord(words[3]),
                        ParseDoubleWord(words[4]), ParseDoubleWord(words[5]));
                return string.Empty;
            }
            case "canvasx":
            {
                int screenX;
                if (!TclString.TryParsePixels(words[1], out screenX))
                {
                    throw new InvalidOperationException("bad screen distance \"" + words[1] + "\"");
                }
                double grid = (words.Count > 2) ? ParseCoordWord(words[2]) : 0.0;
                return TclString.FormatDouble(CanvasX(screenX, grid));
            }
            case "canvasy":
            {
                int screenY;
                if (!TclString.TryParsePixels(words[1], out screenY))
                {
                    throw new InvalidOperationException("bad screen distance \"" + words[1] + "\"");
                }
                double grid = (words.Count > 2) ? ParseCoordWord(words[2]) : 0.0;
                return TclString.FormatDouble(CanvasY(screenY, grid));
            }
            case "xview":
            {
                if (words.Count == 1)
                {
                    double first, last;
                    XViewFractions(out first, out last);
                    return TclString.FormatDouble(first) + " " + TclString.FormatDouble(last);
                }
                ApplyScrollWords(words, true);
                return string.Empty;
            }
            case "yview":
            {
                if (words.Count == 1)
                {
                    double first, last;
                    YViewFractions(out first, out last);
                    return TclString.FormatDouble(first) + " " + TclString.FormatDouble(last);
                }
                ApplyScrollWords(words, false);
                return string.Empty;
            }
            case "scan":
            {
                int x = int.Parse(words[2], CultureInfo.InvariantCulture);
                int y = int.Parse(words[3], CultureInfo.InvariantCulture);
                if (words[1] == "mark")
                {
                    ScanMark(x, y);
                }
                else
                {
                    int gain = (words.Count > 4)
                            ? int.Parse(words[4], CultureInfo.InvariantCulture) : 10;
                    ScanDragTo(x, y, gain);
                }
                return string.Empty;
            }
            case "focus":
            {
                if (words.Count == 1)
                {
                    return (_focusItem != null)
                            ? _focusItem.Id.ToString(CultureInfo.InvariantCulture) : string.Empty;
                }
                SetFocusItem(words[1]);
                return string.Empty;
            }
            case "cget":
            {
                return Options.Get(words[1], CanvasDefaultOptionValue(words[1]));
            }
            case "configure":
            {
                Configure(ParseOptionWords(words, 1));
                return string.Empty;
            }
            // Deferred corners (§3.20): accept and no-op, never throw.
            case "postscript":
            case "icursor":
            case "index":
            case "insert":
            case "dchars":
            case "rchars":
            case "imove":
            case "select":
            {
                return string.Empty;
            }
            default:
            {
                throw new InvalidOperationException(
                        "bad option \"" + subcommand + "\": must be a canvas widget command");
            }
        }
#if PERFORMANCE_DIAGNOSIS
        }
        finally
        {
            CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Add("canvas." + subcommand, __probe);
        }
#endif
    }

    /// <summary>
    /// Runs a find/addtag search-command word list and returns matching ids
    /// in display order (the shared FindItems engine).
    /// </summary>
    private IReadOnlyList<int> RunSearch(IReadOnlyList<string> words)
    {
        switch (words[0])
        {
            case "all":
            {
                return FindAll();
            }
            case "withtag":
            {
                var result = new List<int>();
                foreach (CanvasItem item in Matching(words[1])) { result.Add(item.Id); }
                return result;
            }
            case "above":
            {
                ICanvasItem above = FindAbove(words[1]);
                return (above != null) ? new List<int> { above.Id } : new List<int>();
            }
            case "below":
            {
                ICanvasItem below = FindBelow(words[1]);
                return (below != null) ? new List<int> { below.Id } : new List<int>();
            }
            case "closest":
            {
                double x = ParseCoordWord(words[1]);
                double y = ParseCoordWord(words[2]);
                double halo = 0.0;
                if (words.Count > 3)
                {
                    halo = ParseCoordWord(words[3]);
                    if (halo < 0)
                    {
                        throw new InvalidOperationException(
                                "can't have negative halo value \"" + words[3] + "\"");
                    }
                }
                string start = (words.Count > 4) ? words[4] : null;
                ICanvasItem closest = FindClosest(x, y, halo, start);
                return (closest != null) ? new List<int> { closest.Id } : new List<int>();
            }
            case "enclosed":
            case "overlapping":
            {
                return FindArea(
                        ParseCoordWord(words[1]), ParseCoordWord(words[2]),
                        ParseCoordWord(words[3]), ParseCoordWord(words[4]),
                        words[0] == "enclosed");
            }
            default:
            {
                throw new InvalidOperationException(
                        "bad search command \"" + words[0]
                        + "\": must be above, all, below, closest, enclosed, overlapping, or withtag");
            }
        }
    }

    private void ApplyScrollWords(IReadOnlyList<string> words, bool horizontal)
    {
        if (words[1] == "moveto")
        {
            double fraction = ParseDoubleWord(words[2]);
            if (horizontal) { XViewMoveTo(fraction); }
            else { YViewMoveTo(fraction); }
            return;
        }
        if (words[1] == "scroll")
        {
            int count = int.Parse(words[2], CultureInfo.InvariantCulture);
            bool pages = words[3].StartsWith("p", StringComparison.Ordinal);
            if (horizontal) { XViewScroll(count, pages); }
            else { YViewScroll(count, pages); }
            return;
        }
        throw new InvalidOperationException(
                "unknown option \"" + words[1] + "\": must be moveto or scroll");
    }

    private static bool LooksLikeOption(string word)
    {
        return word.Length >= 2 && word[0] == '-' && word[1] >= 'a' && word[1] <= 'z';
    }

    private static Dictionary<string, string> ParseOptionWords(IReadOnlyList<string> words, int start)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = start; i + 1 < words.Count; i += 2)
        {
            options[words[i]] = words[i + 1];
        }
        return options;
    }

    private static double ParseCoordWord(string word)
    {
        double value;
        if (!TclString.TryParseCoord(word, out value))
        {
            throw new InvalidOperationException("bad coordinate \"" + word + "\"");
        }
        return value;
    }

    private static double ParseDoubleWord(string word)
    {
        double value;
        if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            throw new InvalidOperationException("expected floating-point number but got \"" + word + "\"");
        }
        return value;
    }

    /// <summary>The Tk default an unset item option reads back as (<c>itemcget</c>).</summary>
    private static string DefaultOptionValue(CanvasItem item, string option)
    {
        switch (option)
        {
            case "-width":
                // Anchor items (window) default 0; everything else 1.0.
                return (item is AnchoredCanvasItem) ? "0" : "1.0";
            case "-height": return (item is AnchoredCanvasItem) ? "0" : "";
            case "-splinesteps": return "12";
            case "-smooth": return "0";
            case "-arrow": return "none";
            case "-arrowshape": return "8 10 3";
            case "-capstyle": return "butt";
            case "-joinstyle": return "round";
            case "-anchor": return "center";
            case "-justify": return "left";
            case "-state": return "";
            case "-tags": return "";
            case "-dash": return "";
            case "-text": return "";
            case "-start": return "0.0";
            case "-extent": return "90.0";
            case "-style": return "pieslice";
            case "-activewidth": return "0.0";
            case "-disabledwidth": return "0.0";
            case "-font": return (item is TextItem) ? "TkDefaultFont" : "";
            case "-fill":
                // Tk 8.6's Unix BLACK default reads back as "#000000".
                // Rectangle, oval, arc default to no fill; line/text/polygon to black.
                return (item is RectangleItem || item is OvalItem || item is ArcItem)
                        ? "" : "#000000";
            case "-outline":
                return (item is RectangleItem || item is OvalItem || item is ArcItem) ? "#000000" : "";
            default: return "";
        }
    }

    /// <summary>The Tk default an unset canvas option reads back as (<c>cget</c>).</summary>
    private string CanvasDefaultOptionValue(string option)
    {
        switch (option)
        {
            case "-closeenough": return "1";
            case "-confine": return "1";
            case "-width": return "10c";
            case "-height": return "7c";
            case "-borderwidth": return "0";
            case "-highlightthickness": return "1";
            case "-scrollregion": return "";
            case "-state": return "normal";
            case "-xscrollincrement": return "0";
            case "-yscrollincrement": return "0";
            case "-background": return Window.Tree.Theme.CanvasBackground;
            default: return "";
        }
    }
}
