using System;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace DRAKON.Brix.Drakon;

/// <summary>
/// The <c>__drakonbrix_quit ?code?</c> Tcl command that actually terminates
/// the hosted application, the way real Tk's <c>exit</c> does. The engine's
/// own <c>exit</c> only marks the interpreter as exited; in a hosted app the
/// UI thread keeps the process alive, so DRAKON's File &gt; Quit (a bare
/// <c>exit</c>) would otherwise hang. bootstrap.tcl shadows <c>exit</c> with a
/// proc that routes here.
/// </summary>
internal sealed class QuitCommand : Default
{
    internal QuitCommand()
        : base(new CommandData(
            "__drakonbrix_quit", null, null, null,
            typeof(QuitCommand).FullName, CommandFlags.None, null, 0))
    {
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        int code = 0;
        if (arguments != null && arguments.Count >= 2)
        {
            int parsed;
            if (Int32.TryParse(arguments[1], out parsed)) { code = parsed; }
        }

        // Terminate the whole process, matching Tcl's exit(). This is the
        // single reliable way to end a hosted UI app from the Tcl thread; the
        // UI thread would otherwise keep the process alive after the engine's
        // own exit merely flags the interpreter.
        Environment.Exit(code);

        result = string.Empty;
        return ReturnCode.Ok;
    }
}
