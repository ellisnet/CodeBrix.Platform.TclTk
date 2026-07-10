using System;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The grid <c>-sticky</c> flags: which sides of its cell a gridded window
/// sticks to. Opposite flags together (<see cref="W"/> plus <see cref="E"/>,
/// or <see cref="N"/> plus <see cref="S"/>) stretch the window on that axis;
/// a single flag (or none) positions it within the cell.
/// </summary>
[Flags]
public enum Sticky
{
    /// <summary>No stickiness: centered in the cell at its requested size.</summary>
    None = 0,

    /// <summary>Stick to the top edge of the cell.</summary>
    N = 1,

    /// <summary>Stick to the right edge of the cell.</summary>
    E = 2,

    /// <summary>Stick to the bottom edge of the cell.</summary>
    S = 4,

    /// <summary>Stick to the left edge of the cell.</summary>
    W = 8,

    /// <summary>Stretch to fill the cell on both axes (<c>nsew</c>).</summary>
    All = N | E | S | W,
}
