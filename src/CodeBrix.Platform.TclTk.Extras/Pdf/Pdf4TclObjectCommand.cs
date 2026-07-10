using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.PdfDocuments.Drawing;
using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk.Extras.Pdf;

/// <summary>
/// The per-document Tcl object command created by <c>pdf4tcl::new NAME ?options?</c>,
/// implementing the pdf4tcl 0.7 drawing surface DRAKON exercises — startPage,
/// setFillColor, setStrokeColor, setFont, setLineStyle, getStringWidth, text, line,
/// rectangle, polygon, write, destroy — over CodeBrix.PdfDocuments.
/// </summary>
/// <remarks>
/// Coordinate model (replicates pdf4tcl's <c>Trans</c>/<c>TransR</c>): user
/// coordinates are relative to the margin box; with <c>-orient 1</c> (the default)
/// the origin is the TOP-left corner with y growing down — which maps to
/// CodeBrix.PdfDocuments' native top-left/y-down system as a plain margin offset —
/// and with <c>-orient 0</c> the origin is the BOTTOM-left corner with y growing up,
/// which this class flips. Not implemented (accepted and ignored, never thrown):
/// text rotation/skew options (<c>-angle</c>/<c>-xangle</c>/<c>-yangle</c>), page
/// <c>-rotate</c>, and <c>-compress</c>. DRAKON uses none of them.
/// </remarks>
internal sealed class Pdf4TclObjectCommand : Default
{
    private const string MethodList =
        "destroy, getStringWidth, line, polygon, rectangle, setFillColor, setFont, " +
        "setLineStyle, setStrokeColor, startPage, text, or write";

    private static readonly object MeasureSyncRoot = new object();
    private static XGraphics _measureGraphics;

    // Document-level defaults captured at construction (pdf4tcl "global options").
    private readonly string _defaultPaper;
    private readonly bool _defaultLandscape;
    private readonly string _defaultMargin;
    private readonly bool _defaultOrient;
    private readonly double _unit;
    private readonly string _defaultFile;

    private readonly PdfDocument _document;

    // Current page state.
    private PdfPage _page;
    private XGraphics _graphics;
    private double _pageWidth;
    private double _pageHeight;
    private double _marginLeft;
    private double _marginRight;
    private double _marginTop;
    private double _marginBottom;
    private bool _orient;

    // Current graphics state.
    private XColor _fillColor = XColor.FromArgb(0, 0, 0);
    private XColor _strokeColor = XColor.FromArgb(0, 0, 0);
    private double _lineWidth = 1.0;
    private double[] _dashPoints;
    private string _fontName = string.Empty;
    private double _fontSize = 1.0;
    private XFont _font;

    // Current text position (backend coordinates), advanced by each text call.
    private double _textX;
    private double _textY;

    private bool _finished;
    private bool _destroyed;

