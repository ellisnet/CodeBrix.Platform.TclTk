using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The shared base of the single-point, anchor-positioned canvas items
/// (<c>bitmap</c>, <c>image</c>, <c>window</c>): one (x, y) coordinate, an
/// <c>-anchor</c>, and a content size supplied by the subclass. Bounding box,
/// point distance, and area classification all treat the item as the
/// axis-aligned rectangle the anchored content occupies — Tk's
/// <c>ComputeBitmapBbox</c>/<c>BitmapToArea</c> family. When the content has
/// no size yet (no bitmap/image loaded, no embedded window sized), the box
/// collapses to the anchor point, exactly as Tk does.
/// </summary>
public abstract class AnchoredCanvasItem : CanvasItem
{
    /// <summary>The parsed <c>-anchor</c> (defaults to <see cref="CanvasAnchor.Center"/>).</summary>
    private protected CanvasAnchor Anchor = CanvasAnchor.Center;

    private protected override void ApplyCoords(IReadOnlyList<double> coords)
    {
        if (coords.Count != 2)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected 2, got " + coords.Count);
        }
        CoordArray = new double[] { coords[0], coords[1] };
    }

    /// <summary>The content width in pixels (0 when nothing is loaded).</summary>
    private protected abstract int ContentWidth { get; }

    /// <summary>The content height in pixels (0 when nothing is loaded).</summary>
    private protected abstract int ContentHeight { get; }

    /// <summary>Parses the shared <c>-anchor</c> option; subclasses extend for their own options.</summary>
    private protected override void OnConfigured()
    {
        Anchor = CanvasAnchorMath.Parse(Options.Get("-anchor", "center"));
    }

    internal override void ComputeBounds()
    {
        if (CoordArray.Length < 2) { SetEmptyHeaderBox(); return; }

        int x = (int)(CoordArray[0] + ((CoordArray[0] >= 0) ? 0.5 : -0.5));
        int y = (int)(CoordArray[1] + ((CoordArray[1] >= 0) ? 0.5 : -0.5));

        int width = ContentWidth;
        int height = ContentHeight;
        if (EffectiveState == CanvasItemState.Hidden || (width <= 0 && height <= 0))
        {
            SetHeaderBox(x, y, x, y);
            return;
        }

        int left, top;
        CanvasAnchorMath.TopLeft(Anchor, x, y, width, height, out left, out top);
        SetHeaderBox(left, top, left + width, top + height);
    }

    /// <inheritdoc/>
    public override double DistanceTo(double px, double py)
    {
        double x1 = X1;
        double y1 = Y1;
        double x2 = X2;
        double y2 = Y2;

        double dx = (px < x1) ? (x1 - px) : (px > x2) ? (px - x2) : 0;
        double dy = (py < y1) ? (y1 - py) : (py > y2) ? (py - y2) : 0;
        if (dx == 0 && dy == 0) { return 0.0; }
        return TkCanvasMath.Hypot(dx, dy);
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        if ((rect[2] <= X1) || (rect[0] >= X2) || (rect[3] <= Y1) || (rect[1] >= Y2))
        {
            return -1;
        }
        if ((rect[0] <= X1) && (rect[1] <= Y1) && (rect[2] >= X2) && (rect[3] >= Y2))
        {
            return 1;
        }
        return 0;
    }
}
