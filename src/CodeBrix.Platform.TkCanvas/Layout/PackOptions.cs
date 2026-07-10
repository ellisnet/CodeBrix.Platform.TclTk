using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The options of one <c>pack</c> configuration (the Tk <c>pack</c> command's
/// option set). Defaults match Tk: side top, anchor center, no fill, no
/// expand, all padding zero.
/// </summary>
public sealed class PackOptions
{
    /// <summary>The cavity side to pack against (<c>-side</c>).</summary>
    public Side Side { get; set; } = Side.Top;

    /// <summary>Where the window sits within its allocated frame (<c>-anchor</c>).</summary>
    public Anchor Anchor { get; set; } = Anchor.Center;

    /// <summary>How the window stretches to fill its frame (<c>-fill</c>).</summary>
    public Fill Fill { get; set; } = Fill.None;

    /// <summary>
    /// Whether the window's frame grows to consume extra space left in the
    /// cavity (<c>-expand</c>).
    /// </summary>
    public bool Expand { get; set; }

    /// <summary>External padding left of the window, in pixels (<c>-padx</c>, first value).</summary>
    public int PadLeft { get; set; }

    /// <summary>External padding right of the window, in pixels (<c>-padx</c>, second value).</summary>
    public int PadRight { get; set; }

    /// <summary>External padding above the window, in pixels (<c>-pady</c>, first value).</summary>
    public int PadTop { get; set; }

    /// <summary>External padding below the window, in pixels (<c>-pady</c>, second value).</summary>
    public int PadBottom { get; set; }

    /// <summary>
    /// Internal horizontal padding in pixels (<c>-ipadx</c>): the window is
    /// made this much wider than its requested width.
    /// </summary>
    public int IPadX { get; set; }

    /// <summary>
    /// Internal vertical padding in pixels (<c>-ipady</c>): the window is
    /// made this much taller than its requested height.
    /// </summary>
    public int IPadY { get; set; }

    /// <summary>
    /// The container to pack inside (<c>-in</c>), or null for the window's
    /// parent. Must be the window's parent or a descendant of the parent.
    /// </summary>
    public TkWindow In { get; set; }

    /// <summary>
    /// Pack before this window in its container's packing order
    /// (<c>-before</c>), or null. A positional directive; not reported back
    /// by <see cref="PackLayout.Info"/>.
    /// </summary>
    public TkWindow Before { get; set; }

    /// <summary>
    /// Pack after this window in its container's packing order
    /// (<c>-after</c>), or null. A positional directive; not reported back
    /// by <see cref="PackLayout.Info"/>.
    /// </summary>
    public TkWindow After { get; set; }

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
