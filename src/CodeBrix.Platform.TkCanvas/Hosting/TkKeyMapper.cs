using System;

using CodeBrix.Platform.TkCanvas.Events;

using Windows.System;
using Windows.UI.Core;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// Maps CodeBrix.Platform key input to Tk key symbols and modifier state —
/// the naming Tk bindings match against (<c>Left</c>, <c>BackSpace</c>,
/// <c>Return</c>, <c>Prior</c>, ...).
/// </summary>
internal static class TkKeyMapper
{
    /// <summary>Maps a special (non-character) key to its Tk keysym.</summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <returns>True when the key is a mapped special key.</returns>
    internal static bool TryMapSpecial(VirtualKey key, out string keySym)
    {
        switch (key)
        {
            case VirtualKey.Left: keySym = "Left"; return true;
            case VirtualKey.Right: keySym = "Right"; return true;
            case VirtualKey.Up: keySym = "Up"; return true;
            case VirtualKey.Down: keySym = "Down"; return true;
            case VirtualKey.Home: keySym = "Home"; return true;
            case VirtualKey.End: keySym = "End"; return true;
            case VirtualKey.PageUp: keySym = "Prior"; return true;
            case VirtualKey.PageDown: keySym = "Next"; return true;
            case VirtualKey.Back: keySym = "BackSpace"; return true;
            case VirtualKey.Delete: keySym = "Delete"; return true;
            case VirtualKey.Enter: keySym = "Return"; return true;
            case VirtualKey.Tab: keySym = "Tab"; return true;
            case VirtualKey.Escape: keySym = "Escape"; return true;
            case VirtualKey.F1: keySym = "F1"; return true;
            case VirtualKey.F2: keySym = "F2"; return true;
            case VirtualKey.F3: keySym = "F3"; return true;
            case VirtualKey.F4: keySym = "F4"; return true;
            case VirtualKey.F5: keySym = "F5"; return true;
            case VirtualKey.F6: keySym = "F6"; return true;
            case VirtualKey.F7: keySym = "F7"; return true;
            case VirtualKey.F8: keySym = "F8"; return true;
            case VirtualKey.F9: keySym = "F9"; return true;
            case VirtualKey.F10: keySym = "F10"; return true;
            case VirtualKey.F11: keySym = "F11"; return true;
            case VirtualKey.F12: keySym = "F12"; return true;
            default: keySym = null; return false;
        }
    }

    /// <summary>
    /// The keyboard modifier state at the time of the current input event,
    /// in toolkit terms.
    /// </summary>
    /// <param name="element">The element whose input site is queried.</param>
    /// <param name="windowingSystem">The tree's windowing system.</param>
    /// <returns>The held modifiers.</returns>
    internal static EventModifiers CurrentModifiers(Microsoft.UI.Xaml.UIElement element, string windowingSystem)
    {
        VirtualKeyModifiers mods = VirtualKeyModifiers.None;
        try
        {
            if (IsDown(element, VirtualKey.Shift)) { mods |= VirtualKeyModifiers.Shift; }
            if (IsDown(element, VirtualKey.Control)) { mods |= VirtualKeyModifiers.Control; }
            if (IsDown(element, VirtualKey.Menu)) { mods |= VirtualKeyModifiers.Menu; }

            // The macOS head reports the Command key as the Windows key.
            if (TkWindowingSystem.IsAqua(windowingSystem) &&
                (IsDown(element, VirtualKey.LeftWindows) || IsDown(element, VirtualKey.RightWindows)))
            {
                mods |= VirtualKeyModifiers.Windows;
            }
        }
        catch (Exception)
        {
            // A head without queryable key state simply reports no modifiers.
        }
        return FromVirtualKeyModifiers(mods, windowingSystem);
    }

    /// <summary>
    /// Converts platform modifier flags to the toolkit's modifier state:
    /// Shift, Control, and Menu (Alt; the Option key on macOS) always; the
    /// Windows flag — how the macOS head reports the Command key — becomes
    /// <see cref="EventModifiers.Command"/> under aqua and is ignored
    /// elsewhere (the Windows/Super key is not a Tk modifier there).
    /// </summary>
    /// <param name="mods">The platform modifier flags.</param>
    /// <param name="windowingSystem">The tree's windowing system.</param>
    /// <returns>The toolkit modifier state.</returns>
    internal static EventModifiers FromVirtualKeyModifiers(VirtualKeyModifiers mods, string windowingSystem)
    {
        EventModifiers state = EventModifiers.None;
        if ((mods & VirtualKeyModifiers.Shift) != 0) { state |= EventModifiers.Shift; }
        if ((mods & VirtualKeyModifiers.Control) != 0) { state |= EventModifiers.Control; }
        if ((mods & VirtualKeyModifiers.Menu) != 0) { state |= EventModifiers.Alt; }
        if ((mods & VirtualKeyModifiers.Windows) != 0 && TkWindowingSystem.IsAqua(windowingSystem))
        {
            state |= EventModifiers.Command;
        }
        return state;
    }

