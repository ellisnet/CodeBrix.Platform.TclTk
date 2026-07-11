using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using CodeBrix.Platform.TclTk._Components.Public;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The widget-creation half of the bridge: the classic and <c>ttk::</c>
/// widget commands (<c>frame .f -opt val ...</c> creates the widget,
/// registers the instance command <c>.f</c>, and returns the path), the
/// root window's own <c>.</c> command, and the option wiring shared with
/// <c>configure</c> (-command/-textvariable/-variable/scroll commands).
/// </summary>
internal static class WidgetCommands
{
    private static readonly string[] ClassicNames =
    {
        "frame", "labelframe", "label", "button", "entry", "text", "listbox",
        "checkbutton", "radiobutton", "scrollbar", "panedwindow", "canvas",
        "combobox", "treeview", "separator",
    };

    private static readonly string[] TtkNames =
    {
        "ttk::frame", "ttk::labelframe", "ttk::label", "ttk::button",
        "ttk::entry", "ttk::checkbutton", "ttk::radiobutton",
        "ttk::scrollbar", "ttk::panedwindow", "ttk::combobox",
        "ttk::treeview", "ttk::separator", "ttk::spinbox",
    };

    internal static void Register(BridgeContext ctx)
    {
        Result error = null;
        ctx.Interpreter.EvaluateScript("namespace eval ::ttk {}", ref error);

        foreach (string name in ClassicNames)
        {
            string kind = name;
            BridgeRegistrar.Add(ctx, name, words => CreateWidget(ctx, kind, words));
        }

        foreach (string name in TtkNames)
        {
            string kind = name.Substring("ttk::".Length);
            if (kind == "spinbox") { kind = "entry"; }
            BridgeRegistrar.Add(ctx, name, words => CreateWidget(ctx, kind, words));
        }

        BridgeRegistrar.Add(ctx, "menu", words => CreateMenu(ctx, words));
        BridgeRegistrar.Add(ctx, "toplevel", words => CreateToplevel(ctx, words));

        // The root window's own instance command ("." configure -menu ...).
        BridgeRegistrar.Add(ctx, ".", words => WidgetDispatch.Execute(ctx, ".", words));
    }

    private static string CreateWidget(BridgeContext ctx, string kind, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs(words[0] + " pathName ?-option value ...?");
        }

