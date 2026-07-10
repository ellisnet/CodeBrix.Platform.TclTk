using System;
using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>rectangle</c> item — a port of the rectangle half of Tk
/// 8.6.16 tkRectOval.c: two corner points (normalized so the first is the
/// upper-left), an outline (default black) and an optional fill, with Tk's
/// exact bbox bloat, point-distance, and area-classification formulas.
/// </summary>
public sealed class RectangleItem : CanvasItem
{
    private double _width = 1.0;
    private string _outline = "black";
    private string _fill = "";

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "rectangle"; }
    }

    private bool OutlineVisible
    {
        get { return _outline.Length != 0; }
    }

    private bool FillVisible
    {
        get { return _fill.Length != 0; }
    }

    private protected override void ApplyCoords(IReadOnlyList<double> coords)
    {
        if (coords.Count != 4)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected 0 or 4, got " + coords.Count);
        }
        CoordArray = new double[] { coords[0], coords[1], coords[2], coords[3] };
    }

    private protected override void OnConfigured()
    {
        _width = Options.GetDouble("-width", 1.0);
        if (_width < 0) { _width = 1.0; }
        _outline = Options.Get("-outline", "black");
        _fill = Options.Get("-fill", "");
    }

    internal override void ComputeBounds()
    {
        if (CoordArray.Length < 4) { SetEmptyHeaderBox(); return; }

        double width = _width;
        if (EffectiveState == CanvasItemState.Hidden)
        {
            SetEmptyHeaderBox();
            return;
        }
        if (IsCurrent)
        {
            double active = Options.GetDouble("-activewidth", 0.0);
            if (active > width) { width = active; }
        }
        else if (EffectiveState == CanvasItemState.Disabled)
        {
            double disabled = Options.GetDouble("-disabledwidth", 0.0);
            if (disabled > 0) { width = disabled; }
        }

        // Normalize the stored coordinates so the first point is the
        // upper-left, exactly as Tk mutates its bbox array in place (the
        // coords readback is normalized too).
        if (CoordArray[1] > CoordArray[3])
        {
            double tmpY = CoordArray[3];
            CoordArray[3] = CoordArray[1];
            CoordArray[1] = tmpY;
        }
        if (CoordArray[0] > CoordArray[2])
        {
            double tmpX = CoordArray[2];
            CoordArray[2] = CoordArray[0];
            CoordArray[0] = tmpX;
        }

        int bloat = OutlineVisible ? ((int)(width + 1)) / 2 : 0;

        int tmp = (int)((CoordArray[0] >= 0) ? CoordArray[0] + 0.5 : CoordArray[0] - 0.5);
        int x1 = tmp - bloat;
        tmp = (int)((CoordArray[1] >= 0) ? CoordArray[1] + 0.5 : CoordArray[1] - 0.5);
        int y1 = tmp - bloat;

        double dtmp = CoordArray[2];
        if (dtmp < CoordArray[0] + 1) { dtmp = CoordArray[0] + 1; }
        tmp = (int)((dtmp >= 0) ? dtmp + 0.5 : dtmp - 0.5);
        int x2 = tmp + bloat;

        dtmp = CoordArray[3];
        if (dtmp < CoordArray[1] + 1) { dtmp = CoordArray[1] + 1; }
        tmp = (int)((dtmp >= 0) ? dtmp + 0.5 : dtmp - 0.5);
        int y2 = tmp + bloat;

        SetHeaderBox(x1, y1, x2, y2);
    }

    /// <inheritdoc/>
    public override double DistanceTo(double x, double y)
    {
        double width = EffectiveWidth(_width);

        double x1 = CoordArray[0];
        double y1 = CoordArray[1];
        double x2 = CoordArray[2];
        double y2 = CoordArray[3];
        if (OutlineVisible)
        {
            double inc = width / 2.0;
            x1 -= inc;
            y1 -= inc;
            x2 += inc;
            y2 += inc;
        }

        if ((x >= x1) && (x < x2) && (y >= y1) && (y < y2))
        {
            if (FillVisible || !OutlineVisible) { return 0.0; }

            double xDiff = x - x1;
            double tmp = x2 - x;
            if (tmp < xDiff) { xDiff = tmp; }
            double yDiff = y - y1;
            tmp = y2 - y;
            if (tmp < yDiff) { yDiff = tmp; }
            if (yDiff < xDiff) { xDiff = yDiff; }
            xDiff -= width;
            if (xDiff < 0.0) { return 0.0; }
            return xDiff;
        }

        double dx;
        if (x < x1) { dx = x1 - x; }
        else if (x > x2) { dx = x - x2; }
        else { dx = 0; }

        double dy;
        if (y < y1) { dy = y1 - y; }
        else if (y > y2) { dy = y - y2; }
        else { dy = 0; }

        return TkCanvasMath.Hypot(dx, dy);
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        double width = EffectiveWidth(_width);
        double halfWidth = OutlineVisible ? width / 2.0 : 0.0;

        if ((rect[2] <= (CoordArray[0] - halfWidth))
                || (rect[0] >= (CoordArray[2] + halfWidth))
                || (rect[3] <= (CoordArray[1] - halfWidth))
                || (rect[1] >= (CoordArray[3] + halfWidth)))
        {
            return -1;
        }
        if (!FillVisible && OutlineVisible
                && (rect[0] >= (CoordArray[0] + halfWidth))
                && (rect[1] >= (CoordArray[1] + halfWidth))
                && (rect[2] <= (CoordArray[2] - halfWidth))
                && (rect[3] <= (CoordArray[3] - halfWidth)))
        {
            return -1;
        }
        if ((rect[0] <= (CoordArray[0] - halfWidth))
                && (rect[1] <= (CoordArray[1] - halfWidth))
                && (rect[2] >= (CoordArray[2] + halfWidth))
                && (rect[3] >= (CoordArray[3] + halfWidth)))
        {
            return 1;
        }
        return 0;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }
        if (CoordArray.Length < 4) { return; }

        var rect = new SKRect(
                (float)CoordArray[0], (float)CoordArray[1],
                (float)CoordArray[2], (float)CoordArray[3]);

        string fill = _fill;
        if (IsCurrent && Options.IsSet("-activefill")) { fill = Options.Get("-activefill"); }
        else if (EffectiveState == CanvasItemState.Disabled && Options.IsSet("-disabledfill"))
        {
            fill = Options.Get("-disabledfill");
        }

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;

            SKColor fillColor;
            if (TkColor.TryParse(fill, out fillColor))
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = fillColor;
                canvas.DrawRect(rect, paint);
            }

            SKColor outlineColor;
            if (TkColor.TryParse(_outline, out outlineColor))
            {
                double width = EffectiveWidth(_width);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = outlineColor;
                paint.StrokeWidth = (float)((width < 1.0) ? 1.0 : width);

                SKPathEffect dash = CanvasPaintHelper.CreateDashEffect(
                        Options.Get("-dash"), paint.StrokeWidth);
                if (dash != null) { paint.PathEffect = dash; }
                canvas.DrawRect(rect, paint);
                if (dash != null)
                {
                    paint.PathEffect = null;
                    dash.Dispose();
                }
            }
        }
    }
}
