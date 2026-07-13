using System;
using System.Globalization;
using System.Text;

using CodeBrix.Platform.TclTk._Components.Public;

namespace CodeBrix.Platform.TclTk.Extras.Sqlite;

/// <summary>
/// Conversions between SQLite column/parameter values and Tcl string values,
/// replicating the tclsqlite binding rules that real-world Tcl code relies on.
/// </summary>
internal static class SqliteTclValues
{
    /// <summary>
    /// Converts a value read from a SQLite column to its Tcl string form.
    /// SQL NULL becomes the empty string (tclsqlite's default <c>-nullvalue</c>).
    /// </summary>
    public static string ToTclString(object value)
    {
        if (value == null || value is DBNull) { return string.Empty; }
        if (value is string text) { return text; }
        if (value is long int64) { return int64.ToString(CultureInfo.InvariantCulture); }
        if (value is double real)
        {
            // Match Tcl's double formatting: a lower-case exponent marker, and a
            // ".0" suffix on integral values (Tcl renders REAL 3 as "3.0").
            string formatted = real.ToString(CultureInfo.InvariantCulture).Replace('E', 'e');
            if (formatted.IndexOf('.') < 0 && formatted.IndexOf('e') < 0 &&
                !double.IsNaN(real) && !double.IsInfinity(real))
            {
                formatted += ".0";
            }
            return formatted;
        }
        if (value is byte[] blob) { return Encoding.UTF8.GetString(blob); }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Chooses the value to bind for a Tcl variable, mirroring tclsqlite: tclsqlite
    /// classifies by the Tcl object's INTERNAL representation, so a value that is a
    /// plain string binds as TEXT even when it looks numeric ("007" stays "007";
    /// "1.10" stays "1.10"). Only genuinely numeric boxed values bind as numbers.
    /// Sniffing the string form instead would corrupt numeric-looking text stored
    /// in TEXT-affinity columns — visible in a .drn round-trip diff.
    /// </summary>
    public static object ToBindValue(Result value)
    {
        object raw = (value != null) ? value.Value : null;

        if (raw is bool boolean) { return boolean ? 1L : 0L; }
        if (raw is sbyte || raw is byte || raw is short || raw is ushort ||
            raw is int || raw is uint || raw is long)
        {
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }
        if (raw is ulong uint64)
        {
            return (uint64 <= long.MaxValue)
                ? (object)(long)uint64
                : uint64.ToString(CultureInfo.InvariantCulture);
        }
        if (raw is float single) { return (double)single; }
        if (raw is double real) { return real; }
        if (raw is decimal number) { return (double)number; }
        if (raw is byte[] blob) { return blob; }

        return (value != null) ? value.ToString() : string.Empty;
    }
}
