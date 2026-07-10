namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The nine Tk anchor positions (<c>-anchor</c>) used to place a fixed-size
/// piece of content (a bitmap, image, or embedded window) relative to its
/// single anchor point.
/// </summary>
public enum CanvasAnchor
{
    /// <summary>Anchor point at the content's centre (<c>center</c>).</summary>
    Center,

    /// <summary>Anchor point at the top edge, horizontally centred (<c>n</c>).</summary>
    N,

    /// <summary>Anchor point at the top-right corner (<c>ne</c>).</summary>
    NE,

    /// <summary>Anchor point at the right edge, vertically centred (<c>e</c>).</summary>
    E,

    /// <summary>Anchor point at the bottom-right corner (<c>se</c>).</summary>
    SE,

    /// <summary>Anchor point at the bottom edge, horizontally centred (<c>s</c>).</summary>
    S,

    /// <summary>Anchor point at the bottom-left corner (<c>sw</c>).</summary>
    SW,

    /// <summary>Anchor point at the left edge, vertically centred (<c>w</c>).</summary>
    W,

    /// <summary>Anchor point at the top-left corner (<c>nw</c>).</summary>
    NW,
}

/// <summary>
/// Anchor parsing and the anchor-to-top-left offset math shared by the
/// bitmap, image, and window canvas items (the switch in Tk's
/// <c>ComputeBitmapBbox</c>/<c>ComputeImageBbox</c>/<c>ComputeWindowBbox</c>).
/// </summary>
internal static class CanvasAnchorMath
{
    /// <summary>Parses a Tk anchor name; unknown text keeps <see cref="CanvasAnchor.Center"/>.</summary>
    /// <param name="text">The anchor name.</param>
    /// <returns>The parsed anchor.</returns>
    public static CanvasAnchor Parse(string text)
    {
        switch (text)
        {
            case "n": return CanvasAnchor.N;
            case "ne": return CanvasAnchor.NE;
            case "e": return CanvasAnchor.E;
            case "se": return CanvasAnchor.SE;
            case "s": return CanvasAnchor.S;
            case "sw": return CanvasAnchor.SW;
            case "w": return CanvasAnchor.W;
            case "nw": return CanvasAnchor.NW;
            default: return CanvasAnchor.Center;
        }
    }

    /// <summary>
    /// Computes the top-left corner of a <paramref name="width"/> ×
    /// <paramref name="height"/> content box anchored at
    /// (<paramref name="x"/>, <paramref name="y"/>), using Tk's integer
    /// division (matching the C item bbox code exactly).
    /// </summary>
    /// <param name="anchor">The anchor position.</param>
    /// <param name="x">The anchor point x (already rounded to an int).</param>
    /// <param name="y">The anchor point y.</param>
    /// <param name="width">The content width.</param>
    /// <param name="height">The content height.</param>
    /// <param name="left">Receives the top-left x.</param>
    /// <param name="top">Receives the top-left y.</param>
    public static void TopLeft(CanvasAnchor anchor, int x, int y, int width, int height,
            out int left, out int top)
    {
        switch (anchor)
        {
            case CanvasAnchor.N: x -= width / 2; break;
            case CanvasAnchor.NE: x -= width; break;
            case CanvasAnchor.E: x -= width; y -= height / 2; break;
            case CanvasAnchor.SE: x -= width; y -= height; break;
            case CanvasAnchor.S: x -= width / 2; y -= height; break;
            case CanvasAnchor.SW: y -= height; break;
            case CanvasAnchor.W: y -= height / 2; break;
            case CanvasAnchor.NW: break;
            case CanvasAnchor.Center: x -= width / 2; y -= height / 2; break;
        }
        left = x;
        top = y;
    }
}
