using System;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace DRAKON.Brix.Drakon.Commands;

/// <summary>
/// The <c>__drakonbrix_quit ?code?</c> Tcl command that ends the application,
/// the way real Tk's <c>exit</c> does. The engine's own <c>exit</c> only marks
/// the interpreter as exited; in a hosted app the UI thread keeps the process
/// alive, so DRAKON's File &gt; Quit (a bare <c>exit</c>) would otherwise hang.
/// bootstrap.tcl shadows <c>exit</c> with a proc that routes here.
/// <para>The actual "end the app" behaviour is injected: the hosted
/// application supplies <c>Environment.Exit</c>, while non-hosted callers such
/// as tests supply a safe no-op so a stray <c>exit</c> cannot tear down the
/// test host.</para>
/// </summary>
internal sealed class QuitCommand : Default
{
    private readonly Action<int> _onQuit;

    internal QuitCommand(Action<int> onQuit)
        : base(new CommandData(
            "__drakonbrix_quit", null, null, null,
            typeof(QuitCommand).FullName, CommandFlags.None, null, 0))
    {
        _onQuit = onQuit;
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

        // Route to the injected action. The hosted app passes
        // Environment.Exit(code) — the single reliable way to end a hosted UI
        // app from the Tcl thread, since the UI thread would otherwise keep the
        // process alive after the engine's own exit merely flags the interpreter.
        if (_onQuit != null) { _onQuit(code); }

        result = string.Empty;
        return ReturnCode.Ok;
    }
}
