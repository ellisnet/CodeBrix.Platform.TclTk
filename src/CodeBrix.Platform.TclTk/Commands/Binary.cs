using System;
using System.Globalization;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Commands;

/// <summary>
/// This class implements the standard Tcl <c>binary</c> command, which
/// creates and inspects binary strings via the <c>format</c> and
/// <c>scan</c> sub-commands and converts binary data to and from textual
/// encodings via the <c>encode</c> and <c>decode</c> sub-commands
/// (<c>base64</c>, <c>hex</c>, and <c>uuencode</c>).  The semantics
/// replicate stock Tcl 8.6.  This command is not present in upstream
/// Eagle; it was added by this port.
/// </summary>
[ObjectId("f3c1e6a8-9d24-4b7a-8c05-d16f47b2e9d0")]
[CommandFlags(CommandFlags.Safe | CommandFlags.Standard)]
[ObjectGroup("string")]
internal sealed class Binary : Core
{
    /// <summary>
    /// Constructs an instance of the <c>binary</c> command.
    /// </summary>
    /// <param name="commandData">
    /// The data used to create and identify this command, such as its
    /// name and flags.  This parameter may be null.
    /// </param>
    public Binary(
        ICommandData commandData
        )
        : base(commandData)
    {
        // do nothing.
    }

