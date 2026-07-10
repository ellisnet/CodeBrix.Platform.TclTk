using System;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// Faithful ports of the Tk 8.6.16 canvas geometry helpers (tkTrig.c):
/// point-to-shape distances, shape-versus-rectangle classification, thick
/// polyline handling, and the bezier spline used by <c>-smooth</c>. Every
/// formula follows the C original so hit-testing, <c>find</c>, and
/// <c>bbox</c> results match real Tk numerically.
/// </summary>
internal static class TkCanvasMath
{
    /// <summary>
    /// Distance from a point to a finite line segment
    /// (<c>TkLineToPoint</c>).
    /// </summary>
    public static double LineToPoint(double x1, double y1, double x2, double y2, double px, double py)
    {
        double x, y;

        if (x1 == x2)
        {
            x = x1;
            if (y1 >= y2)
            {
                y = Math.Min(y1, py);
                y = Math.Max(y, y2);
            }
            else
            {
                y = Math.Min(y2, py);
                y = Math.Max(y, y1);
            }
        }
        else if (y1 == y2)
        {
            y = y1;
            if (x1 >= x2)
            {
                x = Math.Min(x1, px);
                x = Math.Max(x, x2);
            }
            else
            {
                x = Math.Min(x2, px);
                x = Math.Max(x, x1);
            }
        }
        else
        {
            double m1 = (y2 - y1) / (x2 - x1);
            double b1 = y1 - m1 * x1;
            double m2 = -1.0 / m1;
            double b2 = py - m2 * px;
            x = (b2 - b1) / (m1 - m2);
            y = m1 * x + b1;
            if (x1 > x2)
            {
                if (x > x1) { x = x1; y = y1; }
                else if (x < x2) { x = x2; y = y2; }
            }
            else
            {
                if (x > x2) { x = x2; y = y2; }
                else if (x < x1) { x = x1; y = y1; }
            }
        }

        return Hypot(px - x, py - y);
    }

    /// <summary>
    /// Classifies a line segment against a rectangle
    /// (<c>TkLineToArea</c>): 1 = entirely inside, -1 = entirely outside,
    /// 0 = overlapping.
    /// </summary>
    public static int LineToArea(double e1x, double e1y, double e2x, double e2y, double[] rect)
    {
        bool inside1 = (e1x >= rect[0]) && (e1x <= rect[2]) && (e1y >= rect[1]) && (e1y <= rect[3]);
        bool inside2 = (e2x >= rect[0]) && (e2x <= rect[2]) && (e2y >= rect[1]) && (e2y <= rect[3]);
        if (inside1 != inside2) { return 0; }
        if (inside1 && inside2) { return 1; }

        if (e1x == e2x)
        {
            if (((e1y >= rect[1]) != (e2y >= rect[1])) && (e1x >= rect[0]) && (e1x <= rect[2]))
            {
                return 0;
            }
        }
        else if (e1y == e2y)
        {
            if (((e1x >= rect[0]) != (e2x >= rect[0])) && (e1y >= rect[1]) && (e1y <= rect[3]))
            {
                return 0;
            }
        }
        else
        {
            double m = (e2y - e1y) / (e2x - e1x);
            double low, high;

            if (e1x < e2x) { low = e1x; high = e2x; }
            else { low = e2x; high = e1x; }

            double y = e1y + (rect[0] - e1x) * m;
            if ((rect[0] >= low) && (rect[0] <= high) && (y >= rect[1]) && (y <= rect[3]))
            {
                return 0;
            }

            y += (rect[2] - rect[0]) * m;
            if ((y >= rect[1]) && (y <= rect[3]) && (rect[2] >= low) && (rect[2] <= high))
            {
                return 0;
            }

            if (e1y < e2y) { low = e1y; high = e2y; }
            else { low = e2y; high = e1y; }

            double x = e1x + (rect[1] - e1y) / m;
            if ((x >= rect[0]) && (x <= rect[2]) && (rect[1] >= low) && (rect[1] <= high))
            {
                return 0;
            }

            x += (rect[3] - rect[1]) / m;
            if ((x >= rect[0]) && (x <= rect[2]) && (rect[3] >= low) && (rect[3] <= high))
            {
                return 0;
            }
        }
        return -1;
    }

