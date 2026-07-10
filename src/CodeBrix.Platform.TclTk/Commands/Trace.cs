using System;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Commands;

/// <summary>
/// This class implements the standard Tcl <c>trace</c> command, which
/// arranges for Tcl callbacks to fire when variables are read, written, or
/// unset (<c>trace add variable</c>), when commands are renamed or deleted
/// (<c>trace add command</c>), and when commands execute
/// (<c>trace add execution</c>, including step traces).  The legacy
/// <c>trace variable</c> / <c>vdelete</c> / <c>vinfo</c> forms are also
/// supported.  The semantics replicate stock Tcl 8.6, with two documented
/// exceptions: the variable "array" operation is accepted but never fires
/// (this port has no corresponding engine hook), and command
/// rename / delete traces fire only for the <c>rename</c> command (not for
/// deletions performed by other means).  This command is not present in
/// upstream Eagle; it was added by this port.
/// </summary>
[ObjectId("9c4e7a25-3d81-4f69-b0c7-e582a4d6f1b9")]
[CommandFlags(CommandFlags.Safe | CommandFlags.Standard)]
[ObjectGroup("control")]
internal sealed class Trace : Core
{
    /// <summary>
    /// Constructs an instance of the <c>trace</c> command.
    /// </summary>
    /// <param name="commandData">
    /// The data used to create and identify this command, such as its
    /// name and flags.  This parameter may be null.
    /// </param>
    public Trace(
        ICommandData commandData
        )
        : base(commandData)
    {
        // do nothing.
    }

    #region IEnsemble Members
    /// <summary>
    /// The set of sub-commands supported by this command.
    /// </summary>
    private readonly EnsembleDictionary subCommands = new EnsembleDictionary(new string[] {
        "add", "info", "remove", "variable", "vdelete", "vinfo"
    });

    /// <summary>
    /// Gets the dictionary of sub-commands supported by this command,
    /// used by the engine to dispatch and validate ensemble invocations.
    /// </summary>
    public override EnsembleDictionary SubCommands
    {
        get { return subCommands; }
    }
    #endregion

    #region IExecute Members
    /// <summary>
    /// This method executes the <c>trace</c> command.
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
    /// command name; element one is the sub-command name; the remaining
    /// elements supply the sub-command arguments.  This parameter should
    /// not be null.
    /// </param>
    /// <param name="result">
    /// Upon success, this contains the sub-command result.  Upon failure,
    /// this contains an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />, with details placed in
    /// <paramref name="result" />.
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

        if (arguments.Count < 2)
        {
            result = "wrong # args: should be \"trace option ?arg ...?\"";
            return ReturnCode.Error;
        }

        string subCommand = arguments[1];
        bool tried = false;

        ReturnCode code = ScriptOps.TryExecuteSubCommandFromEnsemble(
            interpreter, this, clientData, arguments, false,
            null, ref subCommand, ref tried, ref result);

        if ((code != ReturnCode.Ok) || tried)
            return code;