    #region IEnsemble Members
    /// <summary>
    /// The set of sub-commands supported by this command, namely
    /// <c>decode</c>, <c>encode</c>, <c>format</c>, and <c>scan</c>.
    /// </summary>
    private readonly EnsembleDictionary subCommands = new EnsembleDictionary(new string[] {
        "decode", "encode", "format", "scan"
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
    /// This method executes the <c>binary</c> command.  It dispatches to
    /// the <c>format</c>, <c>scan</c>, <c>encode</c>, or <c>decode</c>
    /// sub-command.
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
        ReturnCode code = ReturnCode.Ok;

        if (interpreter != null)
        {
            if (arguments != null)
            {
                if (arguments.Count >= 2)
                {
                    string subCommand = arguments[1];
                    bool tried = false;

                    //
                    // NOTE: Non-strict resolution here: an unknown
                    //       sub-command falls through to the default case
                    //       below, which produces the Tcl-compatible
                    //       "unknown or ambiguous subcommand" message.
                    //
                    code = ScriptOps.TryExecuteSubCommandFromEnsemble(
                        interpreter, this, clientData, arguments, false,
                        null, ref subCommand, ref tried, ref result);

                    if ((code == ReturnCode.Ok) && !tried)
                    {
                        switch (subCommand)
                        {
                            case "format":
                                {
                                    if (arguments.Count >= 3)
                                    {
                                        code = BinaryOps.Format(
                                            interpreter, arguments[2],
                                            arguments, 3, ref result);
                                    }
                                    else
                                    {
                                        result = "wrong # args: should be \"binary format formatString ?arg ...?\"";
                                        code = ReturnCode.Error;
                                    }
                                    break;
                                }
                            case "scan":
                                {
                                    if (arguments.Count >= 4)
                                    {
                                        code = BinaryOps.Scan(
                                            interpreter, arguments[2],
                                            arguments[3], arguments, 4,
                                            ref result);
                                    }
                                    else
                                    {
                                        result = "wrong # args: should be \"binary scan value formatString ?varName ...?\"";
                                        code = ReturnCode.Error;
                                    }
                                    break;
                                }
                            case "encode":
                                {
                                    code = ExecuteEncode(
                                        interpreter, arguments, ref result);

                                    break;
                                }
                            case "decode":
                                {
                                    code = ExecuteDecode(
                                        arguments, ref result);

                                    break;
                                }
                            default:
                                {
                                    result = ScriptOps.BadSubCommand(
                                        interpreter, "unknown or ambiguous",
                                        "subcommand", subCommand, this, null,
                                        null);

                                    code = ReturnCode.Error;
                                    break;
                                }
                        }
                    }
                }
                else
                {
                    result = "wrong # args: should be \"binary subcommand ?arg ...?\"";
                    code = ReturnCode.Error;
                }
            }
            else
            {
                result = "invalid argument list";
                code = ReturnCode.Error;
            }
        }
        else
        {
            result = "invalid interpreter";
            code = ReturnCode.Error;
        }

        return code;
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// This method executes the <c>binary encode</c> sub-command,
    /// dispatching on the encoding name (<c>base64</c>, <c>hex</c>, or
    /// <c>uuencode</c>) and handling the <c>-maxlen</c> and
    /// <c>-wrapchar</c> options.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context this command is executing in.  This
    /// parameter may not be null.
    /// </param>
    /// <param name="arguments">
    /// The list of arguments for this invocation.
    /// </param>
    /// <param name="result">
    /// Upon success, this contains the encoded string.  Upon failure,
    /// this contains an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    private static ReturnCode ExecuteEncode(
        Interpreter interpreter, /* in */
        ArgumentList arguments,  /* in */
        ref Result result        /* out */
        )
    {
        if (arguments.Count < 3)
        {
            result = "wrong # args: should be \"binary encode subcommand ?arg ...?\"";
            return ReturnCode.Error;
        }

        string name = arguments[2];

        switch (name)
        {
            case "hex":
                {
                    if (arguments.Count != 4)
                    {
                        result = "wrong # args: should be \"binary encode hex data\"";
                        return ReturnCode.Error;
                    }

                    result = BinaryOps.EncodeHex(
                        BinaryOps.GetBytesFromString(arguments[3]));

                    return ReturnCode.Ok;
                }
            case "base64":
            case "uuencode":
                {
                    bool uuencode = (name == "uuencode");

                    string wrongNumArgs = String.Format(
                        "wrong # args: should be \"binary encode {0}" +
                        " ?-maxlen len? ?-wrapchar char? data\"", name);

                    if (arguments.Count < 4)
                    {
                        result = wrongNumArgs;
                        return ReturnCode.Error;
                    }

                    int maxLength = uuencode ? 61 : 0;
                    string wrapCharacters = "\n";
                    int lastIndex = arguments.Count - 1;

                    for (int index = 3; index < lastIndex; index += 2)
                    {
                        string option = arguments[index];

                        if ((index + 1) >= lastIndex)
                        {
                            result = wrongNumArgs;
                            return ReturnCode.Error;
                        }

                        if (option == "-maxlen")
                        {
                            Result error = null;

                            if (Value.GetInteger2(
                                    (IGetValue)arguments[index + 1],
                                    ValueFlags.AnyInteger |
                                    ValueFlags.AnySignedness,
                                    interpreter.InternalCultureInfo,
                                    ref maxLength,
                                    ref error) != ReturnCode.Ok)
                            {
                                result = String.Format(
                                    "expected integer but got \"{0}\"",
                                    arguments[index + 1]);

                                return ReturnCode.Error;
                            }
                        }
                        else if (option == "-wrapchar")
                        {
                            wrapCharacters = arguments[index + 1];
                        }
                        else
                        {
                            result = String.Format(
                                "bad option \"{0}\": must be -maxlen or" +
                                " -wrapchar", option);

                            return ReturnCode.Error;
                        }
                    }

                    if (uuencode)
                    {
                        if (maxLength < 5)
                        {
                            result = "line length out of range";
                            return ReturnCode.Error;
                        }

                        foreach (char character in wrapCharacters)
                        {
                            if (character >= (char)32)
                            {
                                result = "invalid wrapchar; will defeat decoding";
                                return ReturnCode.Error;
                            }
                        }
                    }
                    else if (maxLength < 0)
                    {
                        result = "line length out of range";
                        return ReturnCode.Error;
                    }

                    byte[] data = BinaryOps.GetBytesFromString(
                        arguments[lastIndex]);

                    result = uuencode ?
                        BinaryOps.EncodeUuencode(
                            data, maxLength, wrapCharacters) :
                        BinaryOps.EncodeBase64(
                            data, maxLength, wrapCharacters);

                    return ReturnCode.Ok;
                }
            default:
                {
                    result = String.Format(
                        "unknown subcommand \"{0}\": must be base64, hex," +
                        " or uuencode", name);

                    return ReturnCode.Error;
                }
        }
    }

    /// <summary>
    /// This method executes the <c>binary decode</c> sub-command,
    /// dispatching on the encoding name (<c>base64</c>, <c>hex</c>, or
    /// <c>uuencode</c>) and handling the <c>-strict</c> option.
    /// </summary>
    /// <param name="arguments">
    /// The list of arguments for this invocation.
    /// </param>
    /// <param name="result">
    /// Upon success, this contains the decoded binary string.  Upon
    /// failure, this contains an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    private static ReturnCode ExecuteDecode(
        ArgumentList arguments, /* in */
        ref Result result       /* out */
        )
    {
        if (arguments.Count < 3)
        {
            result = "wrong # args: should be \"binary decode subcommand ?arg ...?\"";
            return ReturnCode.Error;
        }

        string name = arguments[2];

        switch (name)
        {
            case "base64":
            case "hex":
            case "uuencode":
                {
                    bool strict = false;

                    if (arguments.Count == 5)
                    {
                        string option = arguments[3];

                        if (option != "-strict")
                        {
                            result = String.Format(
                                "bad option \"{0}\": must be -strict",
                                option);

                            return ReturnCode.Error;
                        }

                        strict = true;
                    }
                    else if (arguments.Count != 4)
                    {
                        result = String.Format(
                            "wrong # args: should be \"binary decode {0}" +
                            " ?options? data\"", name);

                        return ReturnCode.Error;
                    }

                    string data = arguments[arguments.Count - 1];

                    switch (name)
                    {
                        case "hex":
                            return BinaryOps.DecodeHex(
                                data, strict, ref result);
                        case "base64":
                            return BinaryOps.DecodeBase64(
                                data, strict, ref result);
                        default:
                            return BinaryOps.DecodeUuencode(
                                data, strict, ref result);
                    }
                }
            default:
                {
                    result = String.Format(
                        "unknown subcommand \"{0}\": must be base64, hex," +
                        " or uuencode", name);

                    return ReturnCode.Error;
                }
        }
    }
    #endregion
}
