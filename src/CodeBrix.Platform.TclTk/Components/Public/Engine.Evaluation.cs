/*
 * Engine.Evaluation.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Evaluation Methods" region group) so that no
 * single source file grows unmanageably large.  See Engine.cs for the
 * type-level documentation and the [ObjectId] declaration.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

#if NETWORK
using System.Net;
#endif

using System.Reflection;
using System.Text;
using System.Threading;

#if XML
using System.Xml;
#endif

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public.Delegates;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Private;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Private;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using RSCD = CodeBrix.Platform.TclTk._Components.Private.ReadScriptClientData;
using GSCD = CodeBrix.Platform.TclTk._Components.Private.GetScriptClientData;
using SharedStringOps = CodeBrix.Platform.TclTk._Components.Shared.StringOps;

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Components.Public //was previously: Eagle._Components.Public;
{
    public static partial class Engine
    {
        #region Evaluation Methods
        #region Evaluation Cleanup Methods
        /// <summary>
        /// This method attempts to clean up any object references held by the
        /// specified interpreter that are no longer needed, complaining (via
        /// <see cref="DebugOps" />) if the cleanup fails.  The interpreter
        /// engine lock is acquired for the duration of the operation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose object references should be cleaned up.  If
        /// this parameter is null, no action is taken.
        /// </param>
        /// <returns>
        /// Non-zero if the object references were successfully cleaned up;
        /// otherwise, zero (for example, when the interpreter is null, is not
        /// usable, the lock could not be acquired, or the cleanup failed).
        /// </returns>
        private static bool CleanupObjectReferencesOrComplain(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return false;

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter))
                        return false;

                    ReturnCode cleanupCode;
                    Result cleanupError = null;

                    cleanupCode = interpreter.CleanupObjectReferences(
                        false, ref cleanupError);

                    if (cleanupCode == ReturnCode.Ok)
                    {
                        return true;
                    }
                    else
                    {
                        DebugOps.Complain(
                            interpreter, cleanupCode, cleanupError);

                        return false;
                    }
                }
                else
                {
                    TraceOps.LockTrace(
                        "CleanupObjectReferencesOrComplain",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    return false;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method attempts to clean up any namespaces of the specified
        /// interpreter that are pending deletion, complaining (via
        /// <see cref="DebugOps" />) if the cleanup fails.  The interpreter
        /// engine lock is acquired for the duration of the operation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose pending namespaces should be cleaned up.  If
        /// this parameter is null, no action is taken.
        /// </param>
        /// <returns>
        /// Non-zero if the namespaces were successfully cleaned up; otherwise,
        /// zero (for example, when the interpreter is null, is not usable, the
        /// lock could not be acquired, or the cleanup failed).
        /// </returns>
        internal static bool CleanupNamespacesOrComplain(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return false;

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter))
                        return false;

                    ReturnCode cleanupCode;
                    Result cleanupResult = null;

                    cleanupCode = interpreter.CleanupNamespaces(
                        VariableFlags.None, false, ref cleanupResult);

                    if (cleanupCode == ReturnCode.Ok)
                    {
                        return true;
                    }
                    else
                    {
                        DebugOps.Complain(
                            interpreter, cleanupCode, cleanupResult);

                        return false;
                    }
                }
                else
                {
                    TraceOps.LockTrace(
                        "CleanupNamespacesOrComplain",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    return false;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation Exit-Hook Methods
        /// <summary>
        /// This method performs the bookkeeping that must occur when a script
        /// evaluation completes (i.e. "exits").  It checks for and handles any
        /// applicable exit breakpoints, raises script completion or evaluation
        /// notifications, and -- when the appropriate nesting level has been
        /// reached -- cleans up object references and namespaces, resets the
        /// stack overflow and debugger state, populates error information, and
        /// resets the result return code as needed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which the script was evaluated.  This
        /// parameter may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the script
        /// that was evaluated.  This parameter may be null.
        /// </param>
        /// <param name="currentLine">
        /// The current script line number at the point of evaluation exit.
        /// </param>
        /// <param name="text">
        /// The text of the script that was evaluated.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within <paramref name="text" /> that
        /// was evaluated.
        /// </param>
        /// <param name="characters">
        /// The number of characters within <paramref name="text" /> that were
        /// evaluated.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that were in effect for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that were in effect for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags that were in effect for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags that were in effect for the evaluation.
        /// </param>
        /// <param name="code">
        /// The return code produced by the evaluation.  This value may be
        /// modified by an exit breakpoint.
        /// </param>
        /// <param name="result">
        /// The result produced by the evaluation.  This value may be modified
        /// by an exit breakpoint or by error information processing.
        /// </param>
        /// <param name="errorLine">
        /// The script line number associated with any error.  It is used when
        /// populating the error information for the result.
        /// </param>
        /// <returns>
        /// The (possibly modified) return code for the completed evaluation.
        /// </returns>
        private static ReturnCode EvaluateExited(
            Interpreter interpreter,             /* in */
            string fileName,                     /* in */
            int currentLine,                     /* in */
            string text,                         /* in */
            int startIndex,                      /* in */
            int characters,                      /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref ReturnCode code,                 /* in, out */
            ref Result result,                   /* in, out */
            ref int errorLine                    /* in, out */
            )
        {
#if DEBUGGER && DEBUGGER_ENGINE
            BreakpointType breakpointType =
                BreakpointType.Exit | BreakpointType.Evaluate;

            if (DebuggerOps.CanHitBreakpoints(
                    interpreter, engineFlags, breakpointType))
            {
                ReturnCode oldCode = code;

                Result oldResult = Result.Copy(
                    result, ResultFlags.CopyObject); /* COPY */

                code = CheckBreakpoints(
                    code, breakpointType, null,
                    null, null, engineFlags,
                    substitutionFlags, eventFlags,
                    expressionFlags, null, null,
                    interpreter, null, null,
                    ref result);

                //
                // TODO: What is the purpose of this if statement and the
                //       associated call to DebugOps.Complain?
                //
                // NOTE: It appears that the purpose of this check is to verify
                //       that the breakpoint, if any, did not cause the overall
                //       result of this script evaluation to be changed.
                //
                if ((code != oldCode) || !Result.Equals(result, oldResult))
                    DebugOps.Complain(interpreter, code, result);
            }
#endif

            if (interpreter != null)
            {
                int scriptLevels = interpreter.ScriptLevels;
                int levels = interpreter.InternalLevels;
                int previousLevels = interpreter.PreviousLevels;

#if NOTIFY
                if (!EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Script,
                        (levels == 0) ? NotifyFlags.Completed : NotifyFlags.Evaluated,
                        //
                        // NOTE: We do not include the code in the data triplet
                        //       directly because after an evaluation it is now
                        //       guaranteed to be accessible via the ReturnCode
                        //       property of the Result object.
                        //
                        // BUGBUG: In order to use this class for notification
                        //         parameters, it really should probably be
                        //         made public.
                        //
                        new ObjectList(fileName, currentLine, text, startIndex, characters),
                        interpreter, null, null, null, ref result);
                }
#endif

                if (scriptLevels == 0)
                {
                    //
                    // NOTE: Cleanup any object references that may no longer
                    //       be needed (e.g. temporary).
                    //
                    /* IGNORED */
                    CleanupObjectReferencesOrComplain(interpreter);
                }

                if (levels == 0)
                {
                    //
                    // NOTE: If appropriate, populate the error information for
                    //       the current result.
                    //
                    interpreter.MaybePopulateResultErrorProperties(
                        "current", code, result, errorLine);

                    //
                    // NOTE: Cleanup any namespaces that are pending deletion
                    //       or complain if we are unable to.
                    //
                    /* IGNORED */
                    CleanupNamespacesOrComplain(interpreter);

                    //
                    // NOTE: Reset the stack overflow flag for the interpreter
                    //       now, if necessary.
                    //
                    /* IGNORED */
                    CheckStackOverflow(interpreter);

#if DEBUGGER
                    //
                    // NOTE: Reset the skip-ready flag for the interpreter.
                    //
                    /* IGNORED */
                    CheckIsDebuggerExiting(interpreter);
#endif

                    //
                    // NOTE: Reset the number of errorInfo frames to zero.
                    //
                    interpreter.ErrorFrames = 0;
                }
                else if (levels == previousLevels)
                {
                    //
                    // NOTE: Reset the stack overflow flag for the interpreter
                    //       now, if necessary.
                    //
                    /* IGNORED */
                    CheckStackOverflow(interpreter);

#if DEBUGGER
                    //
                    // NOTE: Reset the skip-ready flag for the interpreter.
                    //
                    /* IGNORED */
                    CheckIsDebuggerExiting(interpreter);
#endif
                }
            }

            //
            // NOTE: Finally, reset the result return code, if necessary.
            //
            /* IGNORED */
            ResetReturnCode(interpreter, result,
                EngineFlagOps.HasResetReturnCode(engineFlags));

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation Helper Methods
        /// <summary>
        /// This method determines whether a null argument should be used in
        /// place of the specified result, based on its flags.
        /// </summary>
        /// <param name="result">
        /// The result to examine.  This parameter may be null.
        /// </param>
        /// <returns>
        /// Non-zero if the result is non-null and has the
        /// <see cref="ResultFlags.ForceNullArgument" /> flag set; otherwise,
        /// zero.
        /// </returns>
        private static bool ShouldUseNullArgument(
            Result result
            )
        {
            if (result == null)
                return false;

            return FlagOps.HasFlags(
                result.Flags, ResultFlags.ForceNullArgument, true);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether a null result should be used in
        /// place of the specified result, based on its flags.
        /// </summary>
        /// <param name="result">
        /// The result to examine.  This parameter may be null.
        /// </param>
        /// <returns>
        /// Non-zero if the result is non-null and has the
        /// <see cref="ResultFlags.ForceNullResult" /> flag set; otherwise,
        /// zero.
        /// </returns>
        private static bool ShouldUseNullResult(
            Result result
            )
        {
            if (result == null)
                return false;

            return FlagOps.HasFlags(
                result.Flags, ResultFlags.ForceNullResult, true);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

#if DEBUGGER && DEBUGGER_BREAKPOINTS
        /// <summary>
        /// This method determines whether the specified interpreter is
        /// currently tracking argument source locations, which is used by the
        /// debugger to associate breakpoints with command arguments.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to examine.  If this parameter is null, the result
        /// is zero.
        /// </param>
        /// <returns>
        /// Non-zero if the interpreter is non-null and is tracking argument
        /// source locations; otherwise, zero.
        /// </returns>
        internal static bool HasArgumentLocation(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return false;

            return interpreter.HasArgumentLocation();
        }
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method advances to and retrieves the next token from the
        /// specified parser state.  When a current token is supplied via
        /// <paramref name="token" />, the token index is first advanced past
        /// it (and its components) before the next token is fetched.
        /// </summary>
        /// <param name="parseState">
        /// The parser state containing the tokens.  If this parameter is null,
        /// an error is returned.
        /// </param>
        /// <param name="token">
        /// On input, the current token (may be null to begin at the start); on
        /// output, the token located at the resulting token index.
        /// </param>
        /// <param name="tokenIndex">
        /// On input, the current token index; on output, the index of the
        /// returned token.
        /// </param>
        /// <param name="error">
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if a token was retrieved; otherwise,
        /// <see cref="ReturnCode.Error" /> (for example, when the parser state
        /// is null or the resulting token index is out of range).
        /// </returns>
        private static ReturnCode GetToken(
            IParseState parseState, /* in */
            ref IToken token,       /* in, out */
            ref int tokenIndex,     /* in, out */
            ref Result error        /* out */
            )
        {
            if (parseState != null)
            {
                if (token != null)
                    tokenIndex += (token.Components + 1);

                if ((tokenIndex >= 0) &&
                    (tokenIndex < parseState.Tokens.Count))
                {
                    token = parseState.Tokens[tokenIndex];

                    return ReturnCode.Ok;
                }
                else
                {
                    error = "invalid token index";
                }
            }
            else
            {
                error = "invalid parser state";
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method converts the string representation of the specified
        /// value into a boolean, using fast parsing that accepts any numeric
        /// value in any supported radix.
        /// </summary>
        /// <param name="getValue">
        /// The value to convert.  Its string representation is parsed as a
        /// boolean.
        /// </param>
        /// <param name="cultureInfo">
        /// The culture used when parsing the value.  This parameter may be
        /// null to use the default culture behavior.
        /// </param>
        /// <param name="value">
        /// Upon success, this receives the parsed boolean value.
        /// </param>
        /// <param name="error">
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the value was converted to a
        /// boolean; otherwise, <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ToBoolean(
            IGetValue getValue,
            CultureInfo cultureInfo,
            ref bool value,
            ref Result error
            )
        {
            Result localError = null; /* NOT USED */

            if (Value.GetBoolean3(
                    getValue, ValueFlags.AnyNumberAnyRadix | ValueFlags.Fast,
                    cultureInfo, ref value, ref localError) == ReturnCode.Ok)
            {
                return ReturnCode.Ok;
            }
            else
            {
                error = String.Format(
                    "expected boolean value but got {0}", (getValue != null) ?
                    FormatOps.DisplayName(getValue.String) : FormatOps.DisplayNull);
            }

            return ReturnCode.Error;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (IToken) Methods
#if DEBUGGER && DEBUGGER_BREAKPOINTS
        /// <summary>
        /// This method widens the supplied script line range so that it
        /// encompasses the start and end lines of the specified token.  Any
        /// unknown line values are taken from the token, and known token lines
        /// extend the range only when they fall outside it.
        /// </summary>
        /// <param name="token">
        /// The token whose start and end lines are used to adjust the range.
        /// </param>
        /// <param name="startLine">
        /// On input, the current lowest line number; on output, the lowest
        /// line number including the token.
        /// </param>
        /// <param name="endLine">
        /// On input, the current highest line number; on output, the highest
        /// line number including the token.
        /// </param>
        private static void CheckTokenLines(
            IToken token,
            ref int startLine,
            ref int endLine
            )
        {
            if ((startLine == Parser.UnknownLine) ||
                ((token.StartLine != Parser.UnknownLine) &&
                (token.StartLine < startLine)))
            {
                startLine = token.StartLine;
            }

            if ((endLine == Parser.UnknownLine) ||
                ((token.EndLine != Parser.UnknownLine) &&
                (token.EndLine > endLine)))
            {
                endLine = token.EndLine;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a contiguous run of parser-state tokens in
        /// the context of the given interpreter, concatenating their results.
        /// This overload does not report the script line range; it delegates
        /// to the overload that does, discarding that information.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the tokens.
        /// </param>
        /// <param name="parseState">
        /// The parser state that contains the tokens to evaluate.
        /// </param>
        /// <param name="startTokenIndex">
        /// The index of the first token to evaluate.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="tokenCount">
        /// The number of tokens to evaluate, beginning at
        /// <paramref name="startTokenIndex" />.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the concatenated result produced by the
        /// tokens.  Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateTokens(
            Interpreter interpreter,
            IParseState parseState,
            int startTokenIndex,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            int tokenCount,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result
            )
        {
            int startLine = Parser.UnknownLine;
            int endLine = Parser.UnknownLine;

            return EvaluateTokens(
                interpreter, parseState, startTokenIndex,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                tokenCount, engineFlags, substitutionFlags,
                eventFlags, expressionFlags, sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref startLine, ref endLine, ref result);
        }
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method retrieves the value of a named interpreter variable (or
        /// array element) for use while evaluating a token.  The value is
        /// fetched without forcing string conversion of object values.  When
        /// the lookup fails, or yields a null value or error, an empty string
        /// is substituted so callers do not misinterpret it as a request to
        /// use the entire parse-state text.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context whose variable value is retrieved.
        /// </param>
        /// <param name="varName">
        /// The name of the variable (or array) to retrieve.
        /// </param>
        /// <param name="varIndex">
        /// The array element index to retrieve, or null when retrieving a
        /// scalar variable.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the variable value (or an empty string
        /// when the value is null).  Upon failure, this contains the error
        /// message (or an empty string when none is available).
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the variable value was retrieved;
        /// otherwise, a non-Ok value.
        /// </returns>
        private static ReturnCode GetTokenVariableValue(
            Interpreter interpreter,
            string varName,
            string varIndex,
            ref Result result
            )
        {
            ReturnCode code;
            Result value = null;
            Result error = null;

            //
            // BUGFIX: Passing the same variable for both reference parameters
            //         here causes serious problems with the cross-AppDomain
            //         marshalling; therefore, avoid doing that.
            //
            code = interpreter.GetVariableValue2(
                VariableFlags.SkipToString, varName, varIndex, ref value,
                ref error);

            //
            // BUGFIX: Callers of this method cannot handle a null value or
            //         error message because they will interpret that to
            //         mean "use all the parse state text"; therefore, fix
            //         it up to be an empty string instead.
            //
            if (code == ReturnCode.Ok)
                result = (value != null) ? value : (Result)String.Empty;
            else
                result = (error != null) ? error : (Result)String.Empty;

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a contiguous run of parser-state tokens in
        /// the context of the given interpreter, concatenating their results
        /// into a single result.  It additionally reports the lowest and
        /// highest script line numbers spanned by the evaluated tokens.  This
        /// is the core token-evaluation routine to which the other overload
        /// delegates.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the tokens.
        /// </param>
        /// <param name="parseState">
        /// The parser state that contains the tokens to evaluate.
        /// </param>
        /// <param name="startTokenIndex">
        /// The index of the first token to evaluate.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="tokenCount">
        /// The number of tokens to evaluate, beginning at
        /// <paramref name="startTokenIndex" />.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="startLine">
        /// On input, the current lowest line number; on output, the lowest
        /// script line number spanned by the evaluated tokens.
        /// </param>
        /// <param name="endLine">
        /// On input, the current highest line number; on output, the highest
        /// script line number spanned by the evaluated tokens.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the concatenated result produced by the
        /// tokens.  Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
#if DEBUGGER && DEBUGGER_BREAKPOINTS
        private
#else
        internal
#endif
        static ReturnCode EvaluateTokens(
            Interpreter interpreter,
            IParseState parseState,
            int startTokenIndex,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            int tokenCount,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
            ref int startLine,
            ref int endLine,
#endif
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;
            string text = parseState.Text;
            CommandBuilder evalResult = null;
            int startTokenCount = tokenCount;

            for (int tokenIndex = startTokenIndex;
                    tokenCount > 0;
                    tokenCount--, tokenIndex++)
            {
                int index = Index.Invalid;
                int length = 0;
                int thisTokenCount;
                IToken token = parseState.Tokens[tokenIndex];
                Result localResult = null;

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                CheckTokenLines(token, ref startLine, ref endLine);
#endif

                switch (token.Type)
                {
                    case TokenType.Text:
                        {
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            BreakpointType breakpointType = BreakpointType.Token |
                                BreakpointType.BeforeText;

                            if (DebuggerOps.CanHitBreakpoints(interpreter,
                                    engineFlags, breakpointType))
                            {
                                code = CheckBreakpoints(
                                    code, breakpointType, null,
                                    token, null, engineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, null, null,
                                    interpreter, null, null,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto done;
                            }
#endif

                            index = token.Start;
                            length = token.Length;
                            thisTokenCount = 1;

                            break;
                        }
                    case TokenType.Backslash:
                        {
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            BreakpointType breakpointType = BreakpointType.Token |
                                BreakpointType.BeforeBackslash;

                            if (DebuggerOps.CanHitBreakpoints(interpreter,
                                    engineFlags, breakpointType))
                            {
                                code = CheckBreakpoints(
                                    code, breakpointType, null,
                                    token, null, engineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, null, null,
                                    interpreter, null, null,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto done;
                            }
#endif

                            char? character1 = null;
                            char? character2 = null;

                            Parser.ParseBackslash(
                                text, token.Start, token.Length,
                                ref character1, ref character2);

                            localResult = Result.FromCharacters(character1, character2);
                            thisTokenCount = 1;

                            break;
                        }
                    case TokenType.Command:
                        {
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            BreakpointType breakpointType = BreakpointType.Token |
                                BreakpointType.BeforeCommand;

                            if (DebuggerOps.CanHitBreakpoints(interpreter,
                                    engineFlags, breakpointType))
                            {
                                code = CheckBreakpoints(
                                    code, breakpointType, null,
                                    token, null, engineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, null, null,
                                    interpreter, null, null,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto done;
                            }
#endif

                            code = CheckEvents(
                                interpreter, engineFlags, substitutionFlags,
                                eventFlags, expressionFlags, ref localResult);

                            if (code == ReturnCode.Ok)
                                code = EvaluateScript(
                                    interpreter, token.FileName, token.StartLine, text,
                                    token.Start + 1, token.Length - 2, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags,
#if RESULT_LIMITS
                                    executeResultLimit, nestedResultLimit,
#endif
                                    sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                    argumentLocation,
#endif
                                    ref localResult);

                            if (code != ReturnCode.Ok)
                            {
                                result = localResult;
                                goto done;
                            }

                            thisTokenCount = 1;

                            break;
                        }
                    case TokenType.Variable:
                    case TokenType.VariableNameOnly:
                        {
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            BreakpointType breakpointType = BreakpointType.Token |
                                BreakpointType.BeforeVariableGet;

                            if (DebuggerOps.CanHitBreakpoints(interpreter,
                                    engineFlags, breakpointType))
                            {
                                code = CheckBreakpoints(
                                    code, breakpointType, null,
                                    token, null, engineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, null, null,
                                    interpreter, null, null,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto done;
                            }
#endif

                            string varName;
                            string varIndex = null;

                            if (token.Components > 1)
                            {
                                code = CheckEvents(
                                    interpreter, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref localResult);

                                if (code == ReturnCode.Ok)
                                {
                                    code = EvaluateTokens(
                                        interpreter, parseState,
                                        tokenIndex + 2,
#if RESULT_LIMITS
                                        executeResultLimit,
                                        nestedResultLimit,
#endif
                                        token.Components - 1,
                                        engineFlags, substitutionFlags,
                                        eventFlags, expressionFlags,
                                        sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                        argumentLocation,
                                        ref startLine, ref endLine,
#endif
                                        ref localResult);
                                }

                                if (code != ReturnCode.Ok)
                                {
                                    result = localResult;
                                    goto done;
                                }

                                varIndex = localResult;
                            }

                            varName = text.Substring(
                                parseState.Tokens[tokenIndex + 1].Start,
                                parseState.Tokens[tokenIndex + 1].Length);

                            if (token.Type == TokenType.VariableNameOnly)
                            {
                                localResult = FormatOps.VariableName(
                                    varName, varIndex);
                            }
                            else
                            {
                                if (GetTokenVariableValue(
                                        interpreter, varName, varIndex,
                                        ref localResult) != ReturnCode.Ok)
                                {
                                    result = localResult;
                                    code = ReturnCode.Error;
                                    goto done;
                                }
                            }

                            tokenCount -= token.Components;
                            tokenIndex += token.Components;
                            thisTokenCount = token.Components;

                            break;
                        }
                    default:
                        {
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            BreakpointType breakpointType = BreakpointType.Token |
                                BreakpointType.BeforeUnknown;

                            if (DebuggerOps.CanHitBreakpoints(interpreter,
                                    engineFlags, breakpointType))
                            {
                                code = CheckBreakpoints(
                                    code, breakpointType, null,
                                    token, null, engineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, null, null,
                                    interpreter, null, null,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto done;
                            }
#endif

                            result = String.Format(
                                "unexpected token type {0} for evaluation",
                                token.Type);

                            code = ReturnCode.Error;
                            goto done;
                        }
                }

                //
                // NOTE: If there was only one "token", just return the result now.
                //
                if (thisTokenCount >= startTokenCount)
                {
                    if (index == Index.Invalid)
                    {
                        if (localResult != null) // INTL: do not change to String.IsNullOrEmpty
                        {
#if RESULT_LIMITS
                            if (!CommandBuilder.StaticHaveEnoughCapacity(
                                    nestedResultLimit, localResult, ref result))
                            {
                                code = ReturnCode.Error;
                                goto done;
                            }
#endif

                            result = localResult;
                        }
                    }
                    else
                    {
#if RESULT_LIMITS
                        if (!CommandBuilder.StaticHaveEnoughCapacity(
                                nestedResultLimit, length, ref result))
                        {
                            code = ReturnCode.Error;
                            goto done;
                        }
#endif

                        result = text.Substring(index, length);
                    }

                    code = ReturnCode.Ok;
                    goto done;
                }

                //
                // NOTE: If there was no result, there is now.
                //
                if (evalResult == null)
                    evalResult = CommandBuilder.Create();

                if (index == Index.Invalid)
                {
                    if (localResult != null) // INTL: do not change to String.IsNullOrEmpty
                    {
#if RESULT_LIMITS
                        if (!evalResult.HaveEnoughCapacity(
                                nestedResultLimit, localResult, ref result))
                        {
                            code = ReturnCode.Error;
                            goto done;
                        }
#endif

                        evalResult.Add(localResult);
                    }
                }
                else
                {
#if RESULT_LIMITS
                    if (!evalResult.HaveEnoughCapacity(
                            nestedResultLimit, length, ref result))
                    {
                        code = ReturnCode.Error;
                        goto done;
                    }
#endif

                    evalResult.Add(text, index, length);
                }
            }

            if (evalResult != null)
            {
                result = Result.FromCommandBuilder(evalResult);
            }
            else
            {
                /* IGNORED */
                ResetResult(interpreter, engineFlags, ref result);
            }

        done:

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (IScript) Methods
        /// <summary>
        /// This method evaluates the specified compiled script in the context
        /// of the given interpreter.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  This overload records the error line
        /// (if any) on the interpreter automatically; use the overload that
        /// accepts a <c>ref int errorLine</c> when the caller needs the error
        /// line directly.  Most consumers should call the equivalent
        /// <see cref="Interpreter" /> evaluation method instead, which manages
        /// interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="script">
        /// The compiled script to evaluate.  This parameter may be null, in
        /// which case there is nothing to evaluate.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            IScript script,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateScript(
                interpreter, script, ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified compiled script in the context
        /// of the given interpreter, additionally reporting the script line
        /// associated with any error.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  Most consumers should call the
        /// equivalent <see cref="Interpreter" /> evaluation method instead,
        /// which manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="script">
        /// The compiled script to evaluate.  This parameter may be null, in
        /// which case there is nothing to evaluate.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            IScript script,
            ref Result result,
            ref int errorLine
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            EngineFlags engineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

#if RESULT_LIMITS
            int executeResultLimit = 0;
            int nestedResultLimit = 0;
#endif

            if (script != null)
            {
                engineFlags = script.EngineFlags;
                substitutionFlags = script.SubstitutionFlags;
                eventFlags = script.EventFlags;
                expressionFlags = script.ExpressionFlags;

                if ((interpreter != null) &&
                    EngineFlagOps.HasUseInterpreter(engineFlags))
                {
                    if (!TryAugmentAllFlags(
                            interpreter, BlockingFlagsForEvaluate,
                            ref engineFlags, ref substitutionFlags,
                            ref eventFlags, ref expressionFlags,
                            ref result))
                    {
                        return ReturnCode.Error;
                    }

#if RESULT_LIMITS
                    executeResultLimit = interpreter.InternalExecuteResultLimit;
                    nestedResultLimit = interpreter.InternalNestedResultLimit;
#endif
                }
            }
            else if (interpreter != null)
            {
                if (!TryQueryAllFlags(
                        interpreter, BlockingFlagsForEvaluate,
                        out engineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref result))
                {
                    return ReturnCode.Error;
                }

#if RESULT_LIMITS
                executeResultLimit = interpreter.InternalExecuteResultLimit;
                nestedResultLimit = interpreter.InternalNestedResultLimit;
#endif
            }
            else
            {
                InitializeAllFlags(
                    out engineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            //
            // BUGFIX: We need to know if this is the primary AppDomain
            //         for the interpreter so we can check (potentially
            //         many times) if the "cached" ParseState for the
            //         interpreter needs to be manually refreshed from
            //         within the main command loop (below).
            //
            bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation = HasArgumentLocation(interpreter);
#endif

            return EvaluateScript(
                interpreter, script, 0, Length.Invalid,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref engineFlags, ref substitutionFlags, ref eventFlags,
                ref expressionFlags, ref result, ref errorLine);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a portion of the specified compiled script in
        /// the context of the given interpreter.  It reads the script text
        /// (honoring any embedded script-stream handling), optionally pushes a
        /// dedicated engine call frame, tracks the script location, and then
        /// evaluates the resulting text.  It is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="script">
        /// The compiled script to evaluate.  If this parameter is null, or has
        /// null text, an error is returned.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the script text at which to
        /// begin evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the text.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation; these may be augmented
        /// while the script text is read.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  A
        /// <see cref="ReturnCode.Return" /> code is converted into updated
        /// return information for the interpreter.
        /// </returns>
        private static ReturnCode EvaluateScript(
            Interpreter interpreter,
            IScript script,
            int startIndex,
            int characters,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref Result result,
            ref int errorLine
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            if (script == null)
            {
                result = "invalid script";
                return ReturnCode.Error;
            }

            string originalText = script.Text;

            if (originalText == null)
            {
                result = "invalid script";
                return ReturnCode.Error;
            }

            string text = null;

            try
            {
                using (StringReader stringReader = new StringReader(
                        originalText))
                {
                    ReadInt32Callback charCallback = null;
                    ReadCharsCallback charsCallback = null;

                    GetStreamCallbacks(
                        stringReader, ref charCallback,
                        ref charsCallback);

                    engineFlags |= EngineFlags.ExternalScript;

                    RSCD readScriptClientData = null;
                    bool canRetry = false; /* NOT USED */

                    if (ReadScriptStream(interpreter,
                            script, script.Name, charCallback,
                            charsCallback, startIndex, characters,
                            ref engineFlags, ref substitutionFlags,
                            ref eventFlags, ref expressionFlags,
                            ref readScriptClientData, ref canRetry,
                            ref result) != ReturnCode.Ok)
                    {
                        return ReturnCode.Error;
                    }

                    text = readScriptClientData.Text;
                }
            }
            catch (Exception e)
            {
                result = String.Format(
                    "caught exception reading script stream: {0}",
                    e);

                result.Exception = e;

                SetExceptionErrorCode(interpreter, e);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectTriplet(script, startIndex, characters),
                        interpreter, null, null, e, ref result);
                }
#endif

                return ReturnCode.Error;
            }

            string fileName = script.FileName;

            if (fileName == null)
                fileName = script.Name;

            bool newFrame = EngineFlagOps.HasExtraCallFrame(
                engineFlags);

            if (newFrame)
            {
                ICallFrame frame = interpreter.NewEngineCallFrame(
                    StringList.MakeList("script", fileName),
                    CallFrameFlags.Engine);

                interpreter.PushAutomaticCallFrame(frame);
            }

            try
            {
                bool pushed = false;

                interpreter.PushScriptLocation(fileName, true, ref pushed);

                try
                {
                    ReturnCode code = EvaluateScript(
                        interpreter, text, startIndex, characters, engineFlags,
                        substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                        executeResultLimit, nestedResultLimit,
#endif
                        sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                        argumentLocation,
#endif
                        ref result, ref errorLine);

                    if (code == ReturnCode.Return)
                    {
                        code = UpdateReturnInformation(interpreter);
                    }
                    else if (code == ReturnCode.Error)
                    {
                        /* IGNORED */
                        AddErrorInformation(interpreter, result,
                            String.Format(
                                "{0}    (script \"{1}\" line {2})",
                                Environment.NewLine,
                                FormatOps.Ellipsis(fileName),
                                errorLine));
                    }

                    return code;
                }
                finally
                {
                    interpreter.PopScriptLocation(true, ref pushed);
                }
            }
            finally
            {
                if (newFrame)
                {
                    //
                    // NOTE: Pop the original call frame that we
                    //       pushed above and any intervening scope
                    //       call frames that may be leftover (i.e.
                    //       they were not explicitly closed).
                    //
                    /* IGNORED */
                    interpreter.PopScopeCallFramesAndOneMore();
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (Text) Methods
        #region Specialized Evaluation (Text) Methods
        //
        // WARNING: This method creates and disposes its own "single-use"
        //          interpreter object.  Before using this method, make
        //          sure that is what you want.  This method is custom
        //          tailored to work from inside SQL Server.
        //
        /// <summary>
        /// This method evaluates the specified script text using a private,
        /// single-use interpreter that it creates and disposes internally.  It
        /// is a top-level entry point and is thread-safe and re-entrant.  This
        /// overload is custom tailored for hosting scenarios (for example, use
        /// from within SQL Server); most consumers should create an
        /// <see cref="Interpreter" /> and use its evaluation methods instead.
        /// </summary>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// The integer value of the resulting <see cref="ReturnCode" />;
        /// <see cref="ReturnCode.Ok" /> (zero) indicates success.
        /// </returns>
        public static int /* ReturnCode */ EvaluateOneScript(
            string text,
            ref string result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;
            Result localResult = null;

            using (Interpreter interpreter = Interpreter.Create(
                    null, CreateFlags.SingleUse, HostCreateFlags.SingleUse,
                    ref localResult))
            {
                if (interpreter != null)
                    code = EvaluateScript(
                        interpreter, text, ref localResult);
                else
                    code = ReturnCode.Error;
            }

            result = localResult;
            return (int)code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified script text in the context of
        /// the given interpreter, using the default engine, substitution,
        /// event, and expression flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  This overload records the error line
        /// (if any) on the interpreter automatically.  Most consumers should
        /// call the equivalent <see cref="Interpreter" /> evaluation method
        /// instead, which manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  This parameter should not be null.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateScript(
                interpreter, text, EngineFlags.None,
                SubstitutionFlags.Default, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified script text in the context of
        /// the given interpreter, using the default engine, substitution,
        /// event, and expression flags, additionally reporting the script line
        /// associated with any error.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  Most consumers should call the
        /// equivalent <see cref="Interpreter" /> evaluation method instead,
        /// which manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  This parameter should not be null.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            ref Result result,
            ref int errorLine
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateScript(
                interpreter, text, EngineFlags.None,
                SubstitutionFlags.Default, EventFlags.Default,
                ExpressionFlags.Default, ref result, ref errorLine);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified script text in the context of
        /// the given interpreter, using the supplied engine, substitution,
        /// event, and expression flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  This overload records the error line
        /// (if any) on the interpreter automatically; use the overload that
        /// accepts a <c>ref int errorLine</c> when the caller needs the error
        /// line directly.  Most consumers should call the equivalent
        /// <see cref="Interpreter" /> evaluation method instead, which manages
        /// interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  This parameter should not be null.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
#if RESULT_LIMITS
            int executeResultLimit = 0;
            int nestedResultLimit = 0;

            if (interpreter != null)
            {
                executeResultLimit = interpreter.InternalExecuteResultLimit;
                nestedResultLimit = interpreter.InternalNestedResultLimit;
            }
#endif

            //
            // BUGFIX: We need to know if this is the primary AppDomain
            //         for the interpreter so we can check (potentially
            //         many times) if the "cached" ParseState for the
            //         interpreter needs to be manually refreshed from
            //         within the main command loop (below).
            //
            bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation = HasArgumentLocation(interpreter);
#endif

            return EvaluateScript(
                interpreter, text, 0, Length.Invalid, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a portion of the specified script text in the
        /// context of the given interpreter, using the supplied flags and
        /// limits.  It is thread-safe and re-entrant.  This overload records
        /// the error line (if any) on the interpreter automatically; use the
        /// overload that accepts a <c>ref int errorLine</c> when the caller
        /// needs the error line directly.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within <paramref name="text" /> at
        /// which to begin evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the text.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        internal static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateScript(
                interpreter, text, startIndex, characters,
                engineFlags, substitutionFlags, eventFlags,
                expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified script text in the context of
        /// the given interpreter, using the supplied engine, substitution,
        /// event, and expression flags, additionally reporting the script line
        /// associated with any error.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  Most consumers should call the
        /// equivalent <see cref="Interpreter" /> evaluation method instead,
        /// which manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  This parameter should not be null.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result,
            ref int errorLine
            )
        {
#if RESULT_LIMITS
            int executeResultLimit = 0;
            int nestedResultLimit = 0;

            if (interpreter != null)
            {
                executeResultLimit = interpreter.InternalExecuteResultLimit;
                nestedResultLimit = interpreter.InternalNestedResultLimit;
            }
#endif

            //
            // BUGFIX: We need to know if this is the primary AppDomain
            //         for the interpreter so we can check (potentially
            //         many times) if the "cached" ParseState for the
            //         interpreter needs to be manually refreshed from
            //         within the main command loop (below).
            //
            bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation = HasArgumentLocation(interpreter);
#endif

            return EvaluateScript(
                interpreter, text, 0, Length.Invalid, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result, ref errorLine);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a portion of the specified script text in the
        /// context of the given interpreter.  It first resolves the current
        /// script location (file name and line) for breakpoint and error
        /// reporting purposes, then delegates to the core evaluation routine.
        /// It is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within <paramref name="text" /> at
        /// which to begin evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the text.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        private static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result,
            ref int errorLine
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            string fileName = null;
            int currentLine = Parser.StartLine;

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            if (ScriptOps.GetLocation(
                    interpreter, false, false, ref fileName,
                    ref currentLine, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }
#endif

            return EvaluateScript(
                interpreter, fileName, currentLine, text, startIndex, characters,
                engineFlags, substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result, ref errorLine);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a portion of the specified script text in the
        /// context of the given interpreter, using the supplied script file
        /// name and starting line for error and location reporting.  It is
        /// thread-safe and re-entrant.  This overload records the error line
        /// (if any) on the interpreter automatically; use the overload that
        /// accepts a <c>ref int errorLine</c> when the caller needs the error
        /// line directly.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the script,
        /// used for error and location reporting.  This parameter may be null.
        /// </param>
        /// <param name="currentLine">
        /// The script line number at which evaluation begins.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within <paramref name="text" /> at
        /// which to begin evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the text.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        private static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string fileName,
            int currentLine,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateScript(
                interpreter, fileName, currentLine, text, startIndex, characters,
                engineFlags, substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core script-text evaluation routine.  It parses
        /// the specified script text one command at a time and executes each
        /// command in the context of the given interpreter, honoring
        /// breakpoints, cancellation, notifications, and the supplied flags and
        /// limits, until the text is exhausted or a non-Ok return code stops
        /// the loop.  It is thread-safe and re-entrant.  All of the other
        /// script-text evaluation overloads ultimately delegate here.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the script,
        /// used for error and location reporting.  This parameter may be null.
        /// </param>
        /// <param name="currentLine">
        /// The script line number at which evaluation begins.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  If this parameter is null, an error
        /// is returned.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within <paramref name="text" /> at
        /// which to begin evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the text.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        private static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string fileName,
            int currentLine,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result,
            ref int errorLine
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code = ReturnCode.Ok;
            EngineFlags localEngineFlags = engineFlags;

            if (interpreter == null)
            {
                result = "invalid interpreter";
                code = ReturnCode.Error;

                goto exit;
            }

            if (text == null) // INTL: do not change to String.IsNullOrEmpty
            {
                result = "invalid script";
                code = ReturnCode.Error;

                goto exit;
            }

            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            int scriptLevels = interpreter.EnterEngineScriptLevel();

            try
            {
                localEngineFlags = CombineFlags(
                    interpreter, engineFlags, true, true);

                if (EngineFlagOps.HasNoEvaluate(localEngineFlags))
                {
                    result = "interpreter not accepting scripts to evaluate";
                    code = ReturnCode.Error;

                    goto exit;
                }

                bool noReady = EngineFlagOps.HasNoReady(localEngineFlags);
                bool noCacheArgument = false;

#if ARGUMENT_CACHE
                if (EngineFlagOps.HasNoCacheArgument(localEngineFlags))
                    noCacheArgument = true;
#endif

#if CALLBACK_QUEUE
                bool callbackQueue = true;

                //
                // NOTE: Has callback support been disabled for this script or for
                //       the interpreter?
                //
                if (EngineFlagOps.HasNoCallbackQueue(localEngineFlags))
                    callbackQueue = false;
#endif

                if (characters < 0)
                    characters = text.Length;

                interpreter.ResetForEngine(
                    localEngineFlags, GetCancelFlags(localEngineFlags),
                    ref result);

                /*
                 * Are we going to evaluate the script in the global context?
                 */

                bool global = false;

                if (EngineFlagOps.HasEvaluateGlobal(localEngineFlags))
                {
                    interpreter.PushGlobalCallFrame(true);
                    global = true; // pushed.
                }

                int index = startIndex;
                int charactersLeft = characters;
                bool nested = EngineFlagOps.HasBracketTerminator(localEngineFlags);

                int terminator;
                int nextIndex;

                //
                // NOTE: Prevent a null result from being added as an argument to
                //       a subsequent command?  For full backward compatibility,
                //       e.g. with native Tcl, etc, this flag should be used.
                //
                bool noNullArgument = interpreter.HasNoNullArgument(localEngineFlags);

                //
                // NOTE: Disallow use of the Thread.ResetAbort() method when script
                //       is running?
                //
                bool noResetAbort = EngineFlagOps.HasNoResetAbort(localEngineFlags);

                IParseState parseState = new ParseState(
                    localEngineFlags, substitutionFlags, fileName, currentLine);

                interpreter.ParseState = parseState; /* NOTE: Per-thread. */
                ArgumentList arguments = new ArgumentList();

                //
                // NOTE: Opt-in script parse cache (see the CacheParsedScripts
                //       property of the interpreter): when enabled, replay the
                //       previously parsed commands of this exact script text
                //       instead of re-parsing it on every evaluation.  The
                //       readyParseState below always refers to a MUTABLE parser
                //       state whose engine flags reflect THIS evaluation; it is
                //       used for the per-command ready checks while replaying,
                //       mirroring the checks Parser.ParseCommand would perform.
                //
                CachedScriptCommands cachedScript = null;
                int nextCachedCommand = 0;
                IParseState readyParseState = parseState;

                if ((characters > 0) && interpreter.InternalCacheParsedScripts)
                {
                    cachedScript = GetOrBuildCachedScriptCommands(
                        interpreter, fileName, currentLine, text,
                        startIndex, characters, localEngineFlags,
                        substitutionFlags, nested, noReady);
                }

#if PREVIOUS_RESULT
                Result previousResult = null;
#endif

                do
                {
                    /*
                     * Attempt to parse the command.  This can fail in a number of
                     * ways, including being canceled.  When replaying from the
                     * script parse cache, the pre-parsed command is used instead,
                     * after an equivalent per-command ready check; if the cached
                     * commands are exhausted with text remaining (a parse error
                     * exists beyond that point), live parsing resumes seamlessly.
                     */

                    bool replayedCommand = false;
                    CachedScriptCommand replayedCachedCommand = null;

                    if (cachedScript != null)
                    {
                        CachedScriptCommand[] cachedCommands = cachedScript.Commands;

                        if (nextCachedCommand < cachedCommands.Length)
                        {
                            if (!noReady && (Parser.Ready(
                                    interpreter, readyParseState,
                                    ref result) != ReturnCode.Ok))
                            {
                                code = ReturnCode.Error;
                                goto error;
                            }

                            replayedCachedCommand = cachedCommands[nextCachedCommand++];
                            parseState = replayedCachedCommand.ParseState;
                            interpreter.ParseState = parseState; /* NOTE: Per-thread. */

                            replayedCommand = true;
                        }
                        else
                        {
                            IParseState lastParseState = (cachedCommands.Length > 0) ?
                                cachedCommands[cachedCommands.Length - 1].ParseState : null;

                            cachedScript = null;

                            parseState = new ParseState(
                                localEngineFlags, substitutionFlags, fileName,
                                currentLine);

                            if (lastParseState != null)
                            {
                                //
                                // NOTE: Continue the line numbering from where
                                //       the cached commands left off.
                                //
                                parseState.CurrentLine = lastParseState.CurrentLine;
                                parseState.LineStart = lastParseState.LineStart;
                            }

                            readyParseState = parseState;
                            interpreter.ParseState = parseState; /* NOTE: Per-thread. */
                        }
                    }

#if PERFORMANCE_DIAGNOSIS
                    long __probeParseTs = Diagnostics.PerfProbe.Enabled ?
                        Diagnostics.PerfProbe.Now : 0;
#endif

                    if (!replayedCommand && (Parser.ParseCommand(
                            interpreter, text, index,
                            charactersLeft, nested, parseState,
                            noReady, ref result) != ReturnCode.Ok))
                    {
                        code = ReturnCode.Error;
                        goto error;
                    }

#if PERFORMANCE_DIAGNOSIS
                    if (Diagnostics.PerfProbe.Enabled)
                    {
                        Diagnostics.PerfProbe.Add(replayedCommand ?
                            "eval.replay" : "eval.parse", __probeParseTs);
                    }
#endif

                    terminator = parseState.Terminator;

                    if (nested && (terminator == characters))
                    {
                        code = ReturnCode.Error;
                        goto error;
                    }

                    int commandWords = parseState.CommandWords;

                    if (commandWords > 0)
                    {
                        //
                        // NOTE: Build the argument list of the command to execute,
                        //       recursively evaluating terms as necessary.
                        //
                        arguments.Clear();

                        if (arguments.Capacity < commandWords)
                            arguments.Capacity = commandWords;

#if PERFORMANCE_DIAGNOSIS
                        long __probeWordsTs = Diagnostics.PerfProbe.Enabled ?
                            Diagnostics.PerfProbe.Now : 0;
#endif

                        //
                        // NOTE: Initialize token related variables.
                        //
                        IToken token = null;
                        int tokenIndex = 0;

                        //
                        // NOTE: When replaying a cached command, its word walk
                        //       was precomputed and its static words (single
                        //       text tokens without expansion, whose value is
                        //       identical on every execution) were evaluated
                        //       once, at cache build time.
                        //
                        int[] cachedWordTokenIndexes = null;
                        Argument[] cachedStaticWords = null;

                        if (replayedCachedCommand != null)
                        {
                            cachedWordTokenIndexes =
                                replayedCachedCommand.WordTokenIndexes;

                            cachedStaticWords =
                                replayedCachedCommand.StaticWords;

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            if (argumentLocation)
                            {
                                //
                                // NOTE: The debugger needs per-argument source
                                //       locations; use the fully dynamic path.
                                //
                                cachedWordTokenIndexes = null;
                                cachedStaticWords = null;
                            }
#endif

                            if ((cachedStaticWords != null) &&
                                (cachedStaticWords.Length != commandWords))
                            {
                                cachedWordTokenIndexes = null;
                                cachedStaticWords = null;
                            }
                        }

                        for (int wordsUsed = 0; wordsUsed < commandWords; wordsUsed++)
                        {
                            if (cachedStaticWords != null)
                            {
                                //
                                // NOTE: A static word always evaluates to the
                                //       same value; append its pre-evaluated
                                //       argument directly.
                                //
                                Argument staticArgument =
                                    cachedStaticWords[wordsUsed];

                                if (staticArgument != null)
                                {
                                    arguments.Add(staticArgument);
                                    continue;
                                }

                                //
                                // NOTE: A dynamic word; position directly at
                                //       its (precomputed) word token and fall
                                //       through to normal token evaluation.
                                //
                                tokenIndex = cachedWordTokenIndexes[wordsUsed];
                                token = parseState.Tokens[tokenIndex];
                            }
                            else
                            {
                                //
                                // NOTE: Get the first token from the parse state.
                                //
                                code = GetToken(
                                    parseState, ref token, ref tokenIndex,
                                    ref result);

                                if (code != ReturnCode.Ok)
                                    goto error;
                            }

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            int startLine = Parser.UnknownLine;
                            int endLine = Parser.UnknownLine;
#endif

                            Result localResult = null;

                            try
                            {
                                code = EvaluateTokens(
                                    interpreter, parseState,
                                    tokenIndex + 1,
#if RESULT_LIMITS
                                    executeResultLimit,
                                    nestedResultLimit,
#endif
                                    token.Components, localEngineFlags,
                                    substitutionFlags, eventFlags,
                                    expressionFlags, sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                    argumentLocation,
                                    ref startLine, ref endLine,
#endif
                                    ref localResult);
                            }
#if true
                            catch (ThreadAbortException)
                            {
                                if (!noResetAbort)
                                    Thread.ResetAbort();

                                localResult = ThreadAbortException;
                                code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                //
                                // NOTE: This may not actually work.  In that case,
                                //       there is not much else we can do.
                                //
                                DebugOps.Complain(interpreter, code, localResult);
#endif
                            }
#endif
#if true
                            catch (ThreadInterruptedException)
                            {
                                localResult = ThreadInterruptedException;
                                code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                //
                                // NOTE: This may not actually work.  In that case,
                                //       there is not much else we can do.
                                //
                                DebugOps.Complain(interpreter, code, localResult);
#endif
                            }
#endif
#if true
                            catch (StackOverflowException)
                            {
                                localResult = StackOverflowException;
                                code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                //
                                // NOTE: We should (almost) never get here, complain.
                                //       This may not actually work.  In that case,
                                //       there is not much else we can do.
                                //
                                DebugOps.Complain(interpreter, code, localResult);
#endif
                            }
                            catch (OutOfMemoryException e)
                            {
                                try
                                {
                                    //
                                    // HACK: Try to free up some memory.  This is
                                    //       unlikely to work.
                                    //
                                    ObjectOps.CollectGarbage(
                                        GarbageFlags.ForEngine); /* throw */

                                    localResult = e;
                                    code = ReturnCode.Error;
                                }
                                catch (OutOfMemoryException)
                                {
                                    localResult = OutOfMemoryException;
                                    code = ReturnCode.Error;
                                }
                                catch (Exception ex)
                                {
                                    localResult = ex;
                                    code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                    //
                                    // NOTE: This may not actually work.  In that case,
                                    //       there is not much else we can do.
                                    //
                                    DebugOps.Complain(interpreter, code, localResult);
#endif
                                }
                            }
#endif
                            catch (ScriptEngineException e)
                            {
                                localResult = e;
                                code = ReturnCode.Error;
                            }
#if true
                            catch (Exception e)
                            {
                                localResult = e;
                                code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                //
                                // NOTE: We should never get here, complain.
                                //       This may not actually work.  In that case,
                                //       there is not much else we can do.
                                //
                                DebugOps.Complain(interpreter, code, localResult);
#endif
                            }
#endif

                            //
                            // NOTE: If there was any kind of error or exception,
                            //       bail out now.
                            //
                            if (code != ReturnCode.Ok)
                            {
                                result = localResult;
                                goto error;
                            }

                            //
                            // HACK: When preventing null results, reset them to
                            //       an empty string instead.  This is something
                            //       of a hack.  In general, commands should not
                            //       return a null result.  One interesting case
                            //       is that of an empty procedure body, i.e. a
                            //       procedure body with no commands to execute.
                            //       That appears to produce a null result, for
                            //       reasons that are not entirely clear.  If it
                            //       has an initial null result and no commands
                            //       are executed, it stays null?
                            //
                            if (ShouldUseNullArgument(localResult))
                                localResult = null;
                            else if (noNullArgument && (localResult == null))
                                localResult = String.Empty;

                            //
                            // NOTE: Grab the engine data associated with the
                            //       result, if any.  If this engine data is
                            //       valid, it will prevent the (new) argument
                            //       from being cached.
                            //
                            object engineData = null;

                            if (localResult != null)
                                engineData = localResult.EngineData;

                            //
                            // NOTE: Tcl 8.5 argument expansion: when this
                            //       word carried the "{*}" prefix (see
                            //       TokenFlags.Expand), split its value as
                            //       a list and append each element as a
                            //       separate argument.
                            //
                            if ((token.Flags & TokenFlags.Expand) ==
                                    TokenFlags.Expand)
                            {
                                if (localResult != null)
                                {
                                    StringList expandList = null;
                                    Result expandError = null;

                                    if (ListOps.GetOrCopyOrSplitList(
                                            interpreter, localResult, true,
                                            ref expandList,
                                            ref expandError) != ReturnCode.Ok)
                                    {
                                        result = expandError;
                                        code = ReturnCode.Error;
                                        goto error;
                                    }

                                    foreach (string element in expandList)
                                    {
                                        arguments.Add(Argument.GetOrCreate(
                                            interpreter, (Result)element,
                                            noCacheArgument));
                                    }
                                }

                                continue;
                            }

                            //
                            // NOTE: Append the result value to the list of
                            //       arguments for the command to be executed.
                            //
                            Argument argument;

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            if (argumentLocation)
                            {
                                argument = Argument.GetOrCreate(
                                    interpreter, localResult, fileName,
                                    startLine, endLine, false,
                                    noCacheArgument || (engineData != null));
                            }
                            else
#endif
                            {
                                argument = Argument.GetOrCreate(
                                    interpreter, localResult,
                                    noCacheArgument || (engineData != null));
                            }

                            if ((argument != null) && (engineData != null))
                            {
                                argument.SetEngineDataForIHaveStringBuilder(
                                    engineData, arguments);
                            }

                            arguments.Add(argument);
                        }

#if PERFORMANCE_DIAGNOSIS
                        if (Diagnostics.PerfProbe.Enabled)
                        { Diagnostics.PerfProbe.Add("eval.words", __probeWordsTs); }
#endif

                        //
                        // NOTE: Argument expansion may leave an empty
                        //       command (every word expanded to nothing);
                        //       like stock Tcl, skip it entirely, leaving
                        //       the previous result in place.
                        //
                        if (arguments.Count == 0)
                            goto emptyCommand;

                        bool exit = false;
                        int engineLevels = interpreter.EnterEngineLevel(); /* REALLY: Command level? */

                        try
                        {
#if HISTORY
                            //
                            // BUGFIX: Is the interpreter configured to track command history?
                            //         Also, has command history been disabled for this script
                            //         by our caller?  Unfortunately, we cannot simply fetch
                            //         this value upon entry into this method and continue to
                            //         use it [like we used to] because any command execution
                            //         could cause it to change and we want those changes to
                            //         be effective immediately.
                            //
                            if (!EngineFlagOps.HasNoHistory(localEngineFlags) &&
                                interpreter.CanAddHistory())
                            {
                                if (HistoryOps.MatchData(engineLevels,
                                        HistoryFlags.Engine, interpreter.HistoryEngineFilter))
                                {
                                    code = interpreter.AddHistory(arguments, engineLevels,
                                        HistoryFlags.Engine, ref result);

                                    if (code != ReturnCode.Ok)
                                        goto error;
                                }
                            }
#endif

                            // if (arguments.Count > 0)
                            {
#if DEBUGGER && DEBUGGER_ARGUMENTS
                                //
                                // NOTE: Notify the script debugger, if any, of the current
                                //       command name and arguments.
                                //
                                if (!EngineFlagOps.HasNoDebuggerArguments(localEngineFlags))
                                {
                                    /* IGNORED */
                                    SetDebuggerExecuteArguments(interpreter, arguments);
                                }
#endif

                                //
                                // BUGFIX: *HACK* If this is NOT the primary AppDomain for the
                                //         interpreter being used (i.e. remoting is involved),
                                //         then refresh the "cached" ParseState.  This is
                                //         necessary because the "cached" ParseState for the
                                //         interpreter is modified by ParseCommand without
                                //         entering this method again -AND- must be completely
                                //         up-to-date prior to executing each command (e.g.
                                //         [error]).
                                //
                                if (!sameAppDomain)
                                    interpreter.ParseState = parseState; /* NOTE: Per-thread. */

#if SCRIPT_ARGUMENTS
                                bool pushed = false;

                                interpreter.PushScriptArguments(arguments, ref pushed);

                                try
                                {
#endif
                                    //
                                    // NOTE: Execute the command.  The command could do practically
                                    //       anything at this point; therefore, we need to be very
                                    //       careful about making assumptions about the state of the
                                    //       interpreter after this point.
                                    //
                                    code = ExecuteArguments(
                                        interpreter, arguments, localEngineFlags, substitutionFlags,
                                        eventFlags, expressionFlags,
#if RESULT_LIMITS
                                        executeResultLimit,
#endif
                                        ref usable, ref result);
#if SCRIPT_ARGUMENTS
                                }
                                finally
                                {
                                    interpreter.PopScriptArguments(ref pushed);
                                }
#endif

#if NOTIFY && NOTIFY_ARGUMENTS
                                if (usable &&
                                    !EngineFlagOps.HasNoNotify(localEngineFlags) &&
                                    interpreter.ShouldMaybeFireNotification(
                                        NotifyType.Engine, NotifyFlags.Executed))
                                {
                                    /* IGNORED */
                                    interpreter.CheckNotification(
                                        NotifyType.Engine, NotifyFlags.Executed,
                                        new ObjectList(code, localEngineFlags, substitutionFlags,
                                        eventFlags, expressionFlags, usable), interpreter,
                                        null, arguments, null, ref result);
                                }
#endif
                            }

                            //
                            // BUGFIX: We cannot use various properties of the interpreter if
                            //         it has been disposed.
                            //
                            if (!usable)
                                goto unusable;

                            exit = interpreter.ExitNoThrow;

#if CALLBACK_QUEUE
                            //
                            // NOTE: We only want to execute queued callbacks if we have not
                            //       exited (or been canceled, etc) and if we are on the way
                            //       out of this evaluation.
                            //
                            if (callbackQueue &&
                                (code == ReturnCode.Ok) && (engineLevels == 1) && !exit)
                            {
                                //
                                // NOTE: Check for and execute any queued callbacks.  This is
                                //       currently used primarily to implement tailcall-like
                                //       functionality (see TIP #327).
                                //
                                code = ExecuteCallbackQueue(
                                    interpreter, localEngineFlags, substitutionFlags,
                                    eventFlags, expressionFlags,
#if RESULT_LIMITS
                                    executeResultLimit,
#endif
                                    ref usable, ref result);

                                if (!usable)
                                    goto unusable;
                            }
#endif

                            //
                            // BUGFIX: Prevent null command result from causing an exception
                            //         when we try to store the return code.
                            //
                            // NOTE: This used to place String.Empty in the result if it was
                            //       null and then set the return code; however, that seems
                            //       wasteful.
                            //
                            if (result != null)
                            {
                                result.ReturnCode = code;

                                if (ShouldUseNullResult(result))
                                {
                                    if (scriptLevels == 1)
                                        result = null;
                                    else
                                        result.Flags |= ResultFlags.ForceNullArgument;
                                }
                            }

                            //
                            // HACK: Yes, this is a bit ugly; however, it can help make things
                            //       like [append] go (much) faster.
                            //
                            // arguments.ResetForIHaveStringBuilder(interpreter);
                        }
#if true
                        catch (StackOverflowException)
                        {
                            result = StackOverflowException;
                            code = ReturnCode.Error;

#if DEBUG && VERBOSE
                            //
                            // NOTE: We should (almost) never get here, complain.
                            //       This may not actually work.  In that case,
                            //       there is not much else we can do.
                            //
                            DebugOps.Complain(interpreter, code, result);
#endif
                        }
                        catch (OutOfMemoryException e)
                        {
                            try
                            {
                                //
                                // HACK: Try to free up some memory.  This is
                                //       unlikely to work.
                                //
                                ObjectOps.CollectGarbage(
                                    GarbageFlags.ForEngine); /* throw */

                                result = e;
                                code = ReturnCode.Error;
                            }
                            catch (OutOfMemoryException)
                            {
                                result = OutOfMemoryException;
                                code = ReturnCode.Error;
                            }
                            catch (Exception ex)
                            {
                                result = ex;
                                code = ReturnCode.Error;

#if DEBUG && VERBOSE
                                //
                                // NOTE: This may not actually work.  In that case,
                                //       there is not much else we can do.
                                //
                                DebugOps.Complain(interpreter, code, result);
#endif
                            }
                        }
#endif
                        catch (ScriptEngineException e)
                        {
                            result = e;
                            code = ReturnCode.Error;
                        }
#if true
                        catch (Exception e)
                        {
                            result = e;
                            code = ReturnCode.Error;

#if DEBUG && VERBOSE
                            //
                            // NOTE: We should never get here, complain.
                            //       This may not actually work.  In that case,
                            //       there is not much else we can do.
                            //
                            DebugOps.Complain(interpreter, code, result);
#endif
                        }
#endif
                        finally
                        {
                            if (usable)
                            {
                                /* IGNORED */
                                interpreter.ExitEngineLevel();
                            }
                        }

#if PREVIOUS_RESULT
                        //
                        // TODO: Evaluate how much this block impacts the overall
                        //       performance of script evaluation.
                        //
                        if (!EngineFlagOps.HasNoPreviousResult(localEngineFlags))
                        {
                            //
                            // NOTE: Copy result to previous for use by debugger.
                            //
                            previousResult = Result.Copy(
                                result, ResultFlags.CopyObject); /* COPY */

                            //
                            // NOTE: At this point, the error line value will be
                            //       whatever the caller passed in, which is most
                            //       likely zero.  It will be made accurate after
                            //       going to the "error" label, if applicable.
                            //
                            interpreter.MaybePopulateResultErrorProperties(
                                "previous", code, previousResult, errorLine);

                            Interpreter.SetPreviousResult(
                                interpreter, previousResult);
                        }
#endif

                        //
                        // NOTE: Return is also considered an "exception" here because it
                        //       halts evaluation of the current procedure.
                        //
                        if (code != ReturnCode.Ok)
                            goto error;

                        //
                        // NOTE: If the command marked the interpreter as "exited", bail
                        //       out now.
                        //
                        if (exit)
                            goto ok;

                    emptyCommand:
                        ;
                    }

                    //
                    // NOTE: Advance to the next command in the script.  When
                    //       replaying from the script parse cache, the parser
                    //       state is an immutable shared snapshot; its tokens
                    //       must never be cleared.
                    //
                    nextIndex = parseState.CommandStart + parseState.CommandLength;
                    charactersLeft -= (nextIndex - index);
                    index = nextIndex;

                    if (cachedScript == null)
                        parseState.Tokens.Clear();

                    if (nested &&
                        (terminator < text.Length) && // TEST: Test this.
                        (text[terminator] == Characters.CloseBracket))
                    {
                        //
                        // NOTE: If we previously pushed the global call frame (above),
                        //       we also need to pop any leftover scope call frames now;
                        //       otherwise, the call stack will be imbalanced.
                        //
                        if (global)
                            interpreter.PopGlobalCallFrame(true);

                        code = ReturnCode.Ok;

                        goto exit;
                    }
                } while (charactersLeft > 0);

                if (nested)
                {
                    code = ReturnCode.Error;
                    goto error;
                }

            ok:

                //
                // NOTE: If we previously pushed the global call frame (above), we also
                //       need to pop any leftover scope call frames now; otherwise, the
                //       call stack will be imbalanced.
                //
                if (global)
                    interpreter.PopGlobalCallFrame(true);

                code = ReturnCode.Ok;

                goto exit;

            error:

                bool locked = false;

                try
                {
                    interpreter.InternalEngineTryLock(
                        ref locked); /* TRANSACTIONAL */

                    if (locked)
                    {
                        if ((code == ReturnCode.Return) && !interpreter.InternalIsBusy)
                            code = UpdateReturnInformation(interpreter);

                        //
                        // WARNING: The engine flags in the interpreter must be checked here
                        //          because the command we just executed above may have just
                        //          changed them (i.e. the [error] command).
                        //
                        localEngineFlags = CombineFlags(
                            interpreter, localEngineFlags, false, false);

                        if ((code == ReturnCode.Error) &&
                            !EngineFlagOps.HasErrorAlreadyLogged(localEngineFlags))
                        {
                            terminator = parseState.Terminator;

                            int commandStart = parseState.CommandStart;
                            int commandLength = parseState.CommandLength;

                            if (terminator == (commandStart + commandLength - 1))
                                commandLength--; // back off trailing command terminator...

                            /* IGNORED */
                            LogCommandInformation(interpreter, text, commandStart,
                                commandLength, localEngineFlags, result, ref errorLine);

#if PREVIOUS_RESULT
                            if (previousResult != null) previousResult.ErrorLine = errorLine;
#endif
                        }
                    }
                    else
                    {
                        TraceOps.LockTrace(
                            "EvaluateScript",
                            typeof(Engine).Name, false,
                            TracePriority.LockError,
                            interpreter.MaybeWhoHasLock());
                    }
                }
                finally
                {
                    interpreter.InternalExitLock(
                        ref locked); /* TRANSACTIONAL */
                }

                //
                // NOTE: If we previously pushed the global call frame (above), we also
                //       need to pop any leftover scope call frames now; otherwise, the
                //       call stack will be imbalanced.
                //
                if (global)
                    interpreter.PopGlobalCallFrame(true);

                nextIndex = parseState.CommandStart + parseState.CommandLength;
                charactersLeft -= (nextIndex - index);
                index = nextIndex;

                if (!nested)
                    goto exit;

                terminator = parseState.Terminator;
                nextIndex = Index.Invalid;

                if (parseState.IsImmutable())
                {
                    //
                    // NOTE: The error occurred while replaying commands from
                    //       the script parse cache; the nested-script skip
                    //       loop below must re-parse live, which requires a
                    //       mutable parser state.
                    //
                    IParseState localParseState = new ParseState(
                        localEngineFlags, substitutionFlags, fileName,
                        currentLine);

                    localParseState.CurrentLine = parseState.CurrentLine;
                    localParseState.LineStart = parseState.LineStart;

                    parseState = localParseState;
                    interpreter.ParseState = parseState; /* NOTE: Per-thread. */
                }

                while ((terminator < text.Length) && // TEST: Test this.
                       (charactersLeft > 0) &&
                       (text[terminator] != Characters.CloseBracket))
                {
                    if (Parser.ParseCommand(
                            interpreter, text, index,
                            charactersLeft, nested, parseState,
                            noReady, ref result) != ReturnCode.Ok)
                    {
                        goto exit;
                    }

                    terminator = parseState.Terminator;
                    nextIndex = parseState.CommandStart + parseState.CommandLength;
                    charactersLeft -= (nextIndex - index);
                    index = nextIndex;
                }

                if (terminator == characters)
                {
                    result = "missing close-bracket";
                }
                else if ((terminator < text.Length) && // TEST: Test this.
                         (text[terminator] != Characters.CloseBracket))
                {
                    result = "missing close-bracket";
                }

                code = ReturnCode.Error;
            }
            finally
            {
                if (usable)
                {
                    /* IGNORED */
                    interpreter.ExitEngineScriptLevel();
                }
            }

        exit:

            return EvaluateExited(
                interpreter, fileName, currentLine,
                text, startIndex, characters,
                localEngineFlags, substitutionFlags,
                eventFlags, expressionFlags, ref code,
                ref result, ref errorLine);

        unusable:

            result = Result.Copy(
                InterpreterUnusableError, ResultFlags.CopyValue);

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Script Parse Cache Methods
        /// <summary>
        /// This method implements the lookup and second-sighting-promotion
        /// policy of the opt-in script parse cache (see the
        /// CacheParsedScripts property of the interpreter).  The first time
        /// a given script parse is seen, only its key hash is recorded; the
        /// second time, the script is fully parsed ahead of execution and
        /// the resulting per-command parser-state snapshots are cached for
        /// the lifetime of the interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script parse cache should be consulted.
        /// </param>
        /// <param name="fileName">
        /// The name of the file the script originated from, if any.
        /// </param>
        /// <param name="currentLine">
        /// The line number the script starts on.
        /// </param>
        /// <param name="text">
        /// The script text being evaluated.
        /// </param>
        /// <param name="startIndex">
        /// The index, within the text, where evaluation starts.
        /// </param>
        /// <param name="characters">
        /// The number of characters available for parsing.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect for this evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for this evaluation.
        /// </param>
        /// <param name="nested">
        /// Non-zero if the parse is close-bracket terminated.
        /// </param>
        /// <param name="noReady">
        /// Non-zero to skip interpreter readiness checks while parsing.
        /// </param>
        /// <returns>
        /// The cached commands for the script, or null when the script is
        /// not (yet) cached.
        /// </returns>
        private static CachedScriptCommands GetOrBuildCachedScriptCommands(
            Interpreter interpreter,
            string fileName,
            int currentLine,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            bool nested,
            bool noReady
            )
        {
            ScriptParseCache cache = interpreter.GetScriptParseCache();

            ScriptParseCacheKey key = new ScriptParseCacheKey(
                text, startIndex, characters, nested, substitutionFlags,
                fileName, currentLine);

            bool build;

            CachedScriptCommands cachedScript = cache.GetOrMarkSeen(
                key, out build);

#if PERFORMANCE_DIAGNOSIS
            if (Diagnostics.PerfProbe.Enabled)
            {
                Diagnostics.PerfProbe.Add(
                    (cachedScript != null) ? "sc.hit" :
                    (build ? "sc.build" : "sc.first"),
                    Diagnostics.PerfProbe.Now);
            }
#endif

            if ((cachedScript != null) || !build)
                return cachedScript;

            cachedScript = BuildCachedScriptCommands(
                interpreter, fileName, currentLine, text, startIndex,
                characters, engineFlags, substitutionFlags, nested,
                noReady);

            if (cachedScript != null)
                cache.Add(key, cachedScript);

            return cachedScript;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method parses ALL commands of the specified script range,
        /// ahead of execution, into immutable per-command parser-state
        /// snapshots suitable for replay by <c>EvaluateScript</c>.  Parsing
        /// stops early at a deterministic parse error (the commands before
        /// it are still cached; live parsing reproduces the error exactly,
        /// if and when execution reaches it) or at the close bracket of a
        /// nested script.  A transient failure (interpreter not ready, e.g.
        /// canceled) aborts the build entirely so nothing incorrect can be
        /// cached.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter used for readiness checks while parsing.
        /// </param>
        /// <param name="fileName">
        /// The name of the file the script originated from, if any.
        /// </param>
        /// <param name="currentLine">
        /// The line number the script starts on.
        /// </param>
        /// <param name="text">
        /// The script text being parsed.
        /// </param>
        /// <param name="startIndex">
        /// The index, within the text, where parsing starts.
        /// </param>
        /// <param name="characters">
        /// The number of characters available for parsing.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect while parsing.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect while parsing.
        /// </param>
        /// <param name="nested">
        /// Non-zero if the parse is close-bracket terminated.
        /// </param>
        /// <param name="noReady">
        /// Non-zero to skip interpreter readiness checks while parsing.
        /// </param>
        /// <returns>
        /// The parsed commands, or null when the build was aborted by a
        /// transient (not-ready) failure.
        /// </returns>
        private static CachedScriptCommands BuildCachedScriptCommands(
            Interpreter interpreter,
            string fileName,
            int currentLine,
            string text,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            bool nested,
            bool noReady
            )
        {
            List<CachedScriptCommand> commands = new List<CachedScriptCommand>();

            IParseState parseState = new ParseState(
                engineFlags, substitutionFlags, fileName, currentLine);

            int index = startIndex;
            int charactersLeft = characters;

            do
            {
                Result error = null;

                if (Parser.ParseCommand(
                        interpreter, text, index, charactersLeft,
                        nested, parseState, noReady,
                        ref error) != ReturnCode.Ok)
                {
                    if (parseState.NotReady)
                        return null;

                    break;
                }

                int terminator = parseState.Terminator;

                if (nested && (terminator == characters))
                    break;

                IParseState commandParseState;

                parseState.Save(true, out commandParseState);

                //
                // NOTE: Saving copies each token by re-deriving its source
                //       location from the (already advanced) parser state;
                //       restore the true per-token locations so that error
                //       line numbers within replayed commands are identical
                //       to those of a live parse.
                //
                TokenList oldTokens = parseState.Tokens;
                TokenList newTokens = commandParseState.Tokens;

                if ((oldTokens != null) && (newTokens != null))
                {
                    for (int tokenIndex = 0;
                            tokenIndex < oldTokens.Count; tokenIndex++)
                    {
                        IToken oldToken = oldTokens[tokenIndex];
                        IToken newToken = newTokens[tokenIndex];

                        if ((oldToken == null) || (newToken == null))
                            continue;

                        newToken.FileName = oldToken.FileName;
                        newToken.StartLine = oldToken.StartLine;
                        newToken.EndLine = oldToken.EndLine;
                        newToken.ViaSource = oldToken.ViaSource;
                    }
                }

                commandParseState.MakeImmutable();

                //
                // NOTE: Precompute the command's word walk: record the token
                //       index of every word and pre-evaluate each STATIC word
                //       (a single text token without the "{*}" expansion
                //       prefix), whose value is identical on every execution,
                //       into a reusable argument.  Dynamic words (variable or
                //       command substitutions, backslashes, compound words)
                //       are left null and re-substituted on every execution.
                //
                int commandWords = commandParseState.CommandWords;
                int[] wordTokenIndexes = null;
                Argument[] staticWords = null;

                if (commandWords > 0)
                {
                    wordTokenIndexes = new int[commandWords];
                    staticWords = new Argument[commandWords];

                    TokenList commandTokens = commandParseState.Tokens;
                    IToken wordToken = null;
                    int wordTokenIndex = 0;
                    bool walkOk = true;

                    for (int wordsUsed = 0; wordsUsed < commandWords; wordsUsed++)
                    {
                        if (wordToken != null)
                            wordTokenIndex += (wordToken.Components + 1);

                        if ((wordTokenIndex < 0) ||
                            (wordTokenIndex >= commandTokens.Count))
                        {
                            walkOk = false;
                            break;
                        }

                        wordToken = commandTokens[wordTokenIndex];
                        wordTokenIndexes[wordsUsed] = wordTokenIndex;

                        if ((wordToken.Components == 1) &&
                            ((wordToken.Flags & TokenFlags.Expand) !=
                                TokenFlags.Expand) &&
                            (wordTokenIndex + 1 < commandTokens.Count))
                        {
                            IToken componentToken =
                                commandTokens[wordTokenIndex + 1];

                            if ((componentToken != null) &&
                                (componentToken.Type == TokenType.Text))
                            {
                                string wordValue = text.Substring(
                                    componentToken.Start,
                                    componentToken.Length);

                                staticWords[wordsUsed] = Argument.GetOrCreate(
                                    interpreter, (Result)wordValue,
                                    true /* createOnly */);
                            }
                        }
                    }

                    if (!walkOk)
                    {
                        wordTokenIndexes = null;
                        staticWords = null;
                    }
                }

                commands.Add(new CachedScriptCommand(
                    commandParseState, wordTokenIndexes, staticWords));

                int nextIndex = parseState.CommandStart +
                    parseState.CommandLength;

                charactersLeft -= (nextIndex - index);
                index = nextIndex;
                parseState.Tokens.Clear();

                if (nested && (terminator < text.Length) &&
                    (text[terminator] == Characters.CloseBracket))
                {
                    break;
                }
            } while (charactersLeft > 0);

            return new CachedScriptCommands(commands.ToArray());
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Scope Call Frame Methods
        /// <summary>
        /// This method evaluates the specified script text within a brand new
        /// scope call frame, using the supplied flags.  The scope call frame
        /// created for the evaluation is stored into the result so it remains
        /// accessible to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script (and,
        /// via the stored scope call frame, the created scope).  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateScriptWithScopeFrame(
            Interpreter interpreter,             /* in */
            string text,                         /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            )
        {
            ReturnCode code;
            ICallFrame frame = null;

            code = EvaluateScriptWithScopeFrame(
                interpreter, text, engineFlags, substitutionFlags,
                eventFlags, expressionFlags, ref frame, ref result,
                ref errorLine);

            /* IGNORED */
            CallFrameOps.MaybeStoreInto(frame, true, ref result);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified script text within a scope call
        /// frame, using the supplied flags.  When a scope call frame is
        /// provided, it is used; otherwise, a new engine scope is created and
        /// added to the interpreter.  After evaluation, all scope call frames
        /// opened during the script are popped and a reference to any newly
        /// created frame is returned to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="frame">
        /// On input, an existing scope call frame to use, or null to create a
        /// new one; on output, the newly created scope call frame, when one was
        /// created and the interpreter remains usable.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode EvaluateScriptWithScopeFrame(
            Interpreter interpreter,             /* in */
            string text,                         /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref ICallFrame frame,                /* in, out */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            if ((frame != null) &&
                !interpreter.IsScopeCallFrame(frame, ref result))
            {
                return ReturnCode.Error;
            }

            ICallFrame localFrame = null;
            ICallFrame newFrame = null;

            if (frame != null)
            {
                localFrame = frame;
            }
            else
            {
                newFrame = CallFrameOps.NewEngineScope(interpreter);

                if (interpreter.AddScope(newFrame,
                        ClientData.Empty, ref result) != ReturnCode.Ok)
                {
                    return ReturnCode.Error;
                }

                localFrame = newFrame;
            }

            interpreter.PushCallFrame(localFrame);

            try
            {
                return EvaluateScript(interpreter,
                    text, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, ref result,
                    ref errorLine);
            }
            finally
            {
                usable = IsUsableNoLock(interpreter);

                if (usable)
                {
                    //
                    // NOTE: Pop all scope call frames, including the brand
                    //       new one that we pushed above, just in case any
                    //       scope call frames were left open by the script
                    //       being evaluated.
                    //
                    /* IGNORED */
                    interpreter.PopScopeCallFrames();

                    //
                    // NOTE: Finally, return reference to the newly created
                    //       call frame to our caller.  It is possible that
                    //       this frame was actually deleted by the script.
                    //
                    if (newFrame != null)
                        frame = newFrame;
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Asynchronous Methods
        /// <summary>
        /// This method evaluates the specified script text asynchronously in
        /// the context of the given interpreter, either on a newly created
        /// engine thread or via a queued work item.  It is a top-level entry
        /// point and is thread-safe, re-entrant, and asynchronous; it returns
        /// as soon as the work has been scheduled, and the optional callback is
        /// later invoked with the evaluation outcome.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="text">
        /// The script text to evaluate.  If this parameter is null, an error
        /// is returned.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="callback">
        /// The callback to invoke when the asynchronous evaluation completes.
        /// This parameter may be null for "fire-and-forget" scripts.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-defined data passed through to the callback.
        /// This parameter may be null.
        /// </param>
        /// <param name="error">
        /// Upon failure to schedule the asynchronous evaluation, this contains
        /// an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the asynchronous evaluation was
        /// successfully scheduled; otherwise, <see cref="ReturnCode.Error" />
        /// with details placed in <paramref name="error" />.
        /// </returns>
        public static ReturnCode EvaluateScript(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            AsynchronousCallback callback, /* NOTE: May be null for "fire-and-forget" type scripts. */
            IClientData clientData,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT, ASYNCHRONOUS */
        {
            if (interpreter != null)
            {
                if (text != null)
                {
                    try
                    {
                        if (ScriptOps.HasFlags(
                                interpreter, InterpreterFlags.UseNewEngineThread,
                                true))
                        {
                            Thread thread = CreateThread(
                                interpreter, EngineThreadStart, 0,
                                true, false, true);

                            if (thread == null)
                            {
                                error = "failed to create engine thread";
                                return ReturnCode.Error;
                            }

                            eventFlags &= ~EventFlags.DisposeThread;

                            thread.Start(new AsynchronousContext(
                                GlobalState.GetCurrentSystemThreadId(),
                                EngineMode.EvaluateScript, interpreter,
                                text, engineFlags, substitutionFlags,
                                eventFlags, expressionFlags, callback,
                                clientData));

                            return ReturnCode.Ok;
                        }
                        else
                        {
                            eventFlags |= EventFlags.DisposeThread;

                            if (QueueWorkItem(interpreter, EngineThreadStart,
                                    new AsynchronousContext(
                                        GlobalState.GetCurrentSystemThreadId(),
                                        EngineMode.EvaluateScript, interpreter,
                                        text, engineFlags, substitutionFlags,
                                        eventFlags, expressionFlags, callback,
                                        clientData), ThreadOps.GetQueueFlags(
                                        false)))
                            {
                                return ReturnCode.Ok;
                            }
                            else
                            {
                                error = "could not queue work item";
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                }
                else
                {
                    error = "invalid script";
                }
            }
            else
            {
                error = "invalid interpreter";
            }

            return ReturnCode.Error;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (Stream) Methods
        /// <summary>
        /// This method evaluates a portion of the script obtained from the
        /// specified text reader in the context of the given interpreter,
        /// using the supplied flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  This overload records the error line
        /// (if any) on the interpreter automatically; use the overload that
        /// accepts a <c>ref int errorLine</c> when the caller needs the error
        /// line directly.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="name">
        /// The name associated with the stream, used for error and location
        /// reporting.  This parameter may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader from which the script is read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the stream at which to begin
        /// evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the stream.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateStream(
            Interpreter interpreter,
            string name,
            TextReader textReader,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateStream(
                interpreter, name, textReader, startIndex, characters,
                engineFlags, substitutionFlags, eventFlags, expressionFlags,
                ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This method is somewhat special.  It is the only place where the stream
        //       evaluation pipeline (via EvaluateStream) ends up calling into the string
        //       evaluation pipeline (via EvaluateScript).  Therefore, "special handling"
        //       for that transition (e.g. call frame management) should happen here [and
        //       only here].
        //
        /// <summary>
        /// This method evaluates a portion of the script obtained from the
        /// specified text reader in the context of the given interpreter,
        /// additionally reporting the script line associated with any error.
        /// It is a top-level entry point and is thread-safe and re-entrant.
        /// This is the bridge between the stream evaluation pipeline and the
        /// string evaluation pipeline; the call frame management for that
        /// transition is performed here.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="name">
        /// The name associated with the stream, used for error and location
        /// reporting.  This parameter may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader from which the script is read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the stream at which to begin
        /// evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the stream.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateStream(
            Interpreter interpreter,
            string name,
            TextReader textReader,
            int startIndex,
            int characters,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result,
            ref int errorLine
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            ReadInt32Callback charCallback = null;
            ReadCharsCallback charsCallback = null;

            GetStreamCallbacks(
                textReader, ref charCallback, ref charsCallback);

            ReturnCode code;
            RSCD readScriptClientData = null;
            bool canRetry = false; /* NOT USED */

            code = ReadScriptStream(
                interpreter, null, name, charCallback, charsCallback,
                startIndex, characters, ref engineFlags,
                ref substitutionFlags, ref eventFlags,
                ref expressionFlags, ref readScriptClientData,
                ref canRetry, ref result);

            if (code == ReturnCode.Ok)
            {
                string text = readScriptClientData.Text;

                bool newFrame = EngineFlagOps.HasExtraCallFrame(
                    engineFlags);

                if (newFrame)
                {
                    ICallFrame frame = interpreter.NewEngineCallFrame(
                        StringList.MakeList("stream", name),
                        CallFrameFlags.Engine);

                    interpreter.PushAutomaticCallFrame(frame);
                }

                try
                {
#if RESULT_LIMITS
                    int executeResultLimit = interpreter.InternalExecuteResultLimit;
                    int nestedResultLimit = interpreter.InternalNestedResultLimit;
#endif

                    //
                    // BUGFIX: We need to know if this is the primary AppDomain
                    //         for the interpreter so we can check (potentially
                    //         many times) if the "cached" ParseState for the
                    //         interpreter needs to be manually refreshed from
                    //         within the main command loop (below).
                    //
                    bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                    bool argumentLocation = HasArgumentLocation(interpreter);
#endif

                    code = EvaluateScript(
                        interpreter, text, startIndex, characters,
                        engineFlags, substitutionFlags, eventFlags,
                        expressionFlags,
#if RESULT_LIMITS
                        executeResultLimit, nestedResultLimit,
#endif
                        sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                        argumentLocation,
#endif
                        ref result, ref errorLine);

                    if (code == ReturnCode.Return)
                    {
                        code = UpdateReturnInformation(interpreter);
                    }
                    else if (code == ReturnCode.Error)
                    {
                        /* IGNORED */
                        AddErrorInformation(interpreter, result,
                            String.Format(
                                "{0}    (stream \"{1}\" line {2})",
                                Environment.NewLine,
                                FormatOps.Ellipsis(name),
                                errorLine));
                    }
                }
                finally
                {
                    if (newFrame)
                    {
                        //
                        // NOTE: Pop the original call frame that we
                        //       pushed above and any intervening scope
                        //       call frames that may be leftover (i.e.
                        //       they were not explicitly closed).
                        //
                        /* IGNORED */
                        interpreter.PopScopeCallFramesAndOneMore();
                    }
                }
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Scope Call Frame Methods
        /// <summary>
        /// This method evaluates a portion of the script obtained from the
        /// specified text reader within a brand new scope call frame, using the
        /// supplied flags.  It is a top-level entry point and is thread-safe
        /// and re-entrant.  The scope call frame created for the evaluation is
        /// stored into the result so it remains accessible to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="name">
        /// The name associated with the stream, used for error and location
        /// reporting.  This parameter may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader from which the script is read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the stream at which to begin
        /// evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the stream.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script (and,
        /// via the stored scope call frame, the created scope).  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateStreamWithScopeFrame(
            Interpreter interpreter,             /* in */
            string name,                         /* in */
            TextReader textReader,               /* in */
            int startIndex,                      /* in */
            int characters,                      /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;
            ICallFrame frame = null;

            code = EvaluateStreamWithScopeFrame(
                interpreter, name, textReader, startIndex,
                characters, engineFlags, substitutionFlags,
                eventFlags, expressionFlags, ref frame,
                ref result, ref errorLine);

            /* IGNORED */
            CallFrameOps.MaybeStoreInto(frame, true, ref result);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates a portion of the script obtained from the
        /// specified text reader within a scope call frame, using the supplied
        /// flags.  It is a top-level entry point and is thread-safe and
        /// re-entrant.  When a scope call frame is provided, it is used;
        /// otherwise, a new engine scope is created and added to the
        /// interpreter.  After evaluation, all scope call frames opened during
        /// the script are popped and a reference to any newly created frame is
        /// returned to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="name">
        /// The name associated with the stream, used for error and location
        /// reporting.  This parameter may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader from which the script is read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the stream at which to begin
        /// evaluation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to evaluate, or
        /// <see cref="Length.Invalid" /> to evaluate to the end of the stream.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="frame">
        /// On input, an existing scope call frame to use, or null to create a
        /// new one; on output, the newly created scope call frame, when one was
        /// created and the interpreter remains usable.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode EvaluateStreamWithScopeFrame(
            Interpreter interpreter,             /* in */
            string name,                         /* in */
            TextReader textReader,               /* in */
            int startIndex,                      /* in */
            int characters,                      /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref ICallFrame frame,                /* in, out */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            if ((frame != null) &&
                !interpreter.IsScopeCallFrame(frame, ref result))
            {
                return ReturnCode.Error;
            }

            ICallFrame localFrame = null;
            ICallFrame newFrame = null;

            if (frame != null)
            {
                localFrame = frame;
            }
            else
            {
                newFrame = CallFrameOps.NewEngineScope(interpreter);

                if (interpreter.AddScope(newFrame,
                        ClientData.Empty, ref result) != ReturnCode.Ok)
                {
                    return ReturnCode.Error;
                }

                localFrame = newFrame;
            }

            interpreter.PushCallFrame(localFrame);

            try
            {
                return EvaluateStream(
                    interpreter, name, textReader, startIndex,
                    characters, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, ref result,
                    ref errorLine);
            }
            finally
            {
                usable = IsUsableNoLock(interpreter);

                if (usable)
                {
                    //
                    // NOTE: Pop all scope call frames, including the brand
                    //       new one that we pushed above, just in case any
                    //       scope call frames were left open by the script
                    //       being evaluated.
                    //
                    /* IGNORED */
                    interpreter.PopScopeCallFrames();

                    //
                    // NOTE: Finally, return reference to the newly created
                    //       call frame to our caller.  It is possible that
                    //       this frame was actually deleted by the script.
                    //
                    if (newFrame != null)
                        frame = newFrame;
                }
            }
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (File) Methods
        /// <summary>
        /// This method evaluates the script contained in the specified file in
        /// the context of the given interpreter, using the default engine,
        /// substitution, event, and expression flags.  It is a top-level entry
        /// point and is thread-safe and re-entrant.  This overload records the
        /// error line (if any) on the interpreter automatically.  Most
        /// consumers should call the equivalent <see cref="Interpreter" />
        /// evaluation method instead, which manages interpreter state on the
        /// caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.  Control-flow values such as
        /// <see cref="ReturnCode.Return" />, <see cref="ReturnCode.Break" />,
        /// and <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateFile(
            Interpreter interpreter,
            string fileName,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateFile(
                interpreter, fileName, EngineFlags.None,
                SubstitutionFlags.Default, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the script contained in the specified file in
        /// the context of the given interpreter, using the supplied engine,
        /// substitution, event, and expression flags.  It is a top-level entry
        /// point and is thread-safe and re-entrant.  This overload records the
        /// error line (if any) on the interpreter automatically; use the
        /// overload that accepts a <c>ref int errorLine</c> when the caller
        /// needs the error line directly.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateFile(
            Interpreter interpreter,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateFile(
                interpreter, fileName, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
                ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the script contained in the specified file in
        /// the context of the given interpreter, using the supplied flags,
        /// additionally reporting the script line associated with any error.
        /// It is a top-level entry point and is thread-safe and re-entrant.
        /// Most consumers should call the equivalent <see cref="Interpreter" />
        /// evaluation method instead, which manages interpreter state on the
        /// caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateFile(
            Interpreter interpreter,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result,
            ref int errorLine
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateFile(
                interpreter, null, fileName, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
                ref result, ref errorLine);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the script contained in the specified file in
        /// the context of the given interpreter, reading the file using the
        /// supplied character encoding and the default flags.  It is a
        /// top-level entry point and is thread-safe and re-entrant.  This
        /// overload records the error line (if any) on the interpreter
        /// automatically.  Most consumers should call the equivalent
        /// <see cref="Interpreter" /> evaluation method instead, which manages
        /// interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        public static ReturnCode EvaluateFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateFile(
                interpreter, encoding, fileName, EngineFlags.None,
                SubstitutionFlags.Default, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the script contained in the specified file in
        /// the context of the given interpreter, reading the file using the
        /// supplied character encoding and flags.  It is thread-safe and
        /// re-entrant.  This overload records the error line (if any) on the
        /// interpreter automatically; use the overload that accepts a
        /// <c>ref int errorLine</c> when the caller needs the error line
        /// directly.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        private static ReturnCode EvaluateFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            int errorLine = 0;

            ReturnCode code = EvaluateFile(
                interpreter, encoding, fileName, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
                ref result, ref errorLine);

            if (errorLine != 0)
                Interpreter.SetErrorLine(interpreter, errorLine);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This method is somewhat special.  It is the only place where the file
        //       evaluation pipeline (via EvaluateFile) ends up calling into the string
        //       evaluation pipeline (via EvaluateScript).  Therefore, "special handling"
        //       for that transition (e.g. call frame management) should happen here [and
        //       only here].
        //
        /// <summary>
        /// This method is the core file-evaluation routine.  It reads (or
        /// otherwise obtains) the script from the specified file, optionally
        /// discovers temporary packages, optionally pushes a dedicated engine
        /// call frame, tracks the script location, and then evaluates the
        /// script text in the context of the given interpreter.  It is the
        /// bridge between the file evaluation pipeline and the string
        /// evaluation pipeline.  It is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.  It may be
        /// adjusted while the script is read or obtained.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.  Control-flow
        /// values such as <see cref="ReturnCode.Return" />,
        /// <see cref="ReturnCode.Break" />, and
        /// <see cref="ReturnCode.Continue" /> may also propagate out.
        /// </returns>
        internal static ReturnCode EvaluateFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result,
            ref int errorLine
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;

            if (interpreter != null)
            {
                int levels = interpreter.EnterEngineScriptFileLevel();

                try
                {
                    string text = null;

                    code = ReadOrGetScriptFile(
                        interpreter, encoding, ref fileName,
                        ref engineFlags, ref substitutionFlags,
                        ref eventFlags, ref expressionFlags,
                        ref text, ref result);

                    if (code == ReturnCode.Ok)
                    {
                        bool temporaryPackages = false;

                        try
                        {
                            if (interpreter.HasTemporaryPackages() &&
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                !interpreter.HasLibraryScriptPending() &&
#endif
                                (interpreter.PackageIndexLevels == 0))
                            {
                                temporaryPackages = true;

                                code = PackageOps.FindAll(
                                    interpreter, new StringList(
                                    PathOps.GetDirectoryName(fileName)),
                                    PackageIndexFlags.EvaluateFile,
                                    interpreter.PathComparisonType,
                                    ref result);
                            }

                            if (code == ReturnCode.Ok)
                            {
                                bool newFrame = EngineFlagOps.HasExtraCallFrame(
                                    engineFlags);

                                if (newFrame)
                                {
                                    ICallFrame frame = interpreter.NewEngineCallFrame(
                                        StringList.MakeList("file", fileName),
                                        CallFrameFlags.Engine);

                                    interpreter.PushAutomaticCallFrame(frame);
                                }

                                try
                                {
                                    bool pushed = false;

                                    interpreter.PushScriptLocation(fileName, true, ref pushed);

                                    try
                                    {
#if RESULT_LIMITS
                                        int executeResultLimit = interpreter.InternalExecuteResultLimit;
                                        int nestedResultLimit = interpreter.InternalNestedResultLimit;
#endif

                                        //
                                        // BUGFIX: We need to know if this is the primary AppDomain
                                        //         for the interpreter so we can check (potentially
                                        //         many times) if the "cached" ParseState for the
                                        //         interpreter needs to be manually refreshed from
                                        //         within the main command loop (below).
                                        //
                                        bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                        bool argumentLocation = HasArgumentLocation(interpreter);
#endif

                                        code = EvaluateScript(
                                            interpreter, fileName, Parser.StartLine,
                                            text, 0, Length.Invalid, engineFlags,
                                            substitutionFlags, eventFlags,
                                            expressionFlags,
#if RESULT_LIMITS
                                            executeResultLimit, nestedResultLimit,
#endif
                                            sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                            argumentLocation,
#endif
                                            ref result, ref errorLine);

                                        if (code == ReturnCode.Return)
                                        {
                                            code = UpdateReturnInformation(interpreter);
                                        }
                                        else if (code == ReturnCode.Error)
                                        {
                                            /* IGNORED */
                                            AddErrorInformation(interpreter, result,
                                                String.Format(
                                                    "{0}    (file \"{1}\" line {2})",
                                                    Environment.NewLine,
                                                    FormatOps.Ellipsis(fileName),
                                                    errorLine));
                                        }
                                    }
                                    finally
                                    {
                                        interpreter.PopScriptLocation(true, ref pushed);
                                    }
                                }
                                finally
                                {
                                    if (newFrame)
                                    {
                                        //
                                        // NOTE: Pop the original call frame that we
                                        //       pushed above and any intervening scope
                                        //       call frames that may be leftover (i.e.
                                        //       they were not explicitly closed).
                                        //
                                        /* IGNORED */
                                        interpreter.PopScopeCallFramesAndOneMore();
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (temporaryPackages && (levels == 1)) /* OUTERMOST? */
                            {
                                ReturnCode packageCode;
                                Result packageError = null;
                                LongList tokens = null; /* NOT USED */

                                packageCode = interpreter.RemoveTemporaryPackages(
                                    ClientData.Empty, ref tokens, ref packageError);

                                if (packageCode == ReturnCode.Ok)
                                {
                                    if (tokens != null)
                                    {
                                        TraceOps.DebugTrace(String.Format(
                                            "EvaluateFile: removed temporary packages: {0}",
                                            tokens), typeof(Engine).Name,
                                            TracePriority.PackageDebug);
                                    }
                                }
                                else
                                {
                                    DebugOps.Complain(
                                        interpreter, packageCode, packageError);
                                }
                            }
                        }
                    }

#if NOTIFY
                    if (!EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.File, NotifyFlags.Evaluated,
                            new ObjectTriplet(encoding, fileName, code),
                            interpreter, null, null, null, ref result);
                    }
#endif
                }
                finally
                {
                    /* IGNORED */
                    interpreter.ExitScriptFileLevel();
                }
            }
            else
            {
                result = "invalid interpreter";
                code = ReturnCode.Error;
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Scope Call Frame Methods
        /// <summary>
        /// This method evaluates the script contained in the specified file
        /// within a brand new scope call frame, using the supplied character
        /// encoding and flags.  It is thread-safe and re-entrant.  The scope
        /// call frame created for the evaluation is stored into the result so
        /// it remains accessible to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script (and,
        /// via the stored scope call frame, the created scope).  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateFileWithScopeFrame(
            Interpreter interpreter,             /* in */
            Encoding encoding,                   /* in */
            string fileName,                     /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;
            ICallFrame frame = null;

            code = EvaluateFileWithScopeFrame(
                interpreter, encoding, fileName, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
                ref frame, ref result, ref errorLine);

            /* IGNORED */
            CallFrameOps.MaybeStoreInto(frame, true, ref result);

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the script contained in the specified file
        /// within a scope call frame, using the supplied character encoding and
        /// flags.  It is thread-safe and re-entrant.  When a scope call frame
        /// is provided, it is used; otherwise, a new engine scope is created and
        /// added to the interpreter.  After evaluation, all scope call frames
        /// opened during the script are popped and a reference to any newly
        /// created frame is returned to the caller.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="frame">
        /// On input, an existing scope call frame to use, or null to create a
        /// new one; on output, the newly created scope call frame, when one was
        /// created and the interpreter remains usable.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode EvaluateFileWithScopeFrame(
            Interpreter interpreter,             /* in */
            Encoding encoding,                   /* in */
            string fileName,                     /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref ICallFrame frame,                /* in, out */
            ref Result result,                   /* out */
            ref int errorLine                    /* out */
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            if ((frame != null) &&
                !interpreter.IsScopeCallFrame(frame, ref result))
            {
                return ReturnCode.Error;
            }

            ICallFrame localFrame = null;
            ICallFrame newFrame = null;

            if (frame != null)
            {
                localFrame = frame;
            }
            else
            {
                newFrame = CallFrameOps.NewEngineScope(interpreter);

                if (interpreter.AddScope(newFrame,
                        ClientData.Empty, ref result) != ReturnCode.Ok)
                {
                    return ReturnCode.Error;
                }

                localFrame = newFrame;
            }

            interpreter.PushCallFrame(localFrame);

            try
            {
                return EvaluateFile(
                    interpreter, encoding, fileName, engineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
                    ref result, ref errorLine);
            }
            finally
            {
                usable = IsUsableNoLock(interpreter);

                if (usable)
                {
                    //
                    // NOTE: Pop all scope call frames, including the brand
                    //       new one that we pushed above, just in case any
                    //       scope call frames were left open by the script
                    //       being evaluated.
                    //
                    /* IGNORED */
                    interpreter.PopScopeCallFrames();

                    //
                    // NOTE: Finally, return reference to the newly created
                    //       call frame to our caller.  It is possible that
                    //       this frame was actually deleted by the script.
                    //
                    if (newFrame != null)
                        frame = newFrame;
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Asynchronous Methods
        /// <summary>
        /// This method evaluates the script contained in the specified file
        /// asynchronously in the context of the given interpreter, either on a
        /// newly created engine thread or via a queued work item.  It is a
        /// top-level entry point and is thread-safe, re-entrant, and
        /// asynchronous; it returns as soon as the work has been scheduled, and
        /// the optional callback is later invoked with the evaluation outcome.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="fileName">
        /// The name of the file containing the script to evaluate.  If this
        /// parameter is null or empty, an error is returned.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="callback">
        /// The callback to invoke when the asynchronous evaluation completes.
        /// This parameter may be null for "fire-and-forget" scripts.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-defined data passed through to the callback.
        /// This parameter may be null.
        /// </param>
        /// <param name="error">
        /// Upon failure to schedule the asynchronous evaluation, this contains
        /// an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the asynchronous evaluation was
        /// successfully scheduled; otherwise, <see cref="ReturnCode.Error" />
        /// with details placed in <paramref name="error" />.
        /// </returns>
        public static ReturnCode EvaluateFile(
            Interpreter interpreter,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            AsynchronousCallback callback, /* NOTE: May be null for "fire-and-forget" type scripts. */
            IClientData clientData,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT, ASYNCHRONOUS */
        {
            if (interpreter != null)
            {
                if (!String.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        if (ScriptOps.HasFlags(
                                interpreter, InterpreterFlags.UseNewEngineThread,
                                true))
                        {
                            Thread thread = CreateThread(
                                interpreter, EngineThreadStart, 0,
                                true, false, true);

                            if (thread == null)
                            {
                                error = "failed to create engine thread";
                                return ReturnCode.Error;
                            }

                            eventFlags &= ~EventFlags.DisposeThread;

                            thread.Start(new AsynchronousContext(
                                GlobalState.GetCurrentSystemThreadId(),
                                EngineMode.EvaluateFile, interpreter,
                                fileName, engineFlags, substitutionFlags,
                                eventFlags, expressionFlags, callback,
                                clientData));

                            return ReturnCode.Ok;
                        }
                        else
                        {
                            eventFlags |= EventFlags.DisposeThread;

                            if (QueueWorkItem(interpreter, EngineThreadStart,
                                    new AsynchronousContext(
                                        GlobalState.GetCurrentSystemThreadId(),
                                        EngineMode.EvaluateFile, interpreter,
                                        fileName, engineFlags, substitutionFlags,
                                        eventFlags, expressionFlags, callback,
                                        clientData), ThreadOps.GetQueueFlags(
                                        false)))
                            {
                                return ReturnCode.Ok;
                            }
                            else
                            {
                                error = "could not queue work item";
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                }
                else
                {
                    error = "invalid file name";
                }
            }
            else
            {
                error = "invalid interpreter";
            }

            return ReturnCode.Error;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (Bundle) Methods
        /// <summary>
        /// This method evaluates the specified compiled script using the
        /// settings carried by the supplied bundle data, which may select a
        /// particular interpreter, isolation level, security level, and rule
        /// set.  The bundle language must match the current package; otherwise,
        /// an error is returned.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the script.  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="script">
        /// The compiled script to evaluate.  If this parameter is null, an
        /// error is returned.
        /// </param>
        /// <param name="bundleData">
        /// The bundle data describing how the script should be evaluated (for
        /// example, language, interpreter, isolation, and security).  If this
        /// parameter is null, an error is returned.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result produced by the script.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon failure, this is set to the script line number associated with
        /// the error, or zero when not applicable.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateScript(
            Interpreter interpreter, /* in */
            IScript script,          /* in */
            IBundleData bundleData,  /* in */
            ref Result result,       /* out */
            ref int errorLine        /* out */
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            if (script == null)
            {
                result = "invalid script";
                return ReturnCode.Error;
            }

            if (bundleData == null)
            {
                result = "invalid bundle data";
                return ReturnCode.Error;
            }

            string language = bundleData.Language;

            if (!SharedStringOps.SystemEquals(
                    language, GlobalState.GetPackageName()))
            {
                result = String.Format(
                    "bundle language mismatch: {0}", language);

                return ReturnCode.Error;
            }

            Interpreter localInterpreter = bundleData.Interpreter;
            IsolationLevel isolationLevel = bundleData.IsolationLevel;
            SecurityLevel securityLevel = bundleData.SecurityLevel;
            IRuleSet ruleSet = bundleData.RuleSet;
            bool? isolated = null;

            switch (isolationLevel)
            {
                case IsolationLevel.None:
                    {
                        if (!interpreter.MatchSecurityLevel(
                                securityLevel))
                        {
                            result = String.Format(
                                "script {0} cannot use " +
                                "security level {1}",
                                script.Id, securityLevel);

                            return ReturnCode.Error;
                        }

                        if (ruleSet != null)
                        {
                            result = String.Format(
                                "script {0} cannot use " +
                                "ruleset with isolation {1}",
                                script.Id, isolationLevel);

                            return ReturnCode.Error;
                        }

                        if (ScriptOps.EnableOrDisableSecurity(
                                interpreter, true, true,
                                ref result) != ReturnCode.Ok)
                        {
                            return ReturnCode.Error;
                        }

                        return EvaluateScript(
                            interpreter, script, ref result,
                            ref errorLine);
                    }
                case IsolationLevel.Interpreter:
                    {
                        isolated = false;
                        goto case IsolationLevel.Isolated;
                    }
                case IsolationLevel.AppDomain:
                    {
#if ISOLATED_INTERPRETERS
                        isolated = true;
                        goto case IsolationLevel.Isolated;
#else
                        result = String.Format(
                            "unimplemented isolation level {0}",
                            isolationLevel);

                        return ReturnCode.Error;
#endif
                    }
                case IsolationLevel.AppDomainOrInterpreter:
                    {
#if ISOLATED_INTERPRETERS
                        goto case IsolationLevel.AppDomain;
#else
                        goto case IsolationLevel.Interpreter;
#endif
                    }
                case IsolationLevel.Isolated:
                    {
                        if (localInterpreter == null)
                        {
                            try
                            {
                                if (isolated == null)
                                {
                                    result = "invalid isolation flag";
                                    return ReturnCode.Error;
                                }

                                IInterpreterSettings interpreterSettings =
                                    InterpreterSettings.Create(
                                        ruleSet, null, securityLevel,
                                        ref result);

                                if (interpreterSettings == null)
                                    return ReturnCode.Error;

                                if (ruleSet != null)
                                    interpreterSettings.DisableInitialize();

                                if (interpreter.CreateChildInterpreter(
                                        null, null, interpreterSettings,
                                        (bool)isolated, true /* security */,
                                        ref result) != ReturnCode.Ok)
                                {
                                    return ReturnCode.Error;
                                }

                                string path = result;

                                if (interpreter.GetChildInterpreter(
                                        path, LookupFlags.Interpreter,
                                        true, false, ref localInterpreter,
                                        ref result) != ReturnCode.Ok)
                                {
                                    return ReturnCode.Error;
                                }

                                if (localInterpreter == null)
                                {
                                    result = "invalid child interpreter";
                                    return ReturnCode.Error;
                                }
                            }
                            finally
                            {
                                if (localInterpreter != null)
                                    bundleData.Interpreter = localInterpreter;
                            }
                        }

                        return EvaluateScript(
                            localInterpreter, script, ref result, ref errorLine);
                    }
                default:
                    {
                        result = String.Format(
                            "unsupported isolation level {0}",
                            isolationLevel);

                        return ReturnCode.Error;
                    }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Evaluation (Expression) Methods
        /// <summary>
        /// This method evaluates the specified expression text in the context
        /// of the given interpreter and, on error, appends the supplied error
        /// information (formatted with the newline and the error line) to the
        /// result.  It is intended for internal use only.  It is a top-level
        /// entry point and is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the expression.
        /// </param>
        /// <param name="text">
        /// The expression text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="errorInfo">
        /// A composite format string used to build the error information that
        /// is appended on failure; it receives the newline and the error line
        /// as format arguments.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the value produced by the expression.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode EvaluateExpressionWithErrorInfo( /* INTERNAL USE ONLY */
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            string errorInfo,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            //
            // BUGFIX: We need to know if this is the primary AppDomain
            //         for the interpreter so we can check (potentially
            //         many times) if the "cached" ParseState for the
            //         interpreter needs to be manually refreshed from
            //         within the main command loop (below).
            //
            bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation = HasArgumentLocation(interpreter);
#endif

            //
            // FIXME: The expression parser does not know the line where
            //        the error happened unless it evaluates a command
            //        contained within the expression.
            //
            Interpreter.SetErrorLine(interpreter, 0);

            ReturnCode code = EvaluateExpression(
                interpreter, text, engineFlags, substitutionFlags,
                eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result);

            if (code == ReturnCode.Error)
            {
                /* IGNORED */
                AddErrorInformation(interpreter, result,
                    String.Format(errorInfo, Environment.NewLine,
                        Interpreter.GetErrorLine(interpreter)));
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified expression text in the context
        /// of the given interpreter, using the default engine, substitution,
        /// event, and expression flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  Most consumers should call the
        /// equivalent <see cref="Interpreter" /> expression evaluation method
        /// instead, which manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the expression.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The expression text to evaluate.  This parameter should not be null.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the value produced by the expression.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.
        /// </returns>
        public static ReturnCode EvaluateExpression(
            Interpreter interpreter,
            string text,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return EvaluateExpression(
                interpreter, text, EngineFlags.None,
                SubstitutionFlags.Default, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified expression text in the context
        /// of the given interpreter, using the supplied engine, substitution,
        /// event, and expression flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  It first resolves the current location
        /// (file name and line) for reporting purposes, then delegates to the
        /// core expression evaluation routine.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the expression.  If
        /// this parameter is not usable, an error is returned.
        /// </param>
        /// <param name="text">
        /// The expression text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the value produced by the expression.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode EvaluateExpression(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            string fileName = null;
            int currentLine = Parser.StartLine;

#if DEBUGGER && DEBUGGER_EXPRESSION && DEBUGGER_BREAKPOINTS
            if (ScriptOps.GetLocation(
                    interpreter, false, false, ref fileName,
                    ref currentLine, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }
#endif

#if RESULT_LIMITS
            int executeResultLimit = interpreter.InternalExecuteResultLimit;
            int nestedResultLimit = interpreter.InternalNestedResultLimit;
#endif

            //
            // BUGFIX: We need to know if this is the primary AppDomain
            //         for the interpreter so we can check (potentially
            //         many times) if the "cached" ParseState for the
            //         interpreter needs to be manually refreshed from
            //         within the main command loop (below).
            //
            bool sameAppDomain = AppDomainOps.IsSame(interpreter);

#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation = HasArgumentLocation(interpreter);
#endif

            return EvaluateExpression(
                interpreter, fileName, currentLine, text,
                engineFlags, substitutionFlags, eventFlags,
                expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method evaluates the specified expression text in the context
        /// of the given interpreter, using the supplied flags and limits.  It
        /// is thread-safe and re-entrant.  It resolves the current location
        /// (file name and line) for reporting purposes, then delegates to the
        /// core expression evaluation routine.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the expression.  If
        /// this parameter is not usable, an error is returned.
        /// </param>
        /// <param name="text">
        /// The expression text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the value produced by the expression.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        private static ReturnCode EvaluateExpression(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            string fileName = null;
            int currentLine = Parser.StartLine;

#if DEBUGGER && DEBUGGER_EXPRESSION && DEBUGGER_BREAKPOINTS
            if (ScriptOps.GetLocation(
                    interpreter, false, false, ref fileName,
                    ref currentLine, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }
#endif

            return EvaluateExpression(
                interpreter, fileName, currentLine, text, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                executeResultLimit, nestedResultLimit,
#endif
                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                argumentLocation,
#endif
                ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core expression-evaluation routine.  It parses
        /// the specified expression text and evaluates it in the context of the
        /// given interpreter, honoring events, cancellation, and the supplied
        /// flags and limits.  It is thread-safe and re-entrant.  All of the
        /// other expression evaluation overloads ultimately delegate here.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to evaluate the expression.  If
        /// this parameter is null or not usable, an error is returned.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the
        /// expression, used for error and location reporting.  This parameter
        /// may be null.
        /// </param>
        /// <param name="currentLine">
        /// The line number at which the expression begins.
        /// </param>
        /// <param name="text">
        /// The expression text to evaluate.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the evaluation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use for the evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the evaluation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size, in characters, permitted for the result produced
        /// by a single executed command.
        /// </param>
        /// <param name="nestedResultLimit">
        /// The maximum size, in characters, permitted for a nested result.
        /// </param>
        /// <param name="sameAppDomain">
        /// Non-zero if the interpreter belongs to the current application
        /// domain.
        /// </param>
        /// <param name="argumentLocation">
        /// Non-zero if argument source locations should be tracked for the
        /// debugger during evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the value produced by the expression.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        private static ReturnCode EvaluateExpression(
            Interpreter interpreter,
            string fileName,
            int currentLine,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            EngineFlags localEngineFlags = CombineFlags(
                interpreter, engineFlags, true, true);

            if (EngineFlagOps.HasNoEvaluate(localEngineFlags))
            {
                result = "interpreter not accepting expressions to evaluate";
                return ReturnCode.Error;
            }

            interpreter.ResetForEngine(
                localEngineFlags, GetCancelFlags(localEngineFlags),
                ref result);

            ReturnCode code;

            code = CheckEvents(
                interpreter, localEngineFlags, substitutionFlags,
                eventFlags, expressionFlags, ref result);

            if (code != ReturnCode.Ok)
                return code;

            bool noReady = EngineFlagOps.HasNoReady(localEngineFlags);
            IParseState parseState = null;

            /*
             * NOTE: This code is part of an experimental effort to
             *       improve performance and may be modified and/or
             *       removed later.
             */
#if PARSE_CACHE
            if (!interpreter.GetCachedParseState(text, ref parseState))
#endif
            {
                Result localError = null;

                parseState = new ParseState(
                    localEngineFlags, substitutionFlags, fileName, currentLine);

                code = ExpressionParser.ParseExpression(
                    interpreter, text, 0, Length.Invalid, parseState,
                    noReady, ref localError);

                if (code == ReturnCode.Ok)
                {
#if PARSE_CACHE
                    if (!EngineFlagOps.HasNoCacheParseState(localEngineFlags))
                    {
                        parseState.MakeImmutable();

                        /* IGNORED */
                        interpreter.AddCachedParseState(parseState);
                    }
#endif
                }
                else
                {
                    result = localError;
                    return code;
                }
            }

#if (DEBUGGER && DEBUGGER_EXPRESSION) || (NOTIFY && NOTIFY_EXPRESSION)
            IClientData parseStateClientData = null;
            ArgumentList arguments = null;
#endif

#if DEBUGGER && DEBUGGER_EXPRESSION
            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    localEngineFlags, BreakpointType.BeforeExpression))
            {
                arguments = new ArgumentList("text", text);

                code = CheckBreakpoints(
                    code, BreakpointType.BeforeExpression, null,
                    null, null, localEngineFlags, substitutionFlags,
                    eventFlags, expressionFlags, null, null,
                    interpreter, ClientData.WrapOrReplace(
                        parseStateClientData, parseState),
                    arguments, ref result);

                if (code != ReturnCode.Ok)
                    return code;
            }
#endif

            int savedExpressionLevels = 0;
            bool exception = false; /* NOT USED */
            Argument value = null;
            Result error = null;

            interpreter.PushSubExpression(ref savedExpressionLevels);

            try
            {
                code = ExpressionEvaluator.EvaluateSubExpression(
                    interpreter, parseState, 0, localEngineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit, nestedResultLimit,
#endif
                    noReady, sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                    argumentLocation,
#endif
                    ref usable, ref exception, ref value, ref error);
            }
            finally
            {
                interpreter.PopSubExpression(ref savedExpressionLevels);
            }

            if (code == ReturnCode.Ok)
                result = value;
            else
                result = error;

            if (usable)
            {
#if DEBUGGER && DEBUGGER_EXPRESSION
                if (DebuggerOps.CanHitBreakpoints(interpreter,
                        localEngineFlags, BreakpointType.AfterExpression))
                {
                    arguments = new ArgumentList(
                        "text", text, "value", value, "error", error);

                    code = CheckBreakpoints(
                        code, BreakpointType.AfterExpression, null,
                        null, null, localEngineFlags, substitutionFlags,
                        eventFlags, expressionFlags, null, null,
                        interpreter, ClientData.WrapOrReplace(
                            parseStateClientData, parseState),
                        arguments, ref result);
                }
#endif

#if NOTIFY && NOTIFY_EXPRESSION
                if (!EngineFlagOps.HasNoNotify(localEngineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Expression, NotifyFlags.Evaluated,
                        new ObjectList(fileName, currentLine, text,
                        localEngineFlags, substitutionFlags, eventFlags,
                        expressionFlags, code), interpreter,
                        ClientData.WrapOrReplace(
                            parseStateClientData, parseState),
                        arguments, null, ref result);
                }
#endif
            }

            return code;
        }
        #endregion
        #endregion
    }
}
