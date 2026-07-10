using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The per-container state of the pack geometry manager: the ordered packing
/// list and the geometry-propagation flag (Tk's <c>Packer</c> record for a
/// container window). Created lazily on a <see cref="TkWindow"/> the first
/// time something is packed inside it (or its propagate flag is set).
/// </summary>
internal sealed class PackContainerState
{
    /// <summary>The packed content, in packing order.</summary>
    public readonly List<PackContent> Content = new List<PackContent>();

    /// <summary>
    /// Whether the container's requested size is computed from its content
    /// (<c>pack propagate</c>; Tk default true).
    /// </summary>
    public bool Propagate = true;

    /// <summary>Finds the packing-list record for a content window.</summary>
    /// <param name="window">The content window.</param>
    /// <returns>The record, or null when the window is not packed here.</returns>
    public PackContent Find(TkWindow window)
    {
        foreach (PackContent content in Content)
        {
            if (content.Window == window) { return content; }
        }
        return null;
    }
}

/// <summary>
/// One entry in a container's packing list: the content window plus its
/// persisted pack options (the positional <c>-before</c>/<c>-after</c>
/// directives are consumed at configure time and not stored).
/// </summary>
internal sealed class PackContent
{
    /// <summary>The packed window.</summary>
    public TkWindow Window;

    /// <summary>The cavity side (<c>-side</c>).</summary>
    public Side Side = Side.Top;

    /// <summary>The in-frame anchor (<c>-anchor</c>).</summary>
    public Anchor Anchor = Anchor.Center;

    /// <summary>The fill mode (<c>-fill</c>).</summary>
    public Fill Fill = Fill.None;

    /// <summary>The expand flag (<c>-expand</c>).</summary>
    public bool Expand;

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
    /// Total horizontal internal padding. Tk's <c>-ipadx</c> is a PER-SIDE
    /// amount, and tkPack.c stores it pre-doubled; every packer formula uses
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
}
