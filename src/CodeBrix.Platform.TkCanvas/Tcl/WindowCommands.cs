using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Windowing;
using CodeBrix.Platform.TkCanvas.Canvas;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The window-level commands: <c>wm</c> (over the overlay window manager),
/// <c>winfo</c>, <c>destroy</c>, <c>focus</c>, <c>grab</c>, <c>raise</c>/
/// <c>lower</c>, and the toolkit-wide <c>selection</c> accept-and-no-op
/// (application selection calls are widget subcommands; the X-selection
/// command is a deferred module).
/// </summary>
internal static class WindowCommands
{
    private static readonly Regex GeometryPattern = new Regex(
        @"^=?(?:(\d+)x(\d+))?(?:([+-]\d+)([+-]\d+))?$", RegexOptions.Compiled);

    internal static void Register(BridgeContext ctx)
    {
        BridgeRegistrar.Add(ctx, "wm", words => ctx.Ui(() => Wm(ctx, words)));
        BridgeRegistrar.Add(ctx, "winfo", words => ctx.Ui(() => Winfo(ctx, words)));
        BridgeRegistrar.Add(ctx, "destroy", words => ctx.Ui(() => Destroy(ctx, words)));
        BridgeRegistrar.Add(ctx, "focus", words => ctx.Ui(() => Focus(ctx, words)));
        BridgeRegistrar.Add(ctx, "grab", words => ctx.Ui(() => Grab(ctx, words)));
        BridgeRegistrar.Add(ctx, "raise", words => ctx.Ui(() => RaiseLower(ctx, words, true)));
        BridgeRegistrar.Add(ctx, "lower", words => ctx.Ui(() => RaiseLower(ctx, words, false)));
        BridgeRegistrar.Add(ctx, "selection", words => "");
        BridgeRegistrar.Add(ctx, "tk", words => Tk(ctx, words));
    }

    // ------------------------------------------------------------------ wm

    private static string Wm(BridgeContext ctx, string[] words)
    {
        if (words.Length < 3)
        {
            throw BridgeRegistrar.WrongArgs("wm option window ?arg ...?");
        }

        string sub = words[1];
        string path = words[2];
        TkWindow window = ctx.ResolveWindow(path);
        WindowManager wm = ctx.Tree.WindowManager;
        bool isRoot = path == TkPaths.Root;

        switch (sub)
        {
            case "title":
                if (words.Length >= 4)
                {
                    wm.SetTitle(window, words[3]);
                    return "";
                }
                if (isRoot) { return wm.RootTitle ?? ""; }
                OverlayState overlay = wm.GetOverlay(window);
                return overlay != null ? overlay.Title ?? "" : "";

            case "geometry":
                if (words.Length >= 4)
                {
                    Match match = GeometryPattern.Match(words[3]);
                    if (!match.Success)
                    {
                        throw new TkTclError("bad geometry specifier \"" + words[3] + "\"");
                    }
                    int? width = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
                    int? height = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : (int?)null;
                    int? x = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : (int?)null;
                    int? y = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : (int?)null;
                    if (!isRoot)
                    {
                        wm.SetGeometry(window, width, height, x, y);
                    }
                    return "";
                }
                return window.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                    window.Height.ToString(CultureInfo.InvariantCulture) +
                    "+" + window.X.ToString(CultureInfo.InvariantCulture) +
                    "+" + window.Y.ToString(CultureInfo.InvariantCulture);

            case "withdraw":
                if (!isRoot) { wm.Withdraw(window); }
                return "";

            case "deiconify":
                if (!isRoot) { wm.Deiconify(window); }
                return "";

            case "transient":
                if (!isRoot && words.Length >= 4)
                {
                    TkWindow master = ctx.ResolveWindow(words[3]);
                    wm.SetTransient(window, master);
                }
                return "";

            case "overrideredirect":
                if (words.Length >= 4)
                {
                    if (!isRoot) { wm.SetOverrideRedirect(window, IsTclTrue(words[3])); }
                    return "";
                }
                return "0";

            case "resizable":
                if (!isRoot && words.Length >= 5)
                {
                    wm.SetResizable(window, IsTclTrue(words[3]), IsTclTrue(words[4]));
                }
                return "";

            case "protocol":
            case "minsize":
            case "maxsize":
            case "attributes":
            case "iconphoto":
            case "iconbitmap":
            case "state":
            case "iconify":
            case "group":
            case "focusmodel":
                // Accepted; the overlay model has no meaningful analogue.
                return "";

            default:
                return "";
        }
    }

    // --------------------------------------------------------------- winfo

