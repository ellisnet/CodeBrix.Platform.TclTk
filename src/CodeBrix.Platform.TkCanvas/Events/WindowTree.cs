using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// The per-tree event nervous system: owns the binding table, the keyboard
/// focus, the pointer-crossing state, and the implicit mouse grab, and routes
/// events to windows by walking their bind tags — the toolkit-level analogue
/// of Tk's event dispatch (tkBind.c/tkFocus.c/tkGrab.c essentials).
/// Reached from any window via <see cref="TkWindow.Tree"/>.
/// </summary>
public sealed class WindowTree
{
    private TkWindow _pointerWindow;
    private TkWindow _mouseGrabWindow;
    private EventModifiers _heldButtons;
    private TkScheduler _scheduler;
    private FontManager _fonts;
    private Overlay.WindowManager _windowManager;

    internal WindowTree(TkWindow root)
    {
        Root = root;
    }

    /// <summary>The root window of this tree.</summary>
    public TkWindow Root { get; }

    /// <summary>The binding table of this tree (<c>bind</c>).</summary>
    public BindingTable Bindings { get; } = new BindingTable();

    /// <summary>
    /// The tree's work scheduler: idle callbacks, <c>after</c> timers, and
    /// the synchronous <c>update</c> flush (created lazily).
    /// </summary>
    public TkScheduler Scheduler
    {
        get
        {
            if (_scheduler == null) { _scheduler = new TkScheduler(Root); }
            return _scheduler;
        }
    }

    /// <summary>
    /// The tree's font service — the single text-measurement seam shared by
    /// the painter and the Tcl-facing font commands (created lazily).
    /// </summary>
    public FontManager Fonts
    {
        get
        {
            if (_fonts == null) { _fonts = new FontManager(); }
            return _fonts;
        }
    }

    /// <summary>
    /// The tree's mini window-manager: overlay toplevels, the <c>wm</c>
    /// surface, chrome interactions, and modal grabs (created lazily).
    /// </summary>
    public Overlay.WindowManager WindowManager
    {
        get
        {
            if (_windowManager == null) { _windowManager = new Overlay.WindowManager(this); }
            return _windowManager;
        }
    }

    /// <summary>The window manager if one was ever created, else null (no allocation).</summary>
    internal Overlay.WindowManager WindowManagerIfCreated
    {
        get { return _windowManager; }
    }

    private Menus.MenuManager _menuManager;

    /// <summary>
    /// The tree's menu system: popup/cascade menus and the menubar, and the
    /// modal pointer routing while a menu is posted (created lazily).
    /// </summary>
    public Menus.MenuManager Menus
    {
        get
        {
            if (_menuManager == null) { _menuManager = new Menus.MenuManager(this); }
            return _menuManager;
        }
    }

    private Images.ImageManager _imageManager;

    /// <summary>
    /// The tree's photo-image registry: the <c>image</c> command model and
    /// the resolver every widget's <c>-image</c> option goes through
    /// (created lazily).
    /// </summary>
    public Images.ImageManager Images
    {
        get
        {
            if (_imageManager == null) { _imageManager = new Images.ImageManager(this); }
            return _imageManager;
        }
    }

    /// <summary>The image manager if one was ever created, else null (no allocation).</summary>
    internal Images.ImageManager ImagesIfCreated
    {
        get { return _imageManager; }
    }

    /// <summary>
    /// The host's hidden-input-element bridge (the §3.13 IME sink), or null
    /// in headless use. The <c>text</c> and <c>entry</c> widgets attach
    /// themselves to it as they gain and lose focus.
    /// </summary>
    public Text.ITextInputSink InputSink { get; set; }

    private Clipboard.ClipboardManager _clipboard;

    /// <summary>
    /// The tree's clipboard: the toolkit-wide <c>clipboard</c> command
    /// model, bridged to the OS through the
    /// <see cref="Clipboard.ITkClipboard"/> host seam (created lazily).
    /// </summary>
    public Clipboard.ClipboardManager Clipboard
    {
        get
        {
            if (_clipboard == null) { _clipboard = new Clipboard.ClipboardManager(); }
            return _clipboard;
        }
    }

    private Theming.TkTheme _theme;

    /// <summary>
    /// The tree's color scheme (the plan's B.12a). Never null: with no theme
    /// set, the classic Tk theme is in effect — the default look IS the
    /// classic theme, so every theme change is round-trippable. Assigning
    /// repaints the tree.
    /// </summary>
    public Theming.TkTheme Theme
    {
        get
        {
            if (_theme == null) { _theme = Theming.TkTheme.CreateClassic(); }
            return _theme;
        }
        set
        {
            _theme = value ?? Theming.TkTheme.CreateClassic();
            Scheduler.ScheduleRepaint();
        }
    }

