using System;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// The modifier state carried by an event and demanded by an event pattern —
/// Tk's modifier mask (keyboard modifiers plus held-down mouse buttons).
/// </summary>
[Flags]
public enum EventModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,

    /// <summary>The Shift key (<c>Shift-</c>).</summary>
    Shift = 1 << 0,

    /// <summary>Caps Lock (<c>Lock-</c>).</summary>
    Lock = 1 << 1,

    /// <summary>The Control key (<c>Control-</c>).</summary>
    Control = 1 << 2,

    /// <summary>Mod1 — the Alt key on typical X servers (<c>Alt-</c>/<c>Mod1-</c>).</summary>
    Alt = 1 << 3,

    /// <summary>The Meta key (<c>Meta-</c>).</summary>
    Meta = 1 << 4,

    /// <summary>The macOS Command key (<c>Command-</c>).</summary>
    Command = 1 << 5,

    /// <summary>Mouse button 1 held (<c>B1-</c>).</summary>
    Button1 = 1 << 8,

    /// <summary>Mouse button 2 held (<c>B2-</c>).</summary>
    Button2 = 1 << 9,

    /// <summary>Mouse button 3 held (<c>B3-</c>).</summary>
    Button3 = 1 << 10,

    /// <summary>Mouse button 4 held (<c>B4-</c>).</summary>
    Button4 = 1 << 11,

    /// <summary>Mouse button 5 held (<c>B5-</c>).</summary>
    Button5 = 1 << 12,

    /// <summary>
    /// The pattern demands a double event (<c>Double-</c>); on an event this
    /// is set when the click count is exactly satisfied.
    /// </summary>
    Double = 1 << 16,

    /// <summary>The pattern demands a triple event (<c>Triple-</c>).</summary>
    Triple = 1 << 17,

    /// <summary>The pattern demands a quadruple event (<c>Quadruple-</c>).</summary>
    Quadruple = 1 << 18,
}
