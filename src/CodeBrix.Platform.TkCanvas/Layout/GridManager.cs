using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The grid geometry manager engine: a faithful reimplementation of Tk's
/// gridder (tkGrid.c). Constraint resolution runs per axis: single-cell
/// content sets slot minimum sizes, spanning content is binned by its right
/// edge and can push boundaries apart, uniform groups equalize slots,
/// min-offset/max-offset chains bound every boundary, and remaining slack is
/// distributed by slot weights (evenly for weightless spans). At arrange
/// time the resolved natural layout is stretched or shrunk to the
/// container's actual size by <see cref="AdjustOffsets"/> and each content
/// window is placed in its cell span by its sticky flags.
/// </summary>
internal sealed class GridManager : IGeometryManager
{
    /// <summary>The single engine instance (state lives on container windows).</summary>
    public static readonly GridManager Instance = new GridManager();

    private GridManager()
    {
    }

    /// <inheritdoc/>
    public string Name
    {
        get { return "grid"; }
    }

    /// <summary>One boundary of the scratch layout used during constraint resolution (Tk's GridLayout).</summary>
    private sealed class LayoutSlot
    {
        /// <summary>The slot's minimum size (grows during resolution).</summary>
        public int MinSize;

        /// <summary>The slot's weight.</summary>
        public int Weight;

        /// <summary>The slot's extra pad.</summary>
        public int Pad;

        /// <summary>The slot's uniform-group name, or null.</summary>
        public string Uniform;

        /// <summary>Spanning content binned by right edge (Tk's binNextPtr chain).</summary>
        public List<GridContent> Bin;

        /// <summary>The smallest pixel offset this boundary can have.</summary>
        public int MinOffset;

        /// <summary>The largest pixel offset this boundary can have.</summary>
        public int MaxOffset;
    }

    /// <inheritdoc/>
    public bool TryComputeRequestedSize(TkWindow container, out int width, out int height)
    {
        width = 0;
        height = 0;

        GridContainerState state = container.GridContainer;
        if (state == null || state.Content.Count == 0) { return false; }

        SetGridSize(state);
        width = ResolveConstraints(state, true, 0)
                + container.InternalBorderLeft + container.InternalBorderRight;
        height = ResolveConstraints(state, false, 0)
                + container.InternalBorderTop + container.InternalBorderBottom;

        if (width < container.MinimumRequestedWidth) { width = container.MinimumRequestedWidth; }
        if (height < container.MinimumRequestedHeight) { height = container.MinimumRequestedHeight; }
        return true;
    }