    /// <summary>
    /// Rebuilds the theme from a base color or palette entries and applies it
    /// — the toolkit-side <c>tk_setPalette</c> (see
    /// <see cref="Theming.TkTheme.FromPalette"/>).
    /// </summary>
    /// <param name="args">A single background color, or option-name/value pairs.</param>
    public void SetPalette(IReadOnlyList<string> args)
    {
        Theme = Theming.TkTheme.FromPalette(args);
    }

    private Theming.OptionDatabase _optionDatabase;

    /// <summary>
    /// The tree's option database (<c>option add/get/clear</c>, the plan's
    /// B.12b) — consulted when widgets are created, for options not
    /// explicitly configured (created lazily).
    /// </summary>
    public Theming.OptionDatabase OptionDatabase
    {
        get
        {
            if (_optionDatabase == null) { _optionDatabase = new Theming.OptionDatabase(); }
            return _optionDatabase;
        }
    }

    /// <summary>The option database if one was ever created, else null (no allocation).</summary>
    internal Theming.OptionDatabase OptionDatabaseIfCreated
    {
        get { return _optionDatabase; }
    }

    private Theming.TtkStyleEngine _styles;

    /// <summary>
    /// The tree's <c>ttk::style</c> engine (the plan's B.12c) — style tables,
    /// state maps, and named ttk themes (created lazily).
    /// </summary>
    public Theming.TtkStyleEngine Styles
    {
        get
        {
            if (_styles == null) { _styles = new Theming.TtkStyleEngine(this); }
            return _styles;
        }
    }

    /// <summary>The style engine if one was ever created, else null (no allocation).</summary>
    internal Theming.TtkStyleEngine StylesIfCreated
    {
        get { return _styles; }
    }

    /// <summary>
    /// Notifies the scheduler that geometry-affecting state changed, so a
    /// coalesced relayout is pending. Mutations occurring INSIDE the layout
    /// pass (request propagation) do not re-schedule.
    /// </summary>
    internal void NotifyGeometryChanged()
    {
        TkScheduler scheduler = Scheduler;
        if (scheduler.IsRunningLayout) { return; }
        scheduler.ScheduleRelayout();
    }

    /// <summary>The window holding keyboard focus, or null (<c>focus</c>).</summary>
    public TkWindow FocusWindow { get; private set; }

    /// <summary>
    /// The window holding an explicit modal grab (<c>grab</c>), or null.
    /// While set, pointer events outside the grab window's subtree are
    /// redirected to the grab window (Tk's local-grab essentials).
    /// </summary>
    public TkWindow GrabWindow { get; set; }

    /// <summary>The window currently under the pointer, or null.</summary>
    public TkWindow PointerWindow
    {
        get { return _pointerWindow; }
    }

    /// <summary>
    /// Dispatches <paramref name="tkEvent"/> to <paramref name="window"/> by
    /// walking its bind tags in order and firing each tag's most specific
    /// matching binding, stopping when a handler returns
    /// <see cref="DispatchResult.Break"/> — the core Tk dispatch rule.
    /// </summary>
    /// <param name="window">The target window.</param>
    /// <param name="tkEvent">The event (its Window is set to the target).</param>
    public void DispatchEvent(TkWindow window, TkEvent tkEvent)
    {
        if (window == null || window.IsDestroyed) { return; }
        tkEvent.Window = window;

        // Widget-internal hook first (the C-event-handler analogue): it runs
        // independently of the script bindings and cannot break them.
        TkEventHandler classHandler = window.ClassEventHandler;
        if (classHandler != null)
        {
            classHandler(tkEvent);
            if (window.IsDestroyed) { return; }
        }

        foreach (string tag in window.EffectiveBindTags())
        {
            TkEventHandler handler = Bindings.FindBest(tag, tkEvent);
            if (handler == null) { continue; }
            if (handler(tkEvent) == DispatchResult.Break) { break; }
        }
    }

