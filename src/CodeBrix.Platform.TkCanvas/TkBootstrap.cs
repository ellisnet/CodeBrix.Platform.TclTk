using System;

using CodeBrix.Platform.TclTk._Components.Public;

namespace CodeBrix.Platform.TkCanvas;

/// <summary>
/// The interpreter-side bootstrap of the toolkit: sets the version globals
/// and provides the packages classic Tcl/Tk programs demand before they draw
/// anything. Run this on a CodeBrix.Platform.TclTk <see cref="Interpreter"/>
/// BEFORE sourcing a Tcl/Tk application — programs like DRAKON Editor open
/// with <c>package require Tk</c>/<c>Img</c> and a hard
/// <c>$tcl_version &gt;= 8.6</c>/<c>$tk_version &gt;= 8.6</c> check that
/// calls <c>exit</c> when unsatisfied.
/// </summary>
public static class TkBootstrap
{
    /// <summary>The Tcl/Tk language level this toolkit presents (the 8.6 surface).</summary>
    public static readonly Version TkVersion = new Version(8, 6);

    /// <summary>The Tk patchlevel reported in <c>tk_patchLevel</c> (the oracle wish build).</summary>
    public const string TkPatchLevel = "8.6.16";

    /// <summary>
    /// The version provided for <c>package require Img</c>. The Img
    /// package's format role (GIF/PNG decode) is subsumed by the toolkit's
    /// CodeBrix.Imaging-backed photo system, so requiring it must simply
    /// succeed.
    /// </summary>
    public static readonly Version ImgVersion = new Version(1, 4, 13);

    /// <summary>
    /// Sets <c>::tcl_version</c>, <c>::tk_version</c>, and
    /// <c>::tk_patchLevel</c>, and provides the <c>Tk</c> and <c>Img</c>
    /// packages on <paramref name="interpreter"/>.
    /// </summary>
    /// <param name="interpreter">The interpreter to prepare.</param>
    /// <param name="error">The failure detail when the result is not <see cref="ReturnCode.Ok"/>.</param>
    /// <returns><see cref="ReturnCode.Ok"/> on success.</returns>
    public static ReturnCode Register(Interpreter interpreter, ref Result error)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }

        string tkVersionText = TkVersion.ToString(2);
        ReturnCode code = interpreter.SetVariableValue(
                "::tcl_version", tkVersionText, ref error);
        if (code != ReturnCode.Ok) { return code; }

        code = interpreter.SetVariableValue("::tk_version", tkVersionText, ref error);
        if (code != ReturnCode.Ok) { return code; }

        code = interpreter.SetVariableValue("::tk_patchLevel", TkPatchLevel, ref error);
        if (code != ReturnCode.Ok) { return code; }

        code = interpreter.ProvidePackage("Tk", TkVersion, ref error);
        if (code != ReturnCode.Ok) { return code; }

        return interpreter.ProvidePackage("Img", ImgVersion, ref error);
    }
}