    private static bool IsDown(Microsoft.UI.Xaml.UIElement element, VirtualKey key)
    {
        CoreVirtualKeyStates states =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (states & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    /// <summary>
    /// Maps a key event on the hidden input element that the toolkit must
    /// handle itself: the special editing/navigation keys, and
    /// Control-letter (and, on macOS, Command-letter) combinations
    /// (Control-c/x/v and friends). Plain character keys return false — their
    /// text arrives through the input element's text-change path instead.
    /// </summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="windowingSystem">The tree's windowing system.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <param name="state">The modifier state on success.</param>
    /// <returns>True when the event should be forwarded as a toolkit key event.</returns>
    internal static bool TryMapSpecialOrControl(VirtualKey key, string windowingSystem, out string keySym,
            out EventModifiers state)
    {
        state = CurrentModifiers(null, windowingSystem);
        return TryMapSpecialOrShortcut(key, state, out keySym);
    }

    /// <summary>
    /// The state-explicit core of <see cref="TryMapSpecialOrControl"/>: a
    /// special key, or a letter held with Control or Command, maps to a
    /// keysym (the letter in lower case).
    /// </summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="state">The modifier state.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <returns>True when the event should be forwarded as a toolkit key event.</returns>
    internal static bool TryMapSpecialOrShortcut(VirtualKey key, EventModifiers state, out string keySym)
    {
        if (TryMapSpecial(key, out keySym)) { return true; }

        if ((state & (EventModifiers.Control | EventModifiers.Command)) != 0
                && key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            keySym = char.ToLowerInvariant((char)('A' + (key - VirtualKey.A))).ToString();
            return true;
        }
        keySym = null;
        return false;
    }

    /// <summary>
    /// Maps a view-level key event (focus NOT inside a text widget) to a Tk
    /// key event: specials map to their keysyms; letters and digits map to
    /// single-character keysyms so canvas/toplevel key bindings
    /// (<c>&lt;KeyPress-d&gt;</c>, <c>&lt;Control-KeyPress&gt;</c>) match.
    /// </summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <param name="character">The printable character, or empty.</param>
    /// <param name="state">The modifier state.</param>
    /// <param name="windowingSystem">The tree's windowing system.</param>
    /// <returns>The toolkit key event mapping result.</returns>
    internal static bool TryMapViewKey(VirtualKey key, string windowingSystem, out string keySym,
            out string character, out EventModifiers state)
    {
        state = CurrentModifiers(null, windowingSystem);
        return TryMapViewKey(key, state, out keySym, out character);
    }

    /// <summary>
    /// The state-explicit core of the view-level key mapping. A key held
    /// with Control or Command carries no printable character.
    /// </summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="state">The modifier state.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <param name="character">The printable character, or empty.</param>
    /// <returns>True when the key maps to a Tk key event.</returns>
    internal static bool TryMapViewKey(VirtualKey key, EventModifiers state, out string keySym,
            out string character)
    {
        character = "";
        if (TryMapSpecial(key, out keySym)) { return true; }

        bool chord = (state & (EventModifiers.Control | EventModifiers.Command)) != 0;
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            bool shifted = (state & EventModifiers.Shift) != 0;
            char lower = (char)('a' + (key - VirtualKey.A));
            char produced = shifted ? char.ToUpperInvariant(lower) : lower;
            keySym = produced.ToString();
            if (!chord) { character = produced.ToString(); }
            return true;
        }
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            char digit = (char)('0' + (key - VirtualKey.Number0));
            keySym = digit.ToString();
            if (!chord) { character = digit.ToString(); }
            return true;
        }
        if (key == VirtualKey.Space)
        {
            keySym = "space";
            character = " ";
            return true;
        }
        keySym = null;
        return false;
    }
}
