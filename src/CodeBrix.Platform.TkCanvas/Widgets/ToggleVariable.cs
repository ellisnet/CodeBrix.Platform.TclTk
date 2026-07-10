using System;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// A shared string value with change notification — the toolkit's stand-in
/// for the Tcl variable a checkbutton/radiobutton is bound to (<c>-variable</c>).
/// Widgets sharing one instance stay in sync: setting the value repaints every
/// bound widget, so radiobuttons in a group deselect each other and a
/// checkbutton reflects external writes. The Phase-C command bridge links one
/// of these to an actual Tcl variable via a variable trace.
/// </summary>
public sealed class ToggleVariable
{
    private string _value = "";

    /// <summary>Creates a variable with an initial value.</summary>
    /// <param name="initial">The initial value.</param>
    public ToggleVariable(string initial = "")
    {
        _value = initial ?? "";
    }

    /// <summary>Raised whenever the value changes.</summary>
    public event Action Changed;

    /// <summary>The current value.</summary>
    public string Value
    {
        get { return _value; }
    }

    /// <summary>Sets the value, notifying bound widgets when it actually changes.</summary>
    /// <param name="value">The new value.</param>
    public void Set(string value)
    {
        value = value ?? "";
        if (value == _value) { return; }
        _value = value;
        Action handler = Changed;
        if (handler != null) { handler(); }
    }
}
