using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The bridge's shared state: the interpreter, the widget tree, the
/// threading apartment, and the Tcl-path registries. One instance per
/// <see cref="TkTclBridge"/>.
/// </summary>
internal sealed class BridgeContext
{
    internal BridgeContext(Interpreter interpreter, WindowTree tree, BridgeApartment apartment)
    {
        Interpreter = interpreter;
        Tree = tree;
        Apartment = apartment;
        WindowsByPath[TkPaths.Root] = tree.Root;
        PathsByWindow[tree.Root] = TkPaths.Root;
    }

    /// <summary>The interpreter the bridge is registered on.</summary>
    internal Interpreter Interpreter { get; }

    /// <summary>The widget tree the bridge drives.</summary>
    internal WindowTree Tree { get; }

    /// <summary>The threading apartment (direct or hosted).</summary>
    internal BridgeApartment Apartment { get; }

    /// <summary>Tcl window path → window. Includes the root at ".".</summary>
    internal Dictionary<string, TkWindow> WindowsByPath { get; }
        = new Dictionary<string, TkWindow>(StringComparer.Ordinal);

    /// <summary>Window → Tcl path (menus/toplevels may differ from PathName).</summary>
    internal Dictionary<TkWindow, string> PathsByWindow { get; }
        = new Dictionary<TkWindow, string>();

    /// <summary>Tcl path → menu widget (menus live on serial-named overlay windows).</summary>
    internal Dictionary<string, MenuWidget> MenusByPath { get; }
        = new Dictionary<string, MenuWidget>(StringComparer.Ordinal);

    /// <summary>Command tokens for widget instance commands, for removal at destroy.</summary>
    internal Dictionary<string, long> CommandTokensByPath { get; }
        = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>The host-side file dialog seam (null when headless).</summary>
    internal ITkFileDialogProvider FileDialogs { get; set; }

    /// <summary>The -textvariable/-variable link machinery.</summary>
    internal VariableLinks VarLinks { get; } = new VariableLinks();

    /// <summary>The root window's option bag ("." configure -cursor ... etc.).</summary>
    internal Widgets.WidgetOptions RootOptions { get; } = new Widgets.WidgetOptions();

    /// <summary>The root menubar presentation window ("." configure -menu), if built.</summary>
    internal TkWindow MenubarWindow { get; set; }

    /// <summary>Variable links currently suppressed (guards trace loops).</summary>
    internal HashSet<string> SuppressedVariableLinks { get; }
        = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Runs a command body on the UI thread and returns its result.</summary>
    internal string Ui(Func<string> body)
    {
        return Apartment.RunOnUi(body);
    }

    /// <summary>Runs a UI-side action with an empty Tcl result.</summary>
    internal string Ui(Action body)
    {
        return Apartment.RunOnUi(() =>
        {
            body();
            return string.Empty;
        });
    }

    /// <summary>
    /// Evaluates a callback script on the Tcl thread (fire-and-forget from
    /// the UI side). Errors go to the background-error handler, like Tk's
    /// <c>bgerror</c>.
    /// </summary>
    internal void EvalCallbackScript(string script)
    {
        if (string.IsNullOrEmpty(script)) { return; }

        Apartment.PostToTcl(() =>
        {
            Result result = null;
            if (Interpreter.EvaluateScript(script, ref result) == ReturnCode.Error)
            {
                throw new TkTclError((result != null ? result.ToString() : "callback error")
                    + "\n    (command bound to event)");
            }
        });
    }

    /// <summary>
    /// Resolves a Tcl window path to a live window, or throws the standard
    /// Tk error. Lazily prunes registry entries whose window has been
    /// destroyed outside the <c>destroy</c> command.
    /// </summary>
    internal TkWindow ResolveWindow(string path)
    {
        TkWindow window;
        if (WindowsByPath.TryGetValue(path, out window) && !window.IsDestroyed)
        {
            return window;
        }

        if (window != null) { ForgetWindow(path, window); }
        throw new TkTclError("bad window path name \"" + path + "\"");
    }

    /// <summary>The Tcl path of a window (falls back to its PathName).</summary>
    internal string PathOf(TkWindow window)
    {
        string path;
        return PathsByWindow.TryGetValue(window, out path) ? path : window.PathName;
    }

    /// <summary>Registers a created widget window under its Tcl path.</summary>
    internal void RegisterWindow(string path, TkWindow window)
    {
        WindowsByPath[path] = window;
        PathsByWindow[window] = path;
    }

    /// <summary>Drops one window's registry entries and instance command.</summary>
    internal void ForgetWindow(string path, TkWindow window)
    {
        WindowsByPath.Remove(path);
        MenusByPath.Remove(path);
        if (window != null) { PathsByWindow.Remove(window); }

        long token;
        if (CommandTokensByPath.TryGetValue(path, out token))
        {
            CommandTokensByPath.Remove(path);
            Result error = null;
            Interpreter.RemoveCommand(token, null, ref error);
        }
    }

    /// <summary>
    /// Drops a window and every registered descendant (path-prefix walk) —
    /// the registry side of <c>destroy</c>.
    /// </summary>
    internal void ForgetWindowTree(string path)
    {
        var doomed = new List<string>();
        string prefix = path == TkPaths.Root ? "." : path + ".";
        foreach (string candidate in WindowsByPath.Keys)
        {
            if (candidate == path || candidate.StartsWith(prefix, StringComparison.Ordinal))
            {
                doomed.Add(candidate);
            }
        }

        foreach (string victim in doomed)
        {
            TkWindow window;
            WindowsByPath.TryGetValue(victim, out window);
            if (victim == TkPaths.Root)
            {
                // The root window itself is never forgotten (Tk keeps "." alive).
                continue;
            }
            ForgetWindow(victim, window);
        }
    }
}

/// <summary>Tcl window-path helpers.</summary>
internal static class TkPaths
{
    /// <summary>The root window path.</summary>
    internal const string Root = ".";

    /// <summary>Splits a path into (parentPath, name); throws on malformed paths.</summary>
    internal static void Split(string path, out string parentPath, out string name)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '.' || path == Root)
        {
            throw new TkTclError("bad window path name \"" + path + "\"");
        }

        int lastDot = path.LastIndexOf('.');
        name = path.Substring(lastDot + 1);
        parentPath = lastDot == 0 ? Root : path.Substring(0, lastDot);

        if (name.Length == 0 || char.IsUpper(name[0]))
        {
            throw new TkTclError("bad window path name \"" + path + "\"");
        }
    }
}