    /// <summary>
    /// Routes a pointer event through the tree: hit-tests to the topmost
    /// displayed window under the point (honoring the implicit mouse grab
    /// during a button hold, and an explicit <see cref="GrabWindow"/>),
    /// synthesizes Enter/Leave crossing events, maintains the held-button
    /// state, and dispatches the event with window-relative coordinates.
    /// </summary>
    /// <param name="type">ButtonPress, ButtonRelease, Motion, or MouseWheel.</param>
    /// <param name="rootX">The pointer x position in root-window coordinates.</param>
    /// <param name="rootY">The pointer y position in root-window coordinates.</param>
    /// <param name="button">The button for press/release events (1-5), else 0.</param>
    /// <param name="state">Keyboard modifiers in effect (held buttons are added automatically).</param>
    /// <param name="delta">The wheel delta for MouseWheel events.</param>
    /// <param name="clickCount">The click count for press events (2 = double-click).</param>
    public void PointerEvent(TkEventType type, int rootX, int rootY, int button = 0,
            EventModifiers state = EventModifiers.None, int delta = 0, int clickCount = 1)
    {
        if (type != TkEventType.ButtonPress && type != TkEventType.ButtonRelease
                && type != TkEventType.Motion && type != TkEventType.MouseWheel)
        {
            throw new ArgumentException("not a pointer event type: " + type, nameof(type));
        }

        // The mini window-manager gets first crack: overlay chrome
        // interactions (title-bar drags, the close box) are ITS events, not
        // Tk's — like OS decorations, they never reach bindings.
        if (_windowManager != null && _windowManager.InterceptPointer(type, rootX, rootY, button))
        {
            return;
        }

        // A posted menu chain is modal: it consumes pointer events (tracking
        // the active entry, opening cascades, invoking, and dismissing) before
        // anything underneath sees them.
        if (_menuManager != null && _menuManager.InterceptPointer(type, rootX, rootY, button))
        {
            return;
        }

        TkWindow hit = HitTest(rootX, rootY);

        // An explicit grab confines events to the grab window's subtree.
        if (GrabWindow != null && !IsInSubtree(hit, GrabWindow))
        {
            hit = GrabWindow;
        }

        // The implicit grab: while any button is held, all pointer events go
        // to the window that received the press.
        TkWindow target = (_mouseGrabWindow != null) ? _mouseGrabWindow : hit;

        // Crossing events fire only while no implicit grab is active.
        if (_mouseGrabWindow == null)
        {
            UpdatePointerWindow(hit, rootX, rootY, state | _heldButtons);
        }

        var tkEvent = new TkEvent
        {
            Type = type,
            RootX = rootX,
            RootY = rootY,
            Button = button,
            Delta = delta,
            ClickCount = clickCount,
            State = state | _heldButtons,
            KeySym = string.Empty,
            Character = string.Empty,
        };
        SetWindowRelative(tkEvent, target, rootX, rootY);

        if (type == TkEventType.ButtonPress)
        {
            if (_mouseGrabWindow == null) { _mouseGrabWindow = target; }
            _heldButtons |= ButtonFlag(button);
        }
        else if (type == TkEventType.ButtonRelease)
        {
            // The release event still carries the pressed button in %s
            // (X reports the state BEFORE the release), so update after.
            tkEvent.State = state | _heldButtons;
            _heldButtons &= ~ButtonFlag(button);
        }

        if (target != null)
        {
            DispatchEvent(target, tkEvent);
        }

        // When the last button goes up, the implicit grab ends and the
        // pointer window is re-derived (Enter/Leave may fire).
        if (type == TkEventType.ButtonRelease && _heldButtons == EventModifiers.None && _mouseGrabWindow != null)
        {
            _mouseGrabWindow = null;
            UpdatePointerWindow(HitTest(rootX, rootY), rootX, rootY, state);
        }
    }

    /// <summary>
    /// Routes a key event to the focus window (or the root when nothing has
    /// focus, like Tk falling back to the toplevel).
    /// </summary>
    /// <param name="type">KeyPress or KeyRelease.</param>
    /// <param name="keySym">The key symbol name (e.g. <c>a</c>, <c>Down</c>, <c>Escape</c>).</param>
    /// <param name="character">The printable text produced, or empty.</param>
    /// <param name="state">Modifiers in effect.</param>
    public void KeyEvent(TkEventType type, string keySym, string character = "",
            EventModifiers state = EventModifiers.None)
    {
        if (type != TkEventType.KeyPress && type != TkEventType.KeyRelease)
        {
            throw new ArgumentException("not a key event type: " + type, nameof(type));
        }

        TkWindow target = (FocusWindow != null) ? FocusWindow : Root;
        var tkEvent = new TkEvent
        {
            Type = type,
            KeySym = keySym ?? string.Empty,
            Character = character ?? string.Empty,
            State = state | _heldButtons,
        };
        DispatchEvent(target, tkEvent);
    }

