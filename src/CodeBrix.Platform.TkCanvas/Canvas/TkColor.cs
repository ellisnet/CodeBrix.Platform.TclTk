using System;
using System.Collections.Generic;
using System.Globalization;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// Tk color-name resolution: <c>#RGB</c>/<c>#RRGGBB</c>/<c>#RRRRGGGGBBBB</c>
/// hex forms, the X11 color names Tk programs actually use (with X11 values,
/// not the diverging CSS ones), <c>gray0</c>-<c>gray100</c>/<c>grey…</c>
/// shades, and the empty string meaning "no color" (an unfilled shape).
/// Unknown names resolve to black rather than throwing — the toolkit's
/// accept-and-no-op discipline.
/// </summary>
public static class TkColor
{
    private static readonly Dictionary<string, uint> Named = BuildNames();

    /// <summary>
    /// Resolves a Tk color specification.
    /// </summary>
    /// <param name="text">The color text (name or hex form).</param>
    /// <param name="color">The resolved color (black when unknown).</param>
    /// <returns>
    /// False when <paramref name="text"/> is empty or null — Tk's "no color",
    /// meaning the element is not drawn; true otherwise.
    /// </returns>
    public static bool TryParse(string text, out SKColor color)
    {
        color = SKColors.Black;
        if (string.IsNullOrEmpty(text)) { return false; }

        if (text[0] == '#')
        {
            string hex = text.Substring(1);
            uint value;
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                switch (hex.Length)
                {
                    case 3:
                    {
                        // #RGB: each nibble is the high nibble of the channel.
                        byte r = (byte)(((value >> 8) & 0xF) * 17);
                        byte g = (byte)(((value >> 4) & 0xF) * 17);
                        byte b = (byte)((value & 0xF) * 17);
                        color = new SKColor(r, g, b);
                        return true;
                    }
                    case 6:
                    {
                        color = new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                        return true;
                    }
                    case 12:
                    {
                        // #RRRRGGGGBBBB: 16 bits per channel; keep the high byte.
                        ulong wide = ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        color = new SKColor(
                                (byte)((wide >> 40) & 0xFF),
                                (byte)((wide >> 24) & 0xFF),
                                (byte)((wide >> 8) & 0xFF));
                        return true;
                    }
                    default:
                    {
                        return true; // malformed hex: accept as black
                    }
                }
            }
            return true;
        }

        string name = text.ToLowerInvariant();

        uint rgb;
        if (Named.TryGetValue(name, out rgb))
        {
            color = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        // gray0..gray100 / grey0..grey100 percentage shades.
        string tail = null;
        if (name.StartsWith("gray", StringComparison.Ordinal)) { tail = name.Substring(4); }
        else if (name.StartsWith("grey", StringComparison.Ordinal)) { tail = name.Substring(4); }
        int percent;
        if (tail != null && int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent)
                && percent >= 0 && percent <= 100)
        {
            byte level = (byte)((percent * 255 + 50) / 100);
            color = new SKColor(level, level, level);
            return true;
        }

