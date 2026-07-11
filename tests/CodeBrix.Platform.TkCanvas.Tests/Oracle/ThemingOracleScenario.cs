using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tests.Oracle;

/// <summary>
/// Replays a theming-oracle scenario (option database, ttk::style,
/// tk_setPalette derivation) against the B.12 engines and returns one output
/// line per query, exactly like the wish capture script
/// (tools/layout-oracle/capture_theming.tcl). Keep the two parsers in sync.
/// </summary>
internal static class ThemingOracleScenario
{
    /// <summary>The directory holding the vendored scenario/fixture pairs.</summary>
    public static string FixtureDirectory
    {
        get { return Path.Combine(AppContext.BaseDirectory, "Assets", "ThemingOracle"); }
    }

    /// <summary>
    /// Runs the scenario and returns the query outputs in order.
    /// </summary>
    /// <param name="scenarioPath">The scenario file path.</param>
    /// <returns>One line per query command.</returns>
    public static IReadOnlyList<string> Run(string scenarioPath)
    {
        TkWindow root = TkWindow.CreateRoot();
        OptionDatabase database = root.Tree.OptionDatabase;
        TtkStyleEngine styles = root.Tree.Styles;
        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        var outputs = new List<string>();

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') { continue; }

            List<string> words = TclString.SplitList(line);
            switch (words[0])
            {
                case "appname":
                {
                    database.ApplicationName = words[1];
                    break;
                }
                case "frame":
                {
                    TkWindow window = EnsureWindow(root, words[1]);
                    window.ClassName = words[2];
                    break;
                }
                case "add":
                {
                    database.Add(words[1], words[2], words[3]);
                    break;
                }
                case "optclear":
                {
                    database.Clear();
                    break;
                }
                case "get":
                {
                    TkWindow window = EnsureWindow(root, words[1]);
                    string value = database.Get(window, words[2], words[3]);
                    outputs.Add("get " + words[1] + " " + words[2] + " " + words[3] + " => " + value);
                    break;
                }
                case "configure":
                {
                    styles.Configure(words[1], words[2], words[3]);
                    break;
                }
                case "map":
                {
                    styles.Map(words[1], words[2], words[3]);
                    break;
                }
                case "lookup":
                {
                    string result;
                    if (words.Count == 3)
                    {
                        result = styles.Lookup(words[1], words[2]) ?? string.Empty;
                        outputs.Add("lookup " + words[1] + " " + words[2] + " => " + result);
                    }
                    else if (words.Count == 4)
                    {
                        result = styles.Lookup(words[1], words[2], TclString.SplitList(words[3])) ?? string.Empty;
                        outputs.Add("lookup " + words[1] + " " + words[2] + " " + words[3] + " => " + result);
                    }
                    else
                    {
                        result = styles.Lookup(words[1], words[2], TclString.SplitList(words[3]), words[4]) ?? string.Empty;
                        outputs.Add("lookup " + words[1] + " " + words[2] + " " + words[3] + " " + words[4] + " => " + result);
                    }
                    break;
                }
                case "theme_create":
                {
                    styles.ThemeCreate(words[1], (words.Count > 2) ? words[2] : null);
                    break;
                }
                case "theme_use":
                {
                    styles.ThemeUse(words[1]);
                    break;
                }
                case "palette":
                {
                    palette = TkTheme.DerivePalette(words.GetRange(1, words.Count - 1));
                    break;
                }
                case "bisque":
                {
                    palette = TkTheme.DerivePalette(BisqueArguments);
                    break;
                }
                case "query":
                {
                    string value;
                    if (!palette.TryGetValue(words[1], out value)) { value = string.Empty; }
                    outputs.Add("query " + words[1] + " => " + value);
                    break;
                }
                default:
                {
                    throw new InvalidDataException("unknown scenario command: " + words[0]);
                }
            }
        }
        return outputs;
    }

    /// <summary>The exact tk_bisque palette arguments from Tk's palette.tcl.</summary>
    private static readonly string[] BisqueArguments =
    {
        "activeBackground", "#e6ceb1", "activeForeground", "black",
        "background", "#ffe4c4", "disabledForeground", "#b0b0b0",
        "foreground", "black", "highlightBackground", "#ffe4c4",
        "highlightColor", "black", "insertBackground", "black",
        "selectBackground", "#e6ceb1", "selectForeground", "black",
        "troughColor", "#cdb79e",
    };

    private static TkWindow EnsureWindow(TkWindow root, string path)
    {
        if (path == ".") { return root; }

        TkWindow current = root;
        foreach (string name in path.TrimStart('.').Split('.'))
        {
            TkWindow next = null;
            foreach (TkWindow child in current.Children)
            {
                if (child.Name == name) { next = child; break; }
            }
            current = next ?? current.CreateChild(name);
        }
        return current;
    }
}
