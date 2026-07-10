using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Components.Private;

/// <summary>
/// This class implements the engine support for the standard Tcl
/// <c>tailcall</c> command.  A tailcall is recorded on the active procedure
/// call frame; when the procedure returns normally, the procedure's
/// invocation site executes the recorded command at the caller's level and
/// its outcome replaces the procedure's result.  A trampoline hand-off
/// keeps chains of tailcalls (self- or mutual recursion) from growing the
/// native stack.
/// </summary>
[ObjectId("b4f7c2d9-8e13-46a5-9f60-3d21c7e8a4b6")]
internal static class TailcallOps
{
    #region Private Types
    /// <summary>
    /// The per-interpreter trampoline state.
    /// </summary>
    private sealed class State
    {
        /// <summary>
        /// Non-zero when the next procedure invocation is the direct target
        /// of a running trampoline; that procedure hands any tailcall it
        /// schedules back to the running trampoline instead of starting a
        /// nested one.
        /// </summary>
        public bool NextInvokeIsTrampolineTarget;

        /// <summary>
        /// The tailcall command handed off to the running trampoline by its
        /// direct target, if any.
        /// </summary>
        public StringList Handoff;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Private Methods
    /// <summary>
    /// This method returns the trampoline state for the specified
    /// interpreter, creating it on first use.  The state lives in a field
    /// on the interpreter itself, so the per-procedure-call check remains
    /// a plain field read.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <returns>
    /// The trampoline state for the specified interpreter.
    /// </returns>
    private static State GetState(
        Interpreter interpreter /* in */
        )
    {
        State state = interpreter.tailcallState as State;

        if (state == null)
        {
            state = new State();
            interpreter.tailcallState = state;
        }

        return state;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Public Methods
    /// <summary>
    /// This method determines whether the specified call frame belongs to a
    /// procedure or lambda, i.e. whether <c>tailcall</c> may be invoked
    /// there.
    /// </summary>
    /// <param name="frame">
    /// The call frame to check.  This parameter may be null.
    /// </param>
    /// <returns>
    /// Non-zero when the frame belongs to a procedure or lambda.
    /// </returns>
    public static bool IsProcedureFrame(
        ICallFrame frame /* in */
        )
    {
        return (frame != null) &&
            frame.HasFlags(CallFrameFlags.Procedure, true);
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method records the specified command words as the pending
    /// tailcall of the specified procedure call frame, replacing any
    /// previously recorded tailcall.  Passing null clears the pending
    /// tailcall.
    /// </summary>
    /// <param name="frame">
    /// The procedure call frame.  This parameter may not be null.
    /// </param>
    /// <param name="words">
    /// The command words to record, or null to clear.
    /// </param>
    public static void SetPending(
        ICallFrame frame, /* in */
        StringList words  /* in */
        )
    {
        frame.ExtraData = (words != null) ?
            new ClientData(words) : null;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method removes and returns the pending tailcall recorded on the
    /// specified procedure call frame, if any.
    /// </summary>
    /// <param name="frame">
    /// The procedure call frame.  This parameter may be null.
    /// </param>
    /// <returns>
    /// The recorded command words, or null when there is no pending
    /// tailcall.
    /// </returns>
    public static StringList TakePending(
        ICallFrame frame /* in */
        )
    {
        if (frame == null)
            return null;

        IClientData clientData = frame.ExtraData;

        if (clientData == null)
            return null;

        frame.ExtraData = null;

        return clientData.Data as StringList;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method reads and clears the flag indicating that the current
    /// procedure invocation is the direct target of a running trampoline.
    /// Every procedure invocation must call this exactly once on entry.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may be null.
    /// </param>
    /// <returns>
    /// Non-zero when the current procedure invocation is the direct target
    /// of a running trampoline.
    /// </returns>
    public static bool CaptureTargetFlag(
        Interpreter interpreter /* in */
        )
    {
        if (interpreter == null)
            return false;

        //
        // NOTE: This runs on every procedure invocation; stay zero-cost
        //       (one field read) until a tailcall has actually happened.
        //
        State state = interpreter.tailcallState as State;

        if (state == null)
            return false;

        bool wasTarget = state.NextInvokeIsTrampolineTarget;
        state.NextInvokeIsTrampolineTarget = false;

        return wasTarget;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method runs after a procedure's call frame has been popped.
    /// When the procedure completed normally and recorded a tailcall, the
    /// tailcall command is executed at the caller's level and its outcome
    /// replaces the procedure's result.  When this procedure invocation was
    /// itself the direct target of a running trampoline, the tailcall is
    /// handed off to that trampoline instead of executing here, which keeps
    /// tailcall chains from growing the native stack.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="frame">
    /// The just-popped procedure call frame.  This parameter may be null.
    /// </param>
    /// <param name="wasTrampolineTarget">
    /// The value captured by <see cref="CaptureTargetFlag" /> when this
    /// procedure invocation started.
    /// </param>
    /// <param name="code">
    /// The procedure's return code; replaced by the tailcall outcome when a
    /// tailcall executes.
    /// </param>
    /// <param name="result">
    /// The procedure's result; replaced by the tailcall outcome when a
    /// tailcall executes.
    /// </param>
    public static void MaybeInvokePending(
        Interpreter interpreter,   /* in */
        ICallFrame frame,          /* in */
        bool wasTrampolineTarget,  /* in */
        ref ReturnCode code,       /* in, out */
        ref Result result          /* in, out */
        )
    {
        StringList pending = TakePending(frame);

        //
        // NOTE: A tailcall only fires when the procedure completes with a
        //       normal (Ok) result; error / break / continue outcomes
        //       discard it, matching stock Tcl.
        //
        if ((pending == null) || (code != ReturnCode.Ok))
            return;

        State state = GetState(interpreter);

        if (wasTrampolineTarget)
        {
            //
            // NOTE: A trampoline invoked this procedure directly; hand the
            //       tailcall back to it.  The procedure result is about to
            //       be replaced by the trampoline anyway.
            //
            state.Handoff = pending;
            return;
        }

        //
        // NOTE: This invocation site owns the trampoline: execute pending
        //       tailcalls until the chain is exhausted.  The current call
        //       frame here is the procedure's caller, so the command runs
        //       at the caller's level, matching stock Tcl.
        //
        while (pending != null)
        {
            state.NextInvokeIsTrampolineTarget = true;

            try
            {
                code = interpreter.EvaluateScript(
                    pending.ToString(), ref result);
            }
            finally
            {
                state.NextInvokeIsTrampolineTarget = false;
            }

            pending = state.Handoff;
            state.Handoff = null;

            //
            // NOTE: A handed-off tailcall only fires when its procedure
            //       completed normally.
            //
            if (code != ReturnCode.Ok)
                pending = null;
        }
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method resolves the tailcall target command name against the
    /// current namespace, matching stock Tcl, which resolves the command in
    /// the current namespace at <c>tailcall</c> time even though it later
    /// executes at the caller's level.  Unqualified names that do not exist
    /// in the current (non-global) namespace are left untouched, so normal
    /// resolution applies at execution time.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="name">
    /// The command name to resolve.
    /// </param>
    /// <returns>
    /// The resolved (possibly namespace-qualified) command name.
    /// </returns>
    public static string ResolveTargetName(
        Interpreter interpreter, /* in */
        string name              /* in */
        )
    {
        if (string.IsNullOrEmpty(name) ||
            name.StartsWith("::", System.StringComparison.Ordinal))
        {
            return name;
        }

        //
        // NOTE: Use the same resolution primitive as the standard
        //       [namespace which -command] sub-command; it returns the
        //       qualified absolute name, or empty when the command does
        //       not (yet) exist.
        //
        Result which = null;

        if ((NamespaceOps.Which(
                interpreter, null, name, NamespaceFlags.Command,
                ref which) == ReturnCode.Ok) &&
            !string.IsNullOrEmpty(which))
        {
            return which;
        }

        return name;
    }
    #endregion
}
