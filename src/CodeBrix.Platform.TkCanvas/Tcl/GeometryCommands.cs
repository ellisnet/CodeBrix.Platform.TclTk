using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The <c>pack</c> and <c>grid</c> Tcl commands over the toolkit's
/// oracle-verified geometry managers.
/// </summary>
internal static class GeometryCommands
{
    internal static void Register(BridgeContext ctx)
    {
        BridgeRegistrar.Add(ctx, "pack", words => ctx.Ui(() => Pack(ctx, words)));
        BridgeRegistrar.Add(ctx, "grid", words => ctx.Ui(() => Grid(ctx, words)));
    }

    // ---------------------------------------------------------------- pack

    private static string Pack(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("pack option arg ?arg ...?");
        }

        string first = words[1];
        switch (first)
        {
            case "forget":
                for (int i = 2; i < words.Length; i++)
                {
                    PackLayout.Forget(ctx.ResolveWindow(words[i]));
                }
                return "";

            case "propagate":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("pack propagate window ?boolean?"); }
                TkWindow container = ctx.ResolveWindow(words[2]);
                if (words.Length == 3)
                {
                    return PackLayout.GetPropagate(container) ? "1" : "0";
                }
                PackLayout.SetPropagate(container, IsTclTrue(words[3]));
                return "";
            }

            case "info":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("pack info window"); }
                TkWindow window = ctx.ResolveWindow(words[2]);
                PackOptions info = PackLayout.Info(window);
                return PackInfoString(ctx, window, info);
            }

            case "slaves":
            case "content":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("pack " + first + " window"); }
                TkWindow container = ctx.ResolveWindow(words[2]);
                return TclString.JoinList(
                    PackLayout.Content(container).Select(w => ctx.PathOf(w)).ToList());
            }

            case "configure":
                return PackConfigure(ctx, words, 2);

            default:
                // "pack .w ?options?" — the common form.
                return PackConfigure(ctx, words, 1);
        }
    }

    private static string PackConfigure(BridgeContext ctx, string[] words, int start)
    {
        var windows = new List<TkWindow>();
        int index = start;
        while (index < words.Length && words[index].StartsWith(".", StringComparison.Ordinal))
        {
            windows.Add(ctx.ResolveWindow(words[index]));
            index++;
        }

        if (windows.Count == 0)
        {
            throw BridgeRegistrar.WrongArgs("pack option arg ?arg ...?");
        }

        Dictionary<string, string> optionWords = BridgeRegistrar.ParseOptionPairs(words, index);

        foreach (TkWindow window in windows)
        {
            // Amend semantics: start from the current options when re-packing.
            PackOptions options = window.ManagedBy == PackManager.Instance && window.Container != null
                ? PackLayout.Info(window)
                : new PackOptions();

            foreach (KeyValuePair<string, string> pair in optionWords)
            {
                ApplyPackOption(ctx, options, pair.Key, pair.Value);
            }

            PackLayout.Configure(window, options);
        }

        return "";
    }

    private static void ApplyPackOption(BridgeContext ctx, PackOptions options, string name, string value)
    {
        switch (name)
        {
            case "-side":
                options.Side = ParseSide(value);
                break;
            case "-fill":
                options.Fill = ParseFill(value);
                break;
            case "-expand":
                options.Expand = IsTclTrue(value);
                break;
            case "-anchor":
                options.Anchor = ParseAnchor(value);
                break;
            case "-padx":
                ApplyPad(value, (l, r) => { options.PadLeft = l; options.PadRight = r; });
                break;
            case "-pady":
                ApplyPad(value, (t, b) => { options.PadTop = t; options.PadBottom = b; });
                break;
            case "-ipadx":
                options.IPadX = 2 * ParsePixels(value);
                break;
            case "-ipady":
                options.IPadY = 2 * ParsePixels(value);
                break;
            case "-in":
                options.In = ctx.ResolveWindow(value);
                break;
            case "-before":
                options.Before = ctx.ResolveWindow(value);
                break;
            case "-after":
                options.After = ctx.ResolveWindow(value);
                break;
            default:
                throw new TkTclError("bad option \"" + name +
                    "\": must be -after, -anchor, -before, -expand, -fill, -in, -ipadx, -ipady, -padx, -pady, or -side");
        }
    }

    private static string PackInfoString(BridgeContext ctx, TkWindow window, PackOptions info)
    {
        if (info == null) { throw new TkTclError("window \"" + ctx.PathOf(window) + "\" isn't packed"); }
        var parts = new List<string>
        {
            "-in", ctx.PathOf(window.Container ?? window.Parent),
            "-anchor", info.Anchor.ToString().ToLowerInvariant(),
            "-expand", info.Expand ? "1" : "0",
            "-fill", info.Fill.ToString().ToLowerInvariant(),
            "-ipadx", (info.IPadX / 2).ToString(CultureInfo.InvariantCulture),
            "-ipady", (info.IPadY / 2).ToString(CultureInfo.InvariantCulture),
            "-padx", info.PadLeft.ToString(CultureInfo.InvariantCulture),
            "-pady", info.PadTop.ToString(CultureInfo.InvariantCulture),
            "-side", info.Side.ToString().ToLowerInvariant(),
        };
        return string.Join(" ", parts);
    }

    // ---------------------------------------------------------------- grid

    private static string Grid(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("grid option arg ?arg ...?");
        }

        string first = words[1];
        switch (first)
        {
            case "forget":
            case "remove":
                for (int i = 2; i < words.Length; i++)
                {
                    GridLayout.Forget(ctx.ResolveWindow(words[i]));
                }
                return "";

            case "propagate":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("grid propagate window ?boolean?"); }
                TkWindow container = ctx.ResolveWindow(words[2]);
                if (words.Length == 3)
                {
                    return GridLayout.GetPropagate(container) ? "1" : "0";
                }
                GridLayout.SetPropagate(container, IsTclTrue(words[3]));
                return "";
            }

            case "columnconfigure":
            case "rowconfigure":
            {
                if (words.Length < 4)
                {
                    throw BridgeRegistrar.WrongArgs("grid " + first + " master index ?-option value...?");
                }
                TkWindow container = ctx.ResolveWindow(words[2]);
                Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 4);

                int? minSize = null;
                int? weight = null;
                int? pad = null;
                string uniform = null;

                string text;
                if (options.TryGetValue("-minsize", out text)) { minSize = ParsePixels(text); }
                if (options.TryGetValue("-weight", out text)) { weight = ParseInt(text); }
                if (options.TryGetValue("-pad", out text)) { pad = ParsePixels(text); }
                options.TryGetValue("-uniform", out uniform);

                foreach (string indexWord in TclString.SplitList(words[3]))
                {
                    int index = ParseInt(indexWord);
                    if (first == "columnconfigure")
                    {
                        GridLayout.ColumnConfigure(container, index, minSize, weight, pad, uniform);
                    }
                    else
                    {
                        GridLayout.RowConfigure(container, index, minSize, weight, pad, uniform);
                    }
                }
                return "";
            }

            case "size":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("grid size window"); }
                TkWindow container = ctx.ResolveWindow(words[2]);
                int columns;
                int rows;
                GridLayout.Size(container, out columns, out rows);
                return columns.ToString(CultureInfo.InvariantCulture) + " " +
                    rows.ToString(CultureInfo.InvariantCulture);
            }

            case "slaves":
            case "content":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("grid " + first + " window"); }
                TkWindow container = ctx.ResolveWindow(words[2]);
                return TclString.JoinList(
                    GridLayout.Content(container).Select(w => ctx.PathOf(w)).ToList());
            }

            case "info":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("grid info window"); }
                TkWindow window = ctx.ResolveWindow(words[2]);
                if (window.ManagedBy != GridManager.Instance || window.Container == null) { return ""; }
                GridOptions info = GridLayout.Info(window);
                var parts = new List<string>
                {
                    "-in", ctx.PathOf(window.Container ?? window.Parent),
                    "-column", info.Column.ToString(CultureInfo.InvariantCulture),
                    "-row", info.Row.ToString(CultureInfo.InvariantCulture),
                    "-columnspan", info.ColumnSpan.ToString(CultureInfo.InvariantCulture),
                    "-rowspan", info.RowSpan.ToString(CultureInfo.InvariantCulture),
                    "-ipadx", info.IPadX.ToString(CultureInfo.InvariantCulture),
                    "-ipady", info.IPadY.ToString(CultureInfo.InvariantCulture),
                    "-padx", info.PadLeft.ToString(CultureInfo.InvariantCulture),
                    "-pady", info.PadTop.ToString(CultureInfo.InvariantCulture),
                    "-sticky", StickyString(info.Sticky),
                };
                return string.Join(" ", parts);
            }

            case "anchor":
            case "bbox":
            case "location":
                return "";

            case "configure":
                return GridConfigure(ctx, words, 2);

            default:
                return GridConfigure(ctx, words, 1);
        }
    }

    private static string GridConfigure(BridgeContext ctx, string[] words, int start)
    {
        var windows = new List<TkWindow>();
        int index = start;
        while (index < words.Length && words[index].StartsWith(".", StringComparison.Ordinal))
        {
            windows.Add(ctx.ResolveWindow(words[index]));
            index++;
        }

        if (windows.Count == 0)
        {
            throw BridgeRegistrar.WrongArgs("grid option arg ?arg ...?");
        }

        Dictionary<string, string> optionWords = BridgeRegistrar.ParseOptionPairs(words, index);

        int column = 0;
        foreach (TkWindow window in windows)
        {
            GridOptions options = window.ManagedBy == GridManager.Instance && window.Container != null
                ? GridLayout.Info(window)
                : new GridOptions();

            // Multiple windows on one grid line advance column by column (Tk's shorthand).
            if (windows.Count > 1 && !optionWords.ContainsKey("-column"))
            {
                options.Column = column;
            }

            foreach (KeyValuePair<string, string> pair in optionWords)
            {
                ApplyGridOption(ctx, options, pair.Key, pair.Value);
            }

            GridLayout.Configure(window, options);
            column++;
        }

        return "";
    }

    private static void ApplyGridOption(BridgeContext ctx, GridOptions options, string name, string value)
    {
        switch (name)
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
                ApplyPad(value, (l, r) => { options.PadLeft = l; options.PadRight = r; });
                break;
            case "-pady":
                ApplyPad(value, (t, b) => { options.PadTop = t; options.PadBottom = b; });
                break;
            case "-ipadx":
                options.IPadX = 2 * ParsePixels(value);
                break;
            case "-ipady":
                options.IPadY = 2 * ParsePixels(value);
                break;
            case "-in":
                options.In = ctx.ResolveWindow(value);
                break;
            default:
                throw new TkTclError("bad option \"" + name +
                    "\": must be -column, -columnspan, -in, -ipadx, -ipady, -padx, -pady, -row, -rowspan, or -sticky");
        }
    }

    // ------------------------------------------------------------- parsing

    private static void ApplyPad(string value, Action<int, int> assign)
    {
        List<string> parts = TclString.SplitList(value);
        if (parts.Count >= 2)
        {
            assign(ParsePixels(parts[0]), ParsePixels(parts[1]));
        }
        else
        {
            int pad = ParsePixels(value);
            assign(pad, pad);
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
            default:
                throw new TkTclError("bad side \"" + value +
                    "\": must be top, bottom, left, or right");
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
            default:
                throw new TkTclError("bad fill style \"" + value +
                    "\": must be none, x, y, or both");
        }
    }

    private static Anchor ParseAnchor(string value)
    {
        switch (value)
        {
            case "center": return Anchor.Center;
            case "n": return Anchor.N;
            case "ne": return Anchor.NE;
            case "e": return Anchor.E;
            case "se": return Anchor.SE;
            case "s": return Anchor.S;
            case "sw": return Anchor.SW;
            case "w": return Anchor.W;
            case "nw": return Anchor.NW;
            default:
                throw new TkTclError("bad anchor \"" + value + "\"");
        }
    }

    private static Sticky ParseSticky(string value)
    {
        Sticky sticky = Sticky.None;
        foreach (char c in value)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'n': sticky |= Sticky.N; break;
                case 'e': sticky |= Sticky.E; break;
                case 's': sticky |= Sticky.S; break;
                case 'w': sticky |= Sticky.W; break;
                case ' ': case ',': break;
                default:
                    throw new TkTclError("bad stickyness value \"" + value +
                        "\": must be a string containing n, e, s, and/or w");
            }
        }
        return sticky;
    }

    private static string StickyString(Sticky sticky)
    {
        string result = "";
        if ((sticky & Sticky.N) != 0) { result += "n"; }
        if ((sticky & Sticky.E) != 0) { result += "e"; }
        if ((sticky & Sticky.S) != 0) { result += "s"; }
        if ((sticky & Sticky.W) != 0) { result += "w"; }
        return result;
    }

    private static bool IsTclTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            case "0": case "false": case "no": case "off": return false;
            default:
                throw new TkTclError("expected boolean value but got \"" + value + "\"");
        }
    }

    private static int ParseInt(string text)
    {
        int value;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            throw new TkTclError("expected integer but got \"" + text + "\"");
        }
        return value;
    }

    private static int ParsePixels(string text)
    {
        int value;
        if (!TclString.TryParsePixels(text, out value))
        {
            throw new TkTclError("bad screen distance \"" + text + "\"");
        }
        return value;
    }
}
