using System;
using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>Where a line carries arrowheads (<c>-arrow</c>).</summary>
public enum ArrowStyle
{
    /// <summary>No arrowheads (<c>none</c>).</summary>
    None,

    /// <summary>An arrowhead at the first point (<c>first</c>).</summary>
    First,

    /// <summary>An arrowhead at the last point (<c>last</c>).</summary>
    Last,

    /// <summary>Arrowheads at both ends (<c>both</c>).</summary>
    Both,
}

/// <summary>
/// The canvas <c>line</c> item — a port of Tk 8.6.16 tkCanvLine.c: an open
/// polyline with width, cap/join styles, optional bezier smoothing
/// (<c>-smooth</c>/<c>-splinesteps</c>), optional arrowheads
/// (<c>-arrow</c>/<c>-arrowshape</c>, including Tk's endpoint backup so the
/// line ends inside the arrowhead), and dash support for painting.
/// </summary>
public sealed class LineItem : CanvasItem
{
    private const int PointsInArrow = 6;

    private double _width = 1.0;
    private CapStyle _capStyle = CapStyle.Butt;
    private JoinStyle _joinStyle = JoinStyle.Round;
    private bool _smooth;
    private int _splineSteps = 12;
    private ArrowStyle _arrow = ArrowStyle.None;
    private double _arrowShapeA = 8;
    private double _arrowShapeB = 10;
    private double _arrowShapeC = 3;
    private string _fill = "black";
    private double[] _firstArrow;
    private double[] _lastArrow;

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "line"; }
    }

    /// <summary>The arrowhead placement in effect.</summary>
    public ArrowStyle Arrow
    {
        get { return _arrow; }
    }

    /// <summary>Whether bezier smoothing is on.</summary>
    public bool Smooth
    {
        get { return _smooth; }
    }

    private protected override void ApplyCoords(IReadOnlyList<double> coords)
    {
        if ((coords.Count & 1) != 0)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected an even number, got " + coords.Count);
        }
        if (coords.Count < 4)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected at least 4, got " + coords.Count);
        }

        CoordArray = new double[coords.Count];
        for (int i = 0; i < coords.Count; i++) { CoordArray[i] = coords[i]; }

        // New coordinates invalidate previously computed arrow polygons;
        // ConfigureArrows rebuilds them (and backs up the endpoints).
        _firstArrow = null;
        _lastArrow = null;
        if (_arrow != ArrowStyle.None) { ConfigureArrows(); }
    }

    private protected override IReadOnlyList<double> ReadCoords()
    {
        // Arrowed ends report the ORIGINAL endpoints stored in the arrow
        // polygons, not the backed-up interior points (tkCanvLine.c
        // LineCoords).
        var result = (double[])CoordArray.Clone();
        if (_firstArrow != null)
        {
            result[0] = _firstArrow[0];
            result[1] = _firstArrow[1];
        }
        if (_lastArrow != null && result.Length >= 2)
        {
            result[result.Length - 2] = _lastArrow[0];
            result[result.Length - 1] = _lastArrow[1];
        }
        return result;
    }

    private protected override void OnConfigured()
    {
        _width = Options.GetDouble("-width", 1.0);
        if (_width < 0) { _width = 1.0; }

        switch (Options.Get("-capstyle", "butt"))
        {
            case "round": _capStyle = CapStyle.Round; break;
            case "projecting": _capStyle = CapStyle.Projecting; break;
            default: _capStyle = CapStyle.Butt; break;
        }

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

        ArrowStyle oldArrow = _arrow;
        switch (Options.Get("-arrow", "none"))
        {
            case "first": _arrow = ArrowStyle.First; break;
            case "last": _arrow = ArrowStyle.Last; break;
            case "both": _arrow = ArrowStyle.Both; break;
            default: _arrow = ArrowStyle.None; break;
        }

        var shape = TclString.SplitList(Options.Get("-arrowshape", "8 10 3"));
        if (shape.Count == 3)
        {
            double a, b, c;
            if (TclString.TryParseCoord(shape[0], out a)
                    && TclString.TryParseCoord(shape[1], out b)
                    && TclString.TryParseCoord(shape[2], out c))
            {
                _arrowShapeA = a;
                _arrowShapeB = b;
                _arrowShapeC = c;
            }
        }

        _fill = Options.Get("-fill", "black");

        if (_arrow == ArrowStyle.None && oldArrow != ArrowStyle.None)
        {
            // Restore the original endpoints the arrows had backed up.
            if (_firstArrow != null && CoordArray.Length >= 2)
            {
                CoordArray[0] = _firstArrow[0];
                CoordArray[1] = _firstArrow[1];
            }
            if (_lastArrow != null && CoordArray.Length >= 2)
            {
                CoordArray[CoordArray.Length - 2] = _lastArrow[0];
                CoordArray[CoordArray.Length - 1] = _lastArrow[1];
            }
            _firstArrow = null;
            _lastArrow = null;
        }
        else if (_arrow != ArrowStyle.None && CoordArray.Length >= 4)
        {
            ConfigureArrows();
        }
    }

    /// <summary>
    /// Computes the arrowhead polygons and backs the line endpoints up into
    /// the arrowheads — the port of tkCanvLine.c ConfigureArrows.
    /// </summary>
    private void ConfigureArrows()
    {
        if (CoordArray.Length < 4) { return; }

        double width = EffectiveWidth(_width);
        double shapeA = _arrowShapeA + 0.001;
        double shapeB = _arrowShapeB + 0.001;
        double shapeC = _arrowShapeC + width / 2.0 + 0.001;
        double fracHeight = (width / 2.0) / shapeC;
        double backup = fracHeight * shapeB + shapeA * (1.0 - fracHeight) / 2.0;

        if (_arrow != ArrowStyle.Last)
        {
            double[] poly = _firstArrow;
            if (poly == null)
            {
                poly = new double[2 * PointsInArrow];
                poly[0] = poly[10] = CoordArray[0];
                poly[1] = poly[11] = CoordArray[1];
                _firstArrow = poly;
            }
            FillArrowPolygon(poly, CoordArray[2], CoordArray[3],
                    shapeA, shapeB, shapeC, fracHeight);
            double dx = poly[0] - CoordArray[2];
            double dy = poly[1] - CoordArray[3];
            double length = TkCanvasMath.Hypot(dx, dy);
            double sinTheta = (length == 0) ? 0.0 : dy / length;
            double cosTheta = (length == 0) ? 0.0 : dx / length;
            CoordArray[0] = poly[0] - backup * cosTheta;
            CoordArray[1] = poly[1] - backup * sinTheta;
        }

        if (_arrow != ArrowStyle.First)
        {
            int last = CoordArray.Length - 2;
            double[] poly = _lastArrow;
            if (poly == null)
            {
                poly = new double[2 * PointsInArrow];
                poly[0] = poly[10] = CoordArray[last];
                poly[1] = poly[11] = CoordArray[last + 1];
                _lastArrow = poly;
            }
            FillArrowPolygon(poly, CoordArray[last - 2], CoordArray[last - 1],
                    shapeA, shapeB, shapeC, fracHeight);
            double dx = poly[0] - CoordArray[last - 2];
            double dy = poly[1] - CoordArray[last - 1];
            double length = TkCanvasMath.Hypot(dx, dy);
            double sinTheta = (length == 0) ? 0.0 : dy / length;
            double cosTheta = (length == 0) ? 0.0 : dx / length;
            CoordArray[last] = poly[0] - backup * cosTheta;
            CoordArray[last + 1] = poly[1] - backup * sinTheta;
        }
    }

    private static void FillArrowPolygon(double[] poly, double towardX, double towardY,
            double shapeA, double shapeB, double shapeC, double fracHeight)
    {
        double dx = poly[0] - towardX;
        double dy = poly[1] - towardY;
        double length = TkCanvasMath.Hypot(dx, dy);
        double sinTheta, cosTheta;
        if (length == 0)
        {
            sinTheta = cosTheta = 0.0;
        }
        else
        {
            sinTheta = dy / length;
            cosTheta = dx / length;
        }
        double vertX = poly[0] - shapeA * cosTheta;
        double vertY = poly[1] - shapeA * sinTheta;
        double temp = shapeC * sinTheta;
        poly[2] = poly[0] - shapeB * cosTheta + temp;
        poly[8] = poly[2] - 2 * temp;
        temp = shapeC * cosTheta;
        poly[3] = poly[1] - shapeB * sinTheta - temp;
        poly[9] = poly[3] + 2 * temp;
        poly[4] = poly[2] * fracHeight + vertX * (1.0 - fracHeight);
        poly[5] = poly[3] * fracHeight + vertY * (1.0 - fracHeight);
        poly[6] = poly[8] * fracHeight + vertX * (1.0 - fracHeight);
        poly[7] = poly[9] * fracHeight + vertY * (1.0 - fracHeight);
    }

    internal override void Translate(double dx, double dy)
    {
        // Arrow polygons move with the line (tkCanvLine.c TranslateLine).
        for (int i = 0; i + 1 < CoordArray.Length; i += 2)
        {
            CoordArray[i] += dx;
            CoordArray[i + 1] += dy;
        }
        if (_firstArrow != null)
        {
            for (int i = 0; i < 2 * PointsInArrow; i += 2)
            {
                _firstArrow[i] += dx;
                _firstArrow[i + 1] += dy;
            }
        }
        if (_lastArrow != null)
        {
            for (int i = 0; i < 2 * PointsInArrow; i += 2)
            {
                _lastArrow[i] += dx;
                _lastArrow[i + 1] += dy;
            }
        }
        ComputeBounds();
    }

    internal override void Scale(double originX, double originY, double scaleX, double scaleY)
    {
        // Restore the original endpoints before scaling, then rebuild the
        // arrowheads from the scaled geometry (tkCanvLine.c ScaleLine).
        if (_firstArrow != null)
        {
            CoordArray[0] = _firstArrow[0];
            CoordArray[1] = _firstArrow[1];
            _firstArrow = null;
        }
        if (_lastArrow != null)
        {
            CoordArray[CoordArray.Length - 2] = _lastArrow[0];
            CoordArray[CoordArray.Length - 1] = _lastArrow[1];
            _lastArrow = null;
        }
        for (int i = 0; i + 1 < CoordArray.Length; i += 2)
        {
            CoordArray[i] = originX + scaleX * (CoordArray[i] - originX);
            CoordArray[i + 1] = originY + scaleY * (CoordArray[i + 1] - originY);
        }
        if (_arrow != ArrowStyle.None) { ConfigureArrows(); }
        ComputeBounds();
    }

    /// <summary>The polyline used for hit-testing/painting: smoothed when <c>-smooth</c> is on.</summary>
    private double[] EffectivePoints(out int numPoints)
    {
        int rawPoints = CoordArray.Length / 2;
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
        int numPoints = CoordArray.Length / 2;
        if (numPoints == 0 || EffectiveState == CanvasItemState.Hidden)
        {
            SetEmptyHeaderBox();
            return;
        }

        SetHeaderBox((int)CoordArray[0], (int)CoordArray[1], (int)CoordArray[0], (int)CoordArray[1]);
        for (int i = 1; i < numPoints; i++)
        {
            IncludePoint(CoordArray[2 * i], CoordArray[2 * i + 1]);
        }

        double width = _width;
        if (width < 1.0) { width = 1.0; }

        if (_arrow != ArrowStyle.None)
        {
            if (_arrow != ArrowStyle.Last && _firstArrow != null)
            {
                IncludePoint(_firstArrow[0], _firstArrow[1]);
            }
            if (_arrow != ArrowStyle.First && _lastArrow != null)
            {
                IncludePoint(_lastArrow[0], _lastArrow[1]);
            }
        }

        int intWidth = (int)(width + 0.5);
        SetHeaderBox(X1 - intWidth, Y1 - intWidth, X2 + intWidth, Y2 + intWidth);

        if (numPoints == 1)
        {
            SetHeaderBox(X1 - 1, Y1 - 1, X2 + 1, Y2 + 1);
            return;
        }

        if (_joinStyle == JoinStyle.Miter)
        {
            var miter = new double[4];
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

        if (_arrow != ArrowStyle.None)
        {
            if (_arrow != ArrowStyle.Last && _firstArrow != null)
            {
                for (int i = 0; i < PointsInArrow; i++)
                {
                    IncludePoint(_firstArrow[2 * i], _firstArrow[2 * i + 1]);
                }
            }
            if (_arrow != ArrowStyle.First && _lastArrow != null)
            {
                for (int i = 0; i < PointsInArrow; i++)
                {
                    IncludePoint(_lastArrow[2 * i], _lastArrow[2 * i + 1]);
                }
            }
        }

        SetHeaderBox(X1 - 1, Y1 - 1, X2 + 1, Y2 + 1);
    }

    /// <inheritdoc/>
    public override double DistanceTo(double x, double y)
    {
        double bestDist = 1.0e36;
        int numPoints;
        double[] points = EffectivePoints(out numPoints);

        double width = EffectiveWidth(_width);
        if (width < 1.0) { width = 1.0; }

        if (numPoints == 0 || State == CanvasItemState.Hidden)
        {
            return bestDist;
        }
        if (numPoints == 1)
        {
            bestDist = TkCanvasMath.Hypot(points[0] - x, points[1] - y) - width / 2.0;
            return (bestDist < 0) ? 0 : bestDist;
        }

        var poly = new double[10];
        bool changedMiterToBevel = false;

        for (int count = numPoints, p = 0; count >= 2; count--, p += 2)
        {
            if (((_capStyle == CapStyle.Round) && (count == numPoints))
                    || ((_joinStyle == JoinStyle.Round) && (count != numPoints)))
            {
                double roundDist = TkCanvasMath.Hypot(points[p] - x, points[p + 1] - y) - width / 2.0;
                if (roundDist <= 0.0) { return 0.0; }
                if (roundDist < bestDist) { bestDist = roundDist; }
            }

            if (count == numPoints)
            {
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3], points[p], points[p + 1],
                        width, _capStyle == CapStyle.Projecting, poly, 0, 2);
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
                TkCanvasMath.GetButtPoints(points[p + 2], points[p + 3], points[p], points[p + 1],
                        width, false, poly, 0, 2);

                if ((_joinStyle == JoinStyle.Bevel) || changedMiterToBevel)
                {
                    poly[8] = poly[0];
                    poly[9] = poly[1];
                    double wedgeDist = TkCanvasMath.PolygonToPoint(poly, 5, x, y);
                    if (wedgeDist <= 0.0) { return 0.0; }
                    if (wedgeDist < bestDist) { bestDist = wedgeDist; }
                    changedMiterToBevel = false;
                }
            }

            if (count == 2)
            {
                TkCanvasMath.GetButtPoints(points[p], points[p + 1], points[p + 2], points[p + 3],
                        width, _capStyle == CapStyle.Projecting, poly, 4, 6);
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
            double dist = TkCanvasMath.PolygonToPoint(poly, 5, x, y);
            if (dist <= 0.0) { return 0.0; }
            if (dist < bestDist) { bestDist = dist; }
        }

        if (_capStyle == CapStyle.Round)
        {
            int last = 2 * (numPoints - 1);
            double dist = TkCanvasMath.Hypot(points[last] - x, points[last + 1] - y) - width / 2.0;
            if (dist <= 0.0) { return 0.0; }
            if (dist < bestDist) { bestDist = dist; }
        }

        if (_arrow != ArrowStyle.None)
        {
            if (_arrow != ArrowStyle.Last && _firstArrow != null)
            {
                double dist = TkCanvasMath.PolygonToPoint(_firstArrow, PointsInArrow, x, y);
                if (dist <= 0.0) { return 0.0; }
                if (dist < bestDist) { bestDist = dist; }
            }
            if (_arrow != ArrowStyle.First && _lastArrow != null)
            {
                double dist = TkCanvasMath.PolygonToPoint(_lastArrow, PointsInArrow, x, y);
                if (dist <= 0.0) { return 0.0; }
                if (dist < bestDist) { bestDist = dist; }
            }
        }

        return bestDist;
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        int rawPoints = CoordArray.Length / 2;
        double width = EffectiveWidth(_width);
        double radius = (width + 1.0) / 2.0;

        if (EffectiveState == CanvasItemState.Hidden || rawPoints == 0)
        {
            return -1;
        }
        if (rawPoints == 1)
        {
            var oval = new double[]
            {
                CoordArray[0] - radius, CoordArray[1] - radius,
                CoordArray[0] + radius, CoordArray[1] + radius,
            };
            return TkCanvasMath.OvalToArea(oval, rect);
        }

        int numPoints;
        double[] points = EffectivePoints(out numPoints);

        if (width < 1.0) { width = 1.0; }

        int result = TkCanvasMath.ThickPolyLineToArea(
                points, numPoints, width, _capStyle, _joinStyle, rect);
        if (result == 0) { return 0; }

        if (_arrow != ArrowStyle.None)
        {
            if (_arrow != ArrowStyle.Last && _firstArrow != null)
            {
                if (TkCanvasMath.PolygonToArea(_firstArrow, PointsInArrow, rect) != result)
                {
                    return 0;
                }
            }
            if (_arrow != ArrowStyle.First && _lastArrow != null)
            {
                if (TkCanvasMath.PolygonToArea(_lastArrow, PointsInArrow, rect) != result)
                {
                    return 0;
                }
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }

        string fill = _fill;
        if (IsCurrent && Options.IsSet("-activefill")) { fill = Options.Get("-activefill"); }
        else if (EffectiveState == CanvasItemState.Disabled && Options.IsSet("-disabledfill"))
        {
            fill = Options.Get("-disabledfill");
        }

        SKColor color;
        if (!TkColor.TryParse(fill, out color)) { return; }

        int numPoints;
        double[] points = EffectivePoints(out numPoints);
        if (numPoints < 2) { return; }

        double width = EffectiveWidth(_width);
        if (width < 1.0) { width = 1.0; }

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)width;
            paint.StrokeCap = (_capStyle == CapStyle.Round) ? SKStrokeCap.Round
                    : (_capStyle == CapStyle.Projecting) ? SKStrokeCap.Square : SKStrokeCap.Butt;
            paint.StrokeJoin = (_joinStyle == JoinStyle.Miter) ? SKStrokeJoin.Miter
                    : (_joinStyle == JoinStyle.Bevel) ? SKStrokeJoin.Bevel : SKStrokeJoin.Round;

            SKPathEffect dash = CanvasPaintHelper.CreateDashEffect(Options.Get("-dash"), (float)width);
            if (dash != null) { paint.PathEffect = dash; }

            using (SKPath path = CanvasPaintHelper.BuildPolylinePath(points, numPoints, false))
            {
                canvas.DrawPath(path, paint);
            }

            if (dash != null)
            {
                paint.PathEffect = null;
                dash.Dispose();
            }

            // Arrowheads are filled polygons.
            paint.Style = SKPaintStyle.Fill;
            if (_arrow != ArrowStyle.None)
            {
                if (_arrow != ArrowStyle.Last && _firstArrow != null)
                {
                    CanvasPaintHelper.FillPolygon(canvas, paint, _firstArrow, PointsInArrow);
                }
                if (_arrow != ArrowStyle.First && _lastArrow != null)
                {
                    CanvasPaintHelper.FillPolygon(canvas, paint, _lastArrow, PointsInArrow);
                }
            }
        }
    }
}