    /// <summary>
    /// Distance from a point to a (possibly self-intersecting) closed
    /// polygon; 0 when the point is inside (<c>TkPolygonToPoint</c>).
    /// The point array holds x0,y0,x1,y1,... and the polygon is traversed
    /// over <paramref name="numPoints"/> vertices.
    /// </summary>
    public static double PolygonToPoint(double[] poly, int numPoints, double px, double py)
    {
        double bestDist = 1.0e36;
        int intersections = 0;

        for (int count = numPoints, p = 0; count > 1; count--, p += 2)
        {
            double x, y;

            if (poly[p + 2] == poly[p])
            {
                x = poly[p];
                if (poly[p + 1] >= poly[p + 3])
                {
                    y = Math.Min(poly[p + 1], py);
                    y = Math.Max(y, poly[p + 3]);
                }
                else
                {
                    y = Math.Min(poly[p + 3], py);
                    y = Math.Max(y, poly[p + 1]);
                }
            }
            else if (poly[p + 3] == poly[p + 1])
            {
                y = poly[p + 1];
                if (poly[p] >= poly[p + 2])
                {
                    x = Math.Min(poly[p], px);
                    x = Math.Max(x, poly[p + 2]);
                    if ((py < y) && (px < poly[p]) && (px >= poly[p + 2]))
                    {
                        intersections++;
                    }
                }
                else
                {
                    x = Math.Min(poly[p + 2], px);
                    x = Math.Max(x, poly[p]);
                    if ((py < y) && (px < poly[p + 2]) && (px >= poly[p]))
                    {
                        intersections++;
                    }
                }
            }
            else
            {
                double m1 = (poly[p + 3] - poly[p + 1]) / (poly[p + 2] - poly[p]);
                double b1 = poly[p + 1] - m1 * poly[p];
                double m2 = -1.0 / m1;
                double b2 = py - m2 * px;
                x = (b2 - b1) / (m1 - m2);
                y = m1 * x + b1;
                if (poly[p] > poly[p + 2])
                {
                    if (x > poly[p]) { x = poly[p]; y = poly[p + 1]; }
                    else if (x < poly[p + 2]) { x = poly[p + 2]; y = poly[p + 3]; }
                }
                else
                {
                    if (x > poly[p + 2]) { x = poly[p + 2]; y = poly[p + 3]; }
                    else if (x < poly[p]) { x = poly[p]; y = poly[p + 1]; }
                }

                bool lower = (m1 * px + b1) > py;
                if (lower && (px >= Math.Min(poly[p], poly[p + 2]))
                        && (px < Math.Max(poly[p], poly[p + 2])))
                {
                    intersections++;
                }
            }

            double dist = Hypot(px - x, py - y);
            if (dist < bestDist) { bestDist = dist; }
        }

        if ((intersections & 1) != 0) { return 0.0; }
        return bestDist;
    }

    /// <summary>
    /// Classifies a closed polygon against a rectangle
    /// (<c>TkPolygonToArea</c>): 1 = polygon entirely inside, -1 = entirely
    /// outside, 0 = overlapping.
    /// </summary>
    public static int PolygonToArea(double[] poly, int numPoints, double[] rect)
    {
        int state = LineToArea(poly[0], poly[1], poly[2], poly[3], rect);
        if (state == 0) { return 0; }

        for (int p = 2, count = numPoints - 1; count >= 2; p += 2, count--)
        {
            if (LineToArea(poly[p], poly[p + 1], poly[p + 2], poly[p + 3], rect) != state)
            {
                return 0;
            }
        }

        if (state == 1) { return 1; }
        if (PolygonToPoint(poly, numPoints, rect[0], rect[1]) == 0.0)
        {
            return 0;
        }
        return -1;
    }