        return true; // unknown name: accept as black
    }

    private static Dictionary<string, uint> BuildNames()
    {
        // X11 rgb.txt values (the table Tk resolves against on Unix).
        return new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            { "black", 0x000000 }, { "white", 0xFFFFFF },
            { "red", 0xFF0000 }, { "green", 0x00FF00 }, { "blue", 0x0000FF },
            { "yellow", 0xFFFF00 }, { "cyan", 0x00FFFF }, { "magenta", 0xFF00FF },
            { "gray", 0xBEBEBE }, { "grey", 0xBEBEBE },
            { "darkgray", 0xA9A9A9 }, { "darkgrey", 0xA9A9A9 },
            { "dimgray", 0x696969 }, { "dimgrey", 0x696969 },
            { "lightgray", 0xD3D3D3 }, { "lightgrey", 0xD3D3D3 },
            { "gainsboro", 0xDCDCDC }, { "silver", 0xC0C0C0 },
            { "whitesmoke", 0xF5F5F5 }, { "snow", 0xFFFAFA },
            { "ivory", 0xFFFFF0 }, { "beige", 0xF5F5DC },
            { "linen", 0xFAF0E6 }, { "antiquewhite", 0xFAEBD7 },
            { "darkred", 0x8B0000 }, { "maroon", 0xB03060 },
            { "firebrick", 0xB22222 }, { "brown", 0xA52A2A },
            { "indianred", 0xCD5C5C }, { "salmon", 0xFA8072 },
            { "lightsalmon", 0xFFA07A }, { "coral", 0xFF7F50 },
            { "tomato", 0xFF6347 }, { "orangered", 0xFF4500 },
            { "orange", 0xFFA500 }, { "darkorange", 0xFF8C00 },
            { "gold", 0xFFD700 }, { "khaki", 0xF0E68C },
            { "darkkhaki", 0xBDB76B }, { "goldenrod", 0xDAA520 },
            { "darkgoldenrod", 0xB8860B }, { "wheat", 0xF5DEB3 },
            { "tan", 0xD2B48C }, { "burlywood", 0xDEB887 },
            { "sandybrown", 0xF4A460 }, { "chocolate", 0xD2691E },
            { "peru", 0xCD853F }, { "sienna", 0xA0522D },
            { "saddlebrown", 0x8B4513 }, { "rosybrown", 0xBC8F8F },
            { "darkgreen", 0x006400 }, { "forestgreen", 0x228B22 },
            { "seagreen", 0x2E8B57 }, { "mediumseagreen", 0x3CB371 },
            { "limegreen", 0x32CD32 }, { "lime", 0x00FF00 },
            { "springgreen", 0x00FF7F }, { "palegreen", 0x98FB98 },
            { "lightgreen", 0x90EE90 }, { "darkseagreen", 0x8FBC8F },
            { "olivedrab", 0x6B8E23 }, { "olive", 0x808000 },
            { "yellowgreen", 0x9ACD32 }, { "lawngreen", 0x7CFC00 },
            { "chartreuse", 0x7FFF00 }, { "greenyellow", 0xADFF2F },
            { "darkolivegreen", 0x556B2F }, { "teal", 0x008080 },
            { "darkcyan", 0x008B8B }, { "lightcyan", 0xE0FFFF },
            { "paleturquoise", 0xAFEEEE }, { "aquamarine", 0x7FFFD4 },
            { "turquoise", 0x40E0D0 }, { "mediumturquoise", 0x48D1CC },
            { "darkturquoise", 0x00CED1 }, { "cadetblue", 0x5F9EA0 },
            { "steelblue", 0x4682B4 }, { "lightsteelblue", 0xB0C4DE },
            { "powderblue", 0xB0E0E6 }, { "lightblue", 0xADD8E6 },
            { "skyblue", 0x87CEEB }, { "lightskyblue", 0x87CEFA },
            { "deepskyblue", 0x00BFFF }, { "dodgerblue", 0x1E90FF },
            { "cornflowerblue", 0x6495ED }, { "royalblue", 0x4169E1 },
            { "mediumblue", 0x0000CD }, { "darkblue", 0x00008B },
            { "navy", 0x000080 }, { "navyblue", 0x000080 },
            { "midnightblue", 0x191970 }, { "slateblue", 0x6A5ACD },
            { "mediumslateblue", 0x7B68EE }, { "darkslateblue", 0x483D8B },
            { "blueviolet", 0x8A2BE2 }, { "indigo", 0x4B0082 },
            { "purple", 0xA020F0 }, { "mediumpurple", 0x9370DB },
            { "darkviolet", 0x9400D3 }, { "darkorchid", 0x9932CC },
            { "mediumorchid", 0xBA55D3 }, { "orchid", 0xDA70D6 },
            { "violet", 0xEE82EE }, { "plum", 0xDDA0DD },
            { "thistle", 0xD8BFD8 }, { "lavender", 0xE6E6FA },
            { "darkmagenta", 0x8B008B }, { "fuchsia", 0xFF00FF },
            { "deeppink", 0xFF1493 }, { "hotpink", 0xFF69B4 },
            { "pink", 0xFFC0CB }, { "lightpink", 0xFFB6C1 },
            { "palevioletred", 0xDB7093 }, { "mediumvioletred", 0xC71585 },
            { "crimson", 0xDC143C }, { "lightcoral", 0xF08080 },
            { "darksalmon", 0xE9967A }, { "mistyrose", 0xFFE4E1 },
            { "peachpuff", 0xFFDAB9 }, { "navajowhite", 0xFFDEAD },
            { "moccasin", 0xFFE4B5 }, { "bisque", 0xFFE4C4 },
            { "blanchedalmond", 0xFFEBCD }, { "papayawhip", 0xFFEFD5 },
            { "lemonchiffon", 0xFFFACD }, { "lightgoldenrodyellow", 0xFAFAD2 },
            { "lightyellow", 0xFFFFE0 }, { "cornsilk", 0xFFF8DC },
            { "oldlace", 0xFDF5E6 }, { "floralwhite", 0xFFFAF0 },
            { "seashell", 0xFFF5EE }, { "honeydew", 0xF0FFF0 },
            { "mintcream", 0xF5FFFA }, { "azure", 0xF0FFFF },
            { "aliceblue", 0xF0F8FF }, { "ghostwhite", 0xF8F8FF },
            { "lavenderblush", 0xFFF0F5 }, { "aqua", 0x00FFFF },
            { "slategray", 0x708090 }, { "slategrey", 0x708090 },
            { "lightslategray", 0x778899 }, { "lightslategrey", 0x778899 },
            { "darkslategray", 0x2F4F4F }, { "darkslategrey", 0x2F4F4F },
            { "lightgoldenrod", 0xEEDD82 }, { "lightslateblue", 0x8470FF },
            { "mediumspringgreen", 0x00FA9A }, { "mediumaquamarine", 0x66CDAA },
            { "lightseagreen", 0x20B2AA }, { "darkorange1", 0xFF7F00 },
        };
    }
}
