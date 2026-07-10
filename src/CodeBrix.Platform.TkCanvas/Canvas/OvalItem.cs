using System;
using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>oval</c> item — a port of the oval half of Tk 8.6.16
/// tkRectOval.c: an ellipse inscribed in the box formed by two corner points
/// (normalized so the first is the upper-left), with an outline (default
/// black), an optional fill, and Tk's exact bbox bloat, point-distance
/// (<c>TkOvalToPoint</c>), and area-classification (<c>TkOvalToArea</c>,
/// including the unfilled-centre correction) formulas.
/// </summary>
public sealed class OvalItem : CanvasItem
{
    private double _width = 1.0;
    private string _outline = "black";
    private string _fill = "";

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "oval"; }
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

        if (EffectiveState == CanvasItemState.Hidden)
        {
            SetEmptyHeaderBox();
            return;
        }

        double width = _width;
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
        // upper-left (ComputeRectOvalBbox mutates its bbox array in place;
        // the coords readback is normalized too).
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
        bool filled = FillVisible;
        if (!OutlineVisible)
        {
            width = 0.0;
            filled = true;
        }
        var point = new double[] { x, y };
        return TkCanvasMath.OvalToPoint(CoordArray, width, filled, point[0], point[1]);
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        double width = EffectiveWidth(_width);
        double halfWidth = OutlineVisible ? width / 2.0 : 0.0;

        var oval = new double[]
        {
            CoordArray[0] - halfWidth, CoordArray[1] - halfWidth,
            CoordArray[2] + halfWidth, CoordArray[3] + halfWidth,
        };

        int result = TkCanvasMath.OvalToArea(oval, rect);

        // If the rectangle overlaps and the oval is unfilled, check whether
        // all four corners fall in the unfilled centre (→ "outside").
        if ((result == 0) && OutlineVisible && !FillVisible)
        {
            double centerX = (CoordArray[0] + CoordArray[2]) / 2.0;
            double centerY = (CoordArray[1] + CoordArray[3]) / 2.0;
            double w = (CoordArray[2] - CoordArray[0]) / 2.0 - halfWidth;
            double h = (CoordArray[3] - CoordArray[1]) / 2.0 - halfWidth;
            double xDelta1 = (rect[0] - centerX) / w;
            xDelta1 *= xDelta1;
            double yDelta1 = (rect[1] - centerY) / h;
            yDelta1 *= yDelta1;
            double xDelta2 = (rect[2] - centerX) / w;
            xDelta2 *= xDelta2;
            double yDelta2 = (rect[3] - centerY) / h;
            yDelta2 *= yDelta2;
            if (((xDelta1 + yDelta1) < 1.0)
                    && ((xDelta1 + yDelta2) < 1.0)
                    && ((xDelta2 + yDelta1) < 1.0)
                    && ((xDelta2 + yDelta2) < 1.0))
            {
                return -1;
            }
        }
        return result;
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

        string outline = _outline;
        if (IsCurrent && Options.IsSet("-activeoutline")) { outline = Options.Get("-activeoutline"); }
        else if (EffectiveState == CanvasItemState.Disabled && Options.IsSet("-disabledoutline"))
        {
            outline = Options.Get("-disabledoutline");
        }

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;

            SKColor fillColor;
            if (TkColor.TryParse(fill, out fillColor))
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = fillColor;
                canvas.DrawOval(rect, paint);
            }

            SKColor outlineColor;
            if (TkColor.TryParse(outline, out outlineColor))
            {
                double width = EffectiveWidth(_width);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = outlineColor;
                paint.StrokeWidth = (float)((width < 1.0) ? 1.0 : width);

                SKPathEffect dash = CanvasPaintHelper.CreateDashEffect(
                        Options.Get("-dash"), paint.StrokeWidth);
                if (dash != null) { paint.PathEffect = dash; }
                canvas.DrawOval(rect, paint);
                if (dash != null)
                {
                    paint.PathEffect = null;
                    dash.Dispose();
                }
            }
        }
    }
}
