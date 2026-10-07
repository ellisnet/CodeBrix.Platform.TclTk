namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// Which Tk windowing system a bridge behaves as — what
/// <c>tk windowingsystem</c> reports and, always together with it, which
/// modifier names event patterns accept and what they mean, how <c>%s</c>
/// encodes the modifier state, and whether the host's Command key is a
/// modifier. Chosen once when the bridge is registered
/// (the <c>TkTclBridge.Register</c> / <c>RegisterHosted</c> overloads that
/// take a mode) and fixed for the widget tree afterwards.
/// </summary>
public enum WindowingSystemMode
{
    /// <summary>
    /// The default: behave as Tk does on the host — <c>aqua</c> on macOS
    /// (Command = Mod1 is the Command key, Option = Mod2 = Alt the Option
    /// key), <c>win32</c> on Windows, <c>x11</c> everywhere else.
    /// </summary>
    HostNative = 0,

    /// <summary>
    /// The opt-out: behave as Tk on X11 on every host — <c>tk
    /// windowingsystem</c> reports <c>x11</c>, Command = Mod1 = M1 = Alt
    /// (the Alt key) and the macOS Command key is not a modifier.
    /// </summary>
    X11 = 1,
}
