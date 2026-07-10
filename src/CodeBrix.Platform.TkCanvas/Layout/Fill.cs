namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// How a packed window stretches to consume the frame the packer allocated to
/// it (the pack <c>-fill</c> option).
/// </summary>
public enum Fill
{
    /// <summary>Keep the window's requested size (the Tk default).</summary>
    None,

    /// <summary>Stretch horizontally to fill the frame's width.</summary>
    X,

    /// <summary>Stretch vertically to fill the frame's height.</summary>
    Y,

    /// <summary>Stretch in both directions.</summary>
    Both,
}
