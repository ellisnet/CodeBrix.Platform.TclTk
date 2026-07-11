using System;
using System.Collections.Generic;
using System.Linq;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;

namespace CodeBrix.Platform.TkCanvas.Theming;

/// <summary>
/// The <c>ttk::style</c> data model (the plan's B.12c): per-theme style
/// tables (<c>configure</c>), state-dependent maps (<c>map</c>), the
/// <c>lookup</c> resolution, and named themes (<c>theme
/// names/use/create</c>). The element/layout engine is deliberately deferred
/// (accept-and-no-op). Resolution follows real wish 8.6.16 (probed): a style
/// name falls back suffix-wise (<c>Fancy.TButton</c> → <c>TButton</c> →
/// <c>.</c>); dynamic map entries anywhere along that chain beat static
/// configure values; within one map the first state-spec matching the
/// current states wins, and an empty spec matches everything. Themes are
/// isolated — creating or switching themes never copies runtime-configured
/// settings between them.
/// </summary>
public sealed class TtkStyleEngine
{
    private sealed class MapEntry
    {
        public string[] States;
        public string Value;
    }

    private sealed class StyleTable
    {
        public readonly Dictionary<string, string> Settings =
                new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<MapEntry>> Maps =
                new Dictionary<string, List<MapEntry>>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> RawMaps =
                new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private sealed class StyleTheme
    {
        public string Parent;
        public readonly Dictionary<string, StyleTable> Styles =
                new Dictionary<string, StyleTable>(StringComparer.Ordinal);
    }

    private readonly WindowTree _tree;
    private readonly Dictionary<string, StyleTheme> _themes =
            new Dictionary<string, StyleTheme>(StringComparer.Ordinal);
    private string _currentTheme = "default";

    /// <summary>Creates the engine with the four standard theme names present.</summary>
    /// <param name="tree">The owning window tree (repainted on style changes).</param>
    internal TtkStyleEngine(WindowTree tree)
    {
        _tree = tree;
        foreach (string name in new[] { "default", "clam", "alt", "classic" })
        {
            _themes[name] = new StyleTheme();
        }
    }

    /// <summary>The current theme name — <c>ttk::style theme use</c> with no argument.</summary>
    public string CurrentTheme
    {
        get { return _currentTheme; }
    }

    /// <summary>The known theme names, sorted — <c>ttk::style theme names</c>.</summary>
    public IReadOnlyList<string> ThemeNames
    {
        get { return _themes.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList(); }
    }

    /// <summary>Switches the current theme — <c>ttk::style theme use name</c>.</summary>
    /// <param name="name">The theme name.</param>
    /// <exception cref="ArgumentException">The theme does not exist.</exception>
    public void ThemeUse(string name)
    {
        if (!_themes.ContainsKey(name))
        {
            throw new ArgumentException("theme \"" + name + "\" doesn't exist");
        }
        _currentTheme = name;
        Repaint();
    }

    /// <summary>Creates a theme — <c>ttk::style theme create name ?-parent p?</c>.</summary>
    /// <param name="name">The new theme name.</param>
    /// <param name="parent">The parent theme name, or null.</param>
    /// <exception cref="ArgumentException">The name already exists.</exception>
    public void ThemeCreate(string name, string parent = null)
    {
        if (_themes.ContainsKey(name))
        {
            throw new ArgumentException("theme \"" + name + "\" already exists");
        }
        _themes[name] = new StyleTheme { Parent = parent };
    }

    /// <summary>Sets one style option — <c>ttk::style configure style -option value</c>.</summary>
    /// <param name="style">The style name (e.g. <c>TButton</c>).</param>
    /// <param name="option">The option (with its dash, e.g. <c>-background</c>).</param>
    /// <param name="value">The value.</param>
    public void Configure(string style, string option, string value)
    {
        Table(style).Settings[option] = value ?? string.Empty;
        Repaint();
    }

