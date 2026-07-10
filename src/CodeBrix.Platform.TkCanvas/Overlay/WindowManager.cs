using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Overlay;

/// <summary>
/// The mini window-manager of one window tree (the plan's §11.5): creates
/// and tracks overlay toplevels, implements the <c>wm</c> surface (title,
/// geometry, withdraw/deiconify, transient, overrideredirect, resizable),
/// keeps the overlay stacking order (overlays always sit above the base
/// layout; raising an overlay reorders it to the top), owns the
/// title-bar-drag / close-affordance pointer interactions — chrome clicks
/// never reach Tk bindings, exactly as OS decorations never reach a Tk app —
/// clamps overlays into the host bounds on resize (§11.4), and applies
/// modality through the tree's grab (<c>grab</c>).
/// </summary>
public sealed class WindowManager
{
    private readonly WindowTree _tree;
    private readonly List<OverlayState> _overlays = new List<OverlayState>();

    private OverlayState _dragging;
    private int _dragOffsetX;
    private int _dragOffsetY;
    private string _rootTitle = "";

    internal WindowManager(WindowTree tree)
    {
        _tree = tree;
    }

    /// <summary>The overlays in stacking order (bottom-most first).</summary>
    public IReadOnlyList<OverlayState> Overlays
    {
        get { return _overlays; }
    }

    /// <summary>
    /// The root window's title (<c>wm title .</c>). The HOST application
    /// window owns the actual chrome; it subscribes to
    /// <see cref="RootTitleChanged"/> and propagates the text.
    /// </summary>
    public string RootTitle
    {
        get { return _rootTitle; }
    }

    /// <summary>Raised when <c>wm title .</c> changes, for the host window.</summary>
    public event Action<string> RootTitleChanged;

    /// <summary>
    /// Raised when an overlay's close affordance is clicked. The subscriber
    /// decides what closing means (Tk maps it to WM_DELETE_WINDOW, which
    /// defaults to destroying the toplevel; with no subscriber the toolkit
    /// destroys it).
    /// </summary>
    public event Action<TkWindow> CloseRequested;

    /// <summary>
    /// Creates an overlay toplevel — the analogue of <c>toplevel .name</c>:
    /// a child of the root window, excluded from the base layout, drawn (with
    /// TkCanvas-chromed decorations) above all base content.
    /// </summary>
    /// <param name="name">The window's leaf name (the toplevel is <c>.name</c>).</param>
    /// <returns>The overlay's window.</returns>
    public TkWindow CreateToplevel(string name)
    {
        TkWindow window = _tree.Root.CreateChild(name);
        window.ClassName = "Toplevel";
        var state = new OverlayState(window);
        window.Overlay = state;
        _overlays.Add(state);
        _tree.NotifyGeometryChanged();
        return window;
    }

    /// <summary>The overlay state of a toplevel, or null for a non-overlay window.</summary>
    /// <param name="window">The window to look up.</param>
    /// <returns>The overlay state, or null.</returns>
    public OverlayState GetOverlay(TkWindow window)
    {
        return (window != null) ? window.Overlay : null;
    }

    /// <summary>
    /// Sets a window title — <c>wm title</c>. On the root window the title
    /// propagates to the host (<see cref="RootTitleChanged"/>); on an overlay
    /// it is drawn in the chrome title bar.
    /// </summary>
    /// <param name="window">The root window or an overlay toplevel.</param>
    /// <param name="title">The title text.</param>
    public void SetTitle(TkWindow window, string title)
    {
        if (window.IsRoot)
        {
            _rootTitle = title ?? "";
            Action<string> handler = RootTitleChanged;
            if (handler != null) { handler(_rootTitle); }
            return;
        }

        OverlayState overlay = RequireOverlay(window);
        overlay.Title = title ?? "";
        RequestRepaint();
    }

    /// <summary>
    /// Applies <c>wm geometry</c> to an overlay: any of content size and
    /// position (null keeps the current value). On the root window size maps
    /// to a forced root size (the host window's job in a real app).
    /// </summary>
    /// <param name="window">The root window or an overlay toplevel.</param>
    /// <param name="width">The content width, or null.</param>
    /// <param name="height">The content height, or null.</param>
    /// <param name="x">The content x, or null.</param>
    /// <param name="y">The content y, or null.</param>
    public void SetGeometry(TkWindow window, int? width, int? height, int? x, int? y)
    {
        if (window.IsRoot)
        {
            if (width.HasValue && height.HasValue)
            {
                window.SetForcedSize(width.Value, height.Value);
            }
            return;
        }

        OverlayState overlay = RequireOverlay(window);
        if (width.HasValue) { overlay.GeometryWidth = width; }
        if (height.HasValue) { overlay.GeometryHeight = height; }
        if (x.HasValue) { overlay.GeometryX = x; }
        if (y.HasValue) { overlay.GeometryY = y; }
        _tree.NotifyGeometryChanged();
    }

