using System.Collections.Generic;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Components.Private;

/// <summary>
/// This class holds the per-interpreter state for the script-level
/// <c>trace</c> command: the command (rename / delete) and execution
/// (enter / leave / enterstep / leavestep) trace registries, the active
/// step-trace stack, and the reentrancy-suppression flag.  Variable traces
/// are not stored here; they attach directly to the traced variable via
/// the engine's own <see cref="ITrace" /> machinery.
/// </summary>
[ObjectId("c8e2f5a1-6b94-4d37-8a20-fd15b3c7e9a4")]
internal sealed class ScriptTraceState
{
    /// <summary>
    /// The execution traces, keyed by the absolute name of the traced
    /// command; each entry is most-recently-added first.
    /// </summary>
    public readonly Dictionary<string, List<ScriptExecutionTrace>>
        ExecutionTraces = new Dictionary<string, List<ScriptExecutionTrace>>();

    /// <summary>
    /// The command (rename / delete) traces, keyed by the absolute name of
    /// the traced command; each entry is most-recently-added first.
    /// </summary>
    public readonly Dictionary<string, List<ScriptCommandTrace>>
        CommandTraces = new Dictionary<string, List<ScriptCommandTrace>>();

    /// <summary>
    /// The stack of step traces belonging to commands currently executing;
    /// while non-empty, every nested command execution fires the enterstep
    /// and leavestep callbacks.
    /// </summary>
    public readonly List<ScriptExecutionTrace> ActiveStepTraces =
        new List<ScriptExecutionTrace>();

    /// <summary>
    /// Non-zero while a trace callback is executing; suppresses all nested
    /// execution-trace processing, matching stock Tcl.
    /// </summary>
    public bool FiringTrace;

    /// <summary>
    /// Non-zero once any variable trace has been added to the interpreter;
    /// gates the post-write re-read that lets "set" return the value as
    /// modified by write traces.  Never cleared (a cheap
    /// over-approximation).
    /// </summary>
    public bool AnyVariableTraces;
}

///////////////////////////////////////////////////////////////////////////

/// <summary>
/// One script-level execution trace: the callback command prefix plus the
/// subscribed operations.
/// </summary>
[ObjectId("d1a6b8c3-4f27-49e0-b5d8-62c9e0f4a713")]
internal sealed class ScriptExecutionTrace
{
    /// <summary>
    /// The Tcl command prefix invoked when the trace fires.
    /// </summary>
    public string CommandPrefix;

    /// <summary>
    /// Non-zero when subscribed to the given operation.
    /// </summary>
    public bool Enter;

    /// <summary>
    /// Non-zero when subscribed to the leave operation.
    /// </summary>
    public bool Leave;

    /// <summary>
    /// Non-zero when subscribed to the enterstep operation.
    /// </summary>
    public bool EnterStep;

    /// <summary>
    /// Non-zero when subscribed to the leavestep operation.
    /// </summary>
    public bool LeaveStep;

    /// <summary>
    /// The operations exactly as supplied to <c>trace add</c>, used by
    /// <c>trace info</c> and matched by <c>trace remove</c>.
    /// </summary>
    public string Operations;
}

///////////////////////////////////////////////////////////////////////////

/// <summary>
/// One script-level command trace: the callback command prefix plus the
/// subscribed operations (rename and/or delete).
/// </summary>
[ObjectId("e7c4d2f8-1a63-45b9-9e02-84f5a6d1c3b7")]
internal sealed class ScriptCommandTrace
{
    /// <summary>
    /// The Tcl command prefix invoked when the trace fires.
    /// </summary>
    public string CommandPrefix;

    /// <summary>
    /// Non-zero when subscribed to the rename operation.
    /// </summary>
    public bool Rename;

    /// <summary>
    /// Non-zero when subscribed to the delete operation.
    /// </summary>
    public bool Delete;

    /// <summary>
    /// The operations exactly as supplied to <c>trace add</c>, used by
    /// <c>trace info</c> and matched by <c>trace remove</c>.
    /// </summary>
    public string Operations;
}

