using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Theming;

/// <summary>
/// The classic Tk option database (<c>option add/get/clear/readfile</c>) —
/// X-resource-style patterns over the window name/class hierarchy, with the
/// four standard priority levels (the plan's B.12b). Lookups happen when a
/// widget is CREATED, for options not explicitly configured; adding entries
/// later does not restyle existing widgets — exactly Tk's behavior.
/// Resolution among matching entries follows real wish 8.6.16 (probed):
/// highest priority wins, and entries of equal priority resolve to the most
/// recently added one — pattern specificity does not enter into it.
/// </summary>
public sealed class OptionDatabase
{
    private sealed class Entry
    {
        public string Pattern;
        public string[] Components;
        public bool[] Tight;
        public string Value;
        public int Priority;
        public int Serial;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private int _nextSerial;

    /// <summary>
    /// The application name a tight root-level pattern component must match —
    /// Tk's <c>tk appname</c> (in wish it defaults to the script name).
    /// </summary>
    public string ApplicationName { get; set; } = "tk";

    /// <summary>The application class for root-level class matches.</summary>
    public string ApplicationClass { get; set; } = "Tk";

    /// <summary>
    /// Adds a pattern to the database — <c>option add pattern value ?priority?</c>.
    /// Components separated by <c>.</c> bind tightly (direct child) and by
    /// <c>*</c> loosely (any depth, including zero); each component matches a
    /// window name or class, and the last component names the option (by its
    /// resource name or class).
    /// </summary>
    /// <param name="pattern">The option pattern.</param>
    /// <param name="value">The value.</param>
    /// <param name="priority">
    /// <c>widgetDefault</c> (20), <c>startupFile</c> (40),
    /// <c>userDefault</c> (60), <c>interactive</c> (80, the default), or a
    /// number 0-100 (clamped).
    /// </param>
    public void Add(string pattern, string value, string priority = "interactive")
    {
        if (string.IsNullOrEmpty(pattern)) { throw new ArgumentException("empty option pattern", nameof(pattern)); }

        var components = new List<string>();
        var tight = new List<bool>();
        int i = 0;
        bool nextTight = true;
        if (pattern[0] == '*') { nextTight = false; i = 1; }
        else if (pattern[0] == '.') { i = 1; components.Add(string.Empty); tight.Add(true); }

        int start = i;
        for (; i <= pattern.Length; i++)
        {
            if (i == pattern.Length || pattern[i] == '.' || pattern[i] == '*')
            {
                components.Add(pattern.Substring(start, i - start));
                tight.Add(nextTight);
                if (i < pattern.Length) { nextTight = pattern[i] == '.'; }
                start = i + 1;
            }
        }

        _entries.Add(new Entry
        {
            Pattern = pattern,
            Components = components.ToArray(),
            Tight = tight.ToArray(),
            Value = value ?? string.Empty,
            Priority = ParsePriority(priority),
            Serial = _nextSerial++,
        });
    }

    /// <summary>Removes every entry — <c>option clear</c>.</summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Loads entries from resource-file content — the body of
    /// <c>option readfile</c> (lines of <c>pattern: value</c>, <c>!</c>
    /// comments, backslash continuations).
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="priority">The priority for every loaded entry.</param>
    public void ReadContent(string content, string priority = "interactive")
    {
        if (string.IsNullOrEmpty(content)) { return; }

        string[] rawLines = content.Replace("\r\n", "\n").Split('\n');
        string pending = string.Empty;
        foreach (string rawLine in rawLines)
        {
            string line = pending + rawLine;
            pending = string.Empty;
            if (line.EndsWith("\\", StringComparison.Ordinal))
            {
                pending = line.Substring(0, line.Length - 1);
                continue;
            }
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '!' || trimmed[0] == '#') { continue; }
            int colon = trimmed.IndexOf(':');
            if (colon <= 0) { continue; }
            string pattern = trimmed.Substring(0, colon).Trim();
            string value = trimmed.Substring(colon + 1).TrimStart();
            Add(pattern, value, priority);
        }
    }

    /// <summary>
    /// Looks up the option value for a window — <c>option get window name class</c>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="name">The option's resource name (e.g. <c>foreground</c>).</param>
    /// <param name="className">The option's resource class (e.g. <c>Foreground</c>).</param>
    /// <returns>The winning entry's value, or an empty string.</returns>
    public string Get(TkWindow window, string name, string className)
    {
        Entry best = null;
        foreach (Entry entry in _entries)
        {
            if (!Matches(entry, window, name, className)) { continue; }
            if (best == null || entry.Priority > best.Priority
                    || (entry.Priority == best.Priority && entry.Serial > best.Serial))
            {
                best = entry;
            }
        }
        return (best != null) ? best.Value : string.Empty;
    }