        string path = words[1];
        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 2);

        ctx.Ui(() =>
        {
            string parentPath;
            string name;
            TkPaths.Split(path, out parentPath, out name);

            if (ctx.WindowsByPath.ContainsKey(path))
            {
                throw new TkTclError("window name \"" + name +
                    "\" already exists in parent");
            }

            TkWindow parent = ctx.ResolveWindow(parentPath);
            TkWindow window = parent.CreateChild(name);
            IWidget widget = CreateWidgetInstance(ctx, kind, window);

            widget.Configure(options);
            ctx.RegisterWindow(path, window);
            WireWidgetEvents(ctx, path, widget);
        });

        ApplyLinkedOptions(ctx, path, options);
        RegisterInstanceCommand(ctx, path);
        return path;
    }

    private static IWidget CreateWidgetInstance(BridgeContext ctx, string kind, TkWindow window)
    {
        switch (kind)
        {
            case "frame": return new FrameWidget(window);
            case "labelframe": return new LabelframeWidget(window);
            case "label": return new LabelWidget(window);
            case "button": return new ButtonWidget(window);
            case "entry": return new EntryWidget(window);
            case "text": return new TextWidget(window);
            case "listbox": return new ListboxWidget(window);
            case "checkbutton": return new CheckbuttonWidget(window);
            case "radiobutton": return new RadiobuttonWidget(window);
            case "scrollbar": return new ScrollbarWidget(window);
            case "panedwindow": return new PanedWindowWidget(window);
            case "canvas": return new CanvasWidget(window);
            case "combobox": return new ComboboxWidget(window);
            case "treeview": return new TreeviewWidget(window);
            case "separator": return new SeparatorWidget(window);
            default:
                throw new TkTclError("invalid command name \"" + kind + "\"");
        }
    }

    private static string CreateMenu(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("menu pathName ?-option value ...?");
        }

        string path = words[1];
        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 2);

        ctx.Ui(() =>
        {
            string parentPath;
            string name;
            TkPaths.Split(path, out parentPath, out name);

            if (ctx.WindowsByPath.ContainsKey(path))
            {
                throw new TkTclError("window name \"" + name +
                    "\" already exists in parent");
            }

            MenuWidget menu = ctx.Tree.Menus.CreateMenu(name);
            menu.Configure(options);
            ctx.MenusByPath[path] = menu;
            ctx.RegisterWindow(path, menu.Window);
        });

        RegisterInstanceCommand(ctx, path);
        return path;
    }

    private static string CreateToplevel(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("toplevel pathName ?-option value ...?");
        }

        string path = words[1];
        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 2);

        ctx.Ui(() =>
        {
            string parentPath;
            string name;
            TkPaths.Split(path, out parentPath, out name);

            if (ctx.WindowsByPath.ContainsKey(path))
            {
                throw new TkTclError("window name \"" + name +
                    "\" already exists in parent");
            }

            TkWindow window = ctx.Tree.WindowManager.CreateToplevel(name);
            var frame = new FrameWidget(window);
            window.ClassName = "Toplevel";
            frame.Configure(options);
            ctx.RegisterWindow(path, window);
        });

        RegisterInstanceCommand(ctx, path);
        return path;
    }

    private static void RegisterInstanceCommand(BridgeContext ctx, string path)
    {
        long token = BridgeRegistrar.AddRemovable(
            ctx, path, words => WidgetDispatch.Execute(ctx, path, words));
        ctx.CommandTokensByPath[path] = token;
    }

    /// <summary>
    /// Hooks the widget's C# events to their Tcl callback options. Handlers
    /// read the option value at fire time, so later <c>configure</c> calls
    /// need no re-wiring. Runs on the UI thread.
    /// </summary>
    private static void WireWidgetEvents(BridgeContext ctx, string path, IWidget widget)
    {
        var button = widget as ButtonWidget;
        if (button != null)
        {
            button.Invoked += () =>
                ctx.EvalCallbackScript(button.Options.Get("-command", ""));
            return;
        }

        var check = widget as CheckbuttonWidget;
        if (check != null)
        {
            check.Invoked += () =>
                ctx.EvalCallbackScript(check.Options.Get("-command", ""));
            return;
        }

        var radio = widget as RadiobuttonWidget;
        if (radio != null)
        {
            radio.Invoked += () =>
                ctx.EvalCallbackScript(radio.Options.Get("-command", ""));
            return;
        }

        var scrollbar = widget as ScrollbarWidget;
        if (scrollbar != null)
        {
            scrollbar.Command += args =>
            {
                string script = scrollbar.Options.Get("-command", "");
                if (script.Length == 0) { return; }
                ctx.EvalCallbackScript(script + " " + string.Join(" ", args));
            };
            return;
        }

        var listbox = widget as ListboxWidget;
        if (listbox != null)
        {
            listbox.YScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, listbox.Options, "-yscrollcommand", first, last);
            return;
        }

        var treeview = widget as TreeviewWidget;
        if (treeview != null)
        {
            treeview.YScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, treeview.Options, "-yscrollcommand", first, last);
            return;
        }

        var canvas = widget as CanvasWidget;
        if (canvas != null)
        {
            canvas.XScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, canvas.Options, "-xscrollcommand", first, last);
            canvas.YScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, canvas.Options, "-yscrollcommand", first, last);
            return;
        }

        var textWidget = widget as TextWidget;
        if (textWidget != null)
        {
            textWidget.XScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, textWidget.Options, "-xscrollcommand", first, last);
            textWidget.YScrollChanged += (first, last) =>
                EvalScrollCommand(ctx, textWidget.Options, "-yscrollcommand", first, last);
        }
    }

    private static void EvalScrollCommand(
        BridgeContext ctx, WidgetOptions options, string optionName, double first, double last)
    {
        string script = options.Get(optionName, "");
        if (script.Length == 0) { return; }
        ctx.EvalCallbackScript(script + " " +
            TclString.FormatDouble(first) + " " + TclString.FormatDouble(last));
    }

    /// <summary>
    /// Applies the option links that live in the interpreter (variable
    /// traces): <c>-textvariable</c> and <c>-variable</c>. Runs on the Tcl
    /// thread; called at creation and by <c>configure</c>.
    /// </summary>
    internal static void ApplyLinkedOptions(
        BridgeContext ctx, string path, IReadOnlyDictionary<string, string> options)
    {
        string textVariable;
        if (options.TryGetValue("-textvariable", out textVariable) && textVariable.Length > 0)
        {
            LinkTextVariable(ctx, path, textVariable);
        }

        string variable;
        if (options.TryGetValue("-variable", out variable) && variable.Length > 0)
        {
            LinkToggleVariable(ctx, path, variable);
        }
    }

    private static void LinkTextVariable(BridgeContext ctx, string path, string variableName)
    {
        // Resolve the widget lazily on the UI thread per access: the link
        // outlives configure calls and must survive option changes.
        Func<IWidget> widgetOf = () =>
        {
            TkWindow window = ctx.ResolveWindow(path);
            return window.Widget;
        };

        // Which widgets have a text value worth two-way sync:
        IWidget widget = null;
        string ignored = ctx.Ui(() =>
        {
            widget = widgetOf();
            return string.Empty;
        });
        _ = ignored;

        if (widget is EntryWidget)
        {
            ctx.VarLinks.LinkText(ctx, path, variableName,
                () => ((EntryWidget)widgetOf()).Text,
                value => ((EntryWidget)widgetOf()).SetText(value),
                readSync: true);
        }
        else if (widget is ComboboxWidget)
        {
            // Tk's combobox variable is authoritative: setting the variable
            // updates the field (the always-added write trace), and choosing
            // a value writes the variable at once. So the link is write-only
            // (no read-sync pull — a readonly combobox never carries typed
            // text) plus a selection hook that pushes the chosen value.
            ComboboxWidget combo = (ComboboxWidget)widget;
            string linkedVariable = variableName;
            ctx.VarLinks.LinkText(ctx, path, variableName,
                () => ((ComboboxWidget)widgetOf()).Value,
                value => ((ComboboxWidget)widgetOf()).SetValue(value),
                readSync: false);
            ctx.Ui(() => combo.Selected +=
                () => ctx.VarLinks.PushValue(ctx, linkedVariable, combo.Value));
        }
        else if (widget is LabelWidget || widget is ButtonWidget)
        {
            ctx.VarLinks.LinkText(ctx, path, variableName,
                () => widgetOf().Options.Get("-text", ""),
                value =>
                {
                    IWidget target = widgetOf();
                    target.Configure(new Dictionary<string, string> { { "-text", value } });
                    target.Measure();
                    ctx.Tree.Scheduler.ScheduleRelayout();
                },
                readSync: false);
        }
    }

    private static void LinkToggleVariable(BridgeContext ctx, string path, string variableName)
    {
        TkWindow window = null;
        IWidget widget = null;
        ctx.Ui(() =>
        {
            window = ctx.ResolveWindow(path);
            widget = window.Widget;
        });

        var check = widget as CheckbuttonWidget;
        if (check != null)
        {
            string offValue = null;
            ctx.Ui(() => { offValue = check.Options.Get("-offvalue", "0"); });
            ToggleVariable toggle = ctx.VarLinks.LinkToggle(ctx, path, variableName, offValue);
            ctx.Ui(() => { check.Variable = toggle; });
            return;
        }

        var radio = widget as RadiobuttonWidget;
        if (radio != null)
        {
            ToggleVariable toggle = ctx.VarLinks.LinkToggle(ctx, path, variableName, null);
            ctx.Ui(() => { radio.Variable = toggle; });
        }
    }
}
