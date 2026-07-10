using System;

namespace CodeBrix.Platform.TkCanvas.Menus;

/// <summary>The kind of a menu entry (the constructor verb it was added with).</summary>
public enum MenuEntryType
{
    /// <summary>An action entry that fires its command when invoked (<c>add command</c>).</summary>
    Command,

    /// <summary>An entry that opens a submenu (<c>add cascade</c>).</summary>
    Cascade,

    /// <summary>A horizontal divider (<c>add separator</c>).</summary>
    Separator,

    /// <summary>A toggle entry bound to a variable (<c>add checkbutton</c>).</summary>
    Checkbutton,

    /// <summary>A mutually-exclusive entry bound to a variable (<c>add radiobutton</c>).</summary>
    Radiobutton,
}

/// <summary>
/// One entry of a <see cref="MenuWidget"/> — the model of a Tk menu item: its
/// type, label, optional accelerator text and mnemonic underline, enabled
/// state, the command it fires (command/checkbutton/radiobutton), and the
/// submenu it opens (cascade). DRAKON uses command / cascade / separator;
/// checkbutton and radiobutton entries are modelled for generality.
/// </summary>
public sealed class MenuEntry
{
    /// <summary>The entry kind.</summary>
    public MenuEntryType Type { get; internal set; }

    /// <summary>The entry label (empty for a separator).</summary>
    public string Label { get; set; } = "";

    /// <summary>The accelerator hint drawn right-aligned (e.g. <c>Ctrl+S</c>), or empty.</summary>
    public string Accelerator { get; set; } = "";

    /// <summary>The 0-based index of the mnemonic character to underline, or -1 for none.</summary>
    public int Underline { get; set; } = -1;

    /// <summary>Whether the entry is disabled (drawn greyed, never invoked).</summary>
    public bool Disabled { get; set; }

    /// <summary>The command fired when the entry is invoked (command/checkbutton/radiobutton).</summary>
    public Action Command { get; set; }

    /// <summary>The submenu a cascade entry opens, or null.</summary>
    public MenuWidget Submenu { get; set; }

    /// <summary>Whether a checkbutton/radiobutton entry is currently selected.</summary>
    public bool Selected { get; set; }
}