    public Pdf4TclObjectCommand(
        ICommandData commandData, string paper, bool landscape, string margin,
        bool orient, double unit, string file)
        : base(commandData)
    {
        _defaultPaper = paper;
        _defaultLandscape = landscape;
        _defaultMargin = margin;
        _defaultOrient = orient;
        _unit = unit;
        _defaultFile = file;
        _document = new PdfDocument();
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (interpreter == null) { result = "invalid interpreter"; return ReturnCode.Error; }
        if (arguments == null || arguments.Count < 2)
        {
            result = string.Format(
                "wrong # args: should be \"{0} method ?arg ...?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        string method = arguments[1];

        if (_destroyed)
        {
            result = string.Format("invalid command name \"{0}\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        try
        {
            switch (method)
            {
                case "startPage": return StartPageMethod(arguments, ref result);
                case "setFillColor": return SetColorMethod(arguments, isFill: true, ref result);
                case "setStrokeColor": return SetColorMethod(arguments, isFill: false, ref result);
                case "setFont": return SetFontMethod(arguments, ref result);
                case "setLineStyle": return SetLineStyleMethod(arguments, ref result);
                case "getStringWidth": return GetStringWidthMethod(arguments, ref result);
                case "text": return TextMethod(arguments, ref result);
                case "line": return LineMethod(arguments, ref result);
                case "rectangle": return RectangleMethod(arguments, ref result);
                case "polygon": return PolygonMethod(arguments, ref result);
                case "write": return WriteMethod(arguments, ref result);
                case "destroy": return DestroyMethod(interpreter, ref result);
                default:
                    result = string.Format("bad option \"{0}\": must be {1}", method, MethodList);
                    return ReturnCode.Error;
            }
        }
        catch (Exception ex)
        {
            result = ex.Message;
            return ReturnCode.Error;
        }
    }

    private static string GetCommandName(ArgumentList arguments)
        => (arguments != null && arguments.Count > 0) ? (string)arguments[0] : "pdf";

    #region Coordinate transforms (pdf4tcl Trans / TransR)

    private double TransX(double x) => (x * _unit) + _marginLeft;

    private double TransY(double y)
        => _orient
            ? (y * _unit) + _marginTop
            : _pageHeight - ((y * _unit) + _marginBottom);

    private double Scale(double value) => value * _unit;

    #endregion

    #region Page lifecycle

    private ReturnCode StartPageMethod(ArgumentList arguments, ref Result result)
    {
        if (_finished)
        {
            result = "PDF document already finished";
            return ReturnCode.Error;
        }

        string paper = _defaultPaper;
        bool landscape = _defaultLandscape;
        string margin = _defaultMargin;
        bool orient = _defaultOrient;

        int extra = arguments.Count - 2;

        if (extra == 1)
        {
            paper = arguments[2];
        }
        else if (extra > 0 && (extra % 2) != 0)
        {
            result = "Uneven number of arguments to startPage";
            return ReturnCode.Error;
        }
        else
        {
            for (int i = 2; i < arguments.Count; i += 2)
            {
                string option = arguments[i];
                string value = arguments[i + 1];
                switch (option)
                {
                    case "-paper": paper = value; break;
                    case "-landscape":
                        if (!TryParseBoolean(value, out landscape))
                        {
                            result = string.Format("expected boolean but got \"{0}\"", value);
                            return ReturnCode.Error;
                        }
                        break;
                    case "-margin": margin = value; break;
                    case "-orient":
                        if (!TryParseBoolean(value, out orient))
                        {
                            result = string.Format("expected boolean but got \"{0}\"", value);
                            return ReturnCode.Error;
                        }
                        break;
                    case "-rotate": break; // accepted, ignored (unused by DRAKON)
                    default:
                        result = string.Format("Unknown option {0}", option);
                        return ReturnCode.Error;
                }
            }
        }

        if (!Pdf4TclUnits.TryGetPaperSize(paper, _unit, out double width, out double height))
        {
            result = string.Format("papersize {0} is unknown", paper);
            return ReturnCode.Error;
        }

        if (!TrySetMargins(margin, ref result)) { return ReturnCode.Error; }

        if (landscape)
        {
            (width, height) = (height, width);
        }

        if (_graphics != null)
        {
            _graphics.Dispose();
            _graphics = null;
        }

        _page = _document.AddPage();
        _page.Width = width;
        _page.Height = height;
        _graphics = XGraphics.FromPdfPage(_page);

        _pageWidth = width;
        _pageHeight = height;
        _orient = orient;
        _textX = 0;
        _textY = 0;
        _font = null; // font itself persists; the XFont is re-created lazily

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private bool TrySetMargins(string margin, ref Result result)
    {
        string[] parts = string.IsNullOrWhiteSpace(margin)
            ? new[] { "0" }
            : margin.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        var points = new List<double>();
        foreach (string part in parts)
        {
            try
            {
                points.Add(Pdf4TclUnits.GetPoints(part, _unit));
            }
            catch (FormatException ex)
            {
                result = ex.Message;
                return false;
            }
        }

        switch (points.Count)
        {
            case 1:
                _marginLeft = _marginRight = _marginTop = _marginBottom = points[0];
                return true;
            case 2:
                _marginLeft = _marginRight = points[0];
                _marginTop = _marginBottom = points[1];
                return true;
            case 4:
                _marginLeft = points[0];
                _marginRight = points[1];
                _marginTop = points[2];
                _marginBottom = points[3];
                return true;
            default:
                result = string.Format("bad margin list '{0}'", margin);
                return false;
        }
    }

    /// <summary>pdf4tcl starts a default page implicitly when drawing without one.</summary>
    private ReturnCode EnsurePage(ref Result result)
    {
        if (_finished)
        {
            result = "PDF document already finished";
            return ReturnCode.Error;
        }
        if (_graphics != null) { return ReturnCode.Ok; }

        var arguments = new ArgumentList { "pdf", "startPage" };
        return StartPageMethod(arguments, ref result);
    }

    #endregion

    #region Graphics state methods

    private ReturnCode SetColorMethod(ArgumentList arguments, bool isFill, ref Result result)
    {
        if (arguments.Count < 3)
        {
            result = string.Format(
                "wrong # args: should be \"{0} {1} COLOR\"",
                GetCommandName(arguments), isFill ? "setFillColor" : "setStrokeColor");
            return ReturnCode.Error;
        }

        // pdf4tcl accepts the color as one argument or as loose r g b arguments.
        string colorText = (arguments.Count == 3)
            ? (string)arguments[2]
            : string.Join(" ", GetTailStrings(arguments, 2));

        if (!Pdf4TclColors.TryParse(colorText, out XColor color))
        {
            result = string.Format("Unknown color: {0}", colorText);
            return ReturnCode.Error;
        }

        if (isFill) { _fillColor = color; } else { _strokeColor = color; }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private ReturnCode SetFontMethod(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 3 && arguments.Count != 4)
        {
            result = string.Format(
                "wrong # args: should be \"{0} setFont SIZE ?FONTNAME?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        string fontName = (arguments.Count == 4) ? (string)arguments[3] : _fontName;
        if (string.IsNullOrEmpty(fontName))
        {
            result = "No font family set";
            return ReturnCode.Error;
        }
        if (!PdfFontStore.IsCreatedFont(fontName))
        {
            result = string.Format("Font {0} doesn't exist", fontName);
            return ReturnCode.Error;
        }

        double size;
        try
        {
            size = Pdf4TclUnits.GetPoints(arguments[2], _unit);
        }
        catch (FormatException ex)
        {
            result = ex.Message;
            return ReturnCode.Error;
        }
        if (size <= 0)
        {
            result = string.Format("bad font size {0}", (string)arguments[2]);
            return ReturnCode.Error;
        }

        _fontName = fontName;
        _fontSize = size;
        _font = null;

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private ReturnCode SetLineStyleMethod(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count < 3)
        {
            result = string.Format(
                "wrong # args: should be \"{0} setLineStyle WIDTH ?DASH ...?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        if (!double.TryParse(
                (string)arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double width))
        {
            result = string.Format("expected number but got \"{0}\"", (string)arguments[2]);
            return ReturnCode.Error;
        }

        double[] dashes = null;
        if (arguments.Count > 3)
        {
            dashes = new double[arguments.Count - 3];
            for (int i = 3; i < arguments.Count; i++)
            {
                if (!double.TryParse(
                        (string)arguments[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out dashes[i - 3]))
                {
                    result = string.Format("expected number but got \"{0}\"", (string)arguments[i]);
                    return ReturnCode.Error;
                }
            }
            if (dashes.Length == 0) { dashes = null; }
        }

        _lineWidth = width;
        _dashPoints = dashes;

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private XPen CreatePen()
    {
        var pen = new XPen(_strokeColor, _lineWidth);
        if (_dashPoints != null && _lineWidth > 0)
        {
            // pdf4tcl dash values are in points; XPen.DashPattern is in multiples
            // of the pen width (the GDI+ convention) — convert.
            var pattern = new double[_dashPoints.Length];
            for (int i = 0; i < _dashPoints.Length; i++)
            {
                pattern[i] = _dashPoints[i] / _lineWidth;
            }
            pen.DashPattern = pattern;
        }
        return pen;
    }

    #endregion

    #region Text methods

    private ReturnCode GetStringWidthMethod(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 3)
        {
            result = string.Format(
                "wrong # args: should be \"{0} getStringWidth TEXT\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        if (!TryGetFont(out XFont font, ref result)) { return ReturnCode.Error; }

        double width = MeasureWidth(arguments[2], font);
        result = FormatDouble(width / _unit);
        return ReturnCode.Ok;
    }

    private ReturnCode TextMethod(ArgumentList arguments, ref Result result)
    {
        if (EnsurePage(ref result) != ReturnCode.Ok) { return ReturnCode.Error; }
        if ((arguments.Count % 2) != 1)
        {
            result = string.Format(
                "wrong # args: should be \"{0} text TEXT ?option value ...?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        string text = arguments[2];
        string align = "left";
        object background = null; // XColor when a color was given; null otherwise
        double x = _textX;
        double y = _textY;

        for (int i = 3; i < arguments.Count; i += 2)
        {
            string option = arguments[i];
            string value = arguments[i + 1];
            switch (option)
            {
                case "-align":
                    align = value;
                    break;
                case "-x":
                    if (!TryParseNumber(value, out double xUser, ref result)) { return ReturnCode.Error; }
                    x = TransX(xUser);
                    break;
                case "-y":
                    if (!TryParseNumber(value, out double yUser, ref result)) { return ReturnCode.Error; }
                    y = TransY(yUser);
                    break;
                case "-background":
                case "-bg":
                case "-fill":
                    if (Pdf4TclColors.TryParse(value, out XColor backgroundColor))
                    {
                        background = backgroundColor;
                    }
                    else if (!TryParseBoolean(value, out bool _))
                    {
                        result = string.Format("Unknown color: {0}", value);
                        return ReturnCode.Error;
                    }
                    // A bare boolean refers to pdf4tcl's setBgColor state, which is
                    // outside this surface; accepted and ignored.
                    break;
                case "-angle":
                case "-xangle":
                case "-yangle":
                    // Rotated/skewed text is not implemented (unused by DRAKON);
                    // accepted and ignored.
                    break;
                default:
                    result = string.Format("unknown option {0}", option);
                    return ReturnCode.Error;
            }
        }

        if (!TryGetFont(out XFont font, ref result)) { return ReturnCode.Error; }

        double strWidth = MeasureWidth(text, font);

        if (align == "right")
        {
            x -= strWidth;
        }
        else if (align == "center")
        {
            x -= strWidth / 2.0;
        }

        if (background is XColor bg)
        {
            // Fill the string's bounding box (ascender to descender) behind the text.
            double ascent = font.Size * font.CellAscent / font.CellSpace;
            double descent = font.Size * font.CellDescent / font.CellSpace;
            _graphics.DrawRectangle(
                new XSolidBrush(bg), x, y - ascent, strWidth, ascent + descent);
        }

        if (text.Length > 0)
        {
            // The position is the text BASELINE origin, exactly pdf4tcl's Td/Tj
            // model; XStringFormats.Default is baseline-left.
            _graphics.DrawString(text, font, new XSolidBrush(_fillColor), x, y);
        }

        _textX = x + strWidth;
        _textY = y;

        result = FormatDouble(strWidth);
        return ReturnCode.Ok;
    }

    private bool TryGetFont(out XFont font, ref Result result)
    {
        font = null;
        if (string.IsNullOrEmpty(_fontName))
        {
            result = "No font set";
            return false;
        }
        if (_font == null)
        {
            _font = new XFont(_fontName, _fontSize);
        }
        font = _font;
        return true;
    }

    private static double MeasureWidth(string text, XFont font)
    {
        if (string.IsNullOrEmpty(text)) { return 0.0; }
        lock (MeasureSyncRoot)
        {
            if (_measureGraphics == null)
            {
                // A throwaway never-saved document provides a stable measuring
                // context so getStringWidth works before any page is started.
                var document = new PdfDocument();
                _measureGraphics = XGraphics.FromPdfPage(document.AddPage());
            }
            return _measureGraphics.MeasureString(text, font).Width;
        }
    }

    #endregion

    #region Drawing methods

    private ReturnCode LineMethod(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 6)
        {
            result = string.Format(
                "wrong # args: should be \"{0} line X1 Y1 X2 Y2\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }
        if (EnsurePage(ref result) != ReturnCode.Ok) { return ReturnCode.Error; }

        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryParseNumber(arguments[i + 2], out values[i], ref result)) { return ReturnCode.Error; }
        }

        _graphics.DrawLine(
            CreatePen(),
            TransX(values[0]), TransY(values[1]),
            TransX(values[2]), TransY(values[3]));

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private ReturnCode RectangleMethod(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count < 6 || (arguments.Count % 2) != 0)
        {
            result = string.Format(
                "wrong # args: should be \"{0} rectangle X Y W H ?option value ...?\"",
                GetCommandName(arguments));
            return ReturnCode.Error;
        }
        if (EnsurePage(ref result) != ReturnCode.Ok) { return ReturnCode.Error; }

        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryParseNumber(arguments[i + 2], out values[i], ref result)) { return ReturnCode.Error; }
        }

        bool filled = false;
        bool stroke = true;
        for (int i = 6; i < arguments.Count; i += 2)
        {
            string option = arguments[i];
            string value = arguments[i + 1];
            switch (option)
            {
                case "-filled":
                    if (!TryParseBoolean(value, out filled))
                    {
                        result = string.Format("expected boolean but got \"{0}\"", value);
                        return ReturnCode.Error;
                    }
                    break;
                case "-stroke":
                    if (!TryParseBoolean(value, out stroke))
                    {
                        result = string.Format("expected boolean but got \"{0}\"", value);
                        return ReturnCode.Error;
                    }
                    break;
                default:
                    result = string.Format("unknown option {0}", option);
                    return ReturnCode.Error;
            }
        }

        double width = Scale(values[2]);
        double height = Scale(values[3]);
        double x = TransX(values[0]);
        // With orient on, (x, y) is the rectangle's top-left corner; with orient
        // off it is the bottom-left corner in a y-up system, so the backend
        // top-left sits one height above the transformed point.
        double y = _orient ? TransY(values[1]) : TransY(values[1]) - height;

        // Normalize negative extents the way PDF's "re" operator tolerates them.
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }

        DrawShape(
            filled, stroke,
            pen => _graphics.DrawRectangle(pen, x, y, width, height),
            brush => _graphics.DrawRectangle(brush, x, y, width, height),
            (pen, brush) => _graphics.DrawRectangle(pen, brush, x, y, width, height));

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private ReturnCode PolygonMethod(ArgumentList arguments, ref Result result)
    {
        if (EnsurePage(ref result) != ReturnCode.Ok) { return ReturnCode.Error; }

        bool filled = false;
        bool stroke = true;
        var points = new List<XPoint>();

        // pdf4tcl interleaves "-option value" pairs with the coordinate pairs.
        int i = 2;
        while (i < arguments.Count)
        {
            string first = arguments[i];

            if (first.Length > 1 && first[0] == '-' && char.IsLetter(first[1]))
            {
                if ((i + 1) >= arguments.Count)
                {
                    result = string.Format("value for \"{0}\" missing", first);
                    return ReturnCode.Error;
                }
                string value = arguments[i + 1];
                switch (first)
                {
                    case "-filled":
                        if (!TryParseBoolean(value, out filled))
                        {
                            result = string.Format("expected boolean but got \"{0}\"", value);
                            return ReturnCode.Error;
                        }
                        break;
                    case "-stroke":
                        if (!TryParseBoolean(value, out stroke))
                        {
                            result = string.Format("expected boolean but got \"{0}\"", value);
                            return ReturnCode.Error;
                        }
                        break;
                    default:
                        result = string.Format("unknown option {0}", first);
                        return ReturnCode.Error;
                }
                i += 2;
                continue;
            }

            if ((i + 1) >= arguments.Count)
            {
                result = "odd number of polygon coordinates";
                return ReturnCode.Error;
            }
            if (!TryParseNumber(first, out double px, ref result)) { return ReturnCode.Error; }
            if (!TryParseNumber(arguments[i + 1], out double py, ref result)) { return ReturnCode.Error; }
            points.Add(new XPoint(TransX(px), TransY(py)));
            i += 2;
        }

        if (points.Count < 2)
        {
            result = "too few polygon coordinates";
            return ReturnCode.Error;
        }

        XPoint[] pointArray = points.ToArray();

        // PDF's "f" fill operator uses the nonzero winding rule.
        DrawShape(
            filled, stroke,
            pen => _graphics.DrawPolygon(pen, pointArray),
            brush => _graphics.DrawPolygon(brush, pointArray, XFillMode.Winding),
            (pen, brush) => _graphics.DrawPolygon(pen, brush, pointArray, XFillMode.Winding));

        result = string.Empty;
        return ReturnCode.Ok;
    }

    /// <summary>
    /// pdf4tcl's fill/stroke rule: <c>-filled 1</c> alone fills AND strokes (PDF "B"/"b");
    /// <c>-filled 1 -stroke 0</c> fills only; the default strokes only.
    /// </summary>
    private void DrawShape(
        bool filled, bool stroke,
        Action<XPen> strokeOnly, Action<XSolidBrush> fillOnly, Action<XPen, XSolidBrush> both)
    {
        if (filled && stroke) { both(CreatePen(), new XSolidBrush(_fillColor)); }
        else if (filled) { fillOnly(new XSolidBrush(_fillColor)); }
        else { strokeOnly(CreatePen()); }
    }

    #endregion

    #region Output / lifecycle methods

    private ReturnCode WriteMethod(ArgumentList arguments, ref Result result)
    {
        string file = _defaultFile;

        if (((arguments.Count - 2) % 2) != 0)
        {
            result = string.Format(
                "wrong # args: should be \"{0} write ?-file FILENAME?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }
        for (int i = 2; i < arguments.Count; i += 2)
        {
            string option = arguments[i];
            if (option == "-file") { file = arguments[i + 1]; }
            else
            {
                result = string.Format("unknown option {0}.", option);
                return ReturnCode.Error;
            }
        }

        if (string.IsNullOrEmpty(file))
        {
            // pdf4tcl streams to stdout here; a GUI-hosted interpreter has no
            // meaningful binary stdout, so require an explicit destination.
            result = "no output file specified: use \"write -file FILENAME\"";
            return ReturnCode.Error;
        }

        if (_graphics == null && _document.PageCount == 0)
        {
            Result ignored = null;
            EnsurePage(ref ignored); // pdf4tcl emits a valid empty page in this case
        }

        if (_graphics != null)
        {
            _graphics.Dispose();
            _graphics = null;
        }

        _finished = true;
        _document.Save(file);

        result = string.Empty;
        return ReturnCode.Ok;
    }

    private ReturnCode DestroyMethod(Interpreter interpreter, ref Result result)
    {
        if (!_destroyed)
        {
            _destroyed = true;

            if (_graphics != null)
            {
                _graphics.Dispose();
                _graphics = null;
            }
            _document.Dispose();

            Result removeError = null;
            interpreter.RemoveCommand(Token, null, ref removeError);
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    #endregion

    #region Parsing / formatting helpers

    private static IEnumerable<string> GetTailStrings(ArgumentList arguments, int start)
    {
        for (int i = start; i < arguments.Count; i++)
        {
            yield return arguments[i];
        }
    }

    private static bool TryParseNumber(string text, out double value, ref Result result)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        result = string.Format("expected number but got \"{0}\"", text);
        return false;
    }

    internal static bool TryParseBoolean(string text, out bool value)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": value = true; return true;
            case "0": case "false": case "no": case "off": value = false; return true;
            default: value = false; return false;
        }
    }

    internal static string FormatDouble(double value)
        => value.ToString(CultureInfo.InvariantCulture).Replace('E', 'e');

    #endregion
}
