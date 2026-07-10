using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// String plumbing shared by the canvas command surface: Tcl-compatible
/// double formatting (the <c>Tcl_PrintDouble</c> shape the coords/canvasx
/// results must match byte-for-byte against real Tk), a brace-aware Tcl list
/// splitter for parsing option values, and a joiner for building list-shaped
/// results.
/// </summary>
internal static class TclString
{
    /// <summary>
    /// Formats a double exactly like <c>Tcl_PrintDouble</c> with the default
    /// <c>tcl_precision</c> of 0: the shortest representation that round-trips,
    /// with a <c>.0</c> appended to integral values and a lowercase exponent.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The Tcl text of the value.</returns>
    public static string FormatDouble(double value)
    {
        if (double.IsNaN(value)) { return "NaN"; }
        if (double.IsPositiveInfinity(value)) { return "Inf"; }
        if (double.IsNegativeInfinity(value)) { return "-Inf"; }

        // .NET's default double formatting is the shortest round-trippable
        // form, same as Tcl 8.6's; align the cosmetic differences.
        string text = value.ToString(CultureInfo.InvariantCulture);

        int exponent = text.IndexOf('E');
        if (exponent >= 0)
        {
            // Tcl prints exponents lowercase ("1e+22").
            text = text.Substring(0, exponent) + "e" + text.Substring(exponent + 1);
            if (text.IndexOf('.') < 0)
            {
                // Tcl prints a mantissa without a fraction as-is ("1e+22").
                return text;
            }
            return text;
        }

        if (text.IndexOf('.') < 0)
        {
            text += ".0";
        }
        return text;
    }

    /// <summary>
    /// Splits Tcl list text into elements: whitespace separation with brace
    /// grouping and backslash-space handling — the subset canvas option
    /// values (<c>-scrollregion {0 0 100 100}</c>, dash lists, tag lists)
    /// require.
    /// </summary>
    /// <param name="text">The list text.</param>
    /// <returns>The elements, in order (empty for empty text).</returns>
    public static List<string> SplitList(string text)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(text)) { return words; }

        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) { i++; }
            if (i >= text.Length) { break; }

            if (text[i] == '{')
            {
                int depth = 1;
                int start = ++i;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '{') { depth++; }
                    else if (text[i] == '}') { depth--; }
                    if (depth > 0) { i++; }
                }
                words.Add(text.Substring(start, i - start));
                if (i < text.Length) { i++; }
            }
            else if (text[i] == '"')
            {
                int start = ++i;
                while (i < text.Length && text[i] != '"') { i++; }
                words.Add(text.Substring(start, i - start));
                if (i < text.Length) { i++; }
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) { i++; }
                words.Add(text.Substring(start, i - start));
            }
        }
        return words;
    }

    /// <summary>
    /// Joins elements into Tcl list text, bracing elements that contain
    /// whitespace or are empty (the presentation canvas results such as
    /// <c>gettags</c> need).
    /// </summary>
    /// <param name="elements">The elements to join.</param>
    /// <returns>The joined list text.</returns>
    public static string JoinList(IReadOnlyList<string> elements)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < elements.Count; i++)
        {
            if (i > 0) { builder.Append(' '); }
            string element = elements[i] ?? string.Empty;

            bool needsBraces = (element.Length == 0);
            foreach (char c in element)
            {
                if (char.IsWhiteSpace(c) || c == '{' || c == '}')
                {
                    needsBraces = true;
                    break;
                }
            }

            if (needsBraces)
            {
                builder.Append('{').Append(element).Append('}');
            }
            else
            {
                builder.Append(element);
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Parses a Tk canvas coordinate: a double with an optional screen-unit
    /// suffix (<c>c</c>entimeters, <c>m</c>illimeters, <c>i</c>nches,
    /// <c>p</c>oints), converted at <see cref="PixelsPerInch"/> — the
    /// analogue of <c>Tk_CanvasGetCoord</c>.
    /// </summary>
    /// <param name="text">The coordinate text.</param>
    /// <param name="value">The parsed value in pixels.</param>
    /// <returns>True when the text parsed.</returns>
    public static bool TryParseCoord(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text)) { return false; }

        double factor = 1.0;
        string number = text;
        switch (text[text.Length - 1])
        {
            case 'c': factor = PixelsPerInch / 2.54; number = text.Substring(0, text.Length - 1); break;
            case 'm': factor = PixelsPerInch / 25.4; number = text.Substring(0, text.Length - 1); break;
            case 'i': factor = PixelsPerInch; number = text.Substring(0, text.Length - 1); break;
            case 'p': factor = PixelsPerInch / 72.0; number = text.Substring(0, text.Length - 1); break;
            default: break;
        }

        double parsed;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }
        value = parsed * factor;
        return true;
    }

    /// <summary>
    /// Parses a Tk pixel specification (integer result): a coordinate per
    /// <see cref="TryParseCoord"/> rounded to the nearest integer — the
    /// analogue of <c>Tk_GetPixels</c>.
    /// </summary>
    /// <param name="text">The distance text.</param>
    /// <param name="value">The parsed value in whole pixels.</param>
    /// <returns>True when the text parsed.</returns>
    public static bool TryParsePixels(string text, out int value)
    {
        double parsed;
        if (!TryParseCoord(text, out parsed))
        {
            value = 0;
            return false;
        }
        value = (int)((parsed >= 0) ? parsed + 0.5 : parsed - 0.5);
        return true;
    }

    /// <summary>
    /// The screen resolution used to convert unit-suffixed distances to
    /// pixels. The classic X default of 96 dpi keeps unit conversion
    /// deterministic across machines (real Tk asks the X server).
    /// </summary>
    public static double PixelsPerInch { get; set; } = 96.0;
}
