using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>Shared registration and argument-parsing helpers for the bridge modules.</summary>
internal static class BridgeRegistrar
{
    /// <summary>Adds a bridge command to the interpreter (throws on failure).</summary>
    internal static void Add(BridgeContext ctx, string name, Func<string[], string> handler)
    {
        long token = AddRemovable(ctx, name, handler);
        _ = token;
    }

    /// <summary>
    /// Adds a bridge command and returns its removal token. When the engine
    /// already ships a command of that name (e.g. its own event-loop
    /// <c>update</c>/<c>after</c>), the original is renamed aside to
    /// <c>::tk::__orig_&lt;name&gt;</c> — Tk semantics supersede, but the
    /// engine command stays reachable.
    /// </summary>
    internal static long AddRemovable(BridgeContext ctx, string name, Func<string[], string> handler)
    {
        var command = new BridgeCommand(
            new CommandData(
                name, "tk", null, null,
                typeof(BridgeCommand).FullName, CommandFlags.None, null, 0),
            handler);

        long token = 0;
        Result error = null;
        if (ctx.Interpreter.AddCommand(command, null, ref token, ref error) != ReturnCode.Ok)
        {
            Result renameError = null;
            ctx.Interpreter.EvaluateScript(
                "namespace eval ::tk {}; rename {" + name + "} {::tk::__orig_" + name + "}",
                ref renameError);

            error = null;
            if (ctx.Interpreter.AddCommand(command, null, ref token, ref error) != ReturnCode.Ok)
            {
                throw new InvalidOperationException(
                    "could not register Tk command \"" + name + "\": " + error);
            }
        }

        return token;
    }

    /// <summary>
    /// Parses <c>-option value</c> word pairs starting at <paramref name="start"/>,
    /// producing the standard Tk errors for stray or unpaired words.
    /// </summary>
    internal static Dictionary<string, string> ParseOptionPairs(string[] words, int start)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = start; index < words.Length; index += 2)
        {
            string name = words[index];
            if (name.Length == 0 || name[0] != '-')
            {
                throw new TkTclError("unknown option \"" + name + "\"");
            }
            if (index + 1 >= words.Length)
            {
                throw new TkTclError("value for \"" + name + "\" missing");
            }
            options[name] = words[index + 1];
        }
        return options;
    }

    /// <summary>Joins words into the standard wrong-#-args error.</summary>
    internal static TkTclError WrongArgs(string usage)
    {
        return new TkTclError("wrong # args: should be \"" + usage + "\"");
    }
}