    /// <inheritdoc/>
    public bool Arrange(TkWindow container)
    {
        GridContainerState state = container.GridContainer;
        if (state == null || state.Content.Count == 0) { return false; }

        bool changed = false;

        // Resolve the natural layout, then stretch/shrink it to the
        // container's actual interior and anchor it (ArrangeGrid).
        SetGridSize(state);
        ResolveConstraints(state, true, 0);
        ResolveConstraints(state, false, 0);

        int realWidth = container.Width - container.InternalBorderLeft - container.InternalBorderRight;
        int realHeight = container.Height - container.InternalBorderTop - container.InternalBorderBottom;

        int columnCount = Math.Max(state.ColumnEnd, state.ColumnMax);
        int rowCount = Math.Max(state.RowEnd, state.RowMax);
        int usedX = AdjustOffsets(realWidth, columnCount, state.Columns);
        int usedY = AdjustOffsets(realHeight, rowCount, state.Rows);
        ComputeAnchor(state.LayoutAnchor, container, usedX, usedY, out state.StartX, out state.StartY);

        foreach (GridContent content in state.Content)
        {
            TkWindow window = content.Window;
            int col = content.Column;
            int row = content.Row;

            int x = (col > 0) ? state.Columns[col - 1].Offset : 0;
            int y = (row > 0) ? state.Rows[row - 1].Offset : 0;
            int width = state.Columns[content.NumCols + col - 1].Offset - x;
            int height = state.Rows[content.NumRows + row - 1].Offset - y;

            x += state.StartX;
            y += state.StartY;

            AdjustForSticky(content, ref x, ref y, ref width, ref height);

            if (width <= 0 || height <= 0)
            {
                if (window.IsDisplayed)
                {
                    window.IsDisplayed = false;
                    changed = true;
                }
            }
            else
            {
                // Content gridded -in a non-parent container: translate
                // container-relative coordinates into the window's parent's
                // coordinate space (like pack; Tk_MaintainGeometry).
                if (container != window.Parent)
                {
                    int offsetX, offsetY;
                    OffsetWithinAncestor(container, window.Parent, out offsetX, out offsetY);
                    x += offsetX;
                    y += offsetY;
                }

                if (x != window.X || y != window.Y || width != window.Width || height != window.Height)
                {
                    window.X = x;
                    window.Y = y;
                    window.Width = width;
                    window.Height = height;
                    changed = true;
                }

                if (!window.IsDisplayed)
                {
                    window.IsDisplayed = true;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Refreshes the occupied-slot counts from the content list and ensures
    /// the constraint lists can hold every slot the layout will touch (Tk's
    /// SetGridSize + CheckSlotData CHECK_SPACE).
    /// </summary>
    /// <param name="state">The container state.</param>
    private static void SetGridSize(GridContainerState state)
    {
        int maxX = 0, maxY = 0;
        foreach (GridContent content in state.Content)
        {
            maxX = Math.Max(maxX, content.Column + content.NumCols);
            maxY = Math.Max(maxY, content.Row + content.NumRows);
        }
        state.ColumnEnd = maxX;
        state.RowEnd = maxY;

        int columnCount = Math.Max(state.ColumnEnd, state.ColumnMax);
        int rowCount = Math.Max(state.RowEnd, state.RowMax);
        if (columnCount > 0) { GridContainerState.GetSlot(state.Columns, columnCount - 1); }
        if (rowCount > 0) { GridContainerState.GetSlot(state.Rows, rowCount - 1); }
    }

    /// <summary>
    /// Resolves all boundary offsets of one axis at the layout's natural
    /// size, writing them into the slot constraints' <c>Offset</c> fields —
    /// a faithful port of Tk's ResolveConstraints. The scratch array carries
    /// one extra leading "dummy" boundary (index 0) representing the
    /// left/top edge, so C's <c>layoutPtr[k]</c> is <c>layout[k + 1]</c>
    /// here.
    /// </summary>
    /// <param name="state">The container state.</param>
    /// <param name="isColumn">True to resolve columns, false for rows.</param>
    /// <param name="maxOffset">The externally imposed layout size, or 0.</param>
    /// <returns>The natural (required) size of the layout in pixels.</returns>
    private static int ResolveConstraints(GridContainerState state, bool isColumn, int maxOffset)
    {
        int constraintCount = isColumn ? state.ColumnMax : state.RowMax;
        int slotCount = isColumn ? state.ColumnEnd : state.RowEnd;
        List<SlotConstraint> slots = isColumn ? state.Columns : state.Rows;

        int gridCount = Math.Max(constraintCount, slotCount);
        if (gridCount == 0) { return 0; }

        // layout[0] is the dummy boundary at the layout's left/top edge.
        var layout = new LayoutSlot[gridCount + 1];
        layout[0] = new LayoutSlot();
        for (int slot = 0; slot < gridCount; slot++)
        {
            var entry = new LayoutSlot();
            if (slot < constraintCount)
            {
                SlotConstraint constraint = slots[slot];
                entry.MinSize = constraint.MinSize;
                entry.Weight = constraint.Weight;
                entry.Uniform = constraint.Uniform;
                entry.Pad = constraint.Pad;
            }
            layout[slot + 1] = entry;
        }

        // Step 2: single-cell content sets slot minimum sizes; spanning
        // content is binned by its right edge.
        foreach (GridContent content in state.Content)
        {
            if (isColumn)
            {
                int rightEdge = content.Column + content.NumCols - 1;
                content.Size = content.Window.RequestedWidth + content.PadX + content.IPadXTotal;
                if (content.NumCols > 1)
                {
                    if (layout[rightEdge + 1].Bin == null) { layout[rightEdge + 1].Bin = new List<GridContent>(); }
                    layout[rightEdge + 1].Bin.Add(content);
                }
                else
                {
                    int size = content.Size + layout[rightEdge + 1].Pad;
                    if (size > layout[rightEdge + 1].MinSize) { layout[rightEdge + 1].MinSize = size; }
                }
            }
            else
            {
                int rightEdge = content.Row + content.NumRows - 1;
                content.Size = content.Window.RequestedHeight + content.PadY + content.IPadYTotal;
                if (content.NumRows > 1)
                {
                    if (layout[rightEdge + 1].Bin == null) { layout[rightEdge + 1].Bin = new List<GridContent>(); }
                    layout[rightEdge + 1].Bin.Add(content);
                }
                else
                {
                    int size = content.Size + layout[rightEdge + 1].Pad;
                    if (size > layout[rightEdge + 1].MinSize) { layout[rightEdge + 1].MinSize = size; }
                }
            }
        }

        // Step 2b: uniform groups. Every slot of a group gets the group's
        // largest per-weight-unit minimum size times its own weight
        // (weightless slots count as weight 1).
        Dictionary<string, int> uniformMin = null;
        for (int slot = 0; slot < gridCount; slot++)
        {
            LayoutSlot entry = layout[slot + 1];
            if (entry.Uniform == null) { continue; }
            if (uniformMin == null) { uniformMin = new Dictionary<string, int>(StringComparer.Ordinal); }
            int weight = (entry.Weight > 0) ? entry.Weight : 1;
            int minSize = (entry.MinSize + weight - 1) / weight;
            int groupMin;
            if (!uniformMin.TryGetValue(entry.Uniform, out groupMin) || minSize > groupMin)
            {
                uniformMin[entry.Uniform] = minSize;
            }
        }
        if (uniformMin != null)
        {
            for (int slot = 0; slot < gridCount; slot++)
            {
                LayoutSlot entry = layout[slot + 1];
                if (entry.Uniform == null) { continue; }
                int weight = (entry.Weight > 0) ? entry.Weight : 1;
                entry.MinSize = uniformMin[entry.Uniform] * weight;
            }
        }

        // Step 3: minimum offsets left-to-right.
        int offset = 0;
        for (int slot = 0; slot < gridCount; slot++)
        {
            LayoutSlot entry = layout[slot + 1];
            entry.MinOffset = entry.MinSize + offset;
            if (entry.Bin != null)
            {
                foreach (GridContent content in entry.Bin)
                {
                    int span = isColumn ? content.NumCols : content.NumRows;
                    int required = content.Size + layout[slot + 1 - span].MinOffset;
                    if (required > entry.MinOffset) { entry.MinOffset = required; }
                }
            }
            offset = entry.MinOffset;
        }

        int requiredSize = offset;
        if (maxOffset > offset) { offset = maxOffset; }

        // Step 4: maximum offsets right-to-left.
        for (int slot = 0; slot < gridCount; slot++)
        {
            layout[slot + 1].MaxOffset = offset;
        }
        for (int slot = gridCount - 1; slot > 0;)
        {
            LayoutSlot entry = layout[slot + 1];
            if (entry.Bin != null)
            {
                foreach (GridContent content in entry.Bin)
                {
                    int span = isColumn ? content.NumCols : content.NumRows;
                    int require = offset - content.Size;
                    int startSlot = slot - span;
                    if (startSlot >= 0 && require < layout[startSlot + 1].MaxOffset)
                    {
                        layout[startSlot + 1].MaxOffset = require;
                    }
                }
            }
            offset -= entry.MinSize;
            slot--;
            if (layout[slot + 1].MaxOffset < offset)
            {
                offset = layout[slot + 1].MaxOffset;
            }
            else
            {
                layout[slot + 1].MaxOffset = offset;
            }
        }

        // Step 5: distribute slack over spans of unresolved boundaries by
        // weight, fixing at least one boundary per pass.
        for (int start = 0; start < gridCount;)
        {
            if (layout[start + 1].MinOffset == layout[start + 1].MaxOffset)
            {
                start++;
                continue;
            }

            int end;
            for (end = start + 1; end < gridCount; end++)
            {
                if (layout[end + 1].MinOffset == layout[end + 1].MaxOffset) { break; }
            }

            int totalWeight = 0;
            int need = 0;
            for (int slot = start; slot <= end; slot++)
            {
                totalWeight += layout[slot + 1].Weight;
                need += layout[slot + 1].MinSize;
            }
            int have = layout[end + 1].MaxOffset - layout[start].MinOffset;

            bool noWeights = false;
            if (totalWeight == 0)
            {
                noWeights = true;
                totalWeight = end - start + 1;
            }

            // Shrink "have" until the distribution violates no boundary's
            // max offset (Tk's cumulative-grow probe loop).
            while (true)
            {
                int prevMinOffset = layout[start].MinOffset;
                int prevGrow = 0;
                int accWeight = 0;
                int slot;
                for (slot = start; slot <= end; slot++)
                {
                    int weight = noWeights ? 1 : layout[slot + 1].Weight;
                    accWeight += weight;
                    int grow = (have - need) * accWeight / totalWeight - prevGrow;
                    prevGrow += grow;

                    if ((weight > 0) &&
                            ((prevMinOffset + layout[slot + 1].MinSize + grow) > layout[slot + 1].MaxOffset))
                    {
                        grow = layout[slot + 1].MaxOffset - layout[slot + 1].MinSize - prevMinOffset;
                        int newHave = grow * totalWeight / weight;
                        if (newHave > totalWeight)
                        {
                            // Distributing multiples of totalWeight keeps the
                            // rounding errors in the last pass(es).
                            newHave = newHave / totalWeight * totalWeight;
                        }
                        if (newHave <= 0)
                        {
                            // The previous slots took all the space; guess a
                            // lower "have" that still terminates.
                            newHave = (have - need) - 1;
                            if (newHave > (3 * totalWeight))
                            {
                                newHave = newHave * 3 / 4;
                            }
                            if (newHave > totalWeight)
                            {
                                newHave = newHave / totalWeight * totalWeight;
                            }
                            if (newHave <= 0)
                            {
                                newHave = 1;
                            }
                        }
                        have = newHave + need;
                        break;
                    }
                    prevMinOffset += layout[slot + 1].MinSize + grow;
                    if (prevMinOffset < layout[slot + 1].MinOffset)
                    {
                        prevMinOffset = layout[slot + 1].MinOffset;
                    }
                }

                if (slot > end) { break; }
            }

            // Distribute the extra space by adjusting minSizes/minOffsets.
            {
                int prevGrow = 0;
                int accWeight = 0;
                for (int slot = start; slot <= end; slot++)
                {
                    accWeight += noWeights ? 1 : layout[slot + 1].Weight;
                    int grow = (have - need) * accWeight / totalWeight - prevGrow;
                    prevGrow += grow;
                    layout[slot + 1].MinSize += grow;
                    if ((layout[slot].MinOffset + layout[slot + 1].MinSize) > layout[slot + 1].MinOffset)
                    {
                        layout[slot + 1].MinOffset = layout[slot].MinOffset + layout[slot + 1].MinSize;
                    }
                }
            }

            // Propagate the new allocation back into the max offsets
            // (they may not go up).
            for (int slot = end; slot > start; slot--)
            {
                if ((layout[slot + 1].MaxOffset - layout[slot + 1].MinSize) < layout[slot].MaxOffset)
                {
                    layout[slot].MaxOffset = layout[slot + 1].MaxOffset - layout[slot + 1].MinSize;
                }
            }
        }

        // Step 6: copy the resolved offsets back into the real slots.
        for (int slot = 0; slot < gridCount; slot++)
        {
            GridContainerState.GetSlot(slots, slot).Offset = layout[slot + 1].MinOffset;
        }

        return requiredSize;
    }

    /// <summary>
    /// Stretches or shrinks the resolved layout to the actual container size
    /// by adjusting the slot offsets according to the slot weights — a
    /// faithful port of Tk's AdjustOffsets. When shrinking, weights are
    /// renormalized every time a slot bottoms out at its minimum size.
    /// </summary>
    /// <param name="size">The total layout size available, in pixels.</param>
    /// <param name="slotCount">The number of slots in the layout.</param>
    /// <param name="slots">The slot constraints (offsets are updated in place).</param>
    /// <returns>The size the layout actually uses.</returns>
    private static int AdjustOffsets(int size, int slotCount, List<SlotConstraint> slots)
    {
        if (slotCount == 0) { return 0; }

        int diff = size - slots[slotCount - 1].Offset;
        if (diff == 0) { return size; }

        int totalWeight = 0;
        for (int slot = 0; slot < slotCount; slot++)
        {
            totalWeight += slots[slot].Weight;
        }
        if (totalWeight == 0) { return slots[slotCount - 1].Offset; }

        // Growing: add the extra space cumulatively by weight.
        if (diff > 0)
        {
            int weight = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                weight += slots[slot].Weight;
                slots[slot].Offset += diff * weight / totalWeight;
            }
            return size;
        }

        // Shrinking below the requested size: find the minimum possible size.
        int minSize = 0;
        for (int slot = 0; slot < slotCount; slot++)
        {
            if (slots[slot].Weight > 0)
            {
                slots[slot].Temp = slots[slot].MinSize;
            }
            else if (slot > 0)
            {
                slots[slot].Temp = slots[slot].Offset - slots[slot - 1].Offset;
            }
            else
            {
                slots[slot].Temp = slots[slot].Offset;
            }
            minSize += slots[slot].Temp;
        }

        // Below the minimum: clamp every slot to its minimum size.
        if (size <= minSize)
        {
            int offset = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                offset += slots[slot].Temp;
                slots[slot].Offset = offset;
            }
            return minSize;
        }

        // Remove space by weight, renormalizing whenever a slot bottoms out.
        while (diff < 0)
        {
            totalWeight = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                int current = (slot == 0) ? slots[slot].Offset
                        : slots[slot].Offset - slots[slot - 1].Offset;
                if (current > slots[slot].MinSize)
                {
                    totalWeight += slots[slot].Weight;
                    slots[slot].Temp = slots[slot].Weight;
                }
                else
                {
                    slots[slot].Temp = 0;
                }
            }
            if (totalWeight == 0) { break; }

            // The most space that can be removed on this pass.
            int newDiff = diff;
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (slots[slot].Temp == 0) { continue; }
                int current = (slot == 0) ? slots[slot].Offset
                        : slots[slot].Offset - slots[slot - 1].Offset;
                int maxDiff = totalWeight * (slots[slot].MinSize - current) / slots[slot].Temp;
                if (maxDiff > newDiff) { newDiff = maxDiff; }
            }

            int weight = 0;
            for (int slot = 0; slot < slotCount; slot++)
            {
                weight += slots[slot].Temp;
                slots[slot].Offset += newDiff * weight / totalWeight;
            }
            diff -= newDiff;
        }
        return size;
    }

