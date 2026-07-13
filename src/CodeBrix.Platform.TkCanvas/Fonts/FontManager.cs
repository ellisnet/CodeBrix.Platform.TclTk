using System;
using System.Collections.Generic;
using System.Globalization;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Fonts;

/// <summary>
/// THE text-measurement seam (the plan's R2): the ONE service that resolves
/// Tk font specifications to Skia fonts, measures text, and reports metrics.
/// The Skia painter and the Tcl-facing <c>font measure</c>/<c>font
/// metrics</c> commands both go through this class and therefore through the
/// SAME <see cref="SKFont"/> — by construction they can never disagree, so
/// text-sized layouts (a consumer sizing its elements via
/// <c>font measure</c>) always fit what gets painted.
/// Also owns named fonts (<c>font create</c>/<c>configure</c>/<c>delete</c>)
/// and Tk's standard font names.
/// </summary>
public sealed class FontManager
{
    private readonly Dictionary<string, TkFont> _named =
            new Dictionary<string, TkFont>(StringComparer.Ordinal);
    private readonly Dictionary<string, SKTypeface> _typefaces =
            new Dictionary<string, SKTypeface>(StringComparer.Ordinal);

    /// <summary>
    /// Creates the manager with Tk's standard named fonts pre-defined
    /// (TkDefaultFont, TkTextFont, TkFixedFont, TkMenuFont, TkHeadingFont,
    /// TkCaptionFont, TkSmallCaptionFont, TkIconFont, TkTooltipFont).
    /// </summary>
    public FontManager()
    {
        DefineStandard("TkDefaultFont", "sans-serif", 10, false);
        DefineStandard("TkTextFont", "sans-serif", 10, false);
        DefineStandard("TkFixedFont", "monospace", 10, false);
        DefineStandard("TkMenuFont", "sans-serif", 10, false);
        DefineStandard("TkHeadingFont", "sans-serif", 10, true);
        DefineStandard("TkCaptionFont", "sans-serif", 12, true);
        DefineStandard("TkSmallCaptionFont", "sans-serif", 9, false);
        DefineStandard("TkIconFont", "sans-serif", 10, false);
        DefineStandard("TkTooltipFont", "sans-serif", 9, false);
    }

    /// <summary>
    /// Pixels per point for positive (point) font sizes. Tk computes this
    /// from the screen (<c>tk scaling</c>); the default is the common
    /// 96 dpi / 72, and the host can adjust it.
    /// </summary>
    public double PixelsPerPoint { get; set; } = 96.0 / 72.0;

    private void DefineStandard(string name, string family, int size, bool bold)
    {
        _named[name] = new TkFont { Name = name, Family = family, Size = size, Bold = bold };
    }

    /// <summary>
    /// Creates a named font — <c>font create NAME ?options?</c>.
    /// </summary>
    /// <param name="name">The font name; must not exist yet.</param>
    /// <param name="template">The attributes to copy, or null for defaults.</param>
    /// <returns>The created (mutable, shared) font.</returns>
    public TkFont CreateNamed(string name, TkFont template = null)
    {
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("empty font name", nameof(name)); }
        if (_named.ContainsKey(name))
        {
            throw new InvalidOperationException("named font \"" + name + "\" already exists");
        }