///////////////////////////////////////////////////////////////////////////

/// <summary>
/// This class implements the script-level variable trace created by the
/// <c>trace add variable</c> (and legacy <c>trace variable</c>) commands.
/// It plugs into the engine's own variable trace machinery: the engine
/// fires it before a variable is read, set, or unset, and this class runs
/// the Tcl callback with the standard (name1, name2, op) arguments.
/// </summary>
[ObjectId("f2b9e6d4-8c15-4a70-b3f6-91d2c8e5a0b4")]
internal sealed class ScriptVariableTrace : _Traces.Default
{
    /// <summary>
    /// Constructs a script-level variable trace.
    /// </summary>
    public ScriptVariableTrace()
        : base(null)
    {
        // do nothing.
    }

    /// <summary>
    /// The Tcl command prefix invoked when the trace fires.
    /// </summary>
    public string CommandPrefix;

    /// <summary>
    /// Non-zero when subscribed to read operations.
    /// </summary>
    public bool Read;

    /// <summary>
    /// Non-zero when subscribed to write operations.
    /// </summary>
    public bool Write;

    /// <summary>
    /// Non-zero when subscribed to unset operations.
    /// </summary>
    public bool Unset;

    /// <summary>
    /// Non-zero when subscribed to array operations.  Accepted for
    /// compatibility; this port has no engine hook that corresponds to the
    /// Tcl "array" operation, so such traces never fire.
    /// </summary>
    public bool Array;

    /// <summary>
    /// Non-zero when the trace was created with the legacy
    /// <c>trace variable</c> command, whose callbacks receive the
    /// single-letter operation names (r / w / u / a).
    /// </summary>
    public bool Legacy;

    /// <summary>
    /// The operations exactly as supplied, used by <c>trace info</c> /
    /// <c>trace vinfo</c> and matched by <c>trace remove</c> /
    /// <c>trace vdelete</c>.
    /// </summary>
    public string Operations;