    /// <summary>
    /// Positions and sizes a content window inside its cell cavity according
    /// to its sticky flags — a faithful port of Tk's AdjustForSticky.
    /// </summary>
    private static void AdjustForSticky(GridContent content, ref int x, ref int y, ref int width, ref int height)
    {
        int diffX = 0;
        int diffY = 0;
        Sticky sticky = content.Sticky;

        x += content.PadLeft;
        width -= content.PadX;
        y += content.PadTop;
        height -= content.PadY;

        int wantedWidth = content.Window.RequestedWidth + content.IPadXTotal;
        if (width > wantedWidth)
        {
            diffX = width - wantedWidth;
            width = wantedWidth;
        }

        int wantedHeight = content.Window.RequestedHeight + content.IPadYTotal;
        if (height > wantedHeight)
        {
            diffY = height - wantedHeight;
            height = wantedHeight;
        }

        if ((sticky & Sticky.E) != 0 && (sticky & Sticky.W) != 0)
        {
            width += diffX;
        }
        if ((sticky & Sticky.N) != 0 && (sticky & Sticky.S) != 0)
        {
            height += diffY;
        }
        if ((sticky & Sticky.W) == 0)
        {
            x += ((sticky & Sticky.E) != 0) ? diffX : diffX / 2;
        }
        if ((sticky & Sticky.N) == 0)
        {
            y += ((sticky & Sticky.S) != 0) ? diffY : diffY / 2;
        }
    }

