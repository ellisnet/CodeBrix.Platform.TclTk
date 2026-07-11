using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Widgets;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The <c>-textvariable</c>/<c>-variable</c> machinery: links interpreter
/// variables to widget state through the interpreter's variable traces
/// (write traces push Tcl-side changes into the widget; read traces pull
/// widget state back before the script reads it — so entry text is always
/// current without a per-keystroke hook).
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

    private bool _registered;

    /// <summary>Registers the internal trace-callback command (idempotent).</summary>
    internal void EnsureRegistered(BridgeContext ctx)
    {
        if (_registered) { return; }
        _registered = true;

        Result error = null;
        ctx.Interpreter.EvaluateScript("namespace eval ::tk {}", ref error);

        BridgeRegistrar.Add(ctx, LinkCommand, words => HandleTrace(ctx, words));
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
