using System;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// The Tk windowing-system rules the toolkit applies: the names
/// (<c>x11</c>, <c>aqua</c>, <c>win32</c>), the host's native system, and
/// the per-system modifier-name meanings and <c>%s</c> encoding. A widget
/// tree carries one system (<see cref="WindowTree.WindowingSystem"/>), fixed
/// when a Tcl bridge is registered on it; every rule here takes that system
/// explicitly so the reported name and the modifier layout never disagree.
/// </summary>
internal static class TkWindowingSystem
{
    /// <summary>The X11 windowing system name.</summary>
    internal const string X11 = "x11";

    /// <summary>The macOS (Aqua) windowing system name.</summary>
    internal const string Aqua = "aqua";

    /// <summary>The Windows windowing system name.</summary>
    internal const string Win32 = "win32";

    /// <summary>
    /// Test seam: when set, the windowing system to treat as the host's
    /// native one instead of the one derived from the operating system.
    /// Read when a tree first needs its system, so set it before creating
    /// the tree, and restore it (null) afterwards.
    /// </summary>
    internal static string HostOverride { get; set; }

    /// <summary>The host's native windowing system.</summary>
    internal static string HostNative
    {
        get
        {
            if (HostOverride != null) { return HostOverride; }
            if (OperatingSystem.IsMacOS()) { return Aqua; }
            if (OperatingSystem.IsWindows()) { return Win32; }
            return X11;
        }
    }

    /// <summary>Resolves a bridge's mode to the windowing system it behaves as.</summary>
    /// <param name="forceX11">True for the X11 opt-out.</param>
    /// <returns>The windowing system name.</returns>
    internal static string Resolve(bool forceX11)
    {
        return forceX11 ? X11 : HostNative;
    }

    /// <summary>Whether a system is aqua.</summary>
    /// <param name="system">The windowing system name.</param>
    /// <returns>True for aqua.</returns>
    internal static bool IsAqua(string system)
    {
        return string.Equals(system, Aqua, StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps a system-dependent modifier name to the toolkit's modifier bit,
    /// as Tk does per windowing system. Under x11 Mod1 (= M1 = Command) is
    /// the Alt key. Under aqua Mod1 (= M1 = Command) is the Command key and
    /// Mod2 (= M2 = Option) the Option key, which Alt also names. Under
    /// win32 Mod1 (= M1 = Command) is NumLock, which the toolkit never
    /// reports, so those patterns never fire; Alt stays the Alt key.
    /// </summary>
    /// <param name="system">The windowing system name.</param>
    /// <param name="word">The modifier word from an event pattern.</param>
    /// <param name="modifier">The modifier bit on success.</param>
    /// <returns>True when the word is a system-dependent modifier name.</returns>
    internal static bool TryMapModifierName(string system, string word, out EventModifiers modifier)
    {
        switch (word)
        {
            case "Command":
            case "Mod1":
            case "M1":
                if (IsAqua(system)) { modifier = EventModifiers.Command; }
                else if (string.Equals(system, Win32, StringComparison.Ordinal)) { modifier = EventModifiers.Command; }
                else { modifier = EventModifiers.Alt; }
                return true;
            case "Option":
            case "Mod2":
            case "M2":
                if (IsAqua(system)) { modifier = EventModifiers.Alt; return true; }
                break;
        }
        modifier = EventModifiers.None;
        return false;
    }

    /// <summary>
    /// The numeric modifier state Tk substitutes for <c>%s</c>. Under x11 the
    /// toolkit's bits already are the X masks (Shift 1, Lock 2, Control 4,
    /// Mod1/Alt 8, buttons from 256). Under aqua Tk reports the Command key as
    /// Mod1 (8) and the Option key as Mod2 (16); under win32 it reports Alt as
    /// its own mask 0x20000 (131072).
    /// </summary>
    /// <param name="state">The event's modifier state.</param>
    /// <param name="system">The windowing system name.</param>
    /// <returns>The state as Tk's modifier mask.</returns>
    internal static int StateValue(EventModifiers state, string system)
    {
        if (IsAqua(system))
        {
            int value = (int)(state & ~(EventModifiers.Command | EventModifiers.Alt));
            if ((state & EventModifiers.Command) != 0) { value |= 1 << 3; }
            if ((state & EventModifiers.Alt) != 0) { value |= 1 << 4; }
            return value;
        }
        if (string.Equals(system, Win32, StringComparison.Ordinal))
        {
            int value = (int)(state & ~EventModifiers.Alt);
            if ((state & EventModifiers.Alt) != 0) { value |= 1 << 17; }
            return value;
        }
        return (int)state;
    }
}
