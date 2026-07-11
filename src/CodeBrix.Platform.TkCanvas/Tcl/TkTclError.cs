using System;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The exception a bridge command body throws to produce a Tcl error with
/// the given message (the message becomes the interpreter result, exactly
/// like <c>return -code error</c>). Command bodies run on the UI thread;
/// the bridge converts this into <c>ReturnCode.Error</c> on the Tcl side.
/// </summary>
public sealed class TkTclError : Exception
{
    /// <summary>Creates the error carrying a Tcl error message.</summary>
    /// <param name="message">The Tcl error message.</param>
    public TkTclError(string message)
        : base(message)
    {
    }
}