    /// <summary>
    /// Computes where an inner rectangle sits inside the container for an
    /// anchor value — a port of TkComputeAnchor as the gridder uses it
    /// (no extra padding).
    /// </summary>
    private static void ComputeAnchor(Anchor anchor, TkWindow container, int innerWidth, int innerHeight, out int x, out int y)
    {
        switch (anchor)
        {
            case Anchor.NW:
            case Anchor.W:
            case Anchor.SW:
                x = container.InternalBorderLeft;
                break;
            case Anchor.N:
            case Anchor.Center:
            case Anchor.S:
                x = (container.Width - innerWidth - container.InternalBorderLeft
                        - container.InternalBorderRight) / 2 + container.InternalBorderLeft;
                break;
            default:
                x = container.Width - container.InternalBorderRight - innerWidth;
                break;
        }

        switch (anchor)
        {
            case Anchor.NW:
            case Anchor.N:
            case Anchor.NE:
                y = container.InternalBorderTop;
                break;
            case Anchor.W:
            case Anchor.Center:
            case Anchor.E:
                y = (container.Height - innerHeight - container.InternalBorderTop
                        - container.InternalBorderBottom) / 2 + container.InternalBorderTop;
                break;
            default:
                y = container.Height - container.InternalBorderBottom - innerHeight;
                break;
        }
    }

