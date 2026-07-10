namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// The event types the toolkit dispatches — the X event families classic Tk
/// binds to, plus Tk virtual events.
/// </summary>
public enum TkEventType
{
    /// <summary>A mouse button was pressed (<c>&lt;ButtonPress&gt;</c>).</summary>
    ButtonPress,

    /// <summary>A mouse button was released (<c>&lt;ButtonRelease&gt;</c>).</summary>
    ButtonRelease,

    /// <summary>The pointer moved (<c>&lt;Motion&gt;</c>).</summary>
    Motion,

    /// <summary>A key was pressed (<c>&lt;KeyPress&gt;</c>).</summary>
    KeyPress,

    /// <summary>A key was released (<c>&lt;KeyRelease&gt;</c>).</summary>
    KeyRelease,

    /// <summary>The pointer entered a window (<c>&lt;Enter&gt;</c>).</summary>
    Enter,

    /// <summary>The pointer left a window (<c>&lt;Leave&gt;</c>).</summary>
    Leave,

    /// <summary>A window gained keyboard focus (<c>&lt;FocusIn&gt;</c>).</summary>
    FocusIn,

    /// <summary>A window lost keyboard focus (<c>&lt;FocusOut&gt;</c>).</summary>
    FocusOut,

    /// <summary>A window's size or position changed (<c>&lt;Configure&gt;</c>).</summary>
    Configure,

    /// <summary>A window is being destroyed (<c>&lt;Destroy&gt;</c>).</summary>
    Destroy,

    /// <summary>A window became viewable (<c>&lt;Map&gt;</c>).</summary>
    Map,

    /// <summary>A window stopped being viewable (<c>&lt;Unmap&gt;</c>).</summary>
    Unmap,

    /// <summary>The mouse wheel turned (<c>&lt;MouseWheel&gt;</c>).</summary>
    MouseWheel,

    /// <summary>A named virtual event (<c>&lt;&lt;Name&gt;&gt;</c>).</summary>
    Virtual,
}
