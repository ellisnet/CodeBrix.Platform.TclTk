using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The options of one <c>grid</c> content configuration (the Tk <c>grid</c>
/// command's per-window option set). Defaults match Tk: cell (0,0), span 1x1,
/// no stickiness, all padding zero.
/// </summary>
public sealed class GridOptions
{
    /// <summary>The row of the window's top-left cell (<c>-row</c>).</summary>
    public int Row { get; set; }

    /// <summary>The column of the window's top-left cell (<c>-column</c>).</summary>
    public int Column { get; set; }

    /// <summary>How many rows the window spans (<c>-rowspan</c>).</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>How many columns the window spans (<c>-columnspan</c>).</summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>Which cell edges the window sticks to (<c>-sticky</c>).</summary>
    public Sticky Sticky { get; set; } = Sticky.None;

    /// <summary>External padding left of the window, in pixels (<c>-padx</c>, first value).</summary>
    public int PadLeft { get; set; }

    /// <summary>External padding right of the window, in pixels (<c>-padx</c>, second value).</summary>
    public int PadRight { get; set; }

    /// <summary>External padding above the window, in pixels (<c>-pady</c>, first value).</summary>
    public int PadTop { get; set; }

    /// <summary>External padding below the window, in pixels (<c>-pady</c>, second value).</summary>
    public int PadBottom { get; set; }

    /// <summary>Internal horizontal padding in pixels, PER SIDE (<c>-ipadx</c>).</summary>
    public int IPadX { get; set; }

    /// <summary>Internal vertical padding in pixels, PER SIDE (<c>-ipady</c>).</summary>
    public int IPadY { get; set; }

    /// <summary>
    /// The container to grid inside (<c>-in</c>), or null for the window's
    /// parent. Must be the window's parent or a descendant of the parent.
    /// </summary>
    public TkWindow In { get; set; }

    /// <summary>Sets <see cref="PadLeft"/> and <see cref="PadRight"/> to the same value.</summary>
    /// <param name="pad">The padding in pixels.</param>
    public void SetPadX(int pad)
    {
        PadLeft = pad;
        PadRight = pad;
    }

    /// <summary>Sets <see cref="PadTop"/> and <see cref="PadBottom"/> to the same value.</summary>
    /// <param name="pad">The padding in pixels.</param>
    public void SetPadY(int pad)
    {
        PadTop = pad;
        PadBottom = pad;
    }
}