    /// <summary>Hides an overlay — <c>wm withdraw</c>.</summary>
    /// <param name="window">The overlay toplevel.</param>
    public void Withdraw(TkWindow window)
    {
        OverlayState overlay = RequireOverlay(window);
        if (overlay.Withdrawn) { return; }
        overlay.Withdrawn = true;
        window.IsDisplayed = false;
        _tree.NotifyGeometryChanged();
    }

    /// <summary>Shows a withdrawn overlay and raises it — <c>wm deiconify</c>.</summary>
    /// <param name="window">The overlay toplevel.</param>
    public void Deiconify(TkWindow window)
    {
        OverlayState overlay = RequireOverlay(window);
        overlay.Withdrawn = false;
        Raise(window);
        _tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Raises an overlay to the top of the overlay stack (transients of the
    /// raised window come along, staying above their master).
    /// </summary>
    /// <param name="window">The overlay toplevel.</param>
    public void Raise(TkWindow window)
    {
        OverlayState overlay = RequireOverlay(window);
        _overlays.Remove(overlay);
        _overlays.Add(overlay);
        _tree.Root.MoveChildToEnd(window);

        // Transients ride above their master.
        var transients = new List<OverlayState>();
        foreach (OverlayState candidate in _overlays)
        {
            if (candidate.TransientFor == window) { transients.Add(candidate); }
        }
        foreach (OverlayState transient in transients)
        {
            _overlays.Remove(transient);
            _overlays.Add(transient);
            _tree.Root.MoveChildToEnd(transient.Window);
        }
        RequestRepaint();
    }

    /// <summary>
    /// Marks an overlay transient for a master window — <c>wm transient</c>:
    /// it stacks above the master and is destroyed with it.
    /// </summary>
    /// <param name="window">The overlay toplevel.</param>
    /// <param name="master">The master window (an overlay or the root).</param>
    public void SetTransient(TkWindow window, TkWindow master)
    {
        OverlayState overlay = RequireOverlay(window);
        overlay.TransientFor = master;
        if (master != null && master.Overlay != null)
        {
            Raise(master.Overlay.Window);
        }
    }

    /// <summary>
    /// Suppresses (or restores) an overlay's chrome —
    /// <c>wm overrideredirect</c>. Chromeless overlays cannot be dragged or
    /// closed by the user (tooltips, splash panels, popups).
    /// </summary>
    /// <param name="window">The overlay toplevel.</param>
    /// <param name="overrideRedirect">True to suppress the chrome.</param>
    public void SetOverrideRedirect(TkWindow window, bool overrideRedirect)
    {
        RequireOverlay(window).OverrideRedirect = overrideRedirect;
        RequestRepaint();
    }

    /// <summary>Sets the user-resizability flags — <c>wm resizable</c>.</summary>
    /// <param name="window">The overlay toplevel.</param>
    /// <param name="width">Whether the width is user-resizable.</param>
    /// <param name="height">Whether the height is user-resizable.</param>
    public void SetResizable(TkWindow window, bool width, bool height)
    {
        OverlayState overlay = RequireOverlay(window);
        overlay.ResizableWidth = width;
        overlay.ResizableHeight = height;
    }

    /// <summary>
    /// Captures all input to a window's subtree — the Tcl <c>grab</c>
    /// command, the modality mechanism for overlay dialogs.
    /// </summary>
    /// <param name="window">The window to grab to (typically an overlay).</param>
    public void Grab(TkWindow window)
    {
        _tree.GrabWindow = window;
    }

    /// <summary>Releases the grab — <c>grab release</c>.</summary>
    public void ReleaseGrab()
    {
        _tree.GrabWindow = null;
    }

    /// <summary>
    /// Clamps every overlay so its frame stays inside the root bounds
    /// (§11.4 step 4) — called by the layout pass after the root resizes.
    /// </summary>
    internal void ClampOverlays()
    {
        foreach (OverlayState overlay in _overlays)
        {
            ClampOverlay(overlay);
        }
    }

    private void ClampOverlay(OverlayState overlay)
    {
        TkWindow root = _tree.Root;
        TkWindow window = overlay.Window;
        SKRectI frame = overlay.FrameRect;

        int dx = 0, dy = 0;
        if (frame.Right > root.Width) { dx = root.Width - frame.Right; }
        if (frame.Left + dx < 0) { dx = -frame.Left; }
        if (frame.Bottom > root.Height) { dy = root.Height - frame.Bottom; }
        if (frame.Top + dy < 0) { dy = -frame.Top; }

        if (dx != 0 || dy != 0)
        {
            window.X += dx;
            window.Y += dy;
            if (overlay.GeometryX.HasValue) { overlay.GeometryX = window.X; }
            if (overlay.GeometryY.HasValue) { overlay.GeometryY = window.Y; }
        }
    }

    /// <summary>
    /// The pointer interception hook the tree consults FIRST: chrome
    /// interactions (title-bar drag, the close box) are handled here and
    /// consumed; a press anywhere in an overlay frame raises it (click-to-
    /// raise) without being consumed. Returns true when the event was a
    /// window-manager interaction that must not reach Tk dispatch.
    /// </summary>
    internal bool InterceptPointer(TkEventType type, int rootX, int rootY, int button)
    {
        if (_dragging != null)
        {
            if (type == TkEventType.Motion)
            {
                _dragging.Window.X = rootX - _dragOffsetX;
                _dragging.Window.Y = rootY - _dragOffsetY;
                ClampOverlay(_dragging);
                RequestRepaint();
                return true;
            }
            if (type == TkEventType.ButtonRelease)
            {
                ClampOverlay(_dragging);
                _dragging = null;
                RequestRepaint();
                return true;
            }
            return true;
        }

        if (type != TkEventType.ButtonPress) { return false; }

        // Walk overlays topmost-first.
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            OverlayState overlay = _overlays[i];
            if (overlay.Withdrawn || overlay.Window.IsDestroyed) { continue; }

            SKRectI frame = overlay.FrameRect;
            if (rootX < frame.Left || rootX >= frame.Right
                    || rootY < frame.Top || rootY >= frame.Bottom)
            {
                continue;
            }

            // A grab confines chrome interaction too: another window's
            // chrome is inert while a modal overlay holds the grab.
            if (_tree.GrabWindow != null && !IsInSubtree(overlay.Window, _tree.GrabWindow))
            {
                return true;
            }

            Raise(overlay.Window);

            SKRectI closeBox = overlay.CloseBoxRect;
            if (rootX >= closeBox.Left && rootX < closeBox.Right
                    && rootY >= closeBox.Top && rootY < closeBox.Bottom)
            {
                Action<TkWindow> handler = CloseRequested;
                if (handler != null) { handler(overlay.Window); }
                else { overlay.Window.Destroy(); }
                RequestRepaint();
                return true;
            }

            SKRectI titleBar = overlay.TitleBarRect;
            if (rootX >= titleBar.Left && rootX < titleBar.Right
                    && rootY >= titleBar.Top && rootY < titleBar.Bottom)
            {
                _dragging = overlay;
                _dragOffsetX = rootX - overlay.Window.X;
                _dragOffsetY = rootY - overlay.Window.Y;
                return true;
            }

            // Content-area press: raised (above), but Tk dispatch proceeds.
            return false;
        }
        return false;
    }

    /// <summary>Removes a destroyed toplevel and its transients from the stack.</summary>
    internal void WindowDestroyed(TkWindow window)
    {
        OverlayState overlay = window.Overlay;
        if (overlay == null) { return; }

        _overlays.Remove(overlay);
        if (_dragging == overlay) { _dragging = null; }

        // Transients die with their master.
        var dependents = new List<OverlayState>();
        foreach (OverlayState candidate in _overlays)
        {
            if (candidate.TransientFor == window) { dependents.Add(candidate); }
        }
        foreach (OverlayState dependent in dependents)
        {
            dependent.Window.Destroy();
        }
        RequestRepaint();
    }

    private OverlayState RequireOverlay(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.Overlay == null)
        {
            throw new InvalidOperationException(
                    "window \"" + window.PathName + "\" is not an overlay toplevel");
        }
        return window.Overlay;
    }

    private static bool IsInSubtree(TkWindow window, TkWindow subtreeRoot)
    {
        for (TkWindow w = window; w != null; w = w.Parent)
        {
            if (w == subtreeRoot) { return true; }
        }
        return false;
    }

    private void RequestRepaint()
    {
        _tree.Scheduler.ScheduleRepaint();
    }
}
