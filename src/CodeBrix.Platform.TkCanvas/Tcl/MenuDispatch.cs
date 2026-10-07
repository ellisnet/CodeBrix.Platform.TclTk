using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Menus;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The menu instance-command dispatcher: <c>add</c>/<c>insert</c>/
/// <c>delete</c>/<c>entryconfigure</c>/<c>entrycget</c>/<c>index</c>/
/// <c>invoke</c>/<c>post</c>/<c>unpost</c>/<c>xposition</c>/<c>yposition</c>
/// over <see cref="MenuWidget"/>. Runs on the UI thread.
/// </summary>
internal static class MenuDispatch
{
    internal static string Execute(BridgeContext ctx, string path, MenuWidget menu, string[] words)
    {
        string sub = words[1];
        switch (sub)
        {
            case "add":
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs(path + " add type ?-option value ...?"); }
                AddEntry(ctx, menu, words[2], BridgeRegistrar.ParseOptionPairs(words, 3));
                return "";

            case "insert":
                // Mid-menu insert is not needed here; treat as append (index accepted, ignored).
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs(path + " insert index type ?-option value ...?"); }
                AddEntry(ctx, menu, words[3], BridgeRegistrar.ParseOptionPairs(words, 4));
                return "";

            case "delete":
                if (words.Length >= 3)
                {
                    int first = EntryIndex(menu, words[2]);
                    int last = words.Length >= 4 ? EntryIndex(menu, words[3]) : first;
                    menu.RemoveEntries(first, last);
                }
                return "";

            case "entryconfigure":
                if (words.Length >= 3)
                {
                    int index = EntryIndex(menu, words[2]);
                    if (index >= 0 && index < menu.Entries.Count)
                    {
                        ApplyEntryOptions(ctx, menu, menu.Entries[index],
                            BridgeRegistrar.ParseOptionPairs(words, 3));
                        menu.Measure();
                    }
                }
                return "";

            case "entrycget":
                if (words.Length >= 4)
                {
                    int index = EntryIndex(menu, words[2]);
                    if (index >= 0 && index < menu.Entries.Count)
                    {
                        return EntryCget(menu.Entries[index], words[3]);
                    }
                }
                return "";

            case "index":
                if (words.Length >= 3)
                {
                    int index = EntryIndex(menu, words[2]);
                    return index < 0 ? "none" : index.ToString(CultureInfo.InvariantCulture);
                }
                return "none";

            case "invoke":
                if (words.Length >= 3)
                {
                    int index = EntryIndex(menu, words[2]);
                    if (index >= 0) { menu.Invoke(index); }
                }
                return "";

            case "post":
                if (words.Length >= 4)
                {
                    ctx.Tree.Menus.Popup(menu,
                        int.Parse(words[2], CultureInfo.InvariantCulture),
                        int.Parse(words[3], CultureInfo.InvariantCulture));
                }
                return "";

            case "unpost":
                ctx.Tree.Menus.Unpost();
                return "";

            case "activate":
                if (words.Length >= 3) { menu.ActiveIndex = EntryIndex(menu, words[2]); }
                return "";

            case "xposition":
            case "yposition":
            {
                if (words.Length != 3) { throw BridgeRegistrar.WrongArgs(path + " " + sub + " index"); }

                // Tk: the entry's left (x) or top (y) edge in the menu window;
                // a number past the last entry means the last entry, and
                // "none" (or no such entry at all) reports 0.
                int index = EntryIndex(menu, words[2]);
                int number;
                if (int.TryParse(words[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
                    number < 0)
                {
                    throw new TkTclError("bad menu entry index \"" + words[2] + "\"");
                }
                if (index >= menu.Entries.Count) { index = menu.Entries.Count - 1; }
                if (index < 0) { return "0"; }

                SKRectI rect = menu.EntryRect(index);
                return (sub == "xposition" ? rect.Left : rect.Top).ToString(CultureInfo.InvariantCulture);
            }

            case "type":
                if (words.Length >= 3)
                {
                    int index = EntryIndex(menu, words[2]);
                    if (index >= 0 && index < menu.Entries.Count)
                    {
                        return menu.Entries[index].Type.ToString().ToLowerInvariant();
                    }
                }
                return "";

            default:
                return string.Empty;
        }
    }