    /// <summary>
    /// Distance from a point to an oval described by its bounding box
    /// (<c>TkOvalToPoint</c>).
    /// </summary>
    public static double OvalToPoint(double[] oval, double width, bool filled, double px, double py)
    {
        double xDelta = px - (oval[0] + oval[2]) / 2.0;
        double yDelta = py - (oval[1] + oval[3]) / 2.0;
        double distToCenter = Hypot(xDelta, yDelta);
        double scaledDistance = Hypot(
                xDelta / ((oval[2] + width - oval[0]) / 2.0),
                yDelta / ((oval[3] + width - oval[1]) / 2.0));

        if (scaledDistance > 1.0)
        {
            return (distToCenter / scaledDistance) * (scaledDistance - 1.0);
        }

        if (filled) { return 0.0; }

        double distToOutline;
        if (scaledDistance > 1E-10)
        {
            distToOutline = (distToCenter / scaledDistance) * (1.0 - scaledDistance) - width;
        }
        else
        {
            double xDiam = oval[2] - oval[0];
            double yDiam = oval[3] - oval[1];
            if (xDiam < yDiam) { distToOutline = (xDiam - width) / 2; }
            else { distToOutline = (yDiam - width) / 2; }
        }

        if (distToOutline < 0.0) { return 0.0; }
        return distToOutline;
    }

    /// <summary>
    /// Classifies a filled oval against a rectangle (<c>TkOvalToArea</c>):
    /// 1 = oval entirely inside, -1 = entirely outside, 0 = overlapping.
    /// </summary>
    public static int OvalToArea(double[] oval, double[] rect)
    {
        if ((rect[0] <= oval[0]) && (rect[2] >= oval[2])
                && (rect[1] <= oval[1]) && (rect[3] >= oval[3]))
        {
            return 1;
        }
        if ((rect[2] < oval[0]) || (rect[0] > oval[2])
                || (rect[3] < oval[1]) || (rect[1] > oval[3]))
        {
            return -1;
        }

        double centerX = (oval[0] + oval[2]) / 2;
        double centerY = (oval[1] + oval[3]) / 2;
        double radX = (oval[2] - oval[0]) / 2;
        double radY = (oval[3] - oval[1]) / 2;

        double deltaY = rect[1] - centerY;
        if (deltaY < 0.0)
        {
            deltaY = centerY - rect[3];
            if (deltaY < 0.0) { deltaY = 0; }
        }
        deltaY /= radY;
        deltaY *= deltaY;

        double deltaX = (rect[0] - centerX) / radX;
        deltaX *= deltaX;
        if ((deltaX + deltaY) <= 1.0) { return 0; }

        deltaX = (rect[2] - centerX) / radX;
        deltaX *= deltaX;
        if ((deltaX + deltaY) <= 1.0) { return 0; }

        deltaX = rect[0] - centerX;
        if (deltaX < 0.0)
        {
            deltaX = centerX - rect[2];
            if (deltaX < 0.0) { deltaX = 0; }
        }
        deltaX /= radX;
        deltaX *= deltaX;

        deltaY = (rect[1] - centerY) / radY;
        deltaY *= deltaY;
        if ((deltaX + deltaY) < 1.0) { return 0; }

        deltaY = (rect[3] - centerY) / radY;
        deltaY *= deltaY;
        if ((deltaX + deltaY) < 1.0) { return 0; }

        return -1;
    }

