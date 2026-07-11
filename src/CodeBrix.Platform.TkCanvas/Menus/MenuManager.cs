using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Menus;

/// <summary>
/// Drives the menu system of one window tree: it creates popup/cascade menus
/// as override-redirect overlay windows (withdrawn until posted), posts them
/// (<c>tk_popup</c> and menubar/cascade opening), and — while any menu is up —
/// takes over pointer routing so the whole posted chain behaves like a modal
/// Tk menu: hovering tracks the active entry and opens cascades, releasing
/// over a command invokes it and tears the chain down, and pressing or
/// releasing outside every menu dismisses it. The <see cref="WindowTree"/>
/// consults <see cref="InterceptPointer"/> before ordinary dispatch, so menu
/// interaction never leaks to the widgets underneath.
/// </summary>
public sealed class MenuManager
{
    private readonly WindowTree _tree;
    private readonly List<MenuWidget> _posted = new List<MenuWidget>();
    private MenuWidget _menubar;
    private int _menubarOpenIndex = -1;
    private int _menuSerial;

    internal MenuManager(WindowTree tree)
    {
        _tree = tree;
    }

    /// <summary>Whether any menu is currently posted (menu mode is active).</summary>
    public bool IsPosted
    {
        get { return _posted.Count > 0; }
    }

    /// <summary>The posted menu chain (root-most first).</summary>
    public IReadOnlyList<MenuWidget> Posted
    {
        get { return _posted; }
    }

    /// <summary>
    /// Creates a popup/cascade menu backed by an override-redirect overlay
    /// window (initially withdrawn) — the analogue of <c>menu .name</c> for a
    /// menu that is posted rather than attached as a menubar.
    /// </summary>
    /// <param name="name">A base leaf name (a serial is appended for uniqueness).</param>
    /// <returns>The created menu.</returns>
    public MenuWidget CreateMenu(string name)
    {
        WindowManager wm = _tree.WindowManager;
        TkWindow window = wm.CreateToplevel((name ?? "menu") + "#" + (_menuSerial++));
        wm.SetOverrideRedirect(window, true);
        wm.Withdraw(window);
        return new MenuWidget(window);
    }

    /// <summary>Registers the menubar menu drawn in the root (its cascades open on click).</summary>
    /// <param name="menubar">The menubar menu (created with <c>-type menubar</c>), or null to clear.</param>
    public void SetMenubar(MenuWidget menubar)
    {
        _menubar = menubar;
    }

    /// <summary>Posts a menu at root coordinates and enters menu mode — <c>tk_popup</c>.</summary>
    /// <param name="menu">The menu to post.</param>
    /// <param name="x">The root x.</param>
    /// <param name="y">The root y.</param>
    public void Popup(MenuWidget menu, int x, int y)
    {
        Unpost();
        PostAt(menu, x, y);
    }

    /// <summary>Tears down the whole posted chain and leaves menu mode — <c>unpost</c>.</summary>
    public void Unpost()
    {
        WindowManager wm = _tree.WindowManager;
        foreach (MenuWidget menu in _posted)
        {
            menu.ActiveIndex = -1;
            wm.Withdraw(menu.Window);
        }
        _posted.Clear();
        _menubarOpenIndex = -1;
        if (_menubar != null) { _menubar.ActiveIndex = -1; }
        _tree.Scheduler.ScheduleRepaint();
    }

    private void PostAt(MenuWidget menu, int x, int y)
    {
        WindowManager wm = _tree.WindowManager;
        menu.Measure();
        wm.SetGeometry(menu.Window, menu.Window.RequestedWidth, menu.Window.RequestedHeight, x, y);
        wm.Deiconify(menu.Window);
        _posted.Add(menu);
    }

    private void CloseDeeperThan(int index)
    {
        WindowManager wm = _tree.WindowManager;
        for (int i = _posted.Count - 1; i > index; i--)
        {
            _posted[i].ActiveIndex = -1;
            wm.Withdraw(_posted[i].Window);
            _posted.RemoveAt(i);
        }
    }

    private void OpenMenubarEntry(int index)
    {
        if (_menubar == null || index < 0 || index >= _menubar.Entries.Count) { return; }
        Unpost();
        _menubar.ActiveIndex = index;
        _menubarOpenIndex = index;

        MenuEntry entry = _menubar.Entries[index];
        if (entry.Submenu == null) { return; }

        SkiaSharp.SKRectI r = _menubar.EntryRect(index);
        int rootX = _menubar.Window.X + r.Left;
        int rootY = _menubar.Window.Y + r.Bottom;
        PostAt(entry.Submenu, rootX, rootY);
    }

    private void OpenCascade(int postedIndex, int entryIndex)
    {
        MenuWidget parent = _posted[postedIndex];
        MenuEntry entry = parent.Entries[entryIndex];
        if (entry.Submenu == null) { return; }

        // Already open as the next menu? then nothing to do.
        if (postedIndex + 1 < _posted.Count && _posted[postedIndex + 1] == entry.Submenu) { return; }
        CloseDeeperThan(postedIndex);

        SkiaSharp.SKRectI r = parent.EntryRect(entryIndex);
        int rootX = parent.Window.X + r.Right;
        int rootY = parent.Window.Y + r.Top;
        PostAt(entry.Submenu, rootX, rootY);
    }

