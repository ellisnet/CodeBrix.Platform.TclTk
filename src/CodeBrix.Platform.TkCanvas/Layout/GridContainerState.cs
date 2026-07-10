using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The per-container state of the grid geometry manager (Tk's GridContainer
/// record): the content list, the row/column slot constraints, the occupied
/// and configured slot counts, the propagation flag, and the layout anchor.
/// Created lazily on a <see cref="TkWindow"/> the first time something is
/// gridded inside it or a slot is configured.
/// </summary>
internal sealed class GridContainerState
{
    /// <summary>The gridded content, in configuration order.</summary>
    public readonly List<GridContent> Content = new List<GridContent>();

    /// <summary>The per-column constraints (grown on demand).</summary>
    public readonly List<SlotConstraint> Columns = new List<SlotConstraint>();

    /// <summary>The per-row constraints (grown on demand).</summary>
    public readonly List<SlotConstraint> Rows = new List<SlotConstraint>();

    /// <summary>
    /// One past the highest column index touched by an explicit
    /// <c>grid columnconfigure</c> (Tk's columnMax).
    /// </summary>
    public int ColumnMax;

    /// <summary>The row counterpart of <see cref="ColumnMax"/> (Tk's rowMax).</summary>
    public int RowMax;

    /// <summary>
    /// One past the highest column occupied by content, refreshed by
    /// <see cref="GridManager"/> before each layout (Tk's columnEnd).
    /// </summary>
    public int ColumnEnd;

    /// <summary>The row counterpart of <see cref="ColumnEnd"/> (Tk's rowEnd).</summary>
    public int RowEnd;

    /// <summary>
    /// Whether the container's requested size is computed from its content
    /// (<c>grid propagate</c>; Tk default true).
    /// </summary>
    public bool Propagate = true;

    /// <summary>
    /// Where the whole layout sits inside the container when the layout is
    /// smaller than the container and no weights absorb the difference
    /// (<c>grid anchor</c>; Tk default nw).
    /// </summary>
    public Anchor LayoutAnchor = Anchor.NW;

    /// <summary>The x position where the laid-out grid starts (Tk's startX).</summary>
    public int StartX;

    /// <summary>The y position where the laid-out grid starts (Tk's startY).</summary>
    public int StartY;

    /// <summary>Finds the content record for a window.</summary>
    /// <param name="window">The content window.</param>
    /// <returns>The record, or null when the window is not gridded here.</returns>
    public GridContent Find(TkWindow window)
    {
        foreach (GridContent content in Content)
        {
            if (content.Window == window) { return content; }
        }
        return null;
    }

    /// <summary>
    /// Returns the constraint record for a slot, growing the list as needed
    /// (all-default constraints for new slots).
    /// </summary>
    /// <param name="slots">The column or row constraint list.</param>
    /// <param name="index">The slot index.</param>
    /// <returns>The constraint record.</returns>
    public static SlotConstraint GetSlot(List<SlotConstraint> slots, int index)
    {
        while (slots.Count <= index)
        {
            slots.Add(new SlotConstraint());
        }
        return slots[index];
    }
}

/// <summary>
/// One entry in a container's grid content list: the content window plus its
/// persisted grid options (Tk's per-content Gridder fields).
/// </summary>
internal sealed class GridContent
{
    /// <summary>The gridded window.</summary>
    public TkWindow Window;

    /// <summary>The top-left cell row (<c>-row</c>).</summary>
    public int Row;

    /// <summary>The top-left cell column (<c>-column</c>).</summary>
    public int Column;

    /// <summary>The row span (<c>-rowspan</c>; Tk's numRows).</summary>
    public int NumRows = 1;

    /// <summary>The column span (<c>-columnspan</c>; Tk's numCols).</summary>
    public int NumCols = 1;

    /// <summary>The sticky flags (<c>-sticky</c>).</summary>
    public Sticky Sticky = Sticky.None;

    /// <summary>External padding, left, in pixels.</summary>
    public int PadLeft;

    /// <summary>External padding, right, in pixels.</summary>
    public int PadRight;

    /// <summary>External padding, top, in pixels.</summary>
    public int PadTop;

    /// <summary>External padding, bottom, in pixels.</summary>
    public int PadBottom;

    /// <summary>Internal horizontal padding in pixels, PER SIDE (<c>-ipadx</c>).</summary>
    public int IPadX;

    /// <summary>Internal vertical padding in pixels, PER SIDE (<c>-ipady</c>).</summary>
    public int IPadY;

    /// <summary>Total horizontal external padding (left plus right).</summary>
    public int PadX
    {
        get { return PadLeft + PadRight; }
    }

    /// <summary>Total vertical external padding (top plus bottom).</summary>
    public int PadY
    {
        get { return PadTop + PadBottom; }
    }

    /// <summary>
    /// Total horizontal internal padding. Like pack, Tk's grid stores
    /// <c>-ipadx</c> pre-doubled (a per-side amount); the layout formulas use
    /// this doubled value.
    /// </summary>
    public int IPadXTotal
    {
        get { return IPadX * 2; }
    }

    /// <summary>The vertical counterpart of <see cref="IPadXTotal"/>.</summary>
    public int IPadYTotal
    {
        get { return IPadY * 2; }
    }

    /// <summary>
    /// Scratch: the content's padded size on the axis being resolved (Tk's
    /// Gridder.size), set during constraint resolution.
    /// </summary>
    public int Size;
}

/// <summary>
/// The constraint and computed offset of one grid slot (row or column) —
/// Tk's SlotInfo: <c>-minsize</c>, <c>-weight</c>, <c>-pad</c>,
/// <c>-uniform</c>, and the resolved boundary offset.
/// </summary>
internal sealed class SlotConstraint
{
    /// <summary>The slot's minimum size in pixels (<c>-minsize</c>).</summary>
    public int MinSize;

    /// <summary>The slot's relative growth/shrink weight (<c>-weight</c>).</summary>
    public int Weight;

    /// <summary>Extra padding added to the largest content of the slot (<c>-pad</c>).</summary>
    public int Pad;

    /// <summary>The uniform-group name (<c>-uniform</c>), or null.</summary>
    public string Uniform;

    /// <summary>
    /// The resolved pixel offset of this slot's right/bottom boundary from
    /// the layout origin (computed by the layout passes).
    /// </summary>
    public int Offset;

    /// <summary>Scratch used by the weighted stretch/shrink pass (Tk's temp).</summary>
    public int Temp;
}