    /// <summary>Reads back one configured style option (null when unset).</summary>
    /// <param name="style">The style name.</param>
    /// <param name="option">The option.</param>
    /// <returns>The configured value, or null.</returns>
    public string ConfigureGet(string style, string option)
    {
        StyleTable table = Find(style);
        string value;
        return (table != null && table.Settings.TryGetValue(option, out value)) ? value : null;
    }

    /// <summary>
    /// Sets one option's state map — <c>ttk::style map style -option {spec
    /// value ...}</c>. The map is a Tcl list of state-spec/value pairs; each
    /// spec is a list of state names, optionally <c>!</c>-negated.
    /// </summary>
    /// <param name="style">The style name.</param>
    /// <param name="option">The option (with its dash).</param>
    /// <param name="tclPairs">The Tcl list of spec/value pairs.</param>
    /// <exception cref="ArgumentException">The list has an odd element count.</exception>
    public void Map(string style, string option, string tclPairs)
    {
        List<string> parts = TclString.SplitList(tclPairs ?? string.Empty);
        if (parts.Count % 2 != 0)
        {
            throw new ArgumentException("state map must have an even number of elements");
        }
        var entries = new List<MapEntry>();
        for (int i = 0; i < parts.Count; i += 2)
        {
            entries.Add(new MapEntry
            {
                States = TclString.SplitList(parts[i]).ToArray(),
                Value = parts[i + 1],
            });
        }
        StyleTable table = Table(style);
        table.Maps[option] = entries;
        table.RawMaps[option] = tclPairs ?? string.Empty;
        Repaint();
    }

    /// <summary>Reads back one option's raw state map (null when unset).</summary>
    /// <param name="style">The style name.</param>
    /// <param name="option">The option.</param>
    /// <returns>The map as configured, or null.</returns>
    public string MapGet(string style, string option)
    {
        StyleTable table = Find(style);
        string value;
        return (table != null && table.RawMaps.TryGetValue(option, out value)) ? value : null;
    }

