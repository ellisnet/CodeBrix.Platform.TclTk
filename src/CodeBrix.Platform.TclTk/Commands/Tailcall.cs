using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Commands;

/// <summary>
/// This class implements the standard Tcl <c>tailcall</c> command, which
/// replaces the currently executing procedure (or lambda) with a call to
/// the specified command.  The target command is resolved in the current
/// namespace but executes at the caller's level once the procedure's call
/// frame has been popped, and its outcome becomes the procedure's result.
/// The semantics replicate stock Tcl 8.6: code following <c>tailcall</c>
/// does not run; an intercepting <c>catch</c> sees the return code but the
/// tailcall still fires when the procedure returns normally; error, break,
/// or continue outcomes discard it; and chains of tailcalls do not grow
/// the stack.  This command is not present in upstream Eagle; it was added
/// by this port.
/// </summary>
[ObjectId("5e0b9c47-2f8d-4a61-b3e9-7c54a8d1f2e6")]
[CommandFlags(CommandFlags.Safe | CommandFlags.Standard)]
[ObjectGroup("control")]
internal sealed class Tailcall : Core
{
    /// <summary>
    /// Constructs an instance of the <c>tailcall</c> command.
    /// </summary>
    /// <param name="commandData">
    /// The data used to create and identify this command, such as its
    /// name and flags.  This parameter may be null.
    /// </param>
    public Tailcall(
        ICommandData commandData
        )
        : base(commandData)
    {
        // do nothing.
    }

    #region IExecute Members
    /// <summary>
    /// This method executes the <c>tailcall</c> command.  It records the
    /// target command on the active procedure call frame and then returns
    /// like <c>return</c>, so the procedure body stops executing; the
    /// procedure's invocation site later executes the recorded command.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context this command is executing in.  This
    /// parameter should not be null.
    /// </param>
    /// <param name="clientData">
    /// The extra, command-specific data supplied when this command was
    /// created, if any.  This parameter may be null.
    /// </param>
    /// <param name="arguments">
    /// The list of arguments for this invocation.  Element zero is the
    /// command name; element one, when present, is the target command
    /// name; the remaining elements are its arguments.  Invoking with no
    /// target clears any pending tailcall and just returns.  This
    /// parameter should not be null.
    /// </param>
    /// <param name="result">
    /// Upon success, this contains an empty result.  Upon failure, this
    /// contains an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Return" /> on success, exactly like the
    /// <c>return</c> command; otherwise, <see cref="ReturnCode.Error" />
    /// when invoked outside a procedure or lambda, the interpreter is
    /// null, or the argument list is null.
    /// </returns>
    public override ReturnCode Execute(
        Interpreter interpreter, /* in */
        IClientData clientData,  /* in */
        ArgumentList arguments,  /* in */
        ref Result result        /* out */
        )
    {
        if (interpreter == null)
        {
            result = "invalid interpreter";
            return ReturnCode.Error;
        }

        if (arguments == null)
        {
            result = "invalid argument list";
            return ReturnCode.Error;
        }

        //
        // NOTE: Use the effective variable frame, not the current frame:
        //       commands like "catch" and "eval" push tracking frames that
        //       variables (and tailcall-ability) resolve straight through,
        //       while "uplevel" genuinely changes the frame.
        //
        ICallFrame frame = null;
        Result error = null;

        if (interpreter.GetVariableFrameViaResolvers(
                LookupFlags.Default, ref frame,
                ref error) != ReturnCode.Ok ||
            !TailcallOps.IsProcedureFrame(frame))
        {
            result = "tailcall can only be called from a proc, lambda or method";
            return ReturnCode.Error;
        }

        if (arguments.Count >= 2)
        {
            StringList words = new StringList();

            words.Add(TailcallOps.ResolveTargetName(
                interpreter, arguments[1]));

            for (int index = 2; index < arguments.Count; index++)
                words.Add(arguments[index]);

            TailcallOps.SetPending(frame, words);
        }
        else
        {
            //
            // NOTE: A bare "tailcall" schedules nothing (and cancels any
            //       previously scheduled tailcall); it just returns.
            //
            TailcallOps.SetPending(frame, null);
        }

        //
        // NOTE: Now behave exactly like a plain "return": the procedure
        //       body stops executing and the procedure returns normally.
        //
        interpreter.ErrorInfo = null;
        interpreter.ErrorCode = null;
        interpreter.ReturnCode = ReturnCode.Ok;

        result = string.Empty;
        return ReturnCode.Return;
    }
    #endregion
}