    private int PostedMenuAt(int rootX, int rootY)
    {
        for (int i = _posted.Count - 1; i >= 0; i--)
        {
            TkWindow w = _posted[i].Window;
            if (rootX >= w.X && rootX < w.X + w.Width && rootY >= w.Y && rootY < w.Y + w.Height)
            {
                return i;
            }
        }
        return -1;
    }

    private bool OverMenubar(int rootX, int rootY)
    {
        if (_menubar == null || !_menubar.Window.IsDisplayed) { return false; }
        TkWindow w = _menubar.Window;
        return rootX >= w.X && rootX < w.X + w.Width && rootY >= w.Y && rootY < w.Y + w.Height;
    }

    /// <summary>
    /// The pointer hook the tree consults before ordinary dispatch. Returns
    /// true when the event is a menu interaction that must be consumed.
    /// </summary>
    /// <param name="type">The pointer event type.</param>
    /// <param name="rootX">Root x.</param>
    /// <param name="rootY">Root y.</param>
    /// <param name="button">The button (for press/release).</param>
    /// <returns>True when the event was consumed by the menu system.</returns>
    internal bool InterceptPointer(TkEventType type, int rootX, int rootY, int button)
    {
        bool inMode = _posted.Count > 0;
        bool overMenubar = OverMenubar(rootX, rootY);

        if (!inMode)
        {
            if (overMenubar && type == TkEventType.ButtonPress)
            {
                int idx = MenubarEntryAt(rootX, rootY);
                if (idx >= 0) { OpenMenubarEntry(idx); return true; }
            }
            return false;
        }

        switch (type)
        {
            case TkEventType.Motion:
            {
                if (overMenubar)
                {
                    int idx = MenubarEntryAt(rootX, rootY);
                    if (idx >= 0 && idx != _menubarOpenIndex) { OpenMenubarEntry(idx); }
                    return true;
                }
                int mi = PostedMenuAt(rootX, rootY);
                if (mi >= 0)
                {
                    MenuWidget m = _posted[mi];
                    int e = m.EntryIndexAt(rootX - m.Window.X, rootY - m.Window.Y);
                    m.ActiveIndex = e;
                    if (e >= 0 && m.Entries[e].Type == MenuEntryType.Cascade && !m.Entries[e].Disabled)
                    {
                        OpenCascade(mi, e);
                    }
                    else
                    {
                        CloseDeeperThan(mi);
                    }
                }
                return true;
            }
            case TkEventType.ButtonRelease:
            {
                int mi = PostedMenuAt(rootX, rootY);
                if (mi >= 0)
                {
                    MenuWidget m = _posted[mi];
                    int e = m.EntryIndexAt(rootX - m.Window.X, rootY - m.Window.Y);
                    if (e >= 0)
                    {
                        MenuEntry entry = m.Entries[e];
                        if (!entry.Disabled && entry.Type != MenuEntryType.Cascade
                                && entry.Type != MenuEntryType.Separator)
                        {
                            m.Invoke(e);
                            Unpost();
                        }
                    }
                }
                else if (!overMenubar)
                {
                    Unpost();
                }
                return true;
            }
            case TkEventType.ButtonPress:
            {
                if (PostedMenuAt(rootX, rootY) < 0 && !overMenubar) { Unpost(); }
                return true;
            }
            default:
            {
                return true; // consume wheel etc. while menus are up
            }
        }
    }

    /// <summary>
    /// The key hook the tree consults before ordinary dispatch — the keyboard
    /// analogue of <see cref="InterceptPointer"/>. When Alt is held and the key
    /// is a single letter matching a menubar cascade's mnemonic
    /// (its <c>-underline</c> character), it opens that menu, mirroring Tk's
    /// automatic <c>Alt+&lt;letter&gt;</c> menubar traversal, and returns true.
    /// Every other key falls through unconsumed.
    /// </summary>
    /// <param name="keySym">The key symbol name (e.g. <c>f</c>).</param>
    /// <param name="state">Keyboard modifiers in effect.</param>
    /// <returns>True when the key matched a menubar mnemonic and was consumed.</returns>
    internal bool InterceptKey(string keySym, EventModifiers state)
    {
        if ((state & EventModifiers.Alt) == 0) { return false; }
        if (_menubar == null || string.IsNullOrEmpty(keySym) || keySym.Length != 1) { return false; }

        char pressed = char.ToLowerInvariant(keySym[0]);
        for (int i = 0; i < _menubar.Entries.Count; i++)
        {
            MenuEntry entry = _menubar.Entries[i];
            if (entry.Type != MenuEntryType.Cascade || entry.Disabled) { continue; }
            if (entry.Underline < 0 || entry.Underline >= entry.Label.Length) { continue; }
            if (char.ToLowerInvariant(entry.Label[entry.Underline]) == pressed)
            {
                OpenMenubarEntry(i);
                return true;
            }
        }
        return false;
    }

    private int MenubarEntryAt(int rootX, int rootY)
    {
        if (_menubar == null) { return -1; }
        return _menubar.EntryIndexAt(rootX - _menubar.Window.X, rootY - _menubar.Window.Y);
    }

    /// <summary>Drops a destroyed menu from the posted chain.</summary>
    /// <param name="window">The destroyed window.</param>
    internal void WindowDestroyed(TkWindow window)
    {
        for (int i = _posted.Count - 1; i >= 0; i--)
        {
            if (_posted[i].Window == window) { _posted.RemoveAt(i); }
        }
        if (_menubar != null && _menubar.Window == window) { _menubar = null; }
    }
}