        switch (subCommand)
        {
            case "add":
            case "remove":
                {
                    bool adding = (subCommand == "add");

                    if (arguments.Count != 6)
                    {
                        result = String.Format(
                            "wrong # args: should be \"trace {0} type" +
                            " name opList command\"", subCommand);

                        return ReturnCode.Error;
                    }

                    string type = arguments[2];

                    switch (type)
                    {
                        case "variable":
                            return AddOrRemoveVariableTrace(
                                interpreter, arguments[3], arguments[4],
                                arguments[5], adding, false, ref result);
                        case "command":
                            return AddOrRemoveCommandTrace(
                                interpreter, arguments[3], arguments[4],
                                arguments[5], adding, ref result);
                        case "execution":
                            return AddOrRemoveExecutionTrace(
                                interpreter, arguments[3], arguments[4],
                                arguments[5], adding, ref result);
                        default:
                            result = String.Format(
                                "bad option \"{0}\": must be execution," +
                                " command, or variable", type);

                            return ReturnCode.Error;
                    }
                }
            case "info":
                {
                    if (arguments.Count != 4)
                    {
                        result = "wrong # args: should be \"trace info type name\"";
                        return ReturnCode.Error;
                    }

                    string type = arguments[2];

                    switch (type)
                    {
                        case "variable":
                            return InfoVariableTraces(
                                interpreter, arguments[3], false,
                                ref result);
                        case "command":
                            return InfoCommandTraces(
                                interpreter, arguments[3], ref result);
                        case "execution":
                            return InfoExecutionTraces(
                                interpreter, arguments[3], ref result);
                        default:
                            result = String.Format(
                                "bad option \"{0}\": must be execution," +
                                " command, or variable", type);

                            return ReturnCode.Error;
                    }
                }
            case "variable":
            case "vdelete":
                {
                    if (arguments.Count != 5)
                    {
                        result = String.Format(
                            "wrong # args: should be \"trace {0} name" +
                            " ops command\"", subCommand);

                        return ReturnCode.Error;
                    }

                    return AddOrRemoveVariableTrace(
                        interpreter, arguments[2], arguments[3],
                        arguments[4], (subCommand == "variable"), true,
                        ref result);
                }
            case "vinfo":
                {
                    if (arguments.Count != 3)
                    {
                        result = "wrong # args: should be \"trace vinfo name\"";
                        return ReturnCode.Error;
                    }

                    return InfoVariableTraces(
                        interpreter, arguments[2], true, ref result);
                }
            default:
                {
                    result = ScriptOps.BadSubCommand(
                        interpreter, "unknown or ambiguous", "subcommand",
                        subCommand, this, null, null);

                    return ReturnCode.Error;
                }
        }
    }
    #endregion

    #region Private Methods -- Variable Traces
    /// <summary>
    /// This method parses a variable operation list: either a Tcl list of
    /// operation words (modern form) or a string of single-letter
    /// operations (legacy form).
    /// </summary>
    private static ReturnCode ParseVariableOperations(
        Interpreter interpreter, /* in */
        string operations,       /* in */
        bool legacy,             /* in */
        out bool read,           /* out */
        out bool write,          /* out */
        out bool unset,          /* out */
        out bool array,          /* out */
        out string canonical,    /* out */
        ref Result error         /* out */
        )
    {
        read = false;
        write = false;
        unset = false;
        array = false;
        canonical = null;

        if (legacy)
        {
            foreach (char character in operations)
            {
                switch (character)
                {
                    case 'r':
                        read = true;
                        break;
                    case 'w':
                        write = true;
                        break;
                    case 'u':
                        unset = true;
                        break;
                    case 'a':
                        array = true;
                        break;
                    default:
                        error = String.Format(
                            "bad operations \"{0}\": should be one or" +
                            " more of rwua", operations);

                        return ReturnCode.Error;
                }
            }

            canonical = operations;
            return ReturnCode.Ok;
        }

        StringList list = null;

        if (ListOps.GetOrCopyOrSplitList(
                interpreter, (Result)operations, true, ref list,
                ref error) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        foreach (string operation in list)
        {
            switch (operation)
            {
                case "read":
                    read = true;
                    break;
                case "write":
                    write = true;
                    break;
                case "unset":
                    unset = true;
                    break;
                case "array":
                    array = true;
                    break;
                default:
                    error = String.Format(
                        "bad operation \"{0}\": must be array, read," +
                        " unset, or write", operation);

                    return ReturnCode.Error;
            }
        }

        canonical = list.ToString();
        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method adds or removes a variable trace.
    /// </summary>
    private static ReturnCode AddOrRemoveVariableTrace(
        Interpreter interpreter, /* in */
        string name,             /* in */
        string operations,       /* in */
        string commandPrefix,    /* in */
        bool adding,             /* in */
        bool legacy,             /* in */
        ref Result result        /* out */
        )
    {
        bool read;
        bool write;
        bool unset;
        bool array;
        string canonical;

        if (ParseVariableOperations(
                interpreter, operations, legacy, out read, out write,
                out unset, out array, out canonical,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        string baseName;
        string elementIndex;

        ScriptTraceOps.SplitVariableName(
            name, out baseName, out elementIndex);

        IVariable variable = null;

        if (interpreter.GetVariableViaResolversWithSplit(
                (ICallFrame)null, name, ref variable) != ReturnCode.Ok)
        {
            variable = null;
        }

        if (adding)
        {
            ScriptTraceOps.GetOrCreateState(
                interpreter).AnyVariableTraces = true;

            ScriptVariableTrace trace = new ScriptVariableTrace();

            trace.CommandPrefix = commandPrefix;
            trace.Read = read;
            trace.Write = write;
            trace.Unset = unset;
            trace.Array = array;
            trace.Legacy = legacy;
            trace.Operations = canonical;
            trace.ElementIndex = elementIndex;

            if (variable != null)
            {
                TraceList traces = variable.Traces;

                if (traces == null)
                {
                    traces = new TraceList();
                    variable.Traces = traces;
                }

                traces.Insert(0, trace);
            }
            else
            {
                //
                // NOTE: The variable does not exist yet: create it in an
                //       undefined state (so [info exists] still reports
                //       zero) carrying the trace, matching stock Tcl.
                //
                TraceList traces = new TraceList();

                traces.Add(trace);

                if (interpreter.AddVariable(
                        VariableFlags.Undefined, baseName, traces,
                        false, ref result) != ReturnCode.Ok)
                {
                    return ReturnCode.Error;
                }
            }

            result = string.Empty;
            return ReturnCode.Ok;
        }

        //
        // NOTE: Removing: silently succeed when the variable or a
        //       matching trace does not exist, matching stock Tcl.
        //
        if (variable != null)
        {
            TraceList traces = variable.Traces;

            if (traces != null)
            {
                for (int index = 0; index < traces.Count; index++)
                {
                    ScriptVariableTrace trace =
                        traces[index] as ScriptVariableTrace;

                    if ((trace != null) &&
                        (trace.Read == read) &&
                        (trace.Write == write) &&
                        (trace.Unset == unset) &&
                        (trace.Array == array) &&
                        (trace.Legacy == legacy) &&
                        (trace.ElementIndex == elementIndex) &&
                        (trace.CommandPrefix == commandPrefix))
                    {
                        traces.RemoveAt(index);
                        break;
                    }
                }
            }
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method lists the traces on a variable, most recent first, for
    /// <c>trace info variable</c> and <c>trace vinfo</c>.
    /// </summary>
    private static ReturnCode InfoVariableTraces(
        Interpreter interpreter, /* in */
        string name,             /* in */
        bool legacy,             /* in */
        ref Result result        /* out */
        )
    {
        string baseName;
        string elementIndex;

        ScriptTraceOps.SplitVariableName(
            name, out baseName, out elementIndex);

        IVariable variable = null;

        if (interpreter.GetVariableViaResolversWithSplit(
                (ICallFrame)null, name, ref variable) != ReturnCode.Ok)
        {
            result = string.Empty;
            return ReturnCode.Ok;
        }

        TraceList traces = variable.Traces;
        StringList list = new StringList();

        if (traces != null)
        {
            foreach (ITrace current in traces)
            {
                ScriptVariableTrace trace =
                    current as ScriptVariableTrace;

                if ((trace == null) ||
                    (trace.ElementIndex != elementIndex))
                {
                    continue;
                }

                //
                // NOTE: Stock Tcl renders the operations from the trace
                //       flags in canonical order: single letters for
                //       [trace vinfo], words for [trace info variable],
                //       regardless of which form created the trace.
                //
                string operations;

                if (legacy)
                {
                    operations =
                        (trace.Read ? "r" : "") +
                        (trace.Write ? "w" : "") +
                        (trace.Unset ? "u" : "") +
                        (trace.Array ? "a" : "");
                }
                else
                {
                    StringList words = new StringList();

                    if (trace.Array)
                        words.Add("array");

                    if (trace.Read)
                        words.Add("read");

                    if (trace.Unset)
                        words.Add("unset");

                    if (trace.Write)
                        words.Add("write");

                    operations = words.ToString();
                }

                StringList pair = new StringList();

                pair.Add(operations);
                pair.Add(trace.CommandPrefix);

                list.Add(pair.ToString());
            }
        }

        result = list.ToString();
        return ReturnCode.Ok;
    }
    #endregion

    #region Private Methods -- Command / Execution Traces
    /// <summary>
    /// This method adds or removes a command (rename / delete) trace.
    /// </summary>
    private static ReturnCode AddOrRemoveCommandTrace(
        Interpreter interpreter, /* in */
        string name,             /* in */
        string operations,       /* in */
        string commandPrefix,    /* in */
        bool adding,             /* in */
        ref Result result        /* out */
        )
    {
        string resolvedName = null;

        if (ScriptTraceOps.ResolveCommandName(
                interpreter, name, ref resolvedName,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        StringList list = null;

        if (ListOps.GetOrCopyOrSplitList(
                interpreter, (Result)operations, true, ref list,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        bool rename = false;
        bool delete = false;

        foreach (string operation in list)
        {
            switch (operation)
            {
                case "rename":
                    rename = true;
                    break;
                case "delete":
                    delete = true;
                    break;
                default:
                    result = String.Format(
                        "bad operation \"{0}\": must be delete or rename",
                        operation);

                    return ReturnCode.Error;
            }
        }

        ScriptTraceState state =
            ScriptTraceOps.GetOrCreateState(interpreter);

        System.Collections.Generic.List<ScriptCommandTrace> traces;

        if (adding)
        {
            if (!state.CommandTraces.TryGetValue(
                    resolvedName, out traces))
            {
                traces =
                    new System.Collections.Generic.List<ScriptCommandTrace>();

                state.CommandTraces[resolvedName] = traces;
            }

            ScriptCommandTrace trace = new ScriptCommandTrace();

            trace.CommandPrefix = commandPrefix;
            trace.Rename = rename;
            trace.Delete = delete;
            trace.Operations = list.ToString();

            traces.Insert(0, trace);

            result = string.Empty;
            return ReturnCode.Ok;
        }

        if (state.CommandTraces.TryGetValue(resolvedName, out traces))
        {
            for (int index = 0; index < traces.Count; index++)
            {
                ScriptCommandTrace trace = traces[index];

                if ((trace.Rename == rename) &&
                    (trace.Delete == delete) &&
                    (trace.CommandPrefix == commandPrefix))
                {
                    traces.RemoveAt(index);
                    break;
                }
            }

            if (traces.Count == 0)
                state.CommandTraces.Remove(resolvedName);
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method adds or removes an execution trace.
    /// </summary>
    private static ReturnCode AddOrRemoveExecutionTrace(
        Interpreter interpreter, /* in */
        string name,             /* in */
        string operations,       /* in */
        string commandPrefix,    /* in */
        bool adding,             /* in */
        ref Result result        /* out */
        )
    {
        string resolvedName = null;

        if (ScriptTraceOps.ResolveCommandName(
                interpreter, name, ref resolvedName,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        StringList list = null;

        if (ListOps.GetOrCopyOrSplitList(
                interpreter, (Result)operations, true, ref list,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        bool enter = false;
        bool leave = false;
        bool enterStep = false;
        bool leaveStep = false;

        foreach (string operation in list)
        {
            switch (operation)
            {
                case "enter":
                    enter = true;
                    break;
                case "leave":
                    leave = true;
                    break;
                case "enterstep":
                    enterStep = true;
                    break;
                case "leavestep":
                    leaveStep = true;
                    break;
                default:
                    result = String.Format(
                        "bad operation \"{0}\": must be enter, leave," +
                        " enterstep, or leavestep", operation);

                    return ReturnCode.Error;
            }
        }

        ScriptTraceState state =
            ScriptTraceOps.GetOrCreateState(interpreter);

        System.Collections.Generic.List<ScriptExecutionTrace> traces;

        if (adding)
        {
            if (!state.ExecutionTraces.TryGetValue(
                    resolvedName, out traces))
            {
                traces =
                    new System.Collections.Generic.List<ScriptExecutionTrace>();

                state.ExecutionTraces[resolvedName] = traces;
            }

            ScriptExecutionTrace trace = new ScriptExecutionTrace();

            trace.CommandPrefix = commandPrefix;
            trace.Enter = enter;
            trace.Leave = leave;
            trace.EnterStep = enterStep;
            trace.LeaveStep = leaveStep;
            trace.Operations = list.ToString();

            traces.Insert(0, trace);

            result = string.Empty;
            return ReturnCode.Ok;
        }

        if (state.ExecutionTraces.TryGetValue(resolvedName, out traces))
        {
            for (int index = 0; index < traces.Count; index++)
            {
                ScriptExecutionTrace trace = traces[index];

                if ((trace.Enter == enter) &&
                    (trace.Leave == leave) &&
                    (trace.EnterStep == enterStep) &&
                    (trace.LeaveStep == leaveStep) &&
                    (trace.CommandPrefix == commandPrefix))
                {
                    traces.RemoveAt(index);
                    break;
                }
            }

            if (traces.Count == 0)
                state.ExecutionTraces.Remove(resolvedName);
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method lists the command (rename / delete) traces on a
    /// command, most recent first.
    /// </summary>
    private static ReturnCode InfoCommandTraces(
        Interpreter interpreter, /* in */
        string name,             /* in */
        ref Result result        /* out */
        )
    {
        string resolvedName = null;

        if (ScriptTraceOps.ResolveCommandName(
                interpreter, name, ref resolvedName,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        StringList list = new StringList();

        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        System.Collections.Generic.List<ScriptCommandTrace> traces;

        if ((state != null) && state.CommandTraces.TryGetValue(
                resolvedName, out traces))
        {
            foreach (ScriptCommandTrace trace in traces)
            {
                StringList words = new StringList();

                if (trace.Rename)
                    words.Add("rename");

                if (trace.Delete)
                    words.Add("delete");

                StringList pair = new StringList();

                pair.Add(words.ToString());
                pair.Add(trace.CommandPrefix);

                list.Add(pair.ToString());
            }
        }

        result = list.ToString();
        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method lists the execution traces on a command, most recent
    /// first.
    /// </summary>
    private static ReturnCode InfoExecutionTraces(
        Interpreter interpreter, /* in */
        string name,             /* in */
        ref Result result        /* out */
        )
    {
        string resolvedName = null;

        if (ScriptTraceOps.ResolveCommandName(
                interpreter, name, ref resolvedName,
                ref result) != ReturnCode.Ok)
        {
            return ReturnCode.Error;
        }

        StringList list = new StringList();

        ScriptTraceState state =
            interpreter.scriptTraceState as ScriptTraceState;

        System.Collections.Generic.List<ScriptExecutionTrace> traces;

        if ((state != null) && state.ExecutionTraces.TryGetValue(
                resolvedName, out traces))
        {
            foreach (ScriptExecutionTrace trace in traces)
            {
                StringList words = new StringList();

                if (trace.Enter)
                    words.Add("enter");

                if (trace.Leave)
                    words.Add("leave");

                if (trace.EnterStep)
                    words.Add("enterstep");

                if (trace.LeaveStep)
                    words.Add("leavestep");

                StringList pair = new StringList();

                pair.Add(words.ToString());
                pair.Add(trace.CommandPrefix);

                list.Add(pair.ToString());
            }
        }

        result = list.ToString();
        return ReturnCode.Ok;
    }
    #endregion
}
