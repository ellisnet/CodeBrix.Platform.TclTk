namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// A Tk anchor position: where a smaller rectangle is placed within a larger
/// one (the pack <c>-anchor</c> option, and later the anchor option family on
/// widgets and canvas items). Values follow compass directions.
/// </summary>
public enum Anchor
{
    /// <summary>Centered (the pack default).</summary>
    Center,

    /// <summary>Top edge, horizontally centered.</summary>
    N,

    /// <summary>Top-right corner.</summary>
    NE,

    /// <summary>Right edge, vertically centered.</summary>
    E,

    /// <summary>Bottom-right corner.</summary>
    SE,

    /// <summary>Bottom edge, horizontally centered.</summary>
    S,

    /// <summary>Bottom-left corner.</summary>
    SW,

    /// <summary>Left edge, vertically centered.</summary>
    W,

    /// <summary>Top-left corner.</summary>
    NW,
}
