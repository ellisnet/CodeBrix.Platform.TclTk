using System;

namespace CodeBrix.Platform.TkCanvas.Fonts;

/// <summary>
/// A Tk font specification: family, size, and style attributes — the parsed
/// form of a Tk font descriptor. Named fonts (created with
/// <c>font create</c>) are MUTABLE and shared: reconfiguring one changes
/// every widget using it, exactly like Tk; anonymous fonts parsed from a
/// descriptor are used as-is.
/// </summary>
public sealed class TkFont
{
    /// <summary>The font family, e.g. <c>Helvetica</c>, <c>Courier</c>, <c>DejaVu Sans</c>.</summary>
    public string Family { get; set; } = "TkDefault";

    /// <summary>
    /// The size: POSITIVE values are points, NEGATIVE values are pixels
    /// (Tk's convention), zero means an unspecified/default size.
    /// </summary>
    public int Size { get; set; }

    /// <summary>Whether the weight is bold (<c>-weight bold</c>).</summary>
    public bool Bold { get; set; }

    /// <summary>Whether the slant is italic (<c>-slant italic</c>).</summary>
    public bool Italic { get; set; }

    /// <summary>Whether text is underlined (<c>-underline</c>).</summary>
    public bool Underline { get; set; }

    /// <summary>Whether text is struck through (<c>-overstrike</c>).</summary>
    public bool Overstrike { get; set; }

    /// <summary>
    /// The name this font was created under (<c>font create NAME</c>), or
    /// null for an anonymous font parsed from a descriptor.
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>Copies all attributes (not the name) from another font.</summary>
    /// <param name="other">The font to copy from.</param>
    public void CopyAttributesFrom(TkFont other)
    {
        if (other == null) { throw new ArgumentNullException(nameof(other)); }
        Family = other.Family;
        Size = other.Size;
        Bold = other.Bold;
        Italic = other.Italic;
        Underline = other.Underline;
        Overstrike = other.Overstrike;
    }

    /// <summary>Returns a Tk-style description of the font.</summary>
    /// <returns>The font's name when named, else "family size ?styles?".</returns>
    public override string ToString()
    {
        if (Name != null) { return Name; }
        string text = Family + " " + Size;
        if (Bold) { text += " bold"; }
        if (Italic) { text += " italic"; }
        if (Underline) { text += " underline"; }
        if (Overstrike) { text += " overstrike"; }
        return text;
    }
}

/// <summary>
/// The vertical metrics of a font as Tk reports them
/// (<c>font metrics</c>): all values in pixels.
/// </summary>
public readonly struct FontMetrics
{
    /// <summary>Creates the metrics record.</summary>
    /// <param name="ascent">Pixels above the baseline (<c>-ascent</c>).</param>
    /// <param name="descent">Pixels below the baseline (<c>-descent</c>).</param>
    /// <param name="isFixed">Whether the font is fixed-pitch (<c>-fixed</c>).</param>
    public FontMetrics(int ascent, int descent, bool isFixed)
    {
        Ascent = ascent;
        Descent = descent;
        IsFixed = isFixed;
    }

    /// <summary>Pixels above the baseline (<c>-ascent</c>).</summary>
    public int Ascent { get; }

    /// <summary>Pixels below the baseline (<c>-descent</c>).</summary>
    public int Descent { get; }

    /// <summary>The full line height (<c>-linespace</c> = ascent + descent).</summary>
    public int LineSpace
    {
        get { return Ascent + Descent; }
    }

    /// <summary>Whether every glyph has the same width (<c>-fixed</c>).</summary>
    public bool IsFixed { get; }
}
