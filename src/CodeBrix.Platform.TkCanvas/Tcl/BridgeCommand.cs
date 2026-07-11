using System;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The generic Tk-command relay: one interpreter command whose body is a
/// bridge handler. The handler runs on the Tcl thread and is responsible
/// for marshaling its widget-tree work to the UI thread (via
/// <c>BridgeContext.Ui</c>); a <see cref="TkTclError"/> becomes a Tcl
/// error with its message.
/// </summary>
internal sealed class BridgeCommand : Default
{
    private readonly Func<string[], string> _handler;

    internal BridgeCommand(ICommandData commandData, Func<string[], string> handler)
        : base(commandData)
    {
        _handler = handler;
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (arguments == null)
        {
            result = "invalid argument list";
            return ReturnCode.Error;
        }

        var words = new string[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            words[index] = arguments[index];
        }

        try
        {
            result = _handler(words) ?? string.Empty;
            return ReturnCode.Ok;
        }
        catch (TkTclError error)
        {
            result = error.Message;
            return ReturnCode.Error;
        }
        catch (Exception ex)
        {
            result = ex.Message;
            return ReturnCode.Error;
        }
    }
}
