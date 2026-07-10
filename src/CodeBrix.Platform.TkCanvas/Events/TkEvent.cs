using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// One dispatched event: the toolkit-level analogue of Tk's XEvent plus the
/// fields Tk's <c>%</c>-substitutions read. Coordinates are window-relative
/// (<see cref="X"/>/<see cref="Y"/>) with the root-window position carried
/// alongside (<see cref="RootX"/>/<see cref="RootY"/>).
/// </summary>
public sealed class TkEvent
{
    /// <summary>The event type.</summary>
    public TkEventType Type { get; set; }

    /// <summary>The window the event was delivered to (<c>%W</c>).</summary>
    public TkWindow Window { get; set; }

    /// <summary>The pointer x position relative to <see cref="Window"/> (<c>%x</c>).</summary>
    public int X { get; set; }

    /// <summary>The pointer y position relative to <see cref="Window"/> (<c>%y</c>).</summary>
    public int Y { get; set; }

    /// <summary>The pointer x position relative to the root window (<c>%X</c>).</summary>
    public int RootX { get; set; }

    /// <summary>The pointer y position relative to the root window (<c>%Y</c>).</summary>
    public int RootY { get; set; }

    /// <summary>The mouse button of a press/release (<c>%b</c>), 1-5, or 0.</summary>
    public int Button { get; set; }

    /// <summary>The key symbol name of a key event (<c>%K</c>), e.g. <c>a</c>, <c>Down</c>, <c>Escape</c>.</summary>
    public string KeySym { get; set; }

    /// <summary>The printable text a key event produced (<c>%A</c>), or empty.</summary>
    public string Character { get; set; }

    /// <summary>The modifier/button state in effect when the event fired (<c>%s</c>).</summary>
    public EventModifiers State { get; set; }

    /// <summary>The wheel delta of a MouseWheel event (<c>%D</c>).</summary>
    public int Delta { get; set; }

    /// <summary>The click count for press events (1 = single, 2 = double, 3 = triple).</summary>
    public int ClickCount { get; set; } = 1;

    /// <summary>The virtual event name for <see cref="TkEventType.Virtual"/> events (without the angle brackets).</summary>
    public string VirtualName { get; set; }

    /// <summary>The new width for Configure events (<c>%w</c>).</summary>
    public int Width { get; set; }

    /// <summary>The new height for Configure events (<c>%h</c>).</summary>
    public int Height { get; set; }
}