    /// <summary>
    /// The array element index this trace is restricted to, or null when
    /// the trace applies to the whole variable.
    /// </summary>
    public string ElementIndex;

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method executes the Tcl callback for a variable operation.
    /// Write callbacks observe the new value already stored (and may
    /// overwrite it); read callbacks may modify the value about to be
    /// returned; errors from read and write callbacks abort the operation
    /// with the standard "can't read/set" message; errors from unset
    /// callbacks are ignored, all matching stock Tcl.
    /// </summary>
    /// <param name="breakpointType">
    /// The variable operation that fired this trace.
    /// </param>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="traceInfo">
    /// The details of the variable operation.
    /// </param>
    /// <param name="result">
    /// Upon failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public override ReturnCode Execute(
        BreakpointType breakpointType, /* in */
        Interpreter interpreter,       /* in */
        ITraceInfo traceInfo,          /* in */
        ref Result result              /* out */
        )
    {
        if ((interpreter == null) || (traceInfo == null))
            return ReturnCode.Ok;

        string operation;

        switch (breakpointType)
        {
            case BreakpointType.BeforeVariableGet:
                if (!Read)
                    return ReturnCode.Ok;
                operation = Legacy ? "r" : "read";
                break;
            case BreakpointType.BeforeVariableSet:
                if (!Write)
                    return ReturnCode.Ok;
                operation = Legacy ? "w" : "write";
                break;
            case BreakpointType.BeforeVariableUnset:
                if (!Unset)
                    return ReturnCode.Ok;
                operation = Legacy ? "u" : "unset";
                break;
            default:
                return ReturnCode.Ok;
        }

        string index = traceInfo.Index;

        //
        // NOTE: An element-specific trace only fires for that element.
        //
        if ((ElementIndex != null) && (ElementIndex != index))
            return ReturnCode.Ok;

        //
        // NOTE: Unsetting a variable destroys its traces, matching stock
        //       Tcl: remove this trace before running the callback.  An
        //       element unset only destroys traces specific to that
        //       element.
        //
        if ((breakpointType == BreakpointType.BeforeVariableUnset) &&
            ((index == null) || (ElementIndex != null)))
        {
            IVariable variable = traceInfo.Variable;

            if (variable != null)
            {
                TraceList traces = variable.Traces;

                if (traces != null)
                    traces.Remove(this);
            }
        }

        string name = traceInfo.Name;

        if (name == null)
        {
            IVariable variable = traceInfo.Variable;

            if (variable != null)
                name = variable.Name;
        }

        //
        // NOTE: For a write, store the new value FIRST, so the callback
        //       observes it (and so the value remains set even when the
        //       callback fails), matching stock Tcl, which fires write
        //       traces after the store.
        //
        if (breakpointType == BreakpointType.BeforeVariableSet)
        {
            object newValue = traceInfo.NewValue;

            string newString = (newValue is string) ?
                (string)newValue : (newValue != null) ?
                newValue.ToString() : null;

            Result storeError = null;

            /* IGNORED */
            interpreter.SetVariableValue2(
                VariableFlags.NonTrace, name, index, newString,
                ref storeError);
        }

        string callback = CommandPrefix + " " + new StringList(
            name, (index != null) ? index : string.Empty,
            operation).ToString();

        Result localResult = null;

        ReturnCode code = interpreter.EvaluateScript(
            callback, ref localResult);

        if (code == ReturnCode.Ok)
        {
            //
            // NOTE: The callback may have modified the variable; make the
            //       engine's subsequent store / read reflect the current
            //       value.
            //
            Result currentValue = null;
            Result getError = null;

            if (interpreter.GetVariableValue2(
                    VariableFlags.NonTrace, name, index,
                    ref currentValue, ref getError) == ReturnCode.Ok)
            {
                if (breakpointType == BreakpointType.BeforeVariableSet)
                    traceInfo.NewValue = currentValue.ToString();
                else if (breakpointType == BreakpointType.BeforeVariableGet)
                    traceInfo.OldValue = currentValue.ToString();
            }

            return ReturnCode.Ok;
        }

        //
        // NOTE: Errors from unset callbacks are ignored, matching stock
        //       Tcl.
        //
        if (breakpointType == BreakpointType.BeforeVariableUnset)
            return ReturnCode.Ok;

        string fullName = ((index != null) && (name != null)) ?
            name + "(" + index + ")" : name;

        result = System.String.Format(
            "can't {0} \"{1}\": {2}",
            (breakpointType == BreakpointType.BeforeVariableGet) ?
                "read" : "set",
            fullName, localResult);

        return ReturnCode.Error;
    }
}

///////////////////////////////////////////////////////////////////////////