    /// <summary>
    /// Delivers a virtual event (<c>&lt;&lt;Name&gt;&gt;</c>) to a window —
    /// the analogue of <c>event generate $w &lt;&lt;Name&gt;&gt;</c>, and the
    /// mechanism widgets use for <c>&lt;&lt;ListboxSelect&gt;&gt;</c>-style
    /// notifications.
    /// </summary>
    /// <param name="window">The target window.</param>
    /// <param name="virtualName">The virtual event name (without angle brackets).</param>
    public void VirtualEvent(TkWindow window, string virtualName)
    {
        var tkEvent = new TkEvent
        {
            Type = TkEventType.Virtual,
            VirtualName = virtualName,
            KeySym = string.Empty,
            Character = string.Empty,
        };
        DispatchEvent(window, tkEvent);
    }

    /// <summary>
    /// Moves keyboard focus — the analogue of <c>focus $w</c>. Fires
    /// FocusOut on the old window and FocusIn on the new one.
    /// </summary>
    /// <param name="window">The window to focus, or null to clear focus.</param>
    public void SetFocus(TkWindow window)
    {
        if (window == FocusWindow) { return; }

        TkWindow old = FocusWindow;
        FocusWindow = window;

        if (old != null && !old.IsDestroyed)
        {
            DispatchEvent(old, new TkEvent { Type = TkEventType.FocusOut, KeySym = string.Empty, Character = string.Empty });
        }
        if (window != null)
        {
            DispatchEvent(window, new TkEvent { Type = TkEventType.FocusIn, KeySym = string.Empty, Character = string.Empty });
        }
    }

    /// <summary>
    /// Finds the next window in keyboard-traversal order after
    /// <paramref name="window"/> — the analogue of <c>tk_focusNext</c>:
    /// depth-first over the tree (children before siblings), wrapping at the
    /// end, visiting only displayed windows that accept focus.
    /// </summary>
    /// <param name="window">The reference window (typically the focus window).</param>
    /// <returns>The next focusable window, or null when none accepts focus.</returns>
    public TkWindow FocusNext(TkWindow window)
    {
        List<TkWindow> order = TraversalOrder();
        if (order.Count == 0) { return null; }

        int start = (window != null) ? order.IndexOf(window) : -1;
        for (int i = 1; i <= order.Count; i++)
        {
            TkWindow candidate = order[(start + i + order.Count) % order.Count];
            if (candidate.Focusable && candidate.IsDisplayed) { return candidate; }
        }
        return null;
    }

    /// <summary>
    /// The backward counterpart of <see cref="FocusNext"/> —
    /// <c>tk_focusPrev</c>.
    /// </summary>
    /// <param name="window">The reference window.</param>
    /// <returns>The previous focusable window, or null when none accepts focus.</returns>
    public TkWindow FocusPrev(TkWindow window)
    {
        List<TkWindow> order = TraversalOrder();
        if (order.Count == 0) { return null; }

        int start = (window != null) ? order.IndexOf(window) : 0;
        if (start < 0) { start = 0; }
        for (int i = 1; i <= order.Count; i++)
        {
            TkWindow candidate = order[(start - i + 2 * order.Count) % order.Count];
            if (candidate.Focusable && candidate.IsDisplayed) { return candidate; }
        }
        return null;
    }

    /// <summary>
    /// Hit-tests the tree: the topmost displayed window containing the point
    /// (children above parents; later siblings above earlier, matching Tk's
    /// default stacking order).
    /// </summary>
    /// <param name="rootX">The x position in root-window coordinates.</param>
    /// <param name="rootY">The y position in root-window coordinates.</param>
    /// <returns>The window under the point, or null when outside the root.</returns>
    public TkWindow HitTest(int rootX, int rootY)
    {
        if (rootX < 0 || rootY < 0 || rootX >= Root.Width || rootY >= Root.Height) { return null; }
        return HitTestWithin(Root, rootX, rootY);
    }

