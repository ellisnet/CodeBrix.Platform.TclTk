using System;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace DRAKON.Brix.Drakon;

/// <summary>
/// A tiny <c>__brixreport MESSAGE</c> Tcl command that routes text to the
/// runtime's diagnostic sink — the interpreter's own <c>puts</c> is not
/// wired to the process console, so this is how startup-time Tcl probes
/// surface in the log.
/// </summary>
internal sealed class DiagnosticReportCommand : Default
{
    private readonly Action<string> _report;

    internal DiagnosticReportCommand(Action<string> report)
        : base(new CommandData(
            "__brixreport", null, null, null,
            typeof(DiagnosticReportCommand).FullName, CommandFlags.None, null, 0))
    {
        _report = report;
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (arguments != null && arguments.Count >= 2)
        {
            _report("PROBE " + arguments[1]);
        }
        result = string.Empty;
        return ReturnCode.Ok;
    }
}