    /// <summary>
    /// Sums the allocated offsets of <paramref name="descendant"/> walking up
    /// to (but excluding) <paramref name="ancestor"/> (see the pack engine's
    /// counterpart).
    /// </summary>
    private static void OffsetWithinAncestor(TkWindow descendant, TkWindow ancestor, out int offsetX, out int offsetY)
    {
        offsetX = 0;
        offsetY = 0;
        TkWindow current = descendant;
        while (current != null && current != ancestor)
        {
            offsetX += current.X;
            offsetY += current.Y;
            current = current.Parent;
        }
    }

    /// <inheritdoc/>
    public void Forget(TkWindow content)
    {
        Unlink(content);
        content.ManagedBy = null;
        content.Container = null;
        content.IsDisplayed = false;
    }

    /// <inheritdoc/>
    public void ContentDestroyed(TkWindow content)
    {
        Unlink(content);
    }

    /// <inheritdoc/>
    public void ContainerDestroyed(TkWindow container)
    {
        GridContainerState state = container.GridContainer;
        if (state == null) { return; }

        foreach (GridContent content in state.Content)
        {
            content.Window.ManagedBy = null;
            content.Window.Container = null;
            content.Window.IsDisplayed = false;
        }
        state.Content.Clear();
        container.ReleaseContainer("grid");
    }

    /// <summary>Removes a content window's record from its container's grid.</summary>
    /// <param name="content">The content window.</param>
    private static void Unlink(TkWindow content)
    {
        TkWindow container = content.Container;
        if (container == null) { return; }

        GridContainerState state = container.GridContainer;
        if (state == null) { return; }

        GridContent record = state.Find(content);
        if (record != null) { state.Content.Remove(record); }

        // An emptied container is no longer handled by this manager.
        if (state.Content.Count == 0)
        {
            container.ReleaseContainer("grid");
        }
    }
}
