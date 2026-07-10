using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Platform.TclTk.Extras.Pdf;

/// <summary>
/// pdf4tcl's measurement tables and value parsing: the <c>pdf4tcl::units</c> and
/// <c>pdf4tcl::paper_sizes</c> arrays (also published as Tcl array variables at
/// registration) and the <c>getPoints</c> rule for values with unit suffixes.
/// </summary>
internal static class Pdf4TclUnits
{
    /// <summary>Known units and their relationship to points (pdf4tcl's <c>units</c> array).</summary>
    public static readonly IReadOnlyDictionary<string, double> Units =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            { "mm", 72.0 / 25.4 },
            { "m", 72.0 / 25.4 },
            { "cm", 72.0 / 2.54 },
            { "c", 72.0 / 2.54 },
            { "i", 72.0 },
            { "p", 1.0 }
        };

    /// <summary>Known paper sizes in points, width then height (pdf4tcl's <c>paper_sizes</c> array).</summary>
    public static readonly IReadOnlyDictionary<string, (double Width, double Height)> PaperSizes =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
        {
            { "a0", (2380.0, 3368.0) },
            { "a1", (1684.0, 2380.0) },
            { "a2", (1190.0, 1684.0) },
            { "a3", (842.0, 1190.0) },
            { "a4", (595.0, 842.0) },
            { "a5", (421.0, 595.0) },
            { "a6", (297.0, 421.0) },
            { "11x17", (792.0, 1224.0) },
            { "ledger", (1224.0, 792.0) },
            { "legal", (612.0, 1008.0) },
            { "letter", (612.0, 792.0) }
        };

    /// <summary>
    /// pdf4tcl's <c>getPoints</c>: a bare number is multiplied by the document unit;
    /// a number with a known unit suffix ("10mm", "1.5 i") converts by that unit.
    /// </summary>
    /// <exception cref="FormatException">The value is neither form.</exception>
    public static double GetPoints(string value, double unit)
    {
        if (value == null) { throw new FormatException("Unknown value "); }

        string trimmed = value.Trim();

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return number * unit;
        }

        int split = trimmed.Length;
        while (split > 0 && char.IsLetter(trimmed[split - 1])) { split--; }

        string numberPart = trimmed.Substring(0, split).Trim();
        string unitPart = trimmed.Substring(split);

        if (numberPart.Length > 0 && Units.TryGetValue(unitPart, out double factor) &&
            double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number * factor;
        }

        throw new FormatException(string.Format("Unknown value {0}", value));
    }

    /// <summary>
    /// pdf4tcl's <c>getPaperSize</c>: a known paper name (case-insensitive), or a
    /// two-element list of width and height (unit-suffix values allowed).
    /// Returns false when the value is neither.
    /// </summary>
    public static bool TryGetPaperSize(string value, double unit, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value)) { return false; }

        string name = value.Trim().ToLowerInvariant();
        if (PaperSizes.TryGetValue(name, out (double Width, double Height) size))
        {
            width = size.Width;
            height = size.Height;
            return true;
        }

        string[] parts = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) { return false; }

        try
        {
            width = GetPoints(parts[0], unit);
            height = GetPoints(parts[1], unit);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
