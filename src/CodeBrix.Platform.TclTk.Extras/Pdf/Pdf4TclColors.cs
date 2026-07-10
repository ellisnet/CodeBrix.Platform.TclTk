using System;
using System.Globalization;

using CodeBrix.PdfDocuments.Drawing;

namespace CodeBrix.Platform.TclTk.Extras.Pdf;

/// <summary>
/// pdf4tcl's <c>GetColor</c> rule: a color is either a <c>#RRGGBB</c> hex string or a
/// list of three 0..1 component values. (Tk color names required a live Tk and are
/// not supported, exactly as in Tk-less pdf4tcl.)
/// </summary>
internal static class Pdf4TclColors
{
    /// <summary>Parses <paramref name="text"/> into an opaque <see cref="XColor"/>.</summary>
    public static bool TryParse(string text, out XColor color)
    {
        color = XColor.FromArgb(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) { return false; }

        string trimmed = text.Trim();

        if (trimmed.Length == 7 && trimmed[0] == '#')
        {
            if (int.TryParse(trimmed.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int red) &&
                int.TryParse(trimmed.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int green) &&
                int.TryParse(trimmed.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int blue))
            {
                color = XColor.FromArgb(red, green, blue);
                return true;
            }
            return false;
        }

        string[] parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) { return false; }

        var components = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                value < 0.0 || value > 1.0)
            {
                return false;
            }
            components[i] = (int)Math.Round(value * 255.0);
        }

        color = XColor.FromArgb(components[0], components[1], components[2]);
        return true;
    }
}
