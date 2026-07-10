using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// A widget's Tk option bag (<c>-background</c>, <c>-text</c>, ...). Options
/// are stored as strings exactly as configured, with typed getters for the
/// ones a widget interprets. UNKNOWN-BUT-VALID options are accepted and
/// stored, never rejected — the toolkit-wide deferral discipline: arbitrary
/// Tk code must be able to poke an unbuilt corner without crashing, and
/// <c>cget</c> must read back whatever <c>configure</c> wrote.
/// </summary>
public sealed class WidgetOptions
{
    private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Stores an option value — the analogue of
    /// <c>$w configure -name value</c>. Option names keep their leading dash.
    /// </summary>
    /// <param name="name">The option name, e.g. <c>-background</c>.</param>
    /// <param name="value">The value as written.</param>
    public void Set(string name, string value)
    {
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("empty option name", nameof(name)); }
        _values[name] = value ?? string.Empty;
    }

    /// <summary>
    /// Reads an option value back — the analogue of <c>$w cget -name</c>.
    /// </summary>
    /// <param name="name">The option name.</param>
    /// <param name="defaultValue">The value when the option was never set.</param>
    /// <returns>The stored value, or <paramref name="defaultValue"/>.</returns>
    public string Get(string name, string defaultValue = "")
    {
        string value;
        return _values.TryGetValue(name, out value) ? value : defaultValue;
    }

    /// <summary>Whether the option has been explicitly set.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>True when a value is stored.</returns>
    public bool IsSet(string name)
    {
        return _values.ContainsKey(name);
    }

    /// <summary>Reads an integer-valued option (pixels, counts).</summary>
    /// <param name="name">The option name.</param>
    /// <param name="defaultValue">The value when unset or not an integer.</param>
    /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
    public int GetInt(string name, int defaultValue = 0)
    {
        string value;
        int parsed;
        if (_values.TryGetValue(name, out value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    /// <summary>Reads a double-valued option.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="defaultValue">The value when unset or not a number.</param>
    /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
    public double GetDouble(string name, double defaultValue = 0)
    {
        string value;
        double parsed;
        if (_values.TryGetValue(name, out value)
                && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    /// <summary>Reads a Tcl-boolean-valued option (<c>1/0 true/false yes/no on/off</c>).</summary>
    /// <param name="name">The option name.</param>
    /// <param name="defaultValue">The value when unset or not a boolean.</param>
    /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
    public bool GetBool(string name, bool defaultValue = false)
    {
        string value;
        if (!_values.TryGetValue(name, out value)) { return defaultValue; }
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            case "0": case "false": case "no": case "off": return false;
            default: return defaultValue;
        }
    }

    /// <summary>The names of every stored option (for <c>configure</c> listings).</summary>
    public IReadOnlyCollection<string> Names
    {
        get { return _values.Keys; }
    }
}
