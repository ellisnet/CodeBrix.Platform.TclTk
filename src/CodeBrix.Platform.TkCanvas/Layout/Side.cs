namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The side of the packing cavity a window is packed against
/// (the pack <c>-side</c> option).
/// </summary>
public enum Side
{
    /// <summary>Pack against the top of the cavity (the Tk default).</summary>
    Top,

    /// <summary>Pack against the bottom of the cavity.</summary>
    Bottom,

    /// <summary>Pack against the left edge of the cavity.</summary>
    Left,

    /// <summary>Pack against the right edge of the cavity.</summary>
    Right,
}
