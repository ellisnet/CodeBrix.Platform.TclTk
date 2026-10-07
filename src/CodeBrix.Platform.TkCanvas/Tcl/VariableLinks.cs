using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The <c>-textvariable</c>/<c>-variable</c>/<c>-listvariable</c> machinery:
/// links interpreter variables to widget state through the interpreter's
/// variable traces (write traces push Tcl-side changes into the widget; read
/// traces pull widget state back before the script reads it — so entry text
/// is always current without a per-keystroke hook).
/// </summary>
internal sealed class VariableLinks
{
    private const string LinkCommand = "tk__varlink";

    private sealed class TextLink
    {
        internal string VariableName;
        internal Func<string> GetWidgetValue;   // UI thread
        internal Action<string> SetWidgetValue; // UI thread
        internal bool ReadSync;                 // pull widget→var on read traces
    }

    private readonly Dictionary<string, Dictionary<string, TextLink>> LinksByPath
        = new Dictionary<string, Dictionary<string, TextLink>>(StringComparer.Ordinal);

    private readonly Dictionary<string, ToggleVariable> TogglesByVariable
        = new Dictionary<string, ToggleVariable>(StringComparer.Ordinal);

    private const string ListLinkCommand = "tk__listvarlink";

    private sealed class ListLink
    {
        internal string Path;
        internal string VariableName;           // fully qualified
        internal string TraceCommand;           // the exact trace callback, for removal
    }

    private readonly Dictionary<string, ListLink> ListLinksByPath
        = new Dictionary<string, ListLink>(StringComparer.Ordinal);

    private bool _registered;

    /// <summary>Registers the internal trace-callback commands (idempotent).</summary>
    internal void EnsureRegistered(BridgeContext ctx)
    {
        if (_registered) { return; }
        _registered = true;

        Result error = null;
        ctx.Interpreter.EvaluateScript("namespace eval ::tk {}", ref error);

        BridgeRegistrar.Add(ctx, LinkCommand, words => HandleTrace(ctx, words));
        BridgeRegistrar.Add(ctx, ListLinkCommand, words => HandleListTrace(ctx, words));
    }

    // ------------------------------------------------------ -listvariable

