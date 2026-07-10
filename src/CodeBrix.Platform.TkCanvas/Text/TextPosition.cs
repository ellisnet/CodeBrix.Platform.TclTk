using System;
using System.Globalization;

namespace CodeBrix.Platform.TkCanvas.Text;

/// <summary>
/// A position in a text widget: 1-based line, 0-based character — the value
/// behind Tk's <c>line.char</c> indices. The position after the buffer's
/// final (implicit) newline is <c>end</c>: line = last line + 1, char = 0.
/// </summary>
public readonly struct TextPosition : IComparable<TextPosition>, IEquatable<TextPosition>
{
    /// <summary>Creates a position.</summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="charIndex">The 0-based character index within the line.</param>
    public TextPosition(int line, int charIndex)
    {
        Line = line;
        Char = charIndex;
    }

    /// <summary>The 1-based line number.</summary>
    public int Line { get; }

    /// <summary>The 0-based character index (the line's length addresses its newline).</summary>
    public int Char { get; }

    /// <inheritdoc/>
    public int CompareTo(TextPosition other)
    {
        if (Line != other.Line) { return Line.CompareTo(other.Line); }
        return Char.CompareTo(other.Char);
    }

    /// <inheritdoc/>
    public bool Equals(TextPosition other)
    {
        return Line == other.Line && Char == other.Char;
    }

    /// <inheritdoc/>
    public override bool Equals(object obj)
    {
        return obj is TextPosition other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Line * 397 ^ Char;
    }

    /// <summary>Whether <paramref name="left"/> is before <paramref name="right"/>.</summary>
    public static bool operator <(TextPosition left, TextPosition right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>Whether <paramref name="left"/> is after <paramref name="right"/>.</summary>
    public static bool operator >(TextPosition left, TextPosition right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>Whether <paramref name="left"/> is at or before <paramref name="right"/>.</summary>
    public static bool operator <=(TextPosition left, TextPosition right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>Whether <paramref name="left"/> is at or after <paramref name="right"/>.</summary>
    public static bool operator >=(TextPosition left, TextPosition right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <summary>Equality.</summary>
    public static bool operator ==(TextPosition left, TextPosition right)
    {
        return left.Equals(right);
    }

    /// <summary>Inequality.</summary>
    public static bool operator !=(TextPosition left, TextPosition right)
    {
        return !left.Equals(right);
    }

    /// <summary>The Tk index text, <c>line.char</c>.</summary>
    /// <returns>The formatted index.</returns>
    public override string ToString()
    {
        return Line.ToString(CultureInfo.InvariantCulture) + "."
                + Char.ToString(CultureInfo.InvariantCulture);
    }
}
