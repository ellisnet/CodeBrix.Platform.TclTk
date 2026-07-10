using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Widgets;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>The per-item display state (<c>-state</c>).</summary>
public enum CanvasItemState
{
    /// <summary>No item-level state set; the canvas <c>-state</c> governs (Tk's empty state).</summary>
    Default,

    /// <summary>Drawn and interactive.</summary>
    Normal,

    /// <summary>Not drawn, not found by area/closest searches, not pickable.</summary>
    Hidden,

    /// <summary>Drawn (with disabled variants) but never the current item.</summary>
    Disabled,
}

/// <summary>
/// The shared base of every canvas item: identity (id, type name, tags),
/// the option bag, the coordinate array, the integer header bounding box
/// that <c>bbox</c> and the quick-reject tests read, and the Tk item-proc
/// surface (point distance, area classification, translate, scale) the
/// canvas engine drives. Concrete types port the matching Tk 8.6 item
/// implementation (tkCanvLine.c, tkRectOval.c, tkCanvPoly.c, tkCanvText.c).
/// </summary>
public abstract class CanvasItem : ICanvasItem
{
    private readonly List<string> _tags = new List<string>();

    /// <summary>The coordinate storage, x/y interleaved (mutable by subclasses).</summary>
    private protected double[] CoordArray = new double[0];

    /// <summary>Creates the item shell; the canvas assigns identity at <c>create</c> time.</summary>
    private protected CanvasItem()
    {
    }

    /// <inheritdoc/>
    public int Id { get; internal set; }

    /// <summary>The canvas this item belongs to (set at creation).</summary>
    public CanvasWidget Canvas { get; internal set; }

    /// <inheritdoc/>
    public abstract string TypeName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> Tags
    {
        get { return _tags; }
    }

    /// <inheritdoc/>
    public WidgetOptions Options { get; } = new WidgetOptions();

    /// <summary>The item-level state parsed from <c>-state</c>.</summary>
    public CanvasItemState State { get; internal set; } = CanvasItemState.Default;

    /// <summary>Left edge of the integer header bounding box (canvas coordinates).</summary>
    public int X1 { get; private protected set; }

    /// <summary>Top edge of the integer header bounding box.</summary>
    public int Y1 { get; private protected set; }

    /// <summary>Right edge of the integer header bounding box.</summary>
    public int X2 { get; private protected set; }

    /// <summary>Bottom edge of the integer header bounding box.</summary>
    public int Y2 { get; private protected set; }

    /// <inheritdoc/>
    public SKRectI Bounds
    {
        get { return new SKRectI(X1, Y1, X2, Y2); }
    }

    /// <summary>
    /// The state that governs drawing and searching: the item state when
    /// set, else the canvas-wide <c>-state</c>.
    /// </summary>
    public CanvasItemState EffectiveState
    {
        get
        {
            if (State != CanvasItemState.Default) { return State; }
            return (Canvas != null) ? Canvas.CanvasState : CanvasItemState.Normal;
        }
    }