    /// <summary>
    /// Checks a <c>-listvariable</c> value before a listbox is created or
    /// configured with it: when the named variable exists, its value must be
    /// a valid list. Throws Tk's error (<c>unmatched open brace in list:
    /// invalid -listvariable value</c>) otherwise, so the caller can refuse
    /// the whole create/configure before anything changes. Runs on the Tcl
    /// thread.
    /// </summary>
    internal static void ValidateListVariable(BridgeContext ctx, string variableName)
    {
        if (string.IsNullOrEmpty(variableName)) { return; }

        string qualified = Qualify(variableName);
        Result value = null;
        Result error = null;
        if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) != ReturnCode.Ok)
        {
            return; // a missing variable is created from the listbox items
        }

        string parseError;
        if (!TrySplitList(ctx, value != null ? value.ToString() : string.Empty, out _, out parseError))
        {
            throw new TkTclError(parseError + ": invalid -listvariable value");
        }
    }

    /// <summary>
    /// Links a listbox's items to a Tcl variable (<c>-listvariable</c>),
    /// replacing any previous link of that listbox. Tk semantics: an existing
    /// variable's list becomes the items; a missing variable is created from
    /// the current items; afterwards writes to the variable (from any scope)
    /// replace the items, an unset recreates the variable from the items,
    /// and <c>insert</c>/<c>delete</c> write back (see <see cref="PushList"/>).
    /// The name is always global, as in Tk. Runs on the Tcl thread; call
    /// <see cref="ValidateListVariable"/> first.
    /// </summary>
    internal void LinkList(BridgeContext ctx, string path, string variableName)
    {
        EnsureRegistered(ctx);
        UnlinkList(ctx, path);

        string qualified = Qualify(variableName);
        var link = new ListLink
        {
            Path = path,
            VariableName = qualified,
            TraceCommand = TclString.JoinList(new[] { ListLinkCommand, path, qualified }),
        };

        Result value = null;
        Result error = null;
        if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) == ReturnCode.Ok)
        {
            List<string> items;
            string parseError;
            if (TrySplitList(ctx, value != null ? value.ToString() : string.Empty, out items, out parseError))
            {
                ctx.Ui(() => ListboxAt(ctx, path).SetItems(items));
            }
        }
        else
        {
            SetVariableGuarded(ctx, qualified, ctx.Ui(() => JoinItems(ListboxAt(ctx, path))));
        }

        ListLinksByPath[path] = link;
        AddListTrace(ctx, link);
    }

    /// <summary>
    /// Detaches a listbox from its <c>-listvariable</c> (configure to another
    /// name or to "", or destroy): removes the trace and forgets the link.
    /// The listbox keeps its items; the variable keeps its value.
    /// </summary>
    internal void UnlinkList(BridgeContext ctx, string path)
    {
        ListLink link;
        if (!ListLinksByPath.TryGetValue(path, out link)) { return; }
        ListLinksByPath.Remove(path);
        RemoveListTrace(ctx, link);
    }

    /// <summary>
    /// Drops the <c>-listvariable</c> links of every listbox whose window is
    /// gone (after <c>destroy</c>, which may take whole subtrees with it).
    /// Runs on the Tcl thread.
    /// </summary>
    internal void UnlinkDestroyedLists(BridgeContext ctx)
    {
        if (ListLinksByPath.Count == 0) { return; }

        var paths = new List<string>(ListLinksByPath.Keys);
        var dead = new List<string>();
        ctx.Ui(() =>
        {
            foreach (string path in paths)
            {
                if (!IsLive(ctx, path)) { dead.Add(path); }
            }
        });

        foreach (string path in dead) { UnlinkList(ctx, path); }
    }

    /// <summary>Whether a listbox path currently has a <c>-listvariable</c> link.</summary>
    internal bool HasListLink(string path)
    {
        return ListLinksByPath.ContainsKey(path);
    }

    /// <summary>
    /// Writes a linked listbox's items back to its variable after
    /// <c>insert</c>/<c>delete</c>, as Tk does. Other write traces on the
    /// variable fire normally. Runs on the Tcl thread.
    /// </summary>
    internal void PushList(BridgeContext ctx, string path)
    {
        ListLink link;
        if (!ListLinksByPath.TryGetValue(path, out link)) { return; }
        string text = ctx.Ui(() => JoinItems(ListboxAt(ctx, path)));
        SetVariableGuarded(ctx, link.VariableName, text);
    }

    private string HandleListTrace(BridgeContext ctx, string[] words)
    {
        // Invoked as: tk__listvarlink PATH QUALIFIED name1 name2 op
        if (words.Length < 6) { return string.Empty; }
        string path = words[1];
        string qualified = words[2];
        string op = words[5];

        if (ctx.SuppressedVariableLinks.Contains(qualified)) { return string.Empty; }

        ListLink link;
        if (!ListLinksByPath.TryGetValue(path, out link) ||
            !string.Equals(link.VariableName, qualified, StringComparison.Ordinal))
        {
            return string.Empty; // a stale trace of a replaced link
        }

        if (!ctx.Ui(() => IsLive(ctx, path) ? "1" : "0").Equals("1", StringComparison.Ordinal))
        {
            // The listbox went away without "destroy" (e.g. a toplevel closed
            // by its window manager): drop the link instead of failing.
            UnlinkList(ctx, path);
            return string.Empty;
        }

        if (op == "unset")
        {
            // Tk: a -listvariable cannot be unset — it is recreated at once
            // from the listbox items and the link is re-established (an unset
            // removes every trace on the variable).
            SetVariableGuarded(ctx, qualified, ctx.Ui(() => JoinItems(ListboxAt(ctx, path))));
            RemoveListTrace(ctx, link);
            AddListTrace(ctx, link);
            return string.Empty;
        }

        if (op != "write") { return string.Empty; }

        Result value = null;
        Result error = null;
        if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) != ReturnCode.Ok)
        {
            return string.Empty;
        }

        List<string> items;
        string parseError;
        if (!TrySplitList(ctx, value != null ? value.ToString() : string.Empty, out items, out parseError))
        {
            // Tk: the variable must always hold a valid list — put the
            // previous list back and fail the write.
            SetVariableGuarded(ctx, qualified, ctx.Ui(() => JoinItems(ListboxAt(ctx, path))));
            throw new TkTclError("invalid listvar value");
        }

        ctx.Ui(() => ListboxAt(ctx, path).SetItems(items));
        return string.Empty;
    }

    private static void AddListTrace(BridgeContext ctx, ListLink link)
    {
        Result error = null;
        ctx.Interpreter.EvaluateScript(
            TclString.JoinList(new[] { "trace", "add", "variable", link.VariableName, "write unset", link.TraceCommand }),
            ref error);
    }

    private static void RemoveListTrace(BridgeContext ctx, ListLink link)
    {
        Result error = null;
        ctx.Interpreter.EvaluateScript(
            TclString.JoinList(new[] { "trace", "remove", "variable", link.VariableName, "write unset", link.TraceCommand }),
            ref error);
    }

    private static bool IsLive(BridgeContext ctx, string path)
    {
        TkWindow window;
        return ctx.WindowsByPath.TryGetValue(path, out window) && !window.IsDestroyed
            && window.Widget is ListboxWidget;
    }

    private static ListboxWidget ListboxAt(BridgeContext ctx, string path)
    {
        return (ListboxWidget)ctx.ResolveWindow(path).Widget;
    }

    private static string JoinItems(ListboxWidget listbox)
    {
        return TclString.JoinList(listbox.Items);
    }

    private static bool TrySplitList(BridgeContext ctx, string text, out List<string> items, out string error)
    {
        StringList list = null;
        Result parseError = null;
        if (Parser.SplitList(ctx.Interpreter, text, 0, Length.Invalid, true, ref list, ref parseError) != ReturnCode.Ok)
        {
            items = null;
            error = parseError != null ? parseError.ToString() : "invalid list";
            return false;
        }

        items = list != null ? new List<string>(list) : new List<string>();
        error = null;
        return true;
    }

    /// <summary>
    /// Links a text-valued widget option (<c>-textvariable</c>) to a Tcl
    /// variable. Runs on the Tcl thread.
    /// </summary>
    internal void LinkText(
        BridgeContext ctx, string path, string variableName,
        Func<string> getWidgetValue, Action<string> setWidgetValue, bool readSync)
    {
        EnsureRegistered(ctx);
        string qualified = Qualify(variableName);

        Dictionary<string, TextLink> forPath;
        if (!LinksByPath.TryGetValue(path, out forPath))
        {
            forPath = new Dictionary<string, TextLink>(StringComparer.Ordinal);
            LinksByPath[path] = forPath;
        }
        forPath[qualified] = new TextLink
        {
            VariableName = qualified,
            GetWidgetValue = getWidgetValue,
            SetWidgetValue = setWidgetValue,
            ReadSync = readSync
        };

        // Tk semantics: an existing variable's value shows in the widget;
        // a missing variable is created from the widget's current content.
        Result value = null;
        Result error = null;
        if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) == ReturnCode.Ok)
        {
            string text = value != null ? value.ToString() : string.Empty;
            ctx.Ui(() => setWidgetValue(text));
        }
        else
        {
            string text = ctx.Ui(() => getWidgetValue() ?? string.Empty);
            SetVariableGuarded(ctx, qualified, text);
        }

        AddTraces(ctx, path, qualified, readSync);
    }

    /// <summary>
    /// Links a toggle widget (<c>-variable</c> on check/radio buttons) to a
    /// shared <see cref="ToggleVariable"/>, creating it on first use so a
    /// radio group over one Tcl variable shares one instance. Runs on the
    /// Tcl thread; returns the toggle for UI-side assignment.
    /// </summary>
    internal ToggleVariable LinkToggle(
        BridgeContext ctx, string path, string variableName, string initialWhenUnset)
    {
        EnsureRegistered(ctx);
        string qualified = Qualify(variableName);

        ToggleVariable toggle;
        bool created = false;
        if (!TogglesByVariable.TryGetValue(qualified, out toggle))
        {
            toggle = new ToggleVariable();
            TogglesByVariable[qualified] = toggle;
            created = true;
        }

        Result value = null;
        Result error = null;
        if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) == ReturnCode.Ok)
        {
            string text = value != null ? value.ToString() : string.Empty;
            ctx.Ui(() => toggle.Set(text));
        }
        else if (initialWhenUnset != null)
        {
            ctx.Ui(() => toggle.Set(initialWhenUnset));
            SetVariableGuarded(ctx, qualified, initialWhenUnset);
        }

        if (created)
        {
            // Toggle→variable: the widget side changes on the UI thread.
            toggle.Changed += () =>
            {
                string newValue = toggle.Value;
                ctx.Apartment.PostToTcl(() => SetVariableGuarded(ctx, qualified, newValue));
            };
            AddTraces(ctx, path, qualified, readSync: false);
        }

        return toggle;
    }

    private void AddTraces(BridgeContext ctx, string path, string qualified, bool readSync)
    {
        string ops = readSync ? "{read write}" : "write";
        string script = "trace add variable {" + qualified + "} " + ops +
            " [list " + LinkCommand + " {" + path + "}]";
        Result error = null;
        ctx.Interpreter.EvaluateScript(script, ref error);
    }

    private string HandleTrace(BridgeContext ctx, string[] words)
    {
        // Invoked as: ::tk::__varlink PATH name1 name2 op
        if (words.Length < 5) { return string.Empty; }
        string path = words[1];
        string name1 = words[2];
        string op = words[4];
        string qualified = Qualify(name1);

        if (ctx.SuppressedVariableLinks.Contains(qualified)) { return string.Empty; }

        // Toggle links (check/radio groups).
        ToggleVariable toggle;
        if (TogglesByVariable.TryGetValue(qualified, out toggle) && op == "write")
        {
            Result value = null;
            Result error = null;
            if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) == ReturnCode.Ok)
            {
                string text = value != null ? value.ToString() : string.Empty;
                ctx.Ui(() => toggle.Set(text));
            }
        }

        // Text links.
        Dictionary<string, TextLink> forPath;
        TextLink link;
        if (LinksByPath.TryGetValue(path, out forPath) && forPath.TryGetValue(qualified, out link))
        {
            if (op == "write")
            {
                Result value = null;
                Result error = null;
                if (ctx.Interpreter.GetVariableValue(qualified, ref value, ref error) == ReturnCode.Ok)
                {
                    string text = value != null ? value.ToString() : string.Empty;
                    ctx.Ui(() => link.SetWidgetValue(text));
                }
            }
            else if (op == "read" && link.ReadSync)
            {
                string text = ctx.Ui(() => link.GetWidgetValue() ?? string.Empty);
                SetVariableGuarded(ctx, qualified, text);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Pushes a widget-side value into its linked Tcl variable immediately,
    /// marshaling to the Tcl thread. Tk writes a <c>ttk::combobox</c>'s
    /// <c>-textvariable</c> the moment a value is chosen (the variable is
    /// authoritative), rather than waiting for a read to pull it — so a
    /// script that reads the variable right after a selection, with no
    /// intervening read to trigger a read-sync, still sees the chosen value.
    /// </summary>
    internal void PushValue(BridgeContext ctx, string variableName, string value)
    {
        string qualified = Qualify(variableName);
        ctx.Apartment.PostToTcl(() => SetVariableGuarded(ctx, qualified, value ?? string.Empty));
    }

    private static void SetVariableGuarded(BridgeContext ctx, string qualified, string value)
    {
        ctx.SuppressedVariableLinks.Add(qualified);
        try
        {
            Result error = null;
            ctx.Interpreter.SetVariableValue(qualified, value, ref error);
        }
        finally
        {
            ctx.SuppressedVariableLinks.Remove(qualified);
        }
    }

    private static string Qualify(string name)
    {
        // Trace callbacks and widget options may name the variable with or
        // without the :: prefix; normalize to fully qualified so one link
        // map key serves both.
        if (string.IsNullOrEmpty(name)) { return name; }
        return name.StartsWith("::", StringComparison.Ordinal) ? name : "::" + name;
    }
}