    /// <summary>
    /// Resolves one option — <c>ttk::style lookup style -option ?states?
    /// ?default?</c>: state maps along the whole fallback chain first, then
    /// static settings along the chain, then the default.
    /// </summary>
    /// <param name="style">The style name.</param>
    /// <param name="option">The option (with its dash).</param>
    /// <param name="states">The current widget states (null = normal).</param>
    /// <param name="defaultValue">Returned when nothing resolves.</param>
    /// <returns>The resolved value, or <paramref name="defaultValue"/>.</returns>
    public string Lookup(string style, string option,
            IReadOnlyCollection<string> states = null, string defaultValue = null)
    {
        StyleTheme theme = _themes[_currentTheme];

        foreach (string name in StyleChain(style))
        {
            StyleTable table;
            List<MapEntry> entries;
            if (theme.Styles.TryGetValue(name, out table)
                    && table.Maps.TryGetValue(option, out entries))
            {
                foreach (MapEntry entry in entries)
                {
                    if (StatesMatch(entry.States, states)) { return entry.Value; }
                }
            }
        }

        foreach (string name in StyleChain(style))
        {
            StyleTable table;
            string value;
            if (theme.Styles.TryGetValue(name, out table)
                    && table.Settings.TryGetValue(option, out value))
            {
                return value;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// The suffix-wise fallback chain of a style name:
    /// <c>Fancy.TButton</c> → <c>TButton</c> → <c>.</c>.
    /// </summary>
    /// <param name="style">The style name.</param>
    /// <returns>The chain, most specific first.</returns>
    public static IEnumerable<string> StyleChain(string style)
    {
        string current = string.IsNullOrEmpty(style) ? "." : style;
        while (current != ".")
        {
            yield return current;
            int dot = current.IndexOf('.');
            if (dot < 0 || dot == current.Length - 1) { break; }
            current = current.Substring(dot + 1);
        }
        yield return ".";
    }

    private static bool StatesMatch(string[] spec, IReadOnlyCollection<string> states)
    {
        foreach (string token in spec)
        {
            if (token.Length == 0) { continue; }
            bool negated = token[0] == '!';
            string name = negated ? token.Substring(1) : token;
            bool held = states != null && states.Contains(name);
            if (negated == held) { return false; }
        }
        return true;
    }

    private StyleTable Table(string style)
    {
        StyleTheme theme = _themes[_currentTheme];
        StyleTable table;
        if (!theme.Styles.TryGetValue(style, out table))
        {
            table = new StyleTable();
            theme.Styles[style] = table;
        }
        return table;
    }

    private StyleTable Find(string style)
    {
        StyleTable table;
        return _themes[_currentTheme].Styles.TryGetValue(style, out table) ? table : null;
    }

    private void Repaint()
    {
        if (_tree != null)
        {
            _tree.Scheduler.ScheduleRepaint();
        }
    }

    /// <summary>
    /// Executes a <c>ttk::style</c> command shape verbatim (the thin layer a
    /// Tcl bridge calls). Supported: <c>configure</c>, <c>map</c>,
    /// <c>lookup</c>, and <c>theme names/use/create</c>. The deferred
    /// element/layout engine subcommands accept and return empty, never
    /// throw. (<c>theme create -settings</c> scripts are the bridge's job:
    /// switch, evaluate, switch back.)
    /// </summary>
    /// <param name="words">The command words after <c>ttk::style</c>.</param>
    /// <returns>The textual result (Tk shapes).</returns>
    public string Execute(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            throw new ArgumentException("wrong # args: should be \"ttk::style option ?args?\"");
        }

        switch (words[0])
        {
            case "configure":
            {
                if (words.Count < 2) { throw new ArgumentException("wrong # args"); }
                string style = words[1];
                if (words.Count == 3) { return ConfigureGet(style, words[2]) ?? string.Empty; }
                for (int i = 2; i + 1 < words.Count; i += 2)
                {
                    Configure(style, words[i], words[i + 1]);
                }
                return string.Empty;
            }
            case "map":
            {
                if (words.Count < 2) { throw new ArgumentException("wrong # args"); }
                string style = words[1];
                if (words.Count == 3) { return MapGet(style, words[2]) ?? string.Empty; }
                for (int i = 2; i + 1 < words.Count; i += 2)
                {
                    Map(style, words[i], words[i + 1]);
                }
                return string.Empty;
            }
            case "lookup":
            {
                if (words.Count < 3) { throw new ArgumentException("wrong # args"); }
                IReadOnlyCollection<string> states =
                        (words.Count >= 4) ? TclString.SplitList(words[3]) : null;
                string defaultValue = (words.Count >= 5) ? words[4] : null;
                return Lookup(words[1], words[2], states, defaultValue) ?? string.Empty;
            }
            case "theme":
            {
                if (words.Count >= 2 && words[1] == "names")
                {
                    return TclString.JoinList(ThemeNames.ToList());
                }
                if (words.Count >= 2 && words[1] == "use")
                {
                    if (words.Count == 2) { return CurrentTheme; }
                    ThemeUse(words[2]);
                    return string.Empty;
                }
                if (words.Count >= 3 && words[1] == "create")
                {
                    string parent = null;
                    for (int i = 3; i + 1 < words.Count; i += 2)
                    {
                        if (words[i] == "-parent") { parent = words[i + 1]; }
                    }
                    ThemeCreate(words[2], parent);
                    return string.Empty;
                }
                throw new ArgumentException("wrong # args for theme");
            }
            case "element":
            case "layout":
            {
                // Deferred subsystem: accept-and-no-op (§3.2).
                return string.Empty;
            }
            default:
            {
                throw new ArgumentException("unknown ttk::style subcommand \"" + words[0] + "\"");
            }
        }
    }
}