    /// <summary>Adds a tag if the item does not already carry it (Tk's DoItem).</summary>
    /// <param name="tag">The tag to add.</param>
    public void AddTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) { return; }
        if (!_tags.Contains(tag)) { _tags.Add(tag); }
    }

    /// <summary>Removes every occurrence of a tag (a missing tag is a no-op).</summary>
    /// <param name="tag">The tag to remove.</param>
    public void RemoveTag(string tag)
    {
        _tags.RemoveAll(t => t == tag);
    }

    /// <summary>Whether the item carries the tag.</summary>
    /// <param name="tag">The tag to test.</param>
    /// <returns>True when present.</returns>
    public bool HasTag(string tag)
    {
        return _tags.Contains(tag);
    }

    /// <inheritdoc/>
    public bool HitTest(SKPoint point, double halo)
    {
        double dist = DistanceTo(point.X, point.Y) - halo;
        return dist <= 0.0;
    }

    /// <inheritdoc/>
    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options != null)
        {
            foreach (KeyValuePair<string, string> option in options)
            {
                Options.Set(option.Key, option.Value);
                if (option.Key == "-tags")
                {
                    _tags.Clear();
                    foreach (string tag in TclString.SplitList(option.Value))
                    {
                        AddTag(tag);
                    }
                }
                else if (option.Key == "-state")
                {
                    State = ParseState(option.Value);
                }
            }
        }
        OnConfigured();
        ComputeBounds();
        if (Canvas != null) { Canvas.NotifyItemChanged(); }
    }

    /// <inheritdoc/>
    public IReadOnlyList<double> GetCoords()
    {
        return ReadCoords();
    }

    /// <inheritdoc/>
    public void SetCoords(IReadOnlyList<double> coords)
    {
        if (coords == null) { throw new ArgumentNullException(nameof(coords)); }
        ApplyCoords(coords);
        ComputeBounds();
        if (Canvas != null) { Canvas.NotifyItemChanged(); }
    }

    /// <inheritdoc/>
    public abstract void Paint(SKCanvas canvas);

    /// <summary>
    /// The Tk point-proc: the distance from the point (canvas coordinates)
    /// to the item, 0 when the point is on/inside it.
    /// </summary>
    /// <param name="x">The point x.</param>
    /// <param name="y">The point y.</param>
    /// <returns>The distance in pixels.</returns>
    public abstract double DistanceTo(double x, double y);

    /// <summary>
    /// The Tk area-proc: classifies the item against a rectangle
    /// (<c>{x1 y1 x2 y2}</c>, normalized): -1 = entirely outside,
    /// 0 = overlapping, 1 = entirely inside.
    /// </summary>
    /// <param name="rect">The four rectangle coordinates.</param>
    /// <returns>The classification.</returns>
    public abstract int AreaTest(double[] rect);

    /// <summary>Recomputes the integer header bounding box from current coords/options.</summary>
    internal abstract void ComputeBounds();

    /// <summary>Interprets the known options after a configure stored them.</summary>
    private protected abstract void OnConfigured();

    /// <summary>Replaces the coordinates (the Tk coord-proc set path).</summary>
    private protected abstract void ApplyCoords(IReadOnlyList<double> coords);

    /// <summary>Reads the coordinates back (the Tk coord-proc get path).</summary>
    private protected virtual IReadOnlyList<double> ReadCoords()
    {
        return (double[])CoordArray.Clone();
    }

    /// <summary>
    /// The Tk translate-proc: moves the item by a delta. The default offsets
    /// every coordinate and recomputes the bounds.
    /// </summary>
    /// <param name="dx">The x delta.</param>
    /// <param name="dy">The y delta.</param>
    internal virtual void Translate(double dx, double dy)
    {
        for (int i = 0; i + 1 < CoordArray.Length; i += 2)
        {
            CoordArray[i] += dx;
            CoordArray[i + 1] += dy;
        }
        ComputeBounds();
    }

    /// <summary>
    /// The Tk scale-proc: scales the item about an origin. The default maps
    /// every coordinate and recomputes the bounds.
    /// </summary>
    /// <param name="originX">The scale origin x.</param>
    /// <param name="originY">The scale origin y.</param>
    /// <param name="scaleX">The x scale factor.</param>
    /// <param name="scaleY">The y scale factor.</param>
    internal virtual void Scale(double originX, double originY, double scaleX, double scaleY)
    {
        for (int i = 0; i + 1 < CoordArray.Length; i += 2)
        {
            CoordArray[i] = originX + scaleX * (CoordArray[i] - originX);
            CoordArray[i + 1] = originY + scaleY * (CoordArray[i + 1] - originY);
        }
        ComputeBounds();
    }

    /// <summary>Sets the header box directly (used by bbox computations).</summary>
    private protected void SetHeaderBox(int x1, int y1, int x2, int y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Marks the header box empty, as Tk does for hidden/empty items.</summary>
    private protected void SetEmptyHeaderBox()
    {
        X1 = Y1 = X2 = Y2 = -1;
    }

    /// <summary>
    /// Grows the header box to include a point, rounding to the nearest
    /// integer first (Tk's <c>TkIncludePoint</c>).
    /// </summary>
    private protected void IncludePoint(double x, double y)
    {
        int tmp = (int)(x + 0.5);
        if (tmp < X1) { X1 = tmp; }
        if (tmp > X2) { X2 = tmp; }
        tmp = (int)(y + 0.5);
        if (tmp < Y1) { Y1 = tmp; }
        if (tmp > Y2) { Y2 = tmp; }
    }

    /// <summary>Whether the current item is this one (drives active-state options).</summary>
    private protected bool IsCurrent
    {
        get { return Canvas != null && ReferenceEquals(Canvas.CurrentItem, this); }
    }

    /// <summary>
    /// The line/outline width in effect, honoring <c>-activewidth</c> for
    /// the current item and <c>-disabledwidth</c> when disabled (the shared
    /// preamble of every Tk item proc).
    /// </summary>
    /// <param name="baseWidth">The configured <c>-width</c>.</param>
    /// <returns>The width to use.</returns>
    private protected double EffectiveWidth(double baseWidth)
    {
        double width = baseWidth;
        if (IsCurrent)
        {
            double active = Options.GetDouble("-activewidth", 0.0);
            if (active > width) { width = active; }
        }
        else if (EffectiveState == CanvasItemState.Disabled)
        {
            double disabled = Options.GetDouble("-disabledwidth", 0.0);
            if (disabled > 0) { width = disabled; }
        }
        return width;
    }

    /// <summary>Parses a Tk <c>-state</c> value (unknown text keeps the default).</summary>
    /// <param name="text">The state text.</param>
    /// <returns>The parsed state.</returns>
    internal static CanvasItemState ParseState(string text)
    {
        switch (text)
        {
            case "normal": return CanvasItemState.Normal;
            case "hidden": return CanvasItemState.Hidden;
            case "disabled": return CanvasItemState.Disabled;
            default: return CanvasItemState.Default;
        }
    }
}