    private static void AddEntry(
        BridgeContext ctx, MenuWidget menu, string type, Dictionary<string, string> options)
    {
        string label;
        options.TryGetValue("-label", out label);
        label = label ?? "";

        string accelerator;
        options.TryGetValue("-accelerator", out accelerator);

        int underline = -1;
        string underlineText;
        if (options.TryGetValue("-underline", out underlineText))
        {
            int.TryParse(underlineText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out underline);
        }

        MenuEntry entry;
        switch (type)
        {
            case "command":
                entry = menu.AddCommand(label, null, accelerator, underline);
                break;
            case "cascade":
            {
                MenuWidget submenu = null;
                string submenuPath;
                if (options.TryGetValue("-menu", out submenuPath))
                {
                    ctx.MenusByPath.TryGetValue(submenuPath, out submenu);
                }
                entry = menu.AddCascade(label, submenu, underline);
                break;
            }
            case "separator":
                entry = menu.AddSeparator();
                break;
            case "checkbutton":
                entry = menu.AddCheckbutton(label);
                break;
            case "radiobutton":
                entry = menu.AddRadiobutton(label);
                break;
            default:
                throw new TkTclError("bad menu entry type \"" + type + "\"");
        }

        ApplyEntryOptions(ctx, menu, entry, options);
    }

    private static void ApplyEntryOptions(
        BridgeContext ctx, MenuWidget menu, MenuEntry entry, Dictionary<string, string> options)
    {
        string label;
        if (options.TryGetValue("-label", out label)) { entry.Label = label; }

        string accelerator;
        if (options.TryGetValue("-accelerator", out accelerator)) { entry.Accelerator = accelerator; }

        string underlineText;
        if (options.TryGetValue("-underline", out underlineText))
        {
            int underline;
            if (int.TryParse(underlineText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out underline))
            {
                entry.Underline = underline;
            }
        }

        string state;
        if (options.TryGetValue("-state", out state))
        {
            entry.Disabled = state.StartsWith("disable", StringComparison.Ordinal);
        }

        string command;
        if (options.TryGetValue("-command", out command))
        {
            string script = command;
            entry.Command = script.Length == 0
                ? (Action)null
                : () => ctx.EvalCallbackScript(script);
        }

        string image;
        if (options.TryGetValue("-image", out image)) { entry.Image = image; }

        string compound;
        if (options.TryGetValue("-compound", out compound)) { entry.Compound = compound; }

        string submenuPath;
        if (options.TryGetValue("-menu", out submenuPath))
        {
            MenuWidget submenu;
            if (ctx.MenusByPath.TryGetValue(submenuPath, out submenu))
            {
                entry.Submenu = submenu;
            }
        }
    }

    private static string EntryCget(MenuEntry entry, string option)
    {
        switch (option)
        {
            case "-label": return entry.Label;
            case "-accelerator": return entry.Accelerator;
            case "-underline": return entry.Underline.ToString(CultureInfo.InvariantCulture);
            case "-state": return entry.Disabled ? "disabled" : "normal";
            case "-image": return entry.Image;
            case "-compound": return entry.Compound;
            default: return "";
        }
    }

    /// <summary>
    /// Resolves a Tk menu entry index (number, end, last, active, none,
    /// <c>@y</c> / <c>@x,y</c>, or a label match).
    /// </summary>
    private static int EntryIndex(MenuWidget menu, string index)
    {
        switch (index)
        {
            case "end":
            case "last":
                return menu.Entries.Count - 1;
            case "active":
                return menu.ActiveIndex;
            case "none":
                return -1;
        }

        if (index.Length > 1 && index[0] == '@')
        {
            return EntryIndexAt(menu, index);
        }

        int value;
        if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        for (int i = 0; i < menu.Entries.Count; i++)
        {
            if (menu.Entries[i].Label == index) { return i; }
        }

        throw new TkTclError("bad menu entry index \"" + index + "\"");
    }

    /// <summary>
    /// The <c>@y</c> / <c>@x,y</c> index form: the entry whose rectangle holds
    /// the point (separators included, as in Tk), or -1 ("none") when the
    /// point is outside every entry. A popup menu uses y, a menubar x.
    /// </summary>
    private static int EntryIndexAt(MenuWidget menu, string index)
    {
        string coords = index.Substring(1);
        string xText = null;
        string yText = coords;
        int comma = coords.IndexOf(',');
        if (comma >= 0)
        {
            xText = coords.Substring(0, comma);
            yText = coords.Substring(comma + 1);
        }

        int x = 0;
        int y;
        if ((xText != null && !int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) ||
            !int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
        {
            throw new TkTclError("bad menu entry index \"" + index + "\"");
        }
        if (xText == null) { x = y; }

        for (int i = 0; i < menu.Entries.Count; i++)
        {
            SKRectI rect = menu.EntryRect(i);
            if (menu.IsMenubar ? (x >= rect.Left && x < rect.Right) : (y >= rect.Top && y < rect.Bottom))
            {
                return i;
            }
        }
        return -1;
    }
}