    /// <summary>
    /// Classifies a thick polyline against a rectangle
    /// (<c>TkThickPolyLineToArea</c>): 1 = inside, -1 = outside,
    /// 0 = overlapping. Cap and join styles come from
    /// <see cref="CapStyle"/>/<see cref="JoinStyle"/>.
    /// </summary>
    public static int ThickPolyLineToArea(double[] coords, int numPoints, double width,
            CapStyle capStyle, JoinStyle joinStyle, double[] rect)
    {
        double radius = width / 2.0;
        int inside = -1;

        if ((coords[0] >= rect[0]) && (coords[0] <= rect[2])
                && (coords[1] >= rect[1]) && (coords[1] <= rect[3]))
        {
            inside = 1;
        }

        var poly = new double[10];
        var ovalBox = new double[4];
        bool changedMiterToBevel = false;

        for (int count = numPoints, p = 0; count >= 2; count--, p += 2)
        {
            if (((capStyle == CapStyle.Round) && (count == numPoints))
                    || ((joinStyle == JoinStyle.Round) && (count != numPoints)))
            {
                ovalBox[0] = coords[p] - radius;
                ovalBox[1] = coords[p + 1] - radius;
                ovalBox[2] = coords[p] + radius;
                ovalBox[3] = coords[p + 1] + radius;
                if (OvalToArea(ovalBox, rect) != inside) { return 0; }
            }

            if (count == numPoints)
            {
                GetButtPoints(coords[p + 2], coords[p + 3], coords[p], coords[p + 1], width,
                        capStyle == CapStyle.Projecting, poly, 0, 2);
            }
            else if ((joinStyle == JoinStyle.Miter) && !changedMiterToBevel)
            {
                poly[0] = poly[6];
                poly[1] = poly[7];
                poly[2] = poly[4];
                poly[3] = poly[5];
            }
            else
            {
                GetButtPoints(coords[p + 2], coords[p + 3], coords[p], coords[p + 1], width,
                        false, poly, 0, 2);

                if ((joinStyle == JoinStyle.Bevel) || changedMiterToBevel)
                {
                    poly[8] = poly[0];
                    poly[9] = poly[1];
                    if (PolygonToArea(poly, 5, rect) != inside) { return 0; }
                    changedMiterToBevel = false;
                }
            }

            if (count == 2)
            {
                GetButtPoints(coords[p], coords[p + 1], coords[p + 2], coords[p + 3], width,
                        capStyle == CapStyle.Projecting, poly, 4, 6);
            }
            else if (joinStyle == JoinStyle.Miter)
            {
                if (!GetMiterPoints(coords[p], coords[p + 1], coords[p + 2], coords[p + 3],
                        coords[p + 4], coords[p + 5], width, poly, 4, 6))
                {
                    changedMiterToBevel = true;
                    GetButtPoints(coords[p], coords[p + 1], coords[p + 2], coords[p + 3], width,
                            false, poly, 4, 6);
                }
            }
            else
            {
                GetButtPoints(coords[p], coords[p + 1], coords[p + 2], coords[p + 3], width,
                        false, poly, 4, 6);
            }

            poly[8] = poly[0];
            poly[9] = poly[1];
            if (PolygonToArea(poly, 5, rect) != inside) { return 0; }
        }

        if (capStyle == CapStyle.Round)
        {
            int last = 2 * (numPoints - 1);
            ovalBox[0] = coords[last] - radius;
            ovalBox[1] = coords[last + 1] - radius;
            ovalBox[2] = coords[last] + radius;
            ovalBox[3] = coords[last + 1] + radius;
            if (OvalToArea(ovalBox, rect) != inside) { return 0; }
        }

        return inside;
    }

    /// <summary>
    /// Computes the two points at the end of a line edge perpendicular to it
    /// (<c>TkGetButtPoints</c>): p1 is the point before the vertex, p2 the
    /// vertex; results are written into <paramref name="output"/> at
    /// <paramref name="m1"/>/<paramref name="m2"/>.
    /// </summary>
    public static void GetButtPoints(double p1x, double p1y, double p2x, double p2y,
            double width, bool project, double[] output, int m1, int m2)
    {
        width *= 0.5;
        double length = Hypot(p2x - p1x, p2y - p1y);
        if (length == 0.0)
        {
            output[m1] = output[m2] = p2x;
            output[m1 + 1] = output[m2 + 1] = p2y;
        }
        else
        {
            double deltaX = -width * (p2y - p1y) / length;
            double deltaY = width * (p2x - p1x) / length;
            output[m1] = p2x + deltaX;
            output[m2] = p2x - deltaX;
            output[m1 + 1] = p2y + deltaY;
            output[m2 + 1] = p2y - deltaY;
            if (project)
            {
                output[m1] += deltaY;
                output[m2] += deltaY;
                output[m1 + 1] -= deltaX;
                output[m2 + 1] -= deltaX;
            }
        }
    }