/// <summary>
/// This class implements the engine support shared by the script-level
/// <c>trace</c> command and the engine execution hook: state access,
/// name resolution, callback construction, and the enter / leave /
/// enterstep / leavestep and rename / delete firing logic.
/// </summary>
[ObjectId("a3d7f1c9-5e82-4b46-a1d0-7c38b9e2f6d5")]
internal static class ScriptTraceOps
{
    #region State Access
    /// <summary>
    /// This method returns the trace state for the specified interpreter,
    /// creating it on first use.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <returns>
    /// The trace state for the specified interpreter.
    /// </returns>
    public static ScriptTraceState GetOrCreateState(
        Interpreter interpreter /* in */
        )
    {
        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        if (state == null)
        {
            state = new ScriptTraceState();
            interpreter.scriptTraceState = state;
        }

        return state;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method returns the trace state for the specified interpreter
    /// when execution-trace processing should run for the current command;
    /// otherwise, null.  This is the per-command fast path: a single field
    /// read when no traces have ever been added.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may be null.
    /// </param>
    /// <returns>
    /// The active trace state, or null.
    /// </returns>
    public static ScriptTraceState GetActiveState(
        Interpreter interpreter /* in */
        )
    {
        if (interpreter == null)
            return null;

        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        if (state == null)
            return null;

        if (state.FiringTrace)
            return null;

        if ((state.ExecutionTraces.Count == 0) &&
            (state.ActiveStepTraces.Count == 0))
        {
            return null;
        }

        return state;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Variable Trace Helpers
    /// <summary>
    /// This method preserves any script-level variable traces (created by
    /// the <c>trace</c> command) when the engine rebuilds a variable's
    /// trace list, which it does whenever a previously undefined variable
    /// is (re)used.  The script traces stay at the front of the list so
    /// they keep firing most-recently-added first.
    /// </summary>
    /// <param name="oldTraces">
    /// The variable's previous trace list.  This parameter may be null.
    /// </param>
    /// <param name="newTraces">
    /// The rebuilt trace list.  This parameter may be null.
    /// </param>
    /// <returns>
    /// The merged trace list.
    /// </returns>
    public static TraceList MergeScriptTraces(
        TraceList oldTraces, /* in */
        TraceList newTraces  /* in */
        )
    {
        if (oldTraces == null)
            return newTraces;

        List<ITrace> preserved = null;

        foreach (ITrace trace in oldTraces)
        {
            if (trace is ScriptVariableTrace)
            {
                if (preserved == null)
                    preserved = new List<ITrace>();

                preserved.Add(trace);
            }
        }

        if (preserved == null)
            return newTraces;

        TraceList merged = (newTraces != null) ?
            newTraces : new TraceList();

        merged.InsertRange(0, preserved);

        return merged;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Variable Trace Gates
    /// <summary>
    /// This method reports whether any variable trace has ever been added
    /// to the interpreter; a single field read when none have.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may be null.
    /// </param>
    /// <returns>
    /// Non-zero when any variable trace has been added.
    /// </returns>
    public static bool HasAnyVariableTraces(
        Interpreter interpreter /* in */
        )
    {
        if (interpreter == null)
            return false;

        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        return (state != null) && state.AnyVariableTraces;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Frame Teardown
    /// <summary>
    /// This method fires the unset traces of a procedure call frame's
    /// variables as the frame is popped, matching stock Tcl, where local
    /// variables are unset (firing their traces) when the procedure
    /// returns.  Callback errors are ignored, like all unset traces.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may be null.
    /// </param>
    /// <param name="frame">
    /// The procedure call frame being popped.  This parameter may be null.
    /// </param>
    public static void FireFrameUnsetTraces(
        Interpreter interpreter, /* in */
        ICallFrame frame         /* in */
        )
    {
        if ((interpreter == null) || (frame == null))
            return;

        VariableDictionary variables = frame.Variables;

        if ((variables == null) || (variables.Count == 0))
            return;

        IVariable[] localVariables =
            new IVariable[variables.Count];

        variables.Values.CopyTo(localVariables, 0);

        foreach (IVariable variable in localVariables)
        {
            if (variable == null)
                continue;

            TraceList traces = variable.Traces;

            if ((traces == null) || (traces.Count == 0))
                continue;

            if (EntityOps.IsUndefined(variable))
                continue;

            foreach (ITrace current in traces.ToArray())
            {
                ScriptVariableTrace trace =
                    current as ScriptVariableTrace;

                if ((trace == null) || !trace.Unset ||
                    (trace.ElementIndex != null))
                {
                    continue;
                }

                traces.Remove(trace);

                string callback = trace.CommandPrefix + " " +
                    new StringList(variable.Name, string.Empty,
                        trace.Legacy ? "u" : "unset").ToString();

                Result localResult = null;

                /* IGNORED */
                interpreter.EvaluateScript(callback, ref localResult);
            }
        }
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Name Helpers
    /// <summary>
    /// This method converts a command name to its absolute (::-prefixed)
    /// form for use as a registry key.
    /// </summary>
    /// <param name="name">
    /// The command name.
    /// </param>
    /// <returns>
    /// The absolute name.
    /// </returns>
    public static string MakeAbsoluteName(
        string name /* in */
        )
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.StartsWith("::", System.StringComparison.Ordinal))
            return name;

        return "::" + name;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method resolves a command name for <c>trace add / remove /
    /// info</c> on commands and executions: the command must exist, and
    /// the absolute resolved name is returned.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="name">
    /// The command name to resolve.
    /// </param>
    /// <param name="resolvedName">
    /// Upon success, receives the absolute resolved name.
    /// </param>
    /// <param name="error">
    /// Upon failure, receives the standard "unknown command" message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode ResolveCommandName(
        Interpreter interpreter, /* in */
        string name,             /* in */
        ref string resolvedName, /* out */
        ref Result error         /* out */
        )
    {
        Result which = null;

        if ((NamespaceOps.Which(
                interpreter, null, name, NamespaceFlags.Command,
                ref which) == ReturnCode.Ok) &&
            !string.IsNullOrEmpty(which))
        {
            resolvedName = MakeAbsoluteName(which);
            return ReturnCode.Ok;
        }

        error = System.String.Format(
            "unknown command \"{0}\"", name);

        return ReturnCode.Error;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method splits a variable name of the form "name(index)" into
    /// its base name and element index components.
    /// </summary>
    /// <param name="name">
    /// The variable name, possibly including an element index.
    /// </param>
    /// <param name="baseName">
    /// Upon return, receives the base variable name.
    /// </param>
    /// <param name="index">
    /// Upon return, receives the element index, or null.
    /// </param>
    public static void SplitVariableName(
        string name,        /* in */
        out string baseName, /* out */
        out string index     /* out */
        )
    {
        baseName = name;
        index = null;

        if (string.IsNullOrEmpty(name) ||
            (name[name.Length - 1] != ')'))
        {
            return;
        }

        int open = name.IndexOf('(');

        if (open < 0)
            return;

        baseName = name.Substring(0, open);
        index = name.Substring(open + 1, name.Length - open - 2);
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Callback Helpers
    /// <summary>
    /// This method runs one trace callback script with the reentrancy
    /// suppression flag held.
    /// </summary>
    /// <param name="state">
    /// The trace state.
    /// </param>
    /// <param name="interpreter">
    /// The interpreter context.
    /// </param>
    /// <param name="prefix">
    /// The callback command prefix.
    /// </param>
    /// <param name="words">
    /// The argument words appended (as a properly quoted list) to the
    /// prefix.
    /// </param>
    /// <param name="result">
    /// Upon failure, receives the callback's error message.
    /// </param>
    /// <returns>
    /// The callback's return code.
    /// </returns>
    private static ReturnCode RunCallback(
        ScriptTraceState state,  /* in */
        Interpreter interpreter, /* in */
        string prefix,           /* in */
        StringList words,        /* in */
        ref Result result        /* out */
        )
    {
        string callback = prefix + " " + words.ToString();

        bool savedFiringTrace = state.FiringTrace;
        state.FiringTrace = true;

        try
        {
            return interpreter.EvaluateScript(callback, ref result);
        }
        finally
        {
            state.FiringTrace = savedFiringTrace;
        }
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Execution Trace Firing
    /// <summary>
    /// This method fires before a command executes: it runs the enterstep
    /// callbacks of any enclosing step-traced commands, then the enter
    /// callbacks of the command itself, and finally activates the
    /// command's own step traces.  An error from any callback aborts the
    /// command.
    /// </summary>
    /// <param name="state">
    /// The trace state.
    /// </param>
    /// <param name="interpreter">
    /// The interpreter context.
    /// </param>
    /// <param name="name">
    /// The name the command is being invoked as.
    /// </param>
    /// <param name="arguments">
    /// The full argument list of the invocation.  This parameter may be
    /// null.
    /// </param>
    /// <param name="stepCount">
    /// Upon return, receives the number of step traces activated (to be
    /// deactivated by the leave firing).
    /// </param>
    /// <param name="result">
    /// Upon failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode FireEnterTraces(
        ScriptTraceState state,  /* in */
        Interpreter interpreter, /* in */
        string name,             /* in */
        ArgumentList arguments,  /* in */
        ref int stepCount,       /* out */
        ref Result result        /* in, out */
        )
    {
        string commandString = (arguments != null) ?
            arguments.ToString() : name;

        //
        // NOTE: Fire enterstep callbacks for enclosing step-traced
        //       commands (most recently activated first).
        //
        if (state.ActiveStepTraces.Count > 0)
        {
            ScriptExecutionTrace[] active = state.ActiveStepTraces.ToArray();

            for (int index = active.Length - 1; index >= 0; index--)
            {
                ScriptExecutionTrace trace = active[index];

                if (!trace.EnterStep)
                    continue;

                Result localResult = null;

                if (RunCallback(state, interpreter, trace.CommandPrefix,
                        new StringList(commandString, "enterstep"),
                        ref localResult) != ReturnCode.Ok)
                {
                    result = localResult;
                    return ReturnCode.Error;
                }
            }
        }

        List<ScriptExecutionTrace> traces =
            FindExecutionTraces(state, name);

        if (traces != null)
        {
            foreach (ScriptExecutionTrace trace in traces.ToArray())
            {
                if (!trace.Enter)
                    continue;

                Result localResult = null;

                if (RunCallback(state, interpreter, trace.CommandPrefix,
                        new StringList(commandString, "enter"),
                        ref localResult) != ReturnCode.Ok)
                {
                    result = localResult;
                    return ReturnCode.Error;
                }
            }

            foreach (ScriptExecutionTrace trace in traces)
            {
                if (trace.EnterStep || trace.LeaveStep)
                {
                    state.ActiveStepTraces.Add(trace);
                    stepCount++;
                }
            }
        }

        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method fires after a command executes: it deactivates the
    /// command's step traces, runs its leave callbacks, and then runs the
    /// leavestep callbacks of any enclosing step-traced commands.  An
    /// error from any callback replaces the command's outcome.
    /// </summary>
    /// <param name="state">
    /// The trace state.
    /// </param>
    /// <param name="interpreter">
    /// The interpreter context.
    /// </param>
    /// <param name="name">
    /// The name the command was invoked as.
    /// </param>
    /// <param name="arguments">
    /// The full argument list of the invocation.  This parameter may be
    /// null.
    /// </param>
    /// <param name="stepCount">
    /// The number of step traces activated by the enter firing.
    /// </param>
    /// <param name="code">
    /// The command's return code; replaced on callback error.
    /// </param>
    /// <param name="result">
    /// The command's result; replaced on callback error.
    /// </param>
    public static void FireLeaveTraces(
        ScriptTraceState state,  /* in */
        Interpreter interpreter, /* in */
        string name,             /* in */
        ArgumentList arguments,  /* in */
        int stepCount,           /* in */
        ref ReturnCode code,     /* in, out */
        ref Result result        /* in, out */
        )
    {
        //
        // NOTE: Deactivate this command's step traces first.
        //
        if (stepCount > 0)
        {
            state.ActiveStepTraces.RemoveRange(
                state.ActiveStepTraces.Count - stepCount, stepCount);
        }

        string commandString = (arguments != null) ?
            arguments.ToString() : name;

        string codeString = ((int)code).ToString();
        string resultString = (result != null) ? result.ToString() : string.Empty;

        List<ScriptExecutionTrace> traces =
            FindExecutionTraces(state, name);

        if (traces != null)
        {
            foreach (ScriptExecutionTrace trace in traces.ToArray())
            {
                if (!trace.Leave)
                    continue;

                Result localResult = null;

                if (RunCallback(state, interpreter, trace.CommandPrefix,
                        new StringList(commandString, codeString,
                            resultString, "leave"),
                        ref localResult) != ReturnCode.Ok)
                {
                    code = ReturnCode.Error;
                    result = localResult;
                    return;
                }
            }
        }

        //
        // NOTE: Fire leavestep callbacks for enclosing step-traced
        //       commands.
        //
        if (state.ActiveStepTraces.Count > 0)
        {
            ScriptExecutionTrace[] active = state.ActiveStepTraces.ToArray();

            for (int index = active.Length - 1; index >= 0; index--)
            {
                ScriptExecutionTrace trace = active[index];

                if (!trace.LeaveStep)
                    continue;

                Result localResult = null;

                if (RunCallback(state, interpreter, trace.CommandPrefix,
                        new StringList(commandString, codeString,
                            resultString, "leavestep"),
                        ref localResult) != ReturnCode.Ok)
                {
                    code = ReturnCode.Error;
                    result = localResult;
                    return;
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method finds the execution traces registered for the specified
    /// invocation name, trying the absolute form of the name as well.
    /// </summary>
    /// <param name="state">
    /// The trace state.
    /// </param>
    /// <param name="name">
    /// The name the command is being invoked as.
    /// </param>
    /// <returns>
    /// The list of execution traces, or null.
    /// </returns>
    private static List<ScriptExecutionTrace> FindExecutionTraces(
        ScriptTraceState state, /* in */
        string name             /* in */
        )
    {
        if (name == null)
            return null;

        List<ScriptExecutionTrace> traces;

        if (state.ExecutionTraces.TryGetValue(name, out traces))
            return traces;

        string absoluteName = MakeAbsoluteName(name);

        if (!object.ReferenceEquals(absoluteName, name) &&
            state.ExecutionTraces.TryGetValue(absoluteName, out traces))
        {
            return traces;
        }

        return null;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Command Trace Firing (rename / delete)
    /// <summary>
    /// This method fires the command traces for a successful rename or
    /// delete performed by the <c>rename</c> command, then re-keys the
    /// registries so traces follow the renamed command (or are dropped
    /// with a deleted one).  Callback errors are ignored, matching stock
    /// Tcl.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may be null.
    /// </param>
    /// <param name="oldName">
    /// The old command name, as supplied to <c>rename</c>.
    /// </param>
    /// <param name="newName">
    /// The new command name, or an empty string when the command was
    /// deleted.
    /// </param>
    public static void HandleRenameOrDelete(
        Interpreter interpreter, /* in */
        string oldName,          /* in */
        string newName           /* in */
        )
    {
        if (interpreter == null)
            return;

        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        if (state == null)
            return;

        bool deleted = string.IsNullOrEmpty(newName);

        string absoluteOldName = MakeAbsoluteName(oldName);
        string absoluteNewName = deleted ?
            string.Empty : MakeAbsoluteName(newName);

        List<ScriptCommandTrace> traces;

        if (state.CommandTraces.TryGetValue(
                absoluteOldName, out traces) && !state.FiringTrace)
        {
            string operation = deleted ? "delete" : "rename";

            foreach (ScriptCommandTrace trace in traces.ToArray())
            {
                if (deleted ? !trace.Delete : !trace.Rename)
                    continue;

                Result localResult = null;

                /* IGNORED */
                RunCallback(state, interpreter, trace.CommandPrefix,
                    new StringList(absoluteOldName, absoluteNewName,
                        operation),
                    ref localResult);
            }
        }

        //
        // NOTE: Re-key both registries: traces follow a renamed command
        //       and are dropped with a deleted one.
        //
        if (state.CommandTraces.TryGetValue(absoluteOldName, out traces))
        {
            state.CommandTraces.Remove(absoluteOldName);

            if (!deleted)
                state.CommandTraces[absoluteNewName] = traces;
        }

        List<ScriptExecutionTrace> executionTraces;

        if (state.ExecutionTraces.TryGetValue(
                absoluteOldName, out executionTraces))
        {
            state.ExecutionTraces.Remove(absoluteOldName);

            if (!deleted)
                state.ExecutionTraces[absoluteNewName] = executionTraces;
        }
    }
    #endregion
}
