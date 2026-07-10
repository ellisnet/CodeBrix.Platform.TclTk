using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tests.Oracle;

/// <summary>
/// Replays a layout-oracle scenario file against the TkCanvas layout engine
/// and formats the resulting geometry exactly like the wish capture script
/// (tools/layout-oracle/capture_layout.tcl), one line per window:
/// <c>PATH x y width height reqwidth reqheight ismapped</c>. The scenario
/// line format is documented in the capture script; keep the two parsers in
/// sync.
/// </summary>
internal static class OracleScenario
{
    /// <summary>The directory holding the vendored scenario/fixture pairs.</summary>
    public static string FixtureDirectory
    {
        get { return Path.Combine(AppContext.BaseDirectory, "Assets", "LayoutOracle"); }
    }

    /// <summary>
    /// Builds the scenario's window tree, runs the layout, and returns the
    /// formatted geometry lines (root window first, then creation order).
    /// </summary>
    /// <param name="scenarioPath">The scenario file path.</param>
    /// <returns>The formatted geometry lines.</returns>
    public static IReadOnlyList<string> Run(string scenarioPath)
    {
        TkWindow root = TkWindow.CreateRoot();
        var order = new List<TkWindow> { root };

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') { continue; }

            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "window":
                {
                    TkWindow window = CreateWindow(root, words[1]);
                    window.SetRequestedSize(ParseInt(words[2]), ParseInt(words[3]));
                    order.Add(window);
                    break;
                }
                case "border":
                {
                    Find(root, words[1]).SetInternalBorder(ParseInt(words[2]));
                    break;
                }
                case "pack":
                {
                    PackLayout.Configure(Find(root, words[1]), ParsePackOptions(root, words));
                    break;
                }
                case "packforget":
                {
                    PackLayout.Forget(Find(root, words[1]));
                    break;
                }
                case "packpropagate":
                {
                    PackLayout.SetPropagate(Find(root, words[1]), ParseInt(words[2]) != 0);
                    break;
                }
                case "grid":
                {
                    GridLayout.Configure(Find(root, words[1]), ParseGridOptions(root, words));
                    break;
                }
                case "gridforget":
                {
                    GridLayout.Forget(Find(root, words[1]));
                    break;
                }
                case "gridpropagate":
                {
                    GridLayout.SetPropagate(Find(root, words[1]), ParseInt(words[2]) != 0);
                    break;
                }
                case "gridanchor":
                {
                    GridLayout.SetAnchor(Find(root, words[1]), ParseAnchor(words[2]));
                    break;
                }
                case "gridcolumn":
                case "gridrow":
                {
                    ParseSlotConfigure(root, words);
                    break;
                }
                case "rootsize":
                {
                    root.SetForcedSize(ParseInt(words[1]), ParseInt(words[2]));
                    break;
                }
                default:
                {
                    throw new InvalidDataException("unknown scenario command: " + words[0]);
                }
            }
        }

        TkLayout.Update(root);

        var lines = new List<string>();
        foreach (TkWindow window in order)
        {
            if (window.IsDestroyed) { continue; }
            int x = window.IsRoot ? 0 : window.X;
            int y = window.IsRoot ? 0 : window.Y;
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2} {3} {4} {5} {6} {7}",
                window.PathName, x, y, window.Width, window.Height,
                window.RequestedWidth, window.RequestedHeight,
                window.IsDisplayed ? 1 : 0));
        }
        return lines;
    }

    private static TkWindow CreateWindow(TkWindow root, string pathName)
    {
        int lastDot = pathName.LastIndexOf('.');
        string parentPath = (lastDot == 0) ? "." : pathName.Substring(0, lastDot);
        string leafName = pathName.Substring(lastDot + 1);
        return Find(root, parentPath).CreateChild(leafName);
    }

    private static TkWindow Find(TkWindow root, string pathName)
    {
        TkWindow window = root.FindDescendant(pathName);
        if (window == null) { throw new InvalidDataException("scenario references unknown window: " + pathName); }
        return window;
    }

    private static PackOptions ParsePackOptions(TkWindow root, string[] words)
    {
        var options = new PackOptions();
        for (int i = 2; i + 1 < words.Length; i += 2)
        {
            string value = words[i + 1];
            switch (words[i])
            {
                case "-side":
                    options.Side = ParseSide(value);
                    break;
                case "-anchor":
                    options.Anchor = ParseAnchor(value);
                    break;
                case "-fill":
                    options.Fill = ParseFill(value);
                    break;
                case "-expand":
                    options.Expand = ParseInt(value) != 0;
                    break;
                case "-padx":
                {
                    int left, right;
                    ParsePad(value, out left, out right);
                    options.PadLeft = left;
                    options.PadRight = right;
                    break;
                }
                case "-pady":
                {
                    int top, bottom;
                    ParsePad(value, out top, out bottom);
                    options.PadTop = top;
                    options.PadBottom = bottom;
                    break;
                }
                case "-ipadx":
                    options.IPadX = ParseInt(value);
                    break;
                case "-ipady":
                    options.IPadY = ParseInt(value);
                    break;
                case "-in":
                    options.In = Find(root, value);
                    break;
                case "-before":
                    options.Before = Find(root, value);
                    break;
                case "-after":
                    options.After = Find(root, value);
                    break;
                default:
                    throw new InvalidDataException("unknown pack option: " + words[i]);
            }
        }
        return options;
    }

    private static GridOptions ParseGridOptions(TkWindow root, string[] words)
    {
        var options = new GridOptions();
        for (int i = 2; i + 1 < words.Length; i += 2)
        {
            string value = words[i + 1];
            switch (words[i])
            {
                case "-row":
                    options.Row = ParseInt(value);
                    break;
                case "-column":
                    options.Column = ParseInt(value);
                    break;
                case "-rowspan":
                    options.RowSpan = ParseInt(value);
                    break;
                case "-columnspan":
                    options.ColumnSpan = ParseInt(value);
                    break;
                case "-sticky":
                    options.Sticky = ParseSticky(value);
                    break;
                case "-padx":
                {
                    int left, right;
                    ParsePad(value, out left, out right);
                    options.PadLeft = left;
                    options.PadRight = right;
                    break;
                }
                case "-pady":
                {
                    int top, bottom;
                    ParsePad(value, out top, out bottom);
                    options.PadTop = top;
                    options.PadBottom = bottom;
                    break;
                }
                case "-ipadx":
                    options.IPadX = ParseInt(value);
                    break;
                case "-ipady":
                    options.IPadY = ParseInt(value);
                    break;
                case "-in":
                    options.In = Find(root, value);
                    break;
                default:
                    throw new InvalidDataException("unknown grid option: " + words[i]);
            }
        }
        return options;
    }

    private static void ParseSlotConfigure(TkWindow root, string[] words)
    {
        TkWindow container = Find(root, words[1]);
        int index = ParseInt(words[2]);
        int? minSize = null, weight = null, pad = null;
        string uniform = null;

        for (int i = 3; i + 1 < words.Length; i += 2)
        {
            string value = words[i + 1];
            switch (words[i])
            {
                case "-minsize":
                    minSize = ParseInt(value);
                    break;
                case "-weight":
                    weight = ParseInt(value);
                    break;
                case "-pad":
                    pad = ParseInt(value);
                    break;
                case "-uniform":
                    uniform = value;
                    break;
                default:
                    throw new InvalidDataException("unknown slot option: " + words[i]);
            }
        }

        if (words[0] == "gridcolumn")
        {
            GridLayout.ColumnConfigure(container, index, minSize, weight, pad, uniform);
        }
        else
        {
            GridLayout.RowConfigure(container, index, minSize, weight, pad, uniform);
        }
    }

    private static Sticky ParseSticky(string value)
    {
        Sticky sticky = Sticky.None;
        foreach (char c in value)
        {
            switch (c)
            {
                case 'n': sticky |= Sticky.N; break;
                case 's': sticky |= Sticky.S; break;
                case 'e': sticky |= Sticky.E; break;
                case 'w': sticky |= Sticky.W; break;
                default: throw new InvalidDataException("unknown sticky flag: " + c);
            }
        }
        return sticky;
    }

    private static void ParsePad(string value, out int first, out int second)
    {
        int colon = value.IndexOf(':');
        if (colon >= 0)
        {
            first = ParseInt(value.Substring(0, colon));
            second = ParseInt(value.Substring(colon + 1));
        }
        else
        {
            first = ParseInt(value);
            second = first;
        }
    }

    private static Side ParseSide(string value)
    {
        switch (value)
        {
            case "top": return Side.Top;
            case "bottom": return Side.Bottom;
            case "left": return Side.Left;
            case "right": return Side.Right;
            default: throw new InvalidDataException("unknown side: " + value);
        }
    }

    private static Fill ParseFill(string value)
    {
        switch (value)
        {
            case "none": return Fill.None;
            case "x": return Fill.X;
            case "y": return Fill.Y;
            case "both": return Fill.Both;
            default: throw new InvalidDataException("unknown fill: " + value);
        }
    }

    private static Anchor ParseAnchor(string value)
    {
        switch (value)
        {
            case "n": return Anchor.N;
            case "ne": return Anchor.NE;
            case "e": return Anchor.E;
            case "se": return Anchor.SE;
            case "s": return Anchor.S;
            case "sw": return Anchor.SW;
            case "w": return Anchor.W;
            case "nw": return Anchor.NW;
            case "center": return Anchor.Center;
            default: throw new InvalidDataException("unknown anchor: " + value);
        }
    }

    private static int ParseInt(string value)
    {
        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
