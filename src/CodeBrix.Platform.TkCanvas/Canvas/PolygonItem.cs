using System;
using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>polygon</c> item — a port of Tk 8.6.16 tkCanvPoly.c: a
/// closed shape (auto-closed when the given coordinates do not end where
/// they start; the closing point is never reported by <c>coords</c>), filled
/// by default (black) with an optional outline, optional bezier smoothing,
/// and Tk's polygon-specific rule that points count as inside even when the
/// polygon is unfilled.
/// </summary>
public sealed class PolygonItem : CanvasItem
{
    private double _width = 1.0;
    private string _outline = "";
    private string _fill = "black";
    private JoinStyle _joinStyle = JoinStyle.Round;
    private bool _smooth;
    private int _splineSteps = 12;
    private int _autoClosed;

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "polygon"; }
    }

    private bool OutlineVisible
    {
        get { return _outline.Length != 0; }
    }

    private int NumPoints
    {
        get { return CoordArray.Length / 2; }
    }

    private protected override void ApplyCoords(IReadOnlyList<double> coords)
    {
        if ((coords.Count & 1) != 0)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected an even number, got " + coords.Count);
        }

        // Tk 8.6.16's polygon accepts any even coordinate count (verified against
        // real wish 8.6.16: 0, 2, and 4 coords are all accepted) — it imposes no
        // minimum-point rule, unlike the line item (which requires at least 4).
        // A degenerate two-point polygon whose endpoints differ is auto-closed
        // below into a three-point ring, so it still renders as a visible line —
        // this is exactly how DRAKON draws its "paw" connectors.

        _autoClosed = 0;
        int count = coords.Count;
        bool needsClose = count > 2
                && (coords[count - 2] != coords[0] || coords[count - 1] != coords[1]);
        CoordArray = new double[needsClose ? count + 2 : count];
        for (int i = 0; i < count; i++) { CoordArray[i] = coords[i]; }
        if (needsClose)
        {
            _autoClosed = 1;
            CoordArray[count] = coords[0];
            CoordArray[count + 1] = coords[1];
        }
    }

    private protected override IReadOnlyList<double> ReadCoords()
    {
        // The auto-added closing point is not reported (tkCanvPoly.c).
        int reported = 2 * (NumPoints - _autoClosed);
        var result = new double[reported];
        Array.Copy(CoordArray, result, reported);
        return result;
    }

    private protected override void OnConfigured()
    {
        _width = Options.GetDouble("-width", 1.0);
        if (_width < 0) { _width = 1.0; }
        _outline = Options.Get("-outline", "");
        _fill = Options.Get("-fill", "black");

        switch (Options.Get("-joinstyle", "round"))
        {
            case "miter": _joinStyle = JoinStyle.Miter; break;
            case "bevel": _joinStyle = JoinStyle.Bevel; break;
            default: _joinStyle = JoinStyle.Round; break;
        }

        string smooth = Options.Get("-smooth", "0");
        _smooth = smooth == "1" || smooth == "true" || smooth == "yes" || smooth == "on"
                || smooth == "bezier" || smooth == "raw";

        _splineSteps = Options.GetInt("-splinesteps", 12);
        if (_splineSteps < 1) { _splineSteps = 1; }
    }

    /// <summary>The point set used for hit-testing/painting: smoothed when <c>-smooth</c> is on.</summary>
    private double[] EffectivePoints(out int numPoints)
    {
        int rawPoints = NumPoints;
        if (_smooth && rawPoints > 2)
        {
            var output = new double[2 * TkCanvasMath.BezierPointCount(rawPoints, _splineSteps)];
            numPoints = TkCanvasMath.MakeBezierCurve(CoordArray, rawPoints, _splineSteps, output);
            return output;
        }
        numPoints = rawPoints;
        return CoordArray;
    }

    internal override void ComputeBounds()
    {
        if (NumPoints < 1 || EffectiveState == CanvasItemState.Hidden)
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

        SetHeaderBox((int)CoordArray[0], (int)CoordArray[1], (int)CoordArray[0], (int)CoordArray[1]);
        for (int i = 1; i < NumPoints - 1; i++)
        {
            IncludePoint(CoordArray[2 * i], CoordArray[2 * i + 1]);
        }

        if (OutlineVisible)
        {
            int bloat = (int)((width + 1.5) / 2.0);
            SetHeaderBox(X1 - bloat, Y1 - bloat, X2 + bloat, Y2 + bloat);

            if (_joinStyle == JoinStyle.Miter)
            {
                var miter = new double[4];
                int numPoints = NumPoints;

                if (numPoints > 3)
                {
                    int lastReal = 2 * (numPoints - 2);
                    if (TkCanvasMath.GetMiterPoints(
                            CoordArray[lastReal], CoordArray[lastReal + 1],
                            CoordArray[0], CoordArray[1],
                            CoordArray[2], CoordArray[3],
                            width, miter, 0, 2))
                    {
                        IncludePoint(miter[0], miter[1]);
                        IncludePoint(miter[2], miter[3]);
                    }
                }
                for (int i = numPoints, p = 0; i >= 3; i--, p += 2)
                {
                    if (TkCanvasMath.GetMiterPoints(
                            CoordArray[p], CoordArray[p + 1],
                            CoordArray[p + 2], CoordArray[p + 3],
                            CoordArray[p + 4], CoordArray[p + 5],
                            width, miter, 0, 2))
                    {
                        IncludePoint(miter[0], miter[1]);
                        IncludePoint(miter[2], miter[3]);
                    }
                }
            }
        }

        SetHeaderBox(X1 - 1, Y1 - 1, X2 + 1, Y2 + 1);
    }

    /// <inheritdoc/>
    public override double DistanceTo(double x, double y)
    {
        double width = EffectiveWidth(_width);
        double radius = width / 2.0;

        int numPoints;
        double[] points = EffectivePoints(out numPoints);

        double bestDist = TkCanvasMath.PolygonToPoint(points, numPoints, x, y);
        if (bestDist <= 0.0) { return bestDist < 0 ? 0.0 : bestDist; }

        if (OutlineVisible && (_joinStyle == JoinStyle.Round))
        {
            double dist = bestDist - radius;
            if (dist <= 0.0) { return 0.0; }
            bestDist = dist;
        }

        if (!OutlineVisible || (width <= 1)) { return bestDist; }

        var poly = new double[10];
        bool changedMiterToBevel = false;

        for (int count = numPoints, p = 0; count >= 2; count--, p += 2)
        {
            if (_joinStyle == JoinStyle.Round)
            {
                double dist = TkCanvasMath.Hypot(points[p] - x, points[p + 1] - y) - radius;
                if (dist <= 0.0) { return 0.0; }
                if (dist < bestDist) { bestDist = dist; }
            }

            if (count == numPoints)
            {
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3],
                        points[p], points[p + 1], width, false, poly, 0, 2);
            }
            else if ((_joinStyle == JoinStyle.Miter) && !changedMiterToBevel)
            {
                poly[0] = poly[6];
                poly[1] = poly[7];
                poly[2] = poly[4];
                poly[3] = poly[5];
            }
            else
            {
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3],
                        points[p], points[p + 1], width, false, poly, 0, 2);

                if ((_joinStyle == JoinStyle.Bevel) || changedMiterToBevel)
                {
                    poly[8] = poly[0];
                    poly[9] = poly[1];
                    double dist = TkCanvasMath.PolygonToPoint(poly, 5, x, y);
                    if (dist <= 0.0) { return 0.0; }
                    if (dist < bestDist) { bestDist = dist; }
                    changedMiterToBevel = false;
                }
            }

            if (count == 2)
            {
                TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], width, false, poly, 4, 6);
            }
            else if (_joinStyle == JoinStyle.Miter)
            {
                if (!TkCanvasMath.GetMiterPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], points[p + 4], points[p + 5],
                        width, poly, 4, 6))
                {
                    changedMiterToBevel = true;
                    TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                            points[p + 2], points[p + 3], width, false, poly, 4, 6);
                }
            }
            else
            {
                TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], width, false, poly, 4, 6);
            }
            poly[8] = poly[0];
            poly[9] = poly[1];
            double edgeDist = TkCanvasMath.PolygonToPoint(poly, 5, x, y);
            if (edgeDist <= 0.0) { return 0.0; }
            if (edgeDist < bestDist) { bestDist = edgeDist; }
        }

        return bestDist;
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        double width = EffectiveWidth(_width);
        double radius = width / 2.0;

        if (EffectiveState == CanvasItemState.Hidden || NumPoints < 2)
        {
            return -1;
        }
        if (NumPoints < 3)
        {
            var oval = new double[]
            {
                CoordArray[0] - radius, CoordArray[1] - radius,
                CoordArray[0] + radius, CoordArray[1] + radius,
            };
            return TkCanvasMath.OvalToArea(oval, rect);
        }

        int numPoints;
        double[] points;
        if (_smooth)
        {
            var output = new double[2 * TkCanvasMath.BezierPointCount(NumPoints, _splineSteps)];
            numPoints = TkCanvasMath.MakeBezierCurve(CoordArray, NumPoints, _splineSteps, output);
            points = output;
        }
        else
        {
            numPoints = NumPoints;
            points = CoordArray;
        }

        int inside = TkCanvasMath.PolygonToArea(points, numPoints, rect);
        if (inside == 0) { return 0; }
        if (!OutlineVisible) { return inside; }

        var poly = new double[10];
        var ovalBox = new double[4];
        bool changedMiterToBevel = false;

        for (int count = numPoints, p = 0; count >= 2; count--, p += 2)
        {
            if (_joinStyle == JoinStyle.Round)
            {
                ovalBox[0] = points[p] - radius;
                ovalBox[1] = points[p + 1] - radius;
                ovalBox[2] = points[p] + radius;
                ovalBox[3] = points[p + 1] + radius;
                if (TkCanvasMath.OvalToArea(ovalBox, rect) != inside) { return 0; }
            }

            if (count == numPoints)
            {
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3],
                        points[p], points[p + 1], width, false, poly, 0, 2);
            }
            else if ((_joinStyle == JoinStyle.Miter) && !changedMiterToBevel)
            {
                poly[0] = poly[6];
                poly[1] = poly[7];
                poly[2] = poly[4];
                poly[3] = poly[5];
            }
            else
            {
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3],
                        points[p], points[p + 1], width, false, poly, 0, 2);

                if ((_joinStyle == JoinStyle.Bevel) || changedMiterToBevel)
                {
                    poly[8] = poly[0];
                    poly[9] = poly[1];
                    if (TkCanvasMath.PolygonToArea(poly, 5, rect) != inside) { return 0; }
                    changedMiterToBevel = false;
                }
            }

            if (count == 2)
            {
                TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], width, false, poly, 4, 6);
            }
            else if (_joinStyle == JoinStyle.Miter)
            {
                if (!TkCanvasMath.GetMiterPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], points[p + 4], points[p + 5],
                        width, poly, 4, 6))
                {
                    changedMiterToBevel = true;
                    TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                            points[p + 2], points[p + 3], width, false, poly, 4, 6);
                }
            }
            else
            {
                TkCanvasMath.GetButtPoints(points[p], points[p + 1],
                        points[p + 2], points[p + 3], width, false, poly, 4, 6);
            }
            poly[8] = poly[0];
            poly[9] = poly[1];
            if (TkCanvasMath.PolygonToArea(poly, 5, rect) != inside) { return 0; }
        }

        return inside;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }
        if (NumPoints < 3) { return; }

        int numPoints;
        double[] points = EffectivePoints(out numPoints);

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
                CanvasPaintHelper.FillPolygon(canvas, paint, points, numPoints);
            }

            SKColor outlineColor;
            if (TkColor.TryParse(_outline, out outlineColor))
            {
                double width = EffectiveWidth(_width);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = outlineColor;
                paint.StrokeWidth = (float)((width < 1.0) ? 1.0 : width);
                paint.StrokeJoin = (_joinStyle == JoinStyle.Miter) ? SKStrokeJoin.Miter
                        : (_joinStyle == JoinStyle.Bevel) ? SKStrokeJoin.Bevel : SKStrokeJoin.Round;

                SKPathEffect dash = CanvasPaintHelper.CreateDashEffect(
                        Options.Get("-dash"), paint.StrokeWidth);
                if (dash != null) { paint.PathEffect = dash; }

                using (SKPath path = CanvasPaintHelper.BuildPolylinePath(points, numPoints, true))
                {
                    canvas.DrawPath(path, paint);
                }

                if (dash != null)
                {
                    paint.PathEffect = null;
                    dash.Dispose();
                }
            }
        }
    }
}