    /// <summary>
    /// Computes the two miter vertex points of a joint
    /// (<c>TkGetMiterPoints</c>). Returns false when the angle is under 11
    /// degrees (caller falls back to a bevel).
    /// </summary>
    public static bool GetMiterPoints(double p1xIn, double p1yIn, double p2xIn, double p2yIn,
            double p3xIn, double p3yIn, double width, double[] output, int m1, int m2)
    {
        const double elevenDegrees = (11.0 * 2.0 * Math.PI) / 360.0;

        double p1x = Math.Floor(p1xIn + 0.5);
        double p1y = Math.Floor(p1yIn + 0.5);
        double p2x = Math.Floor(p2xIn + 0.5);
        double p2y = Math.Floor(p2yIn + 0.5);
        double p3x = Math.Floor(p3xIn + 0.5);
        double p3y = Math.Floor(p3yIn + 0.5);

        double theta1;
        if (p2y == p1y) { theta1 = (p2x < p1x) ? 0 : Math.PI; }
        else if (p2x == p1x) { theta1 = (p2y < p1y) ? Math.PI / 2.0 : -Math.PI / 2.0; }
        else { theta1 = Math.Atan2(p1y - p2y, p1x - p2x); }

        double theta2;
        if (p3y == p2y) { theta2 = (p3x > p2x) ? 0 : Math.PI; }
        else if (p3x == p2x) { theta2 = (p3y > p2y) ? Math.PI / 2.0 : -Math.PI / 2.0; }
        else { theta2 = Math.Atan2(p3y - p2y, p3x - p2x); }

        double theta = theta1 - theta2;
        if (theta > Math.PI) { theta -= 2 * Math.PI; }
        else if (theta < -Math.PI) { theta += 2 * Math.PI; }

        if ((theta < elevenDegrees) && (theta > -elevenDegrees)) { return false; }

        double dist = 0.5 * width / Math.Sin(0.5 * theta);
        if (dist < 0.0) { dist = -dist; }

        double theta3 = (theta1 + theta2) / 2.0;
        if (Math.Sin(theta3 - (theta1 + Math.PI)) < 0.0)
        {
            theta3 += Math.PI;
        }
        double deltaX = dist * Math.Cos(theta3);
        output[m1] = p2x + deltaX;
        output[m2] = p2x - deltaX;
        double deltaY = dist * Math.Sin(theta3);
        output[m1 + 1] = p2y + deltaY;
        output[m2 + 1] = p2y - deltaY;

        return true;
    }

    /// <summary>
    /// Generates the points of one cubic bezier segment
    /// (<c>TkBezierPoints</c>), writing <paramref name="numSteps"/> point
    /// pairs into <paramref name="output"/> starting at
    /// <paramref name="outputIndex"/>.
    /// </summary>
    public static void BezierPoints(double[] control, int numSteps, double[] output, int outputIndex)
    {
        for (int i = 1; i <= numSteps; i++, outputIndex += 2)
        {
            double t = ((double)i) / ((double)numSteps);
            double t2 = t * t;
            double t3 = t2 * t;
            double u = 1.0 - t;
            double u2 = u * u;
            double u3 = u2 * u;
            output[outputIndex] = control[0] * u3
                    + 3.0 * (control[2] * t * u2 + control[4] * t2 * u) + control[6] * t3;
            output[outputIndex + 1] = control[1] * u3
                    + 3.0 * (control[3] * t * u2 + control[5] * t2 * u) + control[7] * t3;
        }
    }

    /// <summary>
    /// The upper bound on points <see cref="MakeBezierCurve"/> can produce
    /// for the given inputs (the <c>TkMakeBezierCurve</c> NULL-array form).
    /// </summary>
    public static int BezierPointCount(int numPoints, int numSteps)
    {
        return 1 + numPoints * numSteps;
    }

