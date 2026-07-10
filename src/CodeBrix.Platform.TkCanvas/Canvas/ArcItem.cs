using System;
using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>The three arc drawing styles (<c>-style</c>).</summary>
public enum ArcStyle
{
    /// <summary>A filled wedge from the oval centre (<c>pieslice</c>, the default).</summary>
    PieSlice,

    /// <summary>A region bounded by the arc and the chord joining its ends (<c>chord</c>).</summary>
    Chord,

    /// <summary>Just the curved outline segment, never filled (<c>arc</c>).</summary>
    Arc,
}

/// <summary>
/// The canvas <c>arc</c> item — a port of Tk 8.6.16 tkCanvArc.c: a section of
/// an oval (the box formed by two corner points) spanning <c>-start</c>
/// degrees through <c>-extent</c> degrees, drawn as a pie slice, chord, or
/// bare arc. Carries Tk's exact bounding box (the endpoints, oval centre for
/// pie slices, and any 3/6/9/12-o'clock extreme the sweep crosses), point
/// distance, and area classification — including the transformed
/// unit-circle line/arc intersection tests.
/// </summary>
public sealed class ArcItem : CanvasItem
{
    private const double DegToRad = Math.PI / 180.0;
    private const int ChordOutlinePoints = 7;
    private const int PieOutline1Points = 6;
    private const int PieOutline2Points = 7;

    private double _start;
    private double _extent = 90.0;
    private ArcStyle _style = ArcStyle.PieSlice;
    private double _width = 1.0;
    private string _outline = "black";
    private string _fill = "";