    private static string Winfo(BridgeContext ctx, string[] words)
    {
        if (words.Length < 3)
        {
            throw BridgeRegistrar.WrongArgs("winfo option ?arg ...?");
        }

        string sub = words[1];
        string path = words[2];

        if (sub == "exists")
        {
            TkWindow candidate;
            return ctx.WindowsByPath.TryGetValue(path, out candidate) &&
                !candidate.IsDestroyed ? "1" : "0";
        }

        TkWindow window = ctx.ResolveWindow(path);

        switch (sub)
        {
            case "width": return window.Width.ToString(CultureInfo.InvariantCulture);
            case "height": return window.Height.ToString(CultureInfo.InvariantCulture);
            case "reqwidth": return window.RequestedWidth.ToString(CultureInfo.InvariantCulture);
            case "reqheight": return window.RequestedHeight.ToString(CultureInfo.InvariantCulture);
            case "x": return window.X.ToString(CultureInfo.InvariantCulture);
            case "y": return window.Y.ToString(CultureInfo.InvariantCulture);
            case "rootx": return RootX(window).ToString(CultureInfo.InvariantCulture);
            case "rooty": return RootY(window).ToString(CultureInfo.InvariantCulture);
            case "class": return window.ClassName;
            case "name": return window.Name;
            case "parent": return window.Parent != null ? ctx.PathOf(window.Parent) : "";
            case "children":
                return TclString.JoinList(window.Children.Select(c => ctx.PathOf(c)).ToList());
            case "toplevel":
            {
                TkWindow current = window;
                while (current.Parent != null && current.Overlay == null)
                {
                    current = current.Parent;
                }
                return ctx.PathOf(current);
            }
            case "screenwidth": return ctx.Tree.Root.Width.ToString(CultureInfo.InvariantCulture);
            case "screenheight": return ctx.Tree.Root.Height.ToString(CultureInfo.InvariantCulture);
            case "ismapped":
            case "viewable":
                return window.IsDisplayed ? "1" : "0";
            case "geometry":
                return window.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                    window.Height.ToString(CultureInfo.InvariantCulture) +
                    "+" + window.X.ToString(CultureInfo.InvariantCulture) +
                    "+" + window.Y.ToString(CultureInfo.InvariantCulture);
            case "pointerx":
            case "pointery":
                return "0";
            case "fpixels":
            case "pixels":
            {
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs("winfo " + sub + " window number"); }
                int pixels;
                if (!TclString.TryParsePixels(words[3], out pixels))
                {
                    throw new TkTclError("bad screen distance \"" + words[3] + "\"");
                }
                return sub == "pixels"
                    ? pixels.ToString(CultureInfo.InvariantCulture)
                    : TclString.FormatDouble(pixels);
            }
            default:
                return "";
        }
    }

    private static int RootX(TkWindow window)
    {
        int x = 0;
        for (TkWindow current = window; current != null; current = current.Parent)
        {
            x += current.X;
        }
        return x;
    }

    private static int RootY(TkWindow window)
    {
        int y = 0;
        for (TkWindow current = window; current != null; current = current.Parent)
        {
            y += current.Y;
        }
        return y;
    }

    // ------------------------------------------------- destroy/focus/grab

    private static string Destroy(BridgeContext ctx, string[] words)
    {
        for (int i = 1; i < words.Length; i++)
        {
            string path = words[i];
            TkWindow window;
            if (!ctx.WindowsByPath.TryGetValue(path, out window) || window.IsDestroyed)
            {
                continue;
            }

            if (path == TkPaths.Root)
            {
                // Tk destroys the application; the host decides what that
                // means — destroy all root children instead.
                foreach (TkWindow child in window.Children.ToList())
                {
                    string childPath = ctx.PathOf(child);
                    ctx.ForgetWindowTree(childPath);
                    child.Destroy();
                }
                continue;
            }

            ctx.ForgetWindowTree(path);
            window.Destroy();
        }

        ctx.Tree.Scheduler.ScheduleRelayout();
        return "";
    }

    private static string Focus(BridgeContext ctx, string[] words)
    {
        if (words.Length == 1)
        {
            TkWindow focused = ctx.Tree.FocusWindow;
            return focused != null ? ctx.PathOf(focused) : "";
        }

        string path = words[1];
        if (path == "-force" || path == "-displayof")
        {
            if (words.Length < 3) { return ""; }
            path = words[2];
        }

        if (path.Length == 0 || path[0] != '.') { return ""; }

        TkWindow window;
        if (ctx.WindowsByPath.TryGetValue(path, out window) && !window.IsDestroyed)
        {
            ctx.Tree.SetFocus(window);
        }
        return "";
    }

    private static string Grab(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("grab ?-global? window");
        }

        string first = words[1];
        switch (first)
        {
            case "release":
                ctx.Tree.WindowManager.ReleaseGrab();
                return "";
            case "current":
                return ctx.Tree.GrabWindow != null ? ctx.PathOf(ctx.Tree.GrabWindow) : "";
            case "set":
                if (words.Length >= 3)
                {
                    ctx.Tree.WindowManager.Grab(ctx.ResolveWindow(words[words.Length - 1]));
                }
                return "";
            case "status":
                return ctx.Tree.GrabWindow != null ? "local" : "none";
            default:
                ctx.Tree.WindowManager.Grab(ctx.ResolveWindow(words[words.Length - 1]));
                return "";
        }
    }

    private static string RaiseLower(BridgeContext ctx, string[] words, bool raise)
    {
        if (words.Length < 2) { return ""; }
        string path = words[1];
        TkWindow window;
        if (!ctx.WindowsByPath.TryGetValue(path, out window) || window.IsDestroyed)
        {
            // Tk errors on an unknown window path (programs rely on catching
            // it — e.g. a "raise this dialog, or build it if absent" idiom).
            throw new TkTclError("bad window path name \"" + path + "\"");
        }

        if (window.Overlay != null && raise)
        {
            ctx.Tree.WindowManager.Raise(window);
        }
        // Sibling stacking for plain windows is a paint-order concern the
        // layout pass owns; accept-and-no-op (application raise/lower calls on
        // plain windows are cosmetic).
        return "";
    }

    private static string Tk(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2) { return ""; }
        switch (words[1])
        {
            case "appname": return "tk";
            case "windowingsystem": return "x11";
            case "scaling":
                return words.Length >= 3 ? "" : TclString.FormatDouble(TclString.PixelsPerInch / 72.0);
            default: return "";
        }
    }

    private static bool IsTclTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            default: return false;
        }
    }
}