    private static TkWindow HitTestWithin(TkWindow window, int x, int y)
    {
        // x/y are relative to "window" already-verified-inside; find the
        // topmost child containing the point.
        IReadOnlyList<TkWindow> children = window.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            TkWindow child = children[i];
            if (!child.IsDisplayed || child.IsDestroyed) { continue; }
            int cx = x - child.X;
            int cy = y - child.Y;
            if (cx >= 0 && cy >= 0 && cx < child.Width && cy < child.Height)
            {
                return HitTestWithin(child, cx, cy);
            }
        }
        return window;
    }

    /// <summary>Fires <c>&lt;Destroy&gt;</c> for a dying window and drops its bindings.</summary>
    /// <param name="window">The window being destroyed.</param>
    internal void WindowDestroyed(TkWindow window)
    {
        DispatchEvent(window, new TkEvent { Type = TkEventType.Destroy, KeySym = string.Empty, Character = string.Empty });
        Bindings.RemoveTag(window.PathName);

        if (_windowManager != null)
        {
            _windowManager.WindowDestroyed(window);
        }
        if (_menuManager != null)
        {
            _menuManager.WindowDestroyed(window);
        }

        if (FocusWindow == window) { FocusWindow = null; }
        if (GrabWindow == window) { GrabWindow = null; }
        if (_mouseGrabWindow == window) { _mouseGrabWindow = null; }
        if (_pointerWindow == window) { _pointerWindow = null; }
    }

    /// <summary>
    /// Synthesizes the Enter/Leave chains for a pointer-window change: Leave
    /// fires from the old window up to (excluding) the common ancestor, Enter
    /// from below the common ancestor down to the new window — Tk's crossing
    /// model without the NotifyDetail refinements.
    /// </summary>
    private void UpdatePointerWindow(TkWindow newWindow, int rootX, int rootY, EventModifiers state)
    {
        if (newWindow == _pointerWindow) { return; }

        TkWindow old = _pointerWindow;
        _pointerWindow = newWindow;

        TkWindow commonAncestor = CommonAncestor(old, newWindow);

        for (TkWindow w = old; w != null && w != commonAncestor; w = w.Parent)
        {
            if (w.IsDestroyed) { continue; }
            var leave = new TkEvent { Type = TkEventType.Leave, RootX = rootX, RootY = rootY, State = state, KeySym = string.Empty, Character = string.Empty };
            SetWindowRelative(leave, w, rootX, rootY);
            DispatchEvent(w, leave);
        }

        var enterChain = new List<TkWindow>();
        for (TkWindow w = newWindow; w != null && w != commonAncestor; w = w.Parent)
        {
            enterChain.Add(w);
        }
        for (int i = enterChain.Count - 1; i >= 0; i--)
        {
            TkWindow w = enterChain[i];
            var enter = new TkEvent { Type = TkEventType.Enter, RootX = rootX, RootY = rootY, State = state, KeySym = string.Empty, Character = string.Empty };
            SetWindowRelative(enter, w, rootX, rootY);
            DispatchEvent(w, enter);
        }
    }

    private static TkWindow CommonAncestor(TkWindow a, TkWindow b)
    {
        if (a == null || b == null) { return null; }

        var ancestors = new HashSet<TkWindow>();
        for (TkWindow w = a; w != null; w = w.Parent) { ancestors.Add(w); }
        for (TkWindow w = b; w != null; w = w.Parent)
        {
            if (ancestors.Contains(w)) { return w; }
        }
        return null;
    }

    private static void SetWindowRelative(TkEvent tkEvent, TkWindow window, int rootX, int rootY)
    {
        if (window == null) { return; }
        int originX = 0, originY = 0;
        for (TkWindow w = window; w != null && w.Parent != null; w = w.Parent)
        {
            originX += w.X;
            originY += w.Y;
        }
        tkEvent.X = rootX - originX;
        tkEvent.Y = rootY - originY;
    }

    private static bool IsInSubtree(TkWindow window, TkWindow subtreeRoot)
    {
        for (TkWindow w = window; w != null; w = w.Parent)
        {
            if (w == subtreeRoot) { return true; }
        }
        return false;
    }

    private static EventModifiers ButtonFlag(int button)
    {
        switch (button)
        {
            case 1: return EventModifiers.Button1;
            case 2: return EventModifiers.Button2;
            case 3: return EventModifiers.Button3;
            case 4: return EventModifiers.Button4;
            case 5: return EventModifiers.Button5;
            default: return EventModifiers.None;
        }
    }

    private List<TkWindow> TraversalOrder()
    {
        var order = new List<TkWindow>();
        CollectDepthFirst(Root, order);
        return order;
    }

    private static void CollectDepthFirst(TkWindow window, List<TkWindow> order)
    {
        order.Add(window);
        foreach (TkWindow child in window.Children)
        {
            if (!child.IsDestroyed) { CollectDepthFirst(child, order); }
        }
    }
}