        var font = new TkFont { Name = name };
        if (template != null) { font.CopyAttributesFrom(template); }
        _named[name] = font;
        return font;
    }

    /// <summary>Looks up a named font, or null — <c>font names</c> membership.</summary>
    /// <param name="name">The font name.</param>
    /// <returns>The shared font instance, or null.</returns>
    public TkFont GetNamed(string name)
    {
        TkFont font;
        return _named.TryGetValue(name, out font) ? font : null;
    }

    /// <summary>Deletes a named font — <c>font delete NAME</c>.</summary>
    /// <param name="name">The font name.</param>
    public void DeleteNamed(string name)
    {
        if (!_named.Remove(name))
        {
            throw new InvalidOperationException("named font \"" + name + "\" doesn't exist");
        }
    }

    /// <summary>The currently defined named-font names — <c>font names</c>.</summary>
    public IReadOnlyCollection<string> Names
    {
        get { return _named.Keys; }
    }

    /// <summary>
    /// Resolves a Tk font descriptor to a font: a named font (shared
    /// instance), a <c>{family size ?styles?}</c> list, a
    /// <c>-family ... -size ...</c> option string, or an X core font name
    /// (accepted and mapped to the default font — accept-and-no-op).
    /// </summary>
    /// <param name="descriptor">The descriptor text.</param>
    /// <returns>The resolved font (never null).</returns>
    public TkFont Parse(string descriptor)
    {
        if (string.IsNullOrEmpty(descriptor)) { return _named["TkDefaultFont"]; }

        TkFont named = GetNamed(descriptor);
        if (named != null) { return named; }

        // X core font names ("-adobe-helvetica-...") are legacy: accept them
        // and fall back to the default font rather than erroring.
        if (descriptor[0] == '-' && descriptor.IndexOf(' ') < 0)
        {
            return _named["TkDefaultFont"];
        }

        List<string> words = SplitTclList(descriptor);
        if (words.Count == 0) { return _named["TkDefaultFont"]; }

        var font = new TkFont();
        if (words[0].Length > 0 && words[0][0] == '-')
        {
            // Option form: -family F -size N -weight bold -slant italic ...
            for (int i = 0; i + 1 < words.Count; i += 2)
            {
                string value = words[i + 1];
                switch (words[i])
                {
                    case "-family": font.Family = value; break;
                    case "-size": font.Size = ParseInt(value); break;
                    case "-weight": font.Bold = (value == "bold"); break;
                    case "-slant": font.Italic = (value == "italic"); break;
                    case "-underline": font.Underline = IsTrue(value); break;
                    case "-overstrike": font.Overstrike = IsTrue(value); break;
                    default: break; // accept-and-ignore unknown options
                }
            }
        }
        else
        {
            // List form: family ?size? ?style style ...?
            font.Family = words[0];
            int index = 1;
            if (words.Count > 1)
            {
                int size;
                if (int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                {
                    font.Size = size;
                    index = 2;
                }
            }
            for (; index < words.Count; index++)
            {
                switch (words[index])
                {
                    case "bold": font.Bold = true; break;
                    case "normal": font.Bold = false; break;
                    case "italic": font.Italic = true; break;
                    case "roman": font.Italic = false; break;
                    case "underline": font.Underline = true; break;
                    case "overstrike": font.Overstrike = true; break;
                    default: break; // accept-and-ignore unknown styles
                }
            }
        }
        return font;
    }

    /// <summary>
    /// Materializes the Skia font for a Tk font — the object the PAINTER
    /// draws with and every measurement is taken from.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>A configured <see cref="SKFont"/> (typefaces are cached).</returns>
    public SKFont GetSkFont(TkFont font)
    {
        if (font == null) { throw new ArgumentNullException(nameof(font)); }

        string family = MapFamily(font.Family);
        string key = family + "|" + (font.Bold ? "b" : "-") + (font.Italic ? "i" : "-");
        SKTypeface typeface;
        if (!_typefaces.TryGetValue(key, out typeface))
        {
            var style = new SKFontStyle(
                    font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            typeface = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
            _typefaces[key] = typeface;
        }

        return new SKFont(typeface, PixelSize(font));
    }

    /// <summary>
    /// Measures the advance width of <paramref name="text"/> — the analogue
    /// of <c>font measure FONT text</c>, in pixels, from the same
    /// <see cref="SKFont"/> the painter uses.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The advance width in pixels (rounded up).</returns>
    public int Measure(TkFont font, string text)
    {
        if (string.IsNullOrEmpty(text)) { return 0; }
        using (SKFont skFont = GetSkFont(font))
        {
            return (int)Math.Ceiling(skFont.MeasureText(text));
        }
    }

    /// <summary>
    /// Reports the vertical metrics — the analogue of
    /// <c>font metrics FONT</c>.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>Ascent, descent, linespace, and fixed-pitch flag.</returns>
    public FontMetrics Metrics(TkFont font)
    {
        using (SKFont skFont = GetSkFont(font))
        {
            SKFontMetrics metrics;
            skFont.GetFontMetrics(out metrics);
            int ascent = (int)Math.Ceiling(-metrics.Ascent);
            int descent = (int)Math.Ceiling(metrics.Descent);
            return new FontMetrics(ascent, descent, skFont.Typeface.IsFixedPitch);
        }
    }

    /// <summary>The pixel size of a Tk font (positive size = points, negative = pixels).</summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>The size in pixels.</returns>
    public float PixelSize(TkFont font)
    {
        int size = (font.Size != 0) ? font.Size : 10;
        if (size < 0) { return -size; }
        return (float)(size * PixelsPerPoint);
    }

    private static string MapFamily(string family)
    {
        if (string.IsNullOrEmpty(family) || family == "TkDefault") { return "sans-serif"; }
        switch (family.ToLowerInvariant())
        {
            case "helvetica": case "arial": return "sans-serif";
            case "courier": case "courier new": return "monospace";
            case "times": case "times new roman": return "serif";
            default: return family;
        }
    }

    private static bool IsTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            default: return false;
        }
    }

    private static int ParseInt(string value)
    {
        int parsed;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    /// <summary>
    /// Splits a Tcl list the small way font descriptors need: whitespace
    /// separation with brace grouping (<c>{DejaVu Sans} 12 bold</c>).
    /// </summary>
    private static List<string> SplitTclList(string text)
    {
        var words = new List<string>();
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
                if (i < text.Length) { i++; } // consume the closing brace
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
}