    /// <summary>
    /// Expands control points into the bezier polyline Tk uses for smoothed
    /// lines and polygons (<c>TkMakeBezierCurve</c>). Returns the number of
    /// output points written into <paramref name="output"/>.
    /// </summary>
    public static int MakeBezierCurve(double[] points, int numPoints, int numSteps, double[] output)
    {
        int numCoords = numPoints * 2;
        var control = new double[8];
        bool closed;
        int outputPoints = 0;
        int outIndex = 0;

        if ((points[0] == points[numCoords - 2]) && (points[1] == points[numCoords - 1]))
        {
            closed = true;
            control[0] = 0.5 * points[numCoords - 4] + 0.5 * points[0];
            control[1] = 0.5 * points[numCoords - 3] + 0.5 * points[1];
            control[2] = 0.167 * points[numCoords - 4] + 0.833 * points[0];
            control[3] = 0.167 * points[numCoords - 3] + 0.833 * points[1];
            control[4] = 0.833 * points[0] + 0.167 * points[2];
            control[5] = 0.833 * points[1] + 0.167 * points[3];
            control[6] = 0.5 * points[0] + 0.5 * points[2];
            control[7] = 0.5 * points[1] + 0.5 * points[3];
            output[0] = control[0];
            output[1] = control[1];
            BezierPoints(control, numSteps, output, 2);
            outIndex += 2 * (numSteps + 1);
            outputPoints += numSteps + 1;
        }
        else
        {
            closed = false;
            output[0] = points[0];
            output[1] = points[1];
            outIndex += 2;
            outputPoints += 1;
        }

        for (int i = 2, p = 0; i < numPoints; i++, p += 2)
        {
            if ((i == 2) && !closed)
            {
                control[0] = points[p];
                control[1] = points[p + 1];
                control[2] = 0.333 * points[p] + 0.667 * points[p + 2];
                control[3] = 0.333 * points[p + 1] + 0.667 * points[p + 3];
            }
            else
            {
                control[0] = 0.5 * points[p] + 0.5 * points[p + 2];
                control[1] = 0.5 * points[p + 1] + 0.5 * points[p + 3];
                control[2] = 0.167 * points[p] + 0.833 * points[p + 2];
                control[3] = 0.167 * points[p + 1] + 0.833 * points[p + 3];
            }

            if ((i == (numPoints - 1)) && !closed)
            {
                control[4] = 0.667 * points[p + 2] + 0.333 * points[p + 4];
                control[5] = 0.667 * points[p + 3] + 0.333 * points[p + 5];
                control[6] = points[p + 4];
                control[7] = points[p + 5];
            }
            else
            {
                control[4] = 0.833 * points[p + 2] + 0.167 * points[p + 4];
                control[5] = 0.833 * points[p + 3] + 0.167 * points[p + 5];
                control[6] = 0.5 * points[p + 2] + 0.5 * points[p + 4];
                control[7] = 0.5 * points[p + 3] + 0.5 * points[p + 5];
            }

            if (((points[p] == points[p + 2]) && (points[p + 1] == points[p + 3]))
                    || ((points[p + 2] == points[p + 4]) && (points[p + 3] == points[p + 5])))
            {
                output[outIndex] = control[6];
                output[outIndex + 1] = control[7];
                outIndex += 2;
                outputPoints += 1;
                continue;
            }

            BezierPoints(control, numSteps, output, outIndex);
            outIndex += 2 * numSteps;
            outputPoints += numSteps;
        }
        return outputPoints;
    }

    /// <summary>
    /// The two-argument hypotenuse, matching the C library's
    /// <c>hypot</c> used throughout tkTrig.c.
    /// </summary>
    public static double Hypot(double x, double y)
    {
        return Math.Sqrt(x * x + y * y);
    }
}

/// <summary>The Tk line cap styles (<c>-capstyle</c>).</summary>
public enum CapStyle
{
    /// <summary>The line ends exactly at its endpoint (<c>butt</c>).</summary>
    Butt,

    /// <summary>The line ends with a half-circle (<c>round</c>).</summary>
    Round,

    /// <summary>The line projects half a width past its endpoint (<c>projecting</c>).</summary>
    Projecting,
}

/// <summary>The Tk line join styles (<c>-joinstyle</c>).</summary>
public enum JoinStyle
{
    /// <summary>Sharp corners (<c>miter</c>).</summary>
    Miter,

    /// <summary>Cut-off corners (<c>bevel</c>).</summary>
    Bevel,

    /// <summary>Rounded corners (<c>round</c>).</summary>
    Round,
}
