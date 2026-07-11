using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The widget instance-command dispatcher: <c>.path subcommand args...</c>
/// routed to the widget's C# API (the canvas and photo systems route to
/// their verbatim <c>Execute(words)</c> string layers). Subcommands the
/// toolkit defers accept-and-return-empty per the deferral discipline.
/// </summary>
internal static class WidgetDispatch
{
    internal static string Execute(BridgeContext ctx, string path, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs(path + " option ?arg ...?");
        }

        string sub = words[1];
        if (sub == "configure" || sub == "config")
        {
            return Configure(ctx, path, words);
        }

        return ctx.Ui(() => ExecuteOnUi(ctx, path, words));
    }

    private static string Configure(BridgeContext ctx, string path, string[] words)
    {
        // Single-option query form: "$w configure -option" returns that
        // option's configuration record {-name dbName dbClass default value},
        // matching Tk. Programs read the current value as [lindex ... end]
        // (DRAKON's cpicker does exactly this on a canvas -width).
        if (words.Length == 3 && words[2].Length > 0 && words[2][0] == '-')
        {
            string option = words[2];
            string current = ctx.Ui(() =>
            {
                if (path == TkPaths.Root) { return ""; }
                TkWindow window = ctx.ResolveWindow(path);
                IWidget widget = window.Widget;
                return widget != null ? widget.Options.Get(option, "") : "";
            });
            string bareName = option.Substring(1);
            string dbClass = bareName.Length > 0
                ? char.ToUpperInvariant(bareName[0]) + bareName.Substring(1)
                : bareName;
            return TclString.JoinList(new[] { option, bareName, dbClass, "", current });
        }

        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 2);

        ctx.Ui(() =>
        {
            if (path == TkPaths.Root)
            {
                ConfigureRoot(ctx, options);
                return;
            }

            TkWindow window = ctx.ResolveWindow(path);

            MenuWidget menu;
            if (ctx.MenusByPath.TryGetValue(path, out menu))
            {
                menu.Configure(options);
                menu.Measure();
                return;
            }

            IWidget widget = window.Widget;
            if (widget == null)
            {
                return;
            }

            string menuPath;
            if (window.ClassName == "Toplevel" && options.TryGetValue("-menu", out menuPath))
            {
                // Accept toplevel menus; only the root menubar is displayed.
                options.Remove("-menu");
            }

            widget.Configure(options);
            widget.Measure();
            ctx.Tree.Scheduler.ScheduleRelayout();
        });

        if (options.Count > 0)
        {
            WidgetCommands.ApplyLinkedOptions(ctx, path, options);
        }

        return string.Empty;
    }

    private static void ConfigureRoot(BridgeContext ctx, Dictionary<string, string> options)
    {
        string menuPath;
        if (options.TryGetValue("-menu", out menuPath))
        {
            if (menuPath.Length == 0)
            {
                ctx.Tree.Menus.SetMenubar(null);
                if (ctx.MenubarWindow != null && !ctx.MenubarWindow.IsDestroyed)
                {
                    ctx.MenubarWindow.Destroy();
                    ctx.MenubarWindow = null;
                }
            }
            else
            {
                MenuWidget source;
                if (ctx.MenusByPath.TryGetValue(menuPath, out source))
                {
                    ShowRootMenubar(ctx, source);
                }
            }
            options.Remove("-menu");
        }

        foreach (KeyValuePair<string, string> option in options)
        {
            ctx.RootOptions.Set(option.Key, option.Value);
        }
    }

    /// <summary>
    /// Presents a Tcl-created menu as the root menubar: a bar window packed
    /// across the top of the root (ahead of the existing content) whose
    /// widget SHARES the menu's entry list, so later entryconfigure calls
    /// on the menu path stay live in the bar.
    /// </summary>
    private static void ShowRootMenubar(BridgeContext ctx, MenuWidget source)
    {
        if (ctx.MenubarWindow != null && !ctx.MenubarWindow.IsDestroyed)
        {
            ctx.MenubarWindow.Destroy();
        }

        TkWindow barWindow = ctx.Tree.Root.CreateChild(
            "__menubar#" + ctx.Tree.Root.Children.Count);
        var bar = new MenuWidget(barWindow, source);
        bar.Configure(new Dictionary<string, string> { { "-type", "menubar" } });
        bar.Measure();

        var packOptions = new Layout.PackOptions
        {
            Side = Layout.Side.Top,
            Fill = Layout.Fill.X,
        };

        IReadOnlyList<TkWindow> existing = Layout.PackLayout.Content(ctx.Tree.Root);
        if (existing.Count > 0)
        {
            packOptions.Before = existing[0];
        }

        Layout.PackLayout.Configure(barWindow, packOptions);
        ctx.RegisterWindow(barWindow.PathName, barWindow);
        ctx.MenubarWindow = barWindow;
        ctx.Tree.Menus.SetMenubar(bar);
        ctx.Tree.Scheduler.ScheduleRelayout();
    }

    private static string ExecuteOnUi(BridgeContext ctx, string path, string[] words)
    {
        string sub = words[1];

        if (path == TkPaths.Root)
        {
            if (sub == "cget")
            {
                return words.Length >= 3 ? ctx.RootOptions.Get(words[2], "") : "";
            }
            return string.Empty;
        }

        TkWindow window = ctx.ResolveWindow(path);

        MenuWidget menu;
        if (ctx.MenusByPath.TryGetValue(path, out menu))
        {
            return MenuDispatch.Execute(ctx, path, menu, words);
        }

        IWidget widget = window.Widget;

        var canvas = widget as CanvasWidget;
        if (canvas != null)
        {
            // Item-level bind evaluates Tcl, so it cannot live in the
            // widget's Execute string layer — intercept it here.
            if (sub == "bind" && words.Length >= 4)
            {
                string tagOrId = words[2];
                string sequence = words[3];
                if (words.Length == 4)
                {
                    return "";
                }
                string script = words[4];
                if (script.Length == 0)
                {
                    canvas.BindItem(tagOrId, sequence, null);
                    return "";
                }
                canvas.BindItem(tagOrId, sequence, tkEvent =>
                {
                    ctx.EvalCallbackScript(BindCommands.SubstitutePercent(ctx, script, tkEvent));
                    return Events.DispatchResult.Continue;
                });
                return "";
            }

            return canvas.Execute(Rest(words));
        }

        if (sub == "cget")
        {
            if (words.Length < 3) { throw BridgeRegistrar.WrongArgs(path + " cget option"); }
            return widget != null ? widget.Options.Get(words[2], "") : "";
        }

        var text = widget as TextWidget;
        if (text != null) { return TextDispatch(ctx, text, words); }

        var entry = widget as EntryWidget;
        if (entry != null) { return EntryDispatch(ctx, entry, words); }

        var listbox = widget as ListboxWidget;
        if (listbox != null) { return ListboxDispatch(listbox, words); }

        var treeview = widget as TreeviewWidget;
        if (treeview != null) { return TreeviewDispatch(ctx, treeview, words); }

        var combobox = widget as ComboboxWidget;
        if (combobox != null) { return ComboboxDispatch(combobox, words); }

        var scrollbar = widget as ScrollbarWidget;
        if (scrollbar != null) { return ScrollbarDispatch(scrollbar, words); }

        var paned = widget as PanedWindowWidget;
        if (paned != null) { return PanedDispatch(ctx, paned, words); }

        var button = widget as ButtonWidget;
        if (button != null && sub == "invoke") { button.Invoke(); return ""; }
        if (button != null && sub == "flash") { return ""; }

        var check = widget as CheckbuttonWidget;
        if (check != null) { return ToggleDispatch(check.Invoke, check.Select, check.Deselect, sub); }

        var radio = widget as RadiobuttonWidget;
        if (radio != null && sub == "invoke") { radio.Invoke(); return ""; }
        if (radio != null && sub == "select") { radio.Select(); return ""; }

        // ttk state/instate and anything else deferred: accept-and-no-op.
        return string.Empty;
    }

    private static string ToggleDispatch(Action invoke, Action select, Action deselect, string sub)
    {
        switch (sub)
        {
            case "invoke": invoke(); return "";
            case "select": select(); return "";
            case "deselect": deselect(); return "";
            case "toggle": invoke(); return "";
            default: return "";
        }
    }

    private static string[] Rest(string[] words)
    {
        var rest = new string[words.Length - 1];
        Array.Copy(words, 1, rest, 0, rest.Length);
        return rest;
    }

    // ---------------------------------------------------------------- text

    private static string TextDispatch(BridgeContext ctx, TextWidget text, string[] words)
    {
        // Tk's "--" end-of-options guard (e.g. "$t get -- 1.0 end").
        if (words.Length > 2 && words[2] == "--")
        {
            var trimmed = new List<string>(words);
            trimmed.RemoveAt(2);
            words = trimmed.ToArray();
        }

        string sub = words[1];
        switch (sub)
        {
            case "insert":
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs("text insert index chars"); }
                text.Insert(words[2], words[3],
                    words.Length >= 5 ? TclString.SplitList(words[4]) : null);
                return "";

            case "delete":
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("text delete index1 ?index2?"); }
                text.Delete(words[2], words.Length >= 4 ? words[3] : null);
                return "";

            case "get":
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("text get index1 ?index2?"); }
                return text.Get(words[2], words.Length >= 4 ? words[3] : null);

            case "index":
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("text index index"); }
                return text.Index(words[2]);

            case "see":
                if (words.Length >= 3) { text.See(words[2]); }
                return "";

            case "mark":
                return TextMarkDispatch(text, words);

            case "tag":
                return TextTagDispatch(text, words);

            case "compare":
                if (words.Length < 5) { throw BridgeRegistrar.WrongArgs("text compare index1 op index2"); }
                return TextCompare(text, words[2], words[3], words[4]);

            case "yview":
                return TextYView(text, words);

            case "xview":
                return "0 1";

            case "focus":
                text.Window.Tree.SetFocus(text.Window);
                return "";

            default:
                return string.Empty;
        }
    }

    private static string TextMarkDispatch(TextWidget text, string[] words)
    {
        if (words.Length < 3) { return ""; }
        switch (words[2])
        {
            case "set":
                if (words.Length >= 5) { text.MarkSet(words[3], words[4]); }
                return "";
            case "unset":
                for (int i = 3; i < words.Length; i++) { text.MarkUnset(words[i]); }
                return "";
            case "gravity":
                if (words.Length >= 5) { return text.MarkGravity(words[3], words[4]); }
                return words.Length >= 4 ? text.MarkGravity(words[3]) : "";
            case "names":
                return TclString.JoinList(text.MarkNames().ToList());
            default:
                return "";
        }
    }

    private static string TextTagDispatch(TextWidget text, string[] words)
    {
        if (words.Length < 3) { return ""; }
        switch (words[2])
        {
            case "add":
                if (words.Length >= 5) { text.TagAdd(words[3], words[4], words.Length >= 6 ? words[5] : null); }
                return "";
            case "remove":
                if (words.Length >= 5) { text.TagRemove(words[3], words[4], words.Length >= 6 ? words[5] : null); }
                return "";
            case "delete":
                for (int i = 3; i < words.Length; i++) { text.TagDelete(words[i]); }
                return "";
            case "configure":
                if (words.Length >= 4)
                {
                    text.TagConfigure(words[3], BridgeRegistrar.ParseOptionPairs(words, 4));
                }
                return "";
            case "ranges":
                return words.Length >= 4 ? TclString.JoinList(text.TagRanges(words[3]).ToList()) : "";
            case "names":
                return TclString.JoinList(text.TagNames().ToList());
            default:
                return "";
        }
    }

    private static string TextCompare(TextWidget text, string left, string op, string right)
    {
        TextPosition a = text.ParseIndex(left);
        TextPosition b = text.ParseIndex(right);
        int cmp = a.CompareTo(b);
        bool result;
        switch (op)
        {
            case "<": result = cmp < 0; break;
            case "<=": result = cmp <= 0; break;
            case "==": result = cmp == 0; break;
            case ">=": result = cmp >= 0; break;
            case ">": result = cmp > 0; break;
            case "!=": result = cmp != 0; break;
            default: throw new TkTclError("bad comparison operator \"" + op + "\"");
        }
        return result ? "1" : "0";
    }

    private static string TextYView(TextWidget text, string[] words)
    {
        if (words.Length == 2)
        {
            double first;
            double last;
            text.YViewFractions(out first, out last);
            return TclString.FormatDouble(first) + " " + TclString.FormatDouble(last);
        }

        if (words[2] == "moveto" && words.Length >= 4)
        {
            text.YViewMoveTo(ParseDouble(words[3]));
            return "";
        }

        if (words[2] == "scroll" && words.Length >= 5)
        {
            text.YViewScroll(ParseInt(words[3]), words[4].StartsWith("page", StringComparison.Ordinal));
            return "";
        }

        // "yview index" — scroll the line into view at the top.
        text.See(words[2]);
        return "";
    }

    // --------------------------------------------------------------- entry

    private static string EntryDispatch(BridgeContext ctx, EntryWidget entry, string[] words)
    {
        string sub = words[1];
        switch (sub)
        {
            case "get":
                return entry.Text;

            case "insert":
                if (words.Length >= 4) { entry.Insert(EntryIndex(entry, words[2]), words[3]); }
                return "";

            case "delete":
                if (words.Length >= 3)
                {
                    int first = EntryIndex(entry, words[2]);
                    int last = words.Length >= 4 ? EntryIndex(entry, words[3]) : first + 1;
                    entry.Delete(first, last);
                }
                return "";

            case "icursor":
                if (words.Length >= 3) { entry.SetCursor(EntryIndex(entry, words[2])); }
                return "";

            case "index":
                return words.Length >= 3
                    ? EntryIndex(entry, words[2]).ToString(CultureInfo.InvariantCulture)
                    : "0";

            case "selection":
                if (words.Length >= 3)
                {
                    switch (words[2])
                    {
                        case "clear": entry.ClearSelection(); return "";
                        case "present": return entry.SelectedText.Length > 0 ? "1" : "0";
                        case "range":
                            if (words.Length >= 5)
                            {
                                entry.SelectRange(EntryIndex(entry, words[3]), EntryIndex(entry, words[4]));
                            }
                            return "";
                        default: return "";
                    }
                }
                return "";

            case "xview":
                return "";

            default:
                return string.Empty;
        }
    }

    private static int EntryIndex(EntryWidget entry, string index)
    {
        switch (index)
        {
            case "end": return entry.Text.Length;
            case "insert": return entry.Cursor;
            case "anchor": return entry.Cursor;
        }

        if (index.StartsWith("sel.", StringComparison.Ordinal))
        {
            return entry.Cursor;
        }

        if (index.StartsWith("@", StringComparison.Ordinal))
        {
            int x;
            return int.TryParse(index.Substring(1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out x) ? entry.IndexAt(x) : 0;
        }

        int value;
        if (!int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            throw new TkTclError("bad entry index \"" + index + "\"");
        }
        return Math.Max(0, Math.Min(value, entry.Text.Length));
    }

    // ------------------------------------------------------------- listbox

    private static string ListboxDispatch(ListboxWidget listbox, string[] words)
    {
        string sub = words[1];
        switch (sub)
        {
            case "insert":
                if (words.Length >= 3)
                {
                    int at = ListboxIndex(listbox, words[2]);
                    var items = new string[words.Length - 3];
                    Array.Copy(words, 3, items, 0, items.Length);
                    listbox.Insert(at, items);
                }
                return "";

            case "delete":
                if (words.Length >= 3)
                {
                    int first = ListboxIndex(listbox, words[2]);
                    int last = words.Length >= 4 ? ListboxIndex(listbox, words[3]) : first;
                    listbox.Delete(first, last);
                }
                return "";

            case "get":
                if (words.Length >= 4)
                {
                    int first = ListboxIndex(listbox, words[2]);
                    int last = ListboxIndex(listbox, words[3]);
                    var result = new List<string>();
                    for (int i = Math.Max(0, first); i <= last && i < listbox.Size; i++)
                    {
                        result.Add(listbox.Get(i));
                    }
                    return TclString.JoinList(result);
                }
                return words.Length >= 3 ? listbox.Get(ListboxIndex(listbox, words[2])) : "";

            case "size":
                return listbox.Size.ToString(CultureInfo.InvariantCulture);

            case "curselection":
                return string.Join(" ", listbox.CurSelection()
                    .Select(i => i.ToString(CultureInfo.InvariantCulture)));

            case "selection":
                if (words.Length >= 4)
                {
                    switch (words[2])
                    {
                        case "set":
                            listbox.SelectionSet(ListboxIndex(listbox, words[3]));
                            return "";
                        case "clear":
                        {
                            int first = ListboxIndex(listbox, words[3]);
                            int last = words.Length >= 5 ? ListboxIndex(listbox, words[4]) : first;
                            listbox.SelectionClear(first, last);
                            return "";
                        }
                        case "includes":
                            return listbox.SelectionIncludes(ListboxIndex(listbox, words[3])) ? "1" : "0";
                        default:
                            return "";
                    }
                }
                return "";

            case "see":
                if (words.Length >= 3) { listbox.See(ListboxIndex(listbox, words[2])); }
                return "";

            case "nearest":
                return words.Length >= 3
                    ? listbox.Nearest(ParseInt(words[2])).ToString(CultureInfo.InvariantCulture)
                    : "-1";

            case "activate":
            case "index":
                return "";

            case "yview":
                if (words.Length == 2) { return "0 1"; }
                if (words[2] == "moveto" && words.Length >= 4)
                {
                    listbox.YViewMoveTo(ParseDouble(words[3]));
                }
                else if (words[2] == "scroll" && words.Length >= 5)
                {
                    listbox.YViewScroll(ParseInt(words[3]),
                        words[4].StartsWith("page", StringComparison.Ordinal));
                }
                return "";

            default:
                return string.Empty;
        }
    }

    private static int ListboxIndex(ListboxWidget listbox, string index)
    {
        switch (index)
        {
            case "end": return Math.Max(0, listbox.Size - 1);
            case "active": return listbox.Active;
            case "anchor": return listbox.Active;
        }

        int value;
        if (!int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            throw new TkTclError("bad listbox index \"" + index + "\"");
        }
        return value;
    }

    // ------------------------------------------------------------ treeview

    private static string TreeviewDispatch(BridgeContext ctx, TreeviewWidget tree, string[] words)
    {
        string sub = words[1];
        switch (sub)
        {
            case "insert":
            {
                // insert parent index ?-id id? ?-text text? ?-values {..}? ?-open bool? ?-image img?
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs("treeview insert parent index ?options?"); }
                string parent = words[2];
                int at = words[3] == "end" ? int.MaxValue : ParseInt(words[3]);
                Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 4);

                string id;
                options.TryGetValue("-id", out id);
                string textValue;
                options.TryGetValue("-text", out textValue);
                string valuesList;
                string[] values = null;
                if (options.TryGetValue("-values", out valuesList))
                {
                    values = TclString.SplitList(valuesList).ToArray();
                }

                string created = tree.Insert(parent, at, textValue ?? "", values, id);

                string openText;
                if (options.TryGetValue("-open", out openText))
                {
                    tree.SetOpen(created, IsTclTrue(openText));
                }
                string image;
                if (options.TryGetValue("-image", out image))
                {
                    TreeItem item = tree.Item(created);
                    if (item != null) { item.Image = image; }
                }
                return created;
            }

            case "delete":
            {
                var ids = new List<string>();
                for (int i = 2; i < words.Length; i++)
                {
                    ids.AddRange(TclString.SplitList(words[i]));
                }
                tree.Delete(ids.ToArray());
                return "";
            }

            case "children":
                if (words.Length >= 4)
                {
                    // set children: not used by DRAKON for reordering — accept-and-no-op.
                    return "";
                }
                return words.Length >= 3
                    ? TclString.JoinList(tree.ChildrenOf(words[2]).ToList())
                    : TclString.JoinList(tree.ChildrenOf("").ToList());

            case "parent":
            {
                if (words.Length < 3) { return ""; }
                TreeItem item = tree.Item(words[2]);
                return item != null ? item.Parent : "";
            }

            case "item":
                return TreeviewItemDispatch(tree, words);

            case "selection":
                if (words.Length == 2)
                {
                    return TclString.JoinList(tree.Selection.ToList());
                }
                if (words.Length >= 4 && words[2] == "set")
                {
                    tree.SelectionSet(TclString.SplitList(words[3]).ToArray());
                    return "";
                }
                if (words.Length >= 3 && (words[2] == "set" || words[2] == "clear"))
                {
                    tree.SelectionSet(new string[0]);
                    return "";
                }
                return "";

            case "focus":
                if (words.Length >= 3)
                {
                    tree.SelectionSet(words[2]);
                    return "";
                }
                return tree.Selection.Count > 0 ? tree.Selection[0] : "";

            case "exists":
                return tree.Item(words.Length >= 3 ? words[2] : "") != null ? "1" : "0";

            case "see":
                return "";

            case "identify":
                // identify row X Y / identify item X Y — the item id at the
                // given widget coordinates ("" for empty space; DRAKON
                // deselects on that).
                if (words.Length >= 5 && (words[2] == "row" || words[2] == "item"))
                {
                    return tree.ItemAt(ParseInt(words[4])) ?? "";
                }
                return "";

            case "move":
            case "heading":
            case "column":
            case "tag":
            case "xview":
                return "";

            case "yview":
                if (words.Length >= 4 && words[2] == "moveto")
                {
                    tree.YViewMoveTo(ParseDouble(words[3]));
                }
                else if (words.Length >= 5 && words[2] == "scroll")
                {
                    tree.YViewScroll(ParseInt(words[3]),
                        words[4].StartsWith("page", StringComparison.Ordinal));
                }
                return "";

            default:
                return string.Empty;
        }
    }

    private static string TreeviewItemDispatch(TreeviewWidget tree, string[] words)
    {
        // item id ?-option? ?value -option value...?
        if (words.Length < 3) { return ""; }
        TreeItem item = tree.Item(words[2]);
        if (item == null)
        {
            throw new TkTclError("Item " + words[2] + " not found");
        }

        if (words.Length == 3)
        {
            var parts = new List<string>
            {
                "-text", item.Text,
                "-values", TclString.JoinList(item.Values),
                "-open", item.Open ? "1" : "0",
                "-image", item.Image,
            };
            return TclString.JoinList(parts);
        }

        if (words.Length == 4)
        {
            switch (words[3])
            {
                case "-text": return item.Text;
                case "-values": return TclString.JoinList(item.Values);
                case "-open": return item.Open ? "1" : "0";
                case "-image": return item.Image;
                default: return "";
            }
        }

        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 3);
        string text;
        if (options.TryGetValue("-text", out text)) { item.Text = text; }
        string values;
        if (options.TryGetValue("-values", out values))
        {
            item.Values.Clear();
            item.Values.AddRange(TclString.SplitList(values));
        }
        string open;
        if (options.TryGetValue("-open", out open)) { tree.SetOpen(item.Id, IsTclTrue(open)); }
        string image;
        if (options.TryGetValue("-image", out image)) { item.Image = image; }
        tree.Measure();
        return "";
    }

    // ------------------------------------------------- combobox and others

    private static string ComboboxDispatch(ComboboxWidget combobox, string[] words)
    {
        switch (words[1])
        {
            case "get":
                return combobox.Value;
            case "set":
                if (words.Length >= 3) { combobox.SetValue(words[2]); }
                return "";
            case "current":
                if (words.Length >= 3)
                {
                    int index = ParseInt(words[2]);
                    if (index >= 0 && index < combobox.Values.Count)
                    {
                        combobox.SetValue(combobox.Values[index]);
                    }
                    return "";
                }
                for (int i = 0; i < combobox.Values.Count; i++)
                {
                    if (combobox.Values[i] == combobox.Value)
                    {
                        return i.ToString(CultureInfo.InvariantCulture);
                    }
                }
                return "-1";
            default:
                return string.Empty;
        }
    }

    private static string ScrollbarDispatch(ScrollbarWidget scrollbar, string[] words)
    {
        switch (words[1])
        {
            case "set":
                if (words.Length >= 4)
                {
                    scrollbar.Set(ParseDouble(words[2]), ParseDouble(words[3]));
                }
                return "";
            case "get":
                return TclString.FormatDouble(scrollbar.First) + " " +
                    TclString.FormatDouble(scrollbar.Last);
            default:
                return string.Empty;
        }
    }

    private static string PanedDispatch(BridgeContext ctx, PanedWindowWidget paned, string[] words)
    {
        switch (words[1])
        {
            case "add":
                if (words.Length >= 3)
                {
                    TkWindow pane = ctx.ResolveWindow(words[2]);
                    paned.Add(pane);
                }
                return "";
            case "forget":
                if (words.Length >= 3)
                {
                    TkWindow pane = ctx.ResolveWindow(words[2]);
                    paned.Forget(pane);
                }
                return "";
            case "panes":
                return TclString.JoinList(paned.Panes.Select(p => ctx.PathOf(p)).ToList());
            default:
                return string.Empty;
        }
    }

    // ------------------------------------------------------------- helpers

    private static bool IsTclTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            default: return false;
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

    private static double ParseDouble(string text)
    {
        double value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            throw new TkTclError("expected floating-point number but got \"" + text + "\"");
        }
        return value;
    }
}
