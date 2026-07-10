using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The public surface of the grid geometry manager (the Tk <c>grid</c>
/// command): configure a window into a container's grid, forget it, query
/// its options, configure row/column constraints, and control geometry
/// propagation and the layout anchor. The layout math itself runs during
/// <see cref="TkLayout.Update"/>.
/// </summary>
public static class GridLayout
{
    /// <summary>
    /// Grids <paramref name="window"/> (or reconfigures it) with the given
    /// options — the analogue of <c>grid configure</c>. The container is
    /// <see cref="GridOptions.In"/> if set, otherwise the window's parent.
    /// </summary>
    /// <param name="window">The window to grid.</param>
    /// <param name="options">The grid options (null means all defaults: cell 0,0 span 1x1).</param>
    public static void Configure(TkWindow window, GridOptions options)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.IsRoot) { throw new InvalidOperationException("can't grid \".\": it's the root window"); }
        if (options == null) { options = new GridOptions(); }
        if (options.Row < 0) { throw new ArgumentException("bad row value: must be a non-negative integer", nameof(options)); }
        if (options.Column < 0) { throw new ArgumentException("bad column value: must be a non-negative integer", nameof(options)); }
        if (options.RowSpan < 1) { throw new ArgumentException("bad rowspan value: must be a positive integer", nameof(options)); }
        if (options.ColumnSpan < 1) { throw new ArgumentException("bad columnspan value: must be a positive integer", nameof(options)); }

        TkWindow container = (options.In != null) ? options.In : window.Parent;
        ValidateContainer(window, container);

        // Steal the window from another geometry manager FIRST (matching
        // Tk's order): if it was that manager's last content here, the
        // container claim is released and gridding into it succeeds.
        if (window.ManagedBy != null && window.ManagedBy != GridManager.Instance)
        {
            window.ManagedBy.Forget(window);
        }

        container.ClaimContainer("grid");

        GridContainerState state = GetOrCreateState(container);

        GridContent record = null;
        if (window.ManagedBy == GridManager.Instance && window.Container != null)
        {
            GridContainerState oldState = window.Container.GridContainer;
            GridContent existing = (oldState != null) ? oldState.Find(window) : null;
            if (existing != null)
            {
                if (window.Container == container)
                {
                    record = existing; // Reconfigure in place.
                }
                else
                {
                    oldState.Content.Remove(existing);
                    if (oldState.Content.Count == 0) { window.Container.ReleaseContainer("grid"); }
                }
            }
        }

        if (record == null)
        {
            record = new GridContent { Window = window };
            state.Content.Add(record);
        }

        record.Row = options.Row;
        record.Column = options.Column;
        record.NumRows = options.RowSpan;
        record.NumCols = options.ColumnSpan;
        record.Sticky = options.Sticky;
        record.PadLeft = Clamp(options.PadLeft);
        record.PadRight = Clamp(options.PadRight);
        record.PadTop = Clamp(options.PadTop);
        record.PadBottom = Clamp(options.PadBottom);
        record.IPadX = Clamp(options.IPadX);
        record.IPadY = Clamp(options.IPadY);

        window.ManagedBy = GridManager.Instance;
        window.Container = container;
        window.Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Removes <paramref name="window"/> from its grid — the analogue of
    /// <c>grid forget</c>. The window becomes unmanaged and undisplayed but
    /// keeps its last geometry. Forgetting an ungridded window is a no-op.
    /// </summary>
    /// <param name="window">The window to forget.</param>
    public static void Forget(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.ManagedBy != GridManager.Instance) { return; }
        GridManager.Instance.Forget(window);
        window.Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Reports the current grid configuration of <paramref name="window"/> —
    /// the analogue of <c>grid info</c>. <see cref="GridOptions.In"/> is set
    /// to the actual container.
    /// </summary>
    /// <param name="window">The gridded window.</param>
    /// <returns>A snapshot of the window's grid options.</returns>
    public static GridOptions Info(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.ManagedBy != GridManager.Instance || window.Container == null)
        {
            throw new InvalidOperationException("window \"" + window.PathName + "\" isn't gridded");
        }

        GridContent record = window.Container.GridContainer.Find(window);
        return new GridOptions
        {
            Row = record.Row,
            Column = record.Column,
            RowSpan = record.NumRows,
            ColumnSpan = record.NumCols,
            Sticky = record.Sticky,
            PadLeft = record.PadLeft,
            PadRight = record.PadRight,
            PadTop = record.PadTop,
            PadBottom = record.PadBottom,
            IPadX = record.IPadX,
            IPadY = record.IPadY,
            In = window.Container,
        };
    }

    /// <summary>
    /// Lists the content gridded in <paramref name="container"/>, in
    /// configuration order — the analogue of <c>grid content</c>
    /// (<c>grid slaves</c>).
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>The gridded windows (empty when none).</returns>
    public static IReadOnlyList<TkWindow> Content(TkWindow container)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }

        var result = new List<TkWindow>();
        GridContainerState state = container.GridContainer;
        if (state != null)
        {
            foreach (GridContent content in state.Content)
            {
                result.Add(content.Window);
            }
        }
        return result;
    }

    /// <summary>
    /// Configures a column's constraints — the analogue of
    /// <c>grid columnconfigure</c>. Null parameters leave the current value.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="index">The column index (non-negative).</param>
    /// <param name="minSize">The minimum column width in pixels (<c>-minsize</c>).</param>
    /// <param name="weight">The relative grow/shrink weight (<c>-weight</c>).</param>
    /// <param name="pad">Extra padding over the largest content (<c>-pad</c>).</param>
    /// <param name="uniform">The uniform group name (<c>-uniform</c>; empty clears it).</param>
    public static void ColumnConfigure(TkWindow container, int index, int? minSize = null, int? weight = null, int? pad = null, string uniform = null)
    {
        SlotConfigure(container, index, true, minSize, weight, pad, uniform);
    }

    /// <summary>
    /// Configures a row's constraints — the analogue of
    /// <c>grid rowconfigure</c>. Null parameters leave the current value.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="index">The row index (non-negative).</param>
    /// <param name="minSize">The minimum row height in pixels (<c>-minsize</c>).</param>
    /// <param name="weight">The relative grow/shrink weight (<c>-weight</c>).</param>
    /// <param name="pad">Extra padding over the largest content (<c>-pad</c>).</param>
    /// <param name="uniform">The uniform group name (<c>-uniform</c>; empty clears it).</param>
    public static void RowConfigure(TkWindow container, int index, int? minSize = null, int? weight = null, int? pad = null, string uniform = null)
    {
        SlotConfigure(container, index, false, minSize, weight, pad, uniform);
    }

    /// <summary>
    /// Reads the effective size of the grid — the analogue of
    /// <c>grid size</c>: the number of columns and rows, counting both
    /// occupied and explicitly configured slots.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    public static void Size(TkWindow container, out int columns, out int rows)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }

        columns = 0;
        rows = 0;
        GridContainerState state = container.GridContainer;
        if (state == null) { return; }

        int maxX = 0, maxY = 0;
        foreach (GridContent content in state.Content)
        {
            maxX = Math.Max(maxX, content.Column + content.NumCols);
            maxY = Math.Max(maxY, content.Row + content.NumRows);
        }
        columns = Math.Max(maxX, state.ColumnMax);
        rows = Math.Max(maxY, state.RowMax);
    }

    /// <summary>
    /// Reads the geometry-propagation flag of <paramref name="container"/> —
    /// the analogue of <c>grid propagate</c> with no value. Defaults to true.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>Whether the container's requested size follows its content.</returns>
    public static bool GetPropagate(TkWindow container)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        GridContainerState state = container.GridContainer;
        return (state == null) || state.Propagate;
    }

    /// <summary>
    /// Sets the geometry-propagation flag of <paramref name="container"/> —
    /// the analogue of <c>grid propagate</c> with a boolean value.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="propagate">Whether the container's requested size follows its content.</param>
    public static void SetPropagate(TkWindow container, bool propagate)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        GridContainerState state = GetOrCreateState(container);
        state.Propagate = propagate;

        // Mirror Tk: turning propagation off releases the container claim;
        // turning it on for a container with content re-claims it.
        if (!propagate)
        {
            container.ReleaseContainer("grid");
        }
        else if (state.Content.Count > 0)
        {
            container.ClaimContainer("grid");
        }
        container.Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Reads where the layout sits inside the container when it is smaller
    /// than the container — the analogue of <c>grid anchor</c> with no
    /// value. Defaults to <see cref="Anchor.NW"/>.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>The layout anchor.</returns>
    public static Anchor GetAnchor(TkWindow container)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        GridContainerState state = container.GridContainer;
        return (state == null) ? Anchor.NW : state.LayoutAnchor;
    }

    /// <summary>
    /// Sets the layout anchor — the analogue of <c>grid anchor</c> with a
    /// value.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="anchor">The layout anchor.</param>
    public static void SetAnchor(TkWindow container, Anchor anchor)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        GetOrCreateState(container).LayoutAnchor = anchor;
        container.Tree.NotifyGeometryChanged();
    }

    private static void SlotConfigure(TkWindow container, int index, bool isColumn, int? minSize, int? weight, int? pad, string uniform)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        if (index < 0) { throw new ArgumentOutOfRangeException(nameof(index), "slot index must be non-negative"); }
        if (minSize.HasValue && minSize.Value < 0) { throw new ArgumentOutOfRangeException(nameof(minSize)); }
        if (weight.HasValue && weight.Value < 0) { throw new ArgumentOutOfRangeException(nameof(weight)); }
        if (pad.HasValue && pad.Value < 0) { throw new ArgumentOutOfRangeException(nameof(pad)); }

        GridContainerState state = GetOrCreateState(container);
        SlotConstraint slot = GridContainerState.GetSlot(isColumn ? state.Columns : state.Rows, index);

        if (minSize.HasValue) { slot.MinSize = minSize.Value; }
        if (weight.HasValue) { slot.Weight = weight.Value; }
        if (pad.HasValue) { slot.Pad = pad.Value; }
        if (uniform != null) { slot.Uniform = (uniform.Length == 0) ? null : uniform; }

        // An explicitly configured slot extends the constraint count even
        // beyond the occupied area (Tk's rowMax/columnMax).
        if (isColumn)
        {
            if (index >= state.ColumnMax) { state.ColumnMax = index + 1; }
        }
        else
        {
            if (index >= state.RowMax) { state.RowMax = index + 1; }
        }
        container.Tree.NotifyGeometryChanged();
    }

    private static GridContainerState GetOrCreateState(TkWindow container)
    {
        if (container.GridContainer == null)
        {
            container.GridContainer = new GridContainerState();
        }
        return container.GridContainer;
    }

    private static int Clamp(int value)
    {
        return (value < 0) ? 0 : value;
    }

    /// <summary>
    /// Enforces Tk's gridding constraints (same shape as pack's): the
    /// container must be the window's parent or a descendant of the parent,
    /// and gridding a window inside itself or its own descendant is a
    /// management loop.
    /// </summary>
    private static void ValidateContainer(TkWindow window, TkWindow container)
    {
        if (container == null)
        {
            throw new InvalidOperationException("window \"" + window.PathName + "\" has no container to grid in");
        }
        if (container == window)
        {
            throw new InvalidOperationException("can't grid \"" + window.PathName + "\" inside itself");
        }

        TkWindow ancestor = container;
        while (ancestor != null && ancestor != window.Parent)
        {
            if (ancestor == window)
            {
                throw new InvalidOperationException(
                    "can't put \"" + window.PathName + "\" inside \"" + container.PathName + "\": would cause a management loop");
            }
            ancestor = ancestor.Parent;
        }
        if (ancestor != window.Parent)
        {
            throw new InvalidOperationException(
                "can't grid \"" + window.PathName + "\" inside \"" + container.PathName + "\"");
        }
    }
}
