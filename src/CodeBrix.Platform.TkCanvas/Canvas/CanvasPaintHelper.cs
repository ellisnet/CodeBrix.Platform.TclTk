using System.Collections.Generic;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// Painting plumbing shared by the canvas item types: Tk dash-pattern
/// translation and polygon filling.
/// </summary>
internal static class CanvasPaintHelper
{
    /// <summary>
    /// Builds a Skia dash effect from a Tk <c>-dash</c> value: either a list
    /// of integers (on/off pixel runs) or the character shorthand
    /// (<c>.</c> <c>,</c> <c>-</c> <c>_</c> and spaces). Returns null for an
    /// empty/solid dash.
    /// </summary>
    /// <param name="dash">The Tk dash text.</param>
    /// <param name="width">The line width (character patterns scale with it, like Tk).</param>
    /// <returns>The path effect, or null when the line is solid.</returns>
    public static SKPathEffect CreateDashEffect(string dash, float width)
    {
        if (string.IsNullOrEmpty(dash)) { return null; }

        var intervals = new List<float>();
        bool numeric = true;
        foreach (string word in TclString.SplitList(dash))
        {
            int value;
            if (int.TryParse(word, out value) && value > 0)
            {
                intervals.Add(value);
            }
            else
            {
                numeric = false;
                break;
            }
        }

        if (!numeric)
        {
            // Character shorthand: each symbol expands to on/off runs in
            // line-width units (the tkCanvUtil.c table).
            intervals.Clear();
            float w = (width < 1f) ? 1f : width;
            foreach (char c in dash)
            {
                switch (c)
                {
                    case '.': intervals.Add(2 * w); intervals.Add(4 * w); break;
                    case ',': intervals.Add(4 * w); intervals.Add(4 * w); break;
                    case '-': intervals.Add(6 * w); intervals.Add(4 * w); break;
                    case '_': intervals.Add(8 * w); intervals.Add(4 * w); break;
                    case ' ':
                        if (intervals.Count > 0) { intervals[intervals.Count - 1] += 4 * w; }
                        break;
                    default: break;
                }
            }
        }

        if (intervals.Count == 0) { return null; }
        if ((intervals.Count & 1) != 0)
        {
            // Skia needs an even count; Tk repeats the pattern.
            int count = intervals.Count;
            for (int i = 0; i < count; i++) { intervals.Add(intervals[i]); }
        }
        return SKPathEffect.CreateDash(intervals.ToArray(), 0);
    }

    /// <summary>Fills a polygon given interleaved coordinates.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="paint">The fill paint.</param>
    /// <param name="points">The interleaved x/y coordinates.</param>
    /// <param name="numPoints">The number of points to use.</param>
    public static void FillPolygon(SKCanvas canvas, SKPaint paint, double[] points, int numPoints)
    {
        if (numPoints < 3) { return; }
        using (SKPath path = BuildPolylinePath(points, numPoints, true))
        {
            canvas.DrawPath(path, paint);
        }
    }

    /// <summary>Builds a path from interleaved polyline coordinates.</summary>
    /// <param name="points">The interleaved x/y coordinates.</param>
    /// <param name="numPoints">The number of points to use.</param>
    /// <param name="close">Whether to close the contour.</param>
    /// <returns>The built path (caller disposes).</returns>
    public static SKPath BuildPolylinePath(double[] points, int numPoints, bool close)
    {
        var builder = new SKPathBuilder();
        builder.MoveTo((float)points[0], (float)points[1]);
        for (int i = 1; i < numPoints; i++)
        {
            builder.LineTo((float)points[2 * i], (float)points[2 * i + 1]);
        }
        if (close) { builder.Close(); }
        return builder.Detach();
    }
}