    private readonly double[] _center1 = new double[2];
    private readonly double[] _center2 = new double[2];
    private double[] _outlinePoints = new double[26];

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "arc"; }
    }

    /// <summary>The drawing style in effect (<c>-style</c>).</summary>
    public ArcStyle Style
    {
        get { return _style; }
    }

    private bool OutlineVisible
    {
        get { return _outline.Length != 0; }
    }

    // The arc fill only applies to pie-slice and chord styles (ARC_STYLE
    // never fills); this mirrors tkCanvArc.c's fillGC being NULL for arcs.
    private bool FillVisible
    {
        get { return _style != ArcStyle.Arc && _fill.Length != 0; }
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

        switch (FirstNonEmpty(Options.Get("-style", "pieslice")))
        {
            case 'a': _style = ArcStyle.Arc; break;
            case 'c': _style = ArcStyle.Chord; break;
            default: _style = ArcStyle.PieSlice; break;
        }

        _start = Options.GetDouble("-start", 0.0);
        _extent = Options.GetDouble("-extent", 90.0);

        // Normalize as ConfigureArc does: start into [0, 360), extent into
        // (-360, 360).
        int i = (int)(_start / 360.0);
        _start -= i * 360.0;
        if (_start < 0) { _start += 360.0; }
        i = (int)(_extent / 360.0);
        _extent -= i * 360.0;
    }

    private static char FirstNonEmpty(string text)
    {
        return (text.Length > 0) ? text[0] : 'p';
    }

    private protected override IReadOnlyList<double> ReadCoords()
    {
        // ArcCoords with no args returns the (normalized) bbox corners.
        return (double[])CoordArray.Clone();
    }

    internal override void ComputeBounds()
    {
        if (CoordArray.Length < 4) { SetEmptyHeaderBox(); return; }

        double width = _width;
        if (width < 1.0) { width = 1.0; }

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

        // Make sure the first coordinates are the lowest ones.
        if (CoordArray[1] > CoordArray[3])
        {
            double tmpv = CoordArray[3];
            CoordArray[3] = CoordArray[1];
            CoordArray[1] = tmpv;
        }
        if (CoordArray[0] > CoordArray[2])
        {
            double tmpv = CoordArray[2];
            CoordArray[2] = CoordArray[0];
            CoordArray[0] = tmpv;
        }

        ComputeArcOutline();

        SetHeaderBox((int)_center1[0], (int)_center1[1], (int)_center1[0], (int)_center1[1]);
        IncludePoint(_center2[0], _center2[1]);

        double centerX = (CoordArray[0] + CoordArray[2]) / 2.0;
        double centerY = (CoordArray[1] + CoordArray[3]) / 2.0;
        if (_style == ArcStyle.PieSlice) { IncludePoint(centerX, centerY); }

        double tmp = -_start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent)) { IncludePoint(CoordArray[2], centerY); }
        tmp = 90.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent)) { IncludePoint(centerX, CoordArray[1]); }
        tmp = 180.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent)) { IncludePoint(CoordArray[0], centerY); }
        tmp = 270.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent)) { IncludePoint(centerX, CoordArray[3]); }

        int bloat = OutlineVisible ? (int)((width + 1.0) / 2.0 + 1) : 1;
        SetHeaderBox(X1 - bloat, Y1 - bloat, X2 + bloat, Y2 + bloat);
    }

    /// <summary>
    /// Computes the two arc-endpoint centres and the chord/pie outline
    /// polygon(s) — the port of tkCanvArc.c ComputeArcOutline.
    /// </summary>
    private void ComputeArcOutline()
    {
        double boxWidth = CoordArray[2] - CoordArray[0];
        double boxHeight = CoordArray[3] - CoordArray[1];
        double angle = -_start * DegToRad;
        double sin1 = Math.Sin(angle);
        double cos1 = Math.Cos(angle);
        angle -= _extent * DegToRad;
        double sin2 = Math.Sin(angle);
        double cos2 = Math.Cos(angle);
        double vertexX = (CoordArray[0] + CoordArray[2]) / 2.0;
        double vertexY = (CoordArray[1] + CoordArray[3]) / 2.0;
        _center1[0] = vertexX + cos1 * boxWidth / 2.0;
        _center1[1] = vertexY + sin1 * boxHeight / 2.0;
        _center2[0] = vertexX + cos2 * boxWidth / 2.0;
        _center2[1] = vertexY + sin2 * boxHeight / 2.0;

        double width = EffectiveWidth(_width);
        double halfWidth = width / 2.0;

        double a1 = (((boxWidth * sin1) == 0.0) && ((boxHeight * cos1) == 0.0))
                ? 0.0 : Math.Atan2(boxWidth * sin1, boxHeight * cos1);
        double corner1X = _center1[0] + Math.Cos(a1) * halfWidth;
        double corner1Y = _center1[1] + Math.Sin(a1) * halfWidth;

        double a2 = (((boxWidth * sin2) == 0.0) && ((boxHeight * cos2) == 0.0))
                ? 0.0 : Math.Atan2(boxWidth * sin2, boxHeight * cos2);
        double corner2X = _center2[0] + Math.Cos(a2) * halfWidth;
        double corner2Y = _center2[1] + Math.Sin(a2) * halfWidth;

        double[] o = _outlinePoints;
        if (_style == ArcStyle.Chord)
        {
            o[0] = o[12] = corner1X;
            o[1] = o[13] = corner1Y;
            GetButtPoints(_center2, _center1, width, o, 10, 2);
            o[4] = _center2[0] + o[2] - _center1[0];
            o[5] = _center2[1] + o[3] - _center1[1];
            o[6] = corner2X;
            o[7] = corner2Y;
            o[8] = _center2[0] + o[10] - _center1[0];
            o[9] = _center2[1] + o[11] - _center1[1];
        }
        else if (_style == ArcStyle.PieSlice)
        {
            var vertex = new double[] { vertexX, vertexY };
            GetButtPoints(_center1, vertex, width, o, 0, 2);
            o[4] = _center1[0] + o[2] - vertexX;
            o[5] = _center1[1] + o[3] - vertexY;
            o[6] = corner1X;
            o[7] = corner1Y;
            o[8] = _center1[0] + o[0] - vertexX;
            o[9] = _center1[1] + o[1] - vertexY;
            o[10] = o[0];
            o[11] = o[1];

            GetButtPoints(_center2, vertex, width, o, 12, 16);
            if ((_extent > 180) || ((_extent < 0) && (_extent > -180)))
            {
                o[14] = o[0];
                o[15] = o[1];
            }
            else
            {
                o[14] = o[2];
                o[15] = o[3];
            }
            o[18] = _center2[0] + o[16] - vertexX;
            o[19] = _center2[1] + o[17] - vertexY;
            o[20] = corner2X;
            o[21] = corner2Y;
            o[22] = _center2[0] + o[12] - vertexX;
            o[23] = _center2[1] + o[13] - vertexY;
            o[24] = o[12];
            o[25] = o[13];
        }
    }

    // TkGetButtPoints written into o[m1..m1+1] and o[m2..m2+1] (never
    // projecting for arcs).
    private static void GetButtPoints(double[] p1, double[] p2, double width,
            double[] o, int m1, int m2)
    {
        double w = width * 0.5;
        double length = TkCanvasMath.Hypot(p2[0] - p1[0], p2[1] - p1[1]);
        if (length == 0.0)
        {
            o[m1] = o[m2] = p2[0];
            o[m1 + 1] = o[m2 + 1] = p2[1];
        }
        else
        {
            double deltaX = -w * (p2[1] - p1[1]) / length;
            double deltaY = w * (p2[0] - p1[0]) / length;
            o[m1] = p2[0] + deltaX;
            o[m2] = p2[0] - deltaX;
            o[m1 + 1] = p2[1] + deltaY;
            o[m2 + 1] = p2[1] - deltaY;
        }
    }

    /// <inheritdoc/>
    public override double DistanceTo(double x, double y)
    {
        double width = EffectiveWidth(_width);

        double vertexX = (CoordArray[0] + CoordArray[2]) / 2.0;
        double vertexY = (CoordArray[1] + CoordArray[3]) / 2.0;
        double t1 = CoordArray[3] - CoordArray[1];
        if (t1 != 0.0) { t1 = (y - vertexY) / t1; }
        double t2 = CoordArray[2] - CoordArray[0];
        if (t2 != 0.0) { t2 = (x - vertexX) / t2; }
        double pointAngle = ((t1 == 0.0) && (t2 == 0.0)) ? 0 : -Math.Atan2(t1, t2) * 180 / Math.PI;
        double diff = pointAngle - _start;
        diff -= ((int)(diff / 360.0)) * 360.0;
        if (diff < 0) { diff += 360.0; }
        bool angleInRange = (diff <= _extent) || ((_extent < 0) && ((diff - 360.0) >= _extent));

        if (_style == ArcStyle.Arc)
        {
            if (angleInRange)
            {
                return TkCanvasMath.OvalToPoint(CoordArray, width, false, x, y);
            }
            double d1 = TkCanvasMath.Hypot(x - _center1[0], y - _center1[1]);
            double d2 = TkCanvasMath.Hypot(x - _center2[0], y - _center2[1]);
            return (d2 < d1) ? d2 : d1;
        }

        bool filled = FillVisible || !OutlineVisible;
        if (!OutlineVisible) { width = 0.0; }

        if (_style == ArcStyle.PieSlice)
        {
            double dist, newDist;
            if (width > 1.0)
            {
                dist = PolygonToPoint(0, PieOutline1Points, x, y);
                newDist = PolygonToPoint(2 * PieOutline1Points, PieOutline2Points, x, y);
            }
            else
            {
                dist = TkCanvasMath.LineToPoint(vertexX, vertexY, _center1[0], _center1[1], x, y);
                newDist = TkCanvasMath.LineToPoint(vertexX, vertexY, _center2[0], _center2[1], x, y);
            }
            if (newDist < dist) { dist = newDist; }
            if (angleInRange)
            {
                newDist = TkCanvasMath.OvalToPoint(CoordArray, width, filled, x, y);
                if (newDist < dist) { dist = newDist; }
            }
            return dist;
        }

        // Chord.
        double cdist;
        if (width > 1.0)
        {
            cdist = PolygonToPoint(0, ChordOutlinePoints, x, y);
        }
        else
        {
            cdist = TkCanvasMath.LineToPoint(
                    _center1[0], _center1[1], _center2[0], _center2[1], x, y);
        }
        var poly = new double[]
        {
            vertexX, vertexY, _center1[0], _center1[1],
            _center2[0], _center2[1], vertexX, vertexY,
        };
        double polyDist = TkCanvasMath.PolygonToPoint(poly, 4, x, y);
        if (angleInRange)
        {
            if ((_extent < -180.0) || (_extent > 180.0) || (polyDist > 0.0))
            {
                double newDist = TkCanvasMath.OvalToPoint(CoordArray, width, filled, x, y);
                if (newDist < cdist) { cdist = newDist; }
            }
        }
        else
        {
            if ((_extent < -180.0) || (_extent > 180.0))
            {
                if (filled && (polyDist < cdist)) { cdist = polyDist; }
            }
        }
        return cdist;
    }

    private double PolygonToPoint(int offset, int numPoints, double px, double py)
    {
        var slice = new double[2 * numPoints];
        Array.Copy(_outlinePoints, offset, slice, 0, 2 * numPoints);
        return TkCanvasMath.PolygonToPoint(slice, numPoints, px, py);
    }

    private int PolygonToArea(int offset, int numPoints, double[] rect)
    {
        var slice = new double[2 * numPoints];
        Array.Copy(_outlinePoints, offset, slice, 0, 2 * numPoints);
        return TkCanvasMath.PolygonToArea(slice, numPoints, rect);
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rectPtr)
    {
        double width = EffectiveWidth(_width);
        bool filled = FillVisible || !OutlineVisible;
        if (!OutlineVisible) { width = 0.0; }

        double centerX = (CoordArray[0] + CoordArray[2]) / 2.0;
        double centerY = (CoordArray[1] + CoordArray[3]) / 2.0;
        var tRect = new double[]
        {
            rectPtr[0] - centerX, rectPtr[1] - centerY,
            rectPtr[2] - centerX, rectPtr[3] - centerY,
        };
        double rx = CoordArray[2] - centerX + width / 2.0;
        double ry = CoordArray[3] - centerY + width / 2.0;

        var points = new double[20];
        int pi = 0;
        double angle = -_start * DegToRad;
        points[pi++] = rx * Math.Cos(angle);
        points[pi++] = ry * Math.Sin(angle);
        angle += -_extent * DegToRad;
        points[pi++] = rx * Math.Cos(angle);
        points[pi++] = ry * Math.Sin(angle);
        int numPoints = 2;

        if ((_style == ArcStyle.PieSlice) && (_extent < 180.0))
        {
            points[pi++] = 0.0;
            points[pi++] = 0.0;
            numPoints++;
        }

        double tmp = -_start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent))
        {
            points[pi++] = rx; points[pi++] = 0.0; numPoints++;
        }
        tmp = 90.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent))
        {
            points[pi++] = 0.0; points[pi++] = -ry; numPoints++;
        }
        tmp = 180.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent))
        {
            points[pi++] = -rx; points[pi++] = 0.0; numPoints++;
        }
        tmp = 270.0 - _start;
        if (tmp < 0) { tmp += 360.0; }
        if ((tmp < _extent) || ((tmp - 360) > _extent))
        {
            points[pi++] = 0.0; points[pi++] = ry; numPoints++;
        }

        bool inside = (points[0] > tRect[0]) && (points[0] < tRect[2])
                && (points[1] > tRect[1]) && (points[1] < tRect[3]);
        for (int p = 2, count = numPoints; count > 1; p += 2, count--)
        {
            bool newInside = (points[p] > tRect[0]) && (points[p] < tRect[2])
                    && (points[p + 1] > tRect[1]) && (points[p + 1] < tRect[3]);
            if (newInside != inside) { return 0; }
        }

        if (inside) { return 1; }

        if (_style == ArcStyle.PieSlice)
        {
            if (width >= 1.0)
            {
                if (PolygonToArea(0, PieOutline1Points, rectPtr) != -1) { return 0; }
                if (PolygonToArea(2 * PieOutline1Points, PieOutline2Points, rectPtr) != -1) { return 0; }
            }
            else
            {
                var center = new double[] { centerX, centerY };
                if ((TkCanvasMath.LineToArea(center[0], center[1], _center1[0], _center1[1], rectPtr) != -1)
                        || (TkCanvasMath.LineToArea(center[0], center[1], _center2[0], _center2[1], rectPtr) != -1))
                {
                    return 0;
                }
            }
        }
        else if (_style == ArcStyle.Chord)
        {
            if (width >= 1.0)
            {
                if (PolygonToArea(0, ChordOutlinePoints, rectPtr) != -1) { return 0; }
            }
            else
            {
                if (TkCanvasMath.LineToArea(_center1[0], _center1[1], _center2[0], _center2[1], rectPtr) != -1)
                {
                    return 0;
                }
            }
        }

        if (HorizLineToArc(tRect[0], tRect[2], tRect[1], rx, ry)
                || HorizLineToArc(tRect[0], tRect[2], tRect[3], rx, ry)
                || VertLineToArc(tRect[0], tRect[1], tRect[3], rx, ry)
                || VertLineToArc(tRect[2], tRect[1], tRect[3], rx, ry))
        {
            return 0;
        }
        if ((width > 1.0) && !filled)
        {
            double rx2 = rx - width;
            double ry2 = ry - width;
            if (HorizLineToArc(tRect[0], tRect[2], tRect[1], rx2, ry2)
                    || HorizLineToArc(tRect[0], tRect[2], tRect[3], rx2, ry2)
                    || VertLineToArc(tRect[0], tRect[1], tRect[3], rx2, ry2)
                    || VertLineToArc(tRect[2], tRect[1], tRect[3], rx2, ry2))
            {
                return 0;
            }
        }

        if (DistanceTo(rectPtr[0], rectPtr[1]) == 0.0) { return 0; }
        return -1;
    }

    private bool HorizLineToArc(double x1, double x2, double y, double rx, double ry)
    {
        double ty = y / ry;
        double tmp = 1 - ty * ty;
        if (tmp < 0) { return false; }
        double tx = Math.Sqrt(tmp);
        double x = tx * rx;
        if ((x >= x1) && (x <= x2) && AngleInRange(tx, ty)) { return true; }
        if ((-x >= x1) && (-x <= x2) && AngleInRange(-tx, ty)) { return true; }
        return false;
    }

    private bool VertLineToArc(double x, double y1, double y2, double rx, double ry)
    {
        double tx = x / rx;
        double tmp = 1 - tx * tx;
        if (tmp < 0) { return false; }
        double ty = Math.Sqrt(tmp);
        double y = ty * ry;
        if ((y > y1) && (y < y2) && AngleInRange(tx, ty)) { return true; }
        if ((-y > y1) && (-y < y2) && AngleInRange(tx, -ty)) { return true; }
        return false;
    }

    private bool AngleInRange(double x, double y)
    {
        if ((x == 0.0) && (y == 0.0)) { return true; }
        double diff = -Math.Atan2(y, x);
        diff = diff * (180.0 / Math.PI) - _start;
        while (diff > 360.0) { diff -= 360.0; }
        while (diff < 0.0) { diff += 360.0; }
        if (_extent >= 0) { return diff <= _extent; }
        return (diff - 360.0) >= _extent;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }
        if (CoordArray.Length < 4) { return; }

        var oval = new SKRect(
                (float)CoordArray[0], (float)CoordArray[1],
                (float)CoordArray[2], (float)CoordArray[3]);
        float startAngle = (float)(-_start);
        float sweepAngle = (float)(-_extent);

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
            if (FillVisible && TkColor.TryParse(fill, out fillColor))
            {
                var builder = new SKPathBuilder();
                if (_style == ArcStyle.PieSlice)
                {
                    builder.MoveTo((oval.Left + oval.Right) / 2f, (oval.Top + oval.Bottom) / 2f);
                    builder.ArcTo(oval, startAngle, sweepAngle, false);
                    builder.Close();
                }
                else
                {
                    builder.ArcTo(oval, startAngle, sweepAngle, true);
                    builder.Close();
                }
                paint.Style = SKPaintStyle.Fill;
                paint.Color = fillColor;
                using (SKPath path = builder.Detach()) { canvas.DrawPath(path, paint); }
            }

            SKColor outlineColor;
            if (OutlineVisible && TkColor.TryParse(outline, out outlineColor))
            {
                double width = EffectiveWidth(_width);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = outlineColor;
                paint.StrokeWidth = (float)((width < 1.0) ? 1.0 : width);

                var builder = new SKPathBuilder();
                builder.ArcTo(oval, startAngle, sweepAngle, true);
                if (_style == ArcStyle.PieSlice)
                {
                    builder.LineTo((oval.Left + oval.Right) / 2f, (oval.Top + oval.Bottom) / 2f);
                    builder.Close();
                }
                else if (_style == ArcStyle.Chord)
                {
                    builder.Close();
                }

                SKPathEffect dash = CanvasPaintHelper.CreateDashEffect(
                        Options.Get("-dash"), paint.StrokeWidth);
                if (dash != null) { paint.PathEffect = dash; }
                using (SKPath path = builder.Detach()) { canvas.DrawPath(path, paint); }
                if (dash != null)
                {
                    paint.PathEffect = null;
                    dash.Dispose();
                }
            }
        }
    }
}
