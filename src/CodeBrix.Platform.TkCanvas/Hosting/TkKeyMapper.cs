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
    /// <returns>The held modifiers.</returns>
    internal static EventModifiers CurrentModifiers(Microsoft.UI.Xaml.UIElement element)
    {
        EventModifiers state = EventModifiers.None;
        try
        {
            if (IsDown(element, VirtualKey.Shift)) { state |= EventModifiers.Shift; }
            if (IsDown(element, VirtualKey.Control)) { state |= EventModifiers.Control; }
            if (IsDown(element, VirtualKey.Menu)) { state |= EventModifiers.Alt; }
        }
        catch (Exception)
        {
            // A head without queryable key state simply reports no modifiers.
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
    /// Control-letter combinations (Control-c/x/v and friends). Plain
    /// character keys return false — their text arrives through the input
    /// element's text-change path instead.
    /// </summary>
    /// <param name="key">The platform virtual key.</param>
    /// <param name="keySym">The Tk keysym on success.</param>
    /// <param name="state">The modifier state on success.</param>
    /// <returns>True when the event should be forwarded as a toolkit key event.</returns>
    internal static bool TryMapSpecialOrControl(VirtualKey key, out string keySym, out EventModifiers state)
    {
        state = CurrentModifiers(null);
        if (TryMapSpecial(key, out keySym)) { return true; }

        if ((state & EventModifiers.Control) != 0
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
    /// <returns>True when the key maps to a Tk key event.</returns>
    internal static bool TryMapViewKey(VirtualKey key, out string keySym, out string character,
            out EventModifiers state)
    {
        state = CurrentModifiers(null);
        character = "";
        if (TryMapSpecial(key, out keySym)) { return true; }

        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            bool shifted = (state & EventModifiers.Shift) != 0;
            char lower = (char)('a' + (key - VirtualKey.A));
            char produced = shifted ? char.ToUpperInvariant(lower) : lower;
            keySym = produced.ToString();
            if ((state & EventModifiers.Control) == 0) { character = produced.ToString(); }
            return true;
        }
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            char digit = (char)('0' + (key - VirtualKey.Number0));
            keySym = digit.ToString();
            if ((state & EventModifiers.Control) == 0) { character = digit.ToString(); }
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
