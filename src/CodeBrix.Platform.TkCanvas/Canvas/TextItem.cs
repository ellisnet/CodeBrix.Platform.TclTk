using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Layout;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>text</c> item (tkCanvText.c): a block of text anchored at
/// one point, laid out through the toolkit's single font-measurement seam
/// (<see cref="FontManager"/>, the plan's R2) so what <c>bbox</c> reports
/// always matches what gets painted. Supports <c>-text</c>, <c>-font</c>,
/// <c>-anchor</c>, <c>-justify</c>, <c>-width</c> (wrap length), and
/// <c>-fill</c>. In-canvas text EDITING (<c>icursor</c>/<c>insert</c>/
/// <c>dchars</c>/<c>select</c>) is deferred per the plan (§3.20): the canvas
/// accepts those subcommands as no-ops.
/// </summary>
public sealed class TextItem : CanvasItem
{
    private string _text = "";
    private string _fontSpec = "";
    private Anchor _anchor = Layout.Anchor.Center;
    private string _justify = "left";
    private double _wrapWidth;
    private string _fill = "black";
    private List<string> _lines = new List<string>();
    private int _layoutWidth;
    private int _layoutHeight;
    private int _lineHeight;

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "text"; }
    }

    /// <summary>The laid-out lines (after wrapping), for tests and painting.</summary>
    public IReadOnlyList<string> Lines
    {
        get { return _lines; }
    }

    private FontManager Fonts
    {
        get { return Canvas.Window.Tree.Fonts; }
    }

    private TkFont Font
    {
        get { return Fonts.Parse(_fontSpec); }
    }

    private protected override void ApplyCoords(IReadOnlyList<double> coords)
    {
        if (coords.Count != 2)
        {
            throw new ArgumentException(
                    "wrong # coordinates: expected 0 or 2, got " + coords.Count);
        }
        CoordArray = new double[] { coords[0], coords[1] };
    }

    private protected override void OnConfigured()
    {
        _text = Options.Get("-text", "");
        _fontSpec = Options.Get("-font", "");
        _justify = Options.Get("-justify", "left");
        _fill = Options.Get("-fill", "black");

        double wrap;
        _wrapWidth = TclString.TryParseCoord(Options.Get("-width", "0"), out wrap) ? wrap : 0;

        switch (Options.Get("-anchor", "center"))
        {
            case "n": _anchor = Layout.Anchor.N; break;
            case "ne": _anchor = Layout.Anchor.NE; break;
            case "e": _anchor = Layout.Anchor.E; break;
            case "se": _anchor = Layout.Anchor.SE; break;
            case "s": _anchor = Layout.Anchor.S; break;
            case "sw": _anchor = Layout.Anchor.SW; break;
            case "w": _anchor = Layout.Anchor.W; break;
            case "nw": _anchor = Layout.Anchor.NW; break;
            default: _anchor = Layout.Anchor.Center; break;
        }
    }

    /// <summary>
    /// Lays the text out through the font seam: split on newlines, then wrap
    /// greedily at space boundaries when <c>-width</c> is positive (words
    /// longer than the wrap width break mid-word, like Tk's text layout).
    /// </summary>
    private void ComputeLayout()
    {
        _lines = new List<string>();
        FontManager fonts = Fonts;
        TkFont font = Font;

        foreach (string raw in _text.Split('\n'))
        {
            if (_wrapWidth <= 0 || fonts.Measure(font, raw) <= _wrapWidth)
            {
                _lines.Add(raw);
                continue;
            }

            string remaining = raw;
            while (remaining.Length > 0)
            {
                if (fonts.Measure(font, remaining) <= _wrapWidth)
                {
                    _lines.Add(remaining);
                    break;
                }

                // Find the longest prefix that fits, preferring a space break.
                int fit = remaining.Length;
                while (fit > 1 && fonts.Measure(font, remaining.Substring(0, fit)) > _wrapWidth)
                {
                    fit--;
                }
                int breakAt = remaining.LastIndexOf(' ', Math.Min(fit, remaining.Length - 1));
                if (breakAt <= 0) { breakAt = fit; }

                _lines.Add(remaining.Substring(0, breakAt).TrimEnd(' '));
                remaining = remaining.Substring(breakAt).TrimStart(' ');
            }
            if (remaining.Length == 0 && raw.Length > 0 && _lines.Count == 0)
            {
                _lines.Add("");
            }
        }
        if (_lines.Count == 0) { _lines.Add(""); }

        _layoutWidth = 0;
        foreach (string line in _lines)
        {
            int width = fonts.Measure(font, line);
            if (width > _layoutWidth) { _layoutWidth = width; }
        }
        _lineHeight = fonts.Metrics(font).LineSpace;
        _layoutHeight = _lineHeight * _lines.Count;
    }

    /// <summary>The top-left corner of the layout box for the current anchor.</summary>
    private void LayoutOrigin(out double leftX, out double topY)
    {
        double x = CoordArray.Length >= 2 ? CoordArray[0] : 0;
        double y = CoordArray.Length >= 2 ? CoordArray[1] : 0;

        switch (_anchor)
        {
            case Layout.Anchor.NW: case Layout.Anchor.W: case Layout.Anchor.SW:
                leftX = x;
                break;
            case Layout.Anchor.NE: case Layout.Anchor.E: case Layout.Anchor.SE:
                leftX = x - _layoutWidth;
                break;
            default:
                leftX = x - _layoutWidth / 2.0;
                break;
        }
        switch (_anchor)
        {
            case Layout.Anchor.NW: case Layout.Anchor.N: case Layout.Anchor.NE:
                topY = y;
                break;
            case Layout.Anchor.SW: case Layout.Anchor.S: case Layout.Anchor.SE:
                topY = y - _layoutHeight;
                break;
            default:
                topY = y - _layoutHeight / 2.0;
                break;
        }
    }

    internal override void ComputeBounds()
    {
        if (CoordArray.Length < 2 || EffectiveState == CanvasItemState.Hidden)
        {
            SetEmptyHeaderBox();
            return;
        }

        ComputeLayout();

        double leftX, topY;
        LayoutOrigin(out leftX, out topY);

        int x1 = (int)Math.Floor(leftX + 0.5);
        int y1 = (int)Math.Floor(topY + 0.5);
        SetHeaderBox(x1, y1, x1 + _layoutWidth, y1 + _layoutHeight);
    }

    /// <summary>The rectangle of one laid-out line, honoring <c>-justify</c>.</summary>
    private void LineRect(int index, out double lx, out double ly, out double lw, out double lh)
    {
        double leftX, topY;
        LayoutOrigin(out leftX, out topY);

        int lineWidth = Fonts.Measure(Font, _lines[index]);
        switch (_justify)
        {
            case "right": lx = leftX + (_layoutWidth - lineWidth); break;
            case "center": lx = leftX + (_layoutWidth - lineWidth) / 2.0; break;
            default: lx = leftX; break;
        }
        ly = topY + index * _lineHeight;
        lw = lineWidth;
        lh = _lineHeight;
    }

    /// <inheritdoc/>
    public override double DistanceTo(double x, double y)
    {
        if (_lines.Count == 0) { ComputeLayout(); }

        double best = 1.0e36;
        for (int i = 0; i < _lines.Count; i++)
        {
            double lx, ly, lw, lh;
            LineRect(i, out lx, out ly, out lw, out lh);
            if (lw <= 0) { continue; }

            double dx;
            if (x < lx) { dx = lx - x; }
            else if (x > lx + lw) { dx = x - (lx + lw); }
            else { dx = 0; }

            double dy;
            if (y < ly) { dy = ly - y; }
            else if (y > ly + lh) { dy = y - (ly + lh); }
            else { dy = 0; }

            double dist = TkCanvasMath.Hypot(dx, dy);
            if (dist < best) { best = dist; }
            if (best == 0.0) { return 0.0; }
        }
        return best;
    }

    /// <inheritdoc/>
    public override int AreaTest(double[] rect)
    {
        if (_lines.Count == 0) { ComputeLayout(); }

        bool anyOverlap = false;
        bool allInside = true;
        bool anyLine = false;

        for (int i = 0; i < _lines.Count; i++)
        {
            double lx, ly, lw, lh;
            LineRect(i, out lx, out ly, out lw, out lh);
            if (lw <= 0) { continue; }
            anyLine = true;

            bool outside = (rect[2] < lx) || (rect[0] > lx + lw)
                    || (rect[3] < ly) || (rect[1] > ly + lh);
            bool inside = (rect[0] <= lx) && (rect[1] <= ly)
                    && (rect[2] >= lx + lw) && (rect[3] >= ly + lh);

            if (!outside) { anyOverlap = true; }
            if (!inside) { allInside = false; }
        }

        if (!anyLine)
        {
            // An empty text still occupies its anchor line box vertically.
            double leftX, topY;
            LayoutOrigin(out leftX, out topY);
            bool outside = (rect[2] < leftX) || (rect[0] > leftX)
                    || (rect[3] < topY) || (rect[1] > topY + _layoutHeight);
            return outside ? -1 : 0;
        }

        if (allInside) { return 1; }
        return anyOverlap ? 0 : -1;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        if (EffectiveState == CanvasItemState.Hidden) { return; }
        if (_lines.Count == 0) { ComputeLayout(); }

        string fill = _fill;
        if (IsCurrent && Options.IsSet("-activefill")) { fill = Options.Get("-activefill"); }
        else if (EffectiveState == CanvasItemState.Disabled && Options.IsSet("-disabledfill"))
        {
            fill = Options.Get("-disabledfill");
        }

        SKColor color;
        if (!TkColor.TryParse(fill, out color)) { return; }

        FontManager fonts = Fonts;
        TkFont font = Font;
        int ascent = fonts.Metrics(font).Ascent;

        using (SKFont skFont = fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Color = color;

            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Length == 0) { continue; }
                double lx, ly, lw, lh;
                LineRect(i, out lx, out ly, out lw, out lh);
                canvas.DrawText(_lines[i], (float)lx, (float)(ly + ascent),
                        SKTextAlign.Left, skFont, paint);
            }
        }
    }
}