    /// <summary>
    /// Applies the database to a newly created widget: for every entry whose
    /// path part matches the window, the winning value per option is stored
    /// into the option bag unless that option is already set. Option names
    /// derive from each entry's final component (lowercased, dash-prefixed) —
    /// so <c>*Button.background</c> and <c>*Button.Background</c> both feed
    /// <c>-background</c>.
    /// </summary>
    /// <param name="options">The widget's option bag.</param>
    /// <param name="window">The widget's window (name/class path source).</param>
    public void ApplyTo(WidgetOptions options, TkWindow window)
    {
        if (_entries.Count == 0) { return; }

        var winners = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (Entry entry in _entries)
        {
            string last = entry.Components[entry.Components.Length - 1];
            if (last.Length == 0) { continue; }
            string optionKey = "-" + last.ToLowerInvariant();

            // Re-run the full match with the entry's own option token as the
            // queried name AND class, so name-form and class-form entries for
            // the same option compete in one bucket (like a real Tk query,
            // where the class is the capitalized resource name).
            string resourceName = char.ToLowerInvariant(last[0]) + last.Substring(1);
            string resourceClass = char.ToUpperInvariant(last[0]) + last.Substring(1);
            if (!Matches(entry, window, resourceName, resourceClass)) { continue; }

            Entry best;
            if (!winners.TryGetValue(optionKey, out best)
                    || entry.Priority > best.Priority
                    || (entry.Priority == best.Priority && entry.Serial > best.Serial))
            {
                winners[optionKey] = entry;
            }
        }

        foreach (KeyValuePair<string, Entry> winner in winners)
        {
            if (!options.IsSet(winner.Key))
            {
                options.Set(winner.Key, winner.Value.Value);
            }
        }
    }

    /// <summary>Whether any entries exist (creation-time fast path).</summary>
    public bool IsEmpty
    {
        get { return _entries.Count == 0; }
    }

    private bool Matches(Entry entry, TkWindow window, string optionName, string optionClass)
    {
        // Build the level list root..window; each level matches by name or class.
        var path = new List<TkWindow>();
        for (TkWindow w = window; w != null; w = w.Parent) { path.Add(w); }
        path.Reverse();

        return MatchFrom(entry, 0, 0, path, optionName, optionClass);
    }

    private bool MatchFrom(Entry entry, int component, int position,
            List<TkWindow> path, string optionName, string optionClass)
    {
        int optionPosition = path.Count; // one past the window levels
        if (component == entry.Components.Length)
        {
            return position == optionPosition + 1;
        }

        bool isLast = component == entry.Components.Length - 1;
        if (entry.Tight[component])
        {
            if (!MatchesAt(entry.Components[component], position, isLast, path, optionName, optionClass))
            {
                return false;
            }
            return MatchFrom(entry, component + 1, position + 1, path, optionName, optionClass);
        }

        for (int candidate = position; candidate <= optionPosition; candidate++)
        {
            if (MatchesAt(entry.Components[component], candidate, isLast, path, optionName, optionClass)
                    && MatchFrom(entry, component + 1, candidate + 1, path, optionName, optionClass))
            {
                return true;
            }
        }
        return false;
    }

    private bool MatchesAt(string component, int position, bool isLast,
            List<TkWindow> path, string optionName, string optionClass)
    {
        int optionPosition = path.Count;
        if (position == optionPosition)
        {
            // Only the final component may name the option.
            return isLast
                    && (string.Equals(component, optionName, StringComparison.Ordinal)
                        || string.Equals(component, optionClass, StringComparison.Ordinal));
        }
        if (position > optionPosition || isLast) { return false; }

        if (position == 0)
        {
            return string.Equals(component, ApplicationName, StringComparison.Ordinal)
                    || string.Equals(component, ApplicationClass, StringComparison.Ordinal);
        }
        TkWindow level = path[position];
        return string.Equals(component, level.Name, StringComparison.Ordinal)
                || string.Equals(component, level.ClassName, StringComparison.Ordinal);
    }

    private static int ParsePriority(string priority)
    {
        switch (priority)
        {
            case null:
            case "":
            case "interactive": return 80;
            case "widgetDefault": return 20;
            case "startupFile": return 40;
            case "userDefault": return 60;
            default:
            {
                int value;
                if (int.TryParse(priority, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    if (value < 0) { return 0; }
                    if (value > 100) { return 100; }
                    return value;
                }
                throw new ArgumentException("bad priority level \"" + priority + "\"");
            }
        }
    }
}
