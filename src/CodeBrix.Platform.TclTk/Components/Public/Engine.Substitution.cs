/*
 * Engine.Substitution.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Substitution Methods" region group) so that no
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
        #region Substitution Methods
        #region Substitution Exit-Hook Methods
#if (DEBUGGER && DEBUGGER_ENGINE) || NOTIFY
        /// <summary>
        /// This method performs the bookkeeping that must occur when a string
        /// substitution completes (i.e. "exits").  It checks for and handles
        /// any applicable exit breakpoints and raises substitution
        /// notifications.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which the substitution was performed.
        /// This parameter may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the string
        /// that was substituted.  This parameter may be null.
        /// </param>
        /// <param name="currentLine">
        /// The current line number at the point of substitution exit.
        /// </param>
        /// <param name="text">
        /// The text that was substituted.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that were in effect for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that were in effect for the substitution.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags that were in effect for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags that were in effect for the substitution.
        /// </param>
        /// <param name="code">
        /// The return code produced by the substitution.  This value may be
        /// modified by an exit breakpoint.
        /// </param>
        /// <param name="result">
        /// The result produced by the substitution.  This value may be
        /// modified by an exit breakpoint.
        /// </param>
        /// <returns>
        /// The (possibly modified) return code for the completed substitution.
        /// </returns>
        private static ReturnCode SubstituteExited(
            Interpreter interpreter,             /* in */
            string fileName,                     /* in */
            int currentLine,                     /* in */
            string text,                         /* in */
            EngineFlags engineFlags,             /* in */
            SubstitutionFlags substitutionFlags, /* in */
            EventFlags eventFlags,               /* in */
            ExpressionFlags expressionFlags,     /* in */
            ref ReturnCode code,                 /* in, out */
            ref Result result                    /* in, out */
            )
        {
#if DEBUGGER && DEBUGGER_ENGINE
            BreakpointType breakpointType =
                BreakpointType.Exit | BreakpointType.Substitute;

            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    engineFlags, breakpointType))
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

#if NOTIFY
            if ((interpreter != null) &&
                !EngineFlagOps.HasNoNotify(engineFlags))
            {
                /* IGNORED */
                interpreter.CheckNotification(
                    NotifyType.String, NotifyFlags.Substituted,
                    //
                    // BUGBUG: In order to use this class for notification
                    //         parameters, it really should probably be
                    //         made public.
                    //
                    new ObjectList(fileName, currentLine, text,
                    engineFlags, substitutionFlags, eventFlags,
                    expressionFlags, code), interpreter, null,
                    null, null, ref result);
            }
#endif

            return code;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Substitution (Token) Methods
        /// <summary>
        /// This method performs substitution over a run of parser-state tokens
        /// in the context of the given interpreter, handling text, backslash,
        /// variable, and command tokens, and concatenating the substituted
        /// pieces into a single result.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.
        /// </param>
        /// <param name="parseState">
        /// The parser state that contains the tokens to substitute.
        /// </param>
        /// <param name="startTokenIndex">
        /// The index of the first token to substitute.
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
        /// debugger during substitution.
        /// </param>
        /// <param name="tokenCount">
        /// On input, the number of tokens to substitute; on output, the number
        /// of tokens that remain unprocessed (decremented as tokens are
        /// consumed).
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the concatenated, substituted result.
        /// Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        private static ReturnCode SubstituteTokens(
            Interpreter interpreter,
            IParseState parseState,
            int startTokenIndex,
#if RESULT_LIMITS
            int executeResultLimit,
            int nestedResultLimit,
#endif
            bool sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
            bool argumentLocation,
#endif
            ref int tokenCount,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;

            /*
             * Each pass through this loop will substitute one token, and its
             * components, if any.
             */

            string text = parseState.Text;
            CommandBuilder substResult = null;

            for (int tokenIndex = startTokenIndex;
                    (tokenCount > 0) && (code == ReturnCode.Ok);
                    tokenCount--, tokenIndex++)
            {
                int index = Index.Invalid;
                int length = 0;
                IToken token = parseState.Tokens[tokenIndex];
                Result localResult = null;

                switch (token.Type)
                {
                    case TokenType.Text:
                        {
                            index = token.Start;
                            length = token.Length;

                            break;
                        }
                    case TokenType.Backslash:
                        {
                            char? character1 = null;
                            char? character2 = null;

                            Parser.ParseBackslash(
                                text, token.Start, token.Length,
                                ref character1, ref character2);

                            localResult = Result.FromCharacters(character1, character2);

                            break;
                        }
                    case TokenType.Command:
                        {
                            code = CheckEvents(
                                interpreter, engineFlags, substitutionFlags,
                                eventFlags, expressionFlags, ref localResult);

                            if (code == ReturnCode.Ok)
                            {
                                code = EvaluateScript(
                                    interpreter, text, token.Start + 1,
                                    token.Length - 2, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags,
#if RESULT_LIMITS
                                    executeResultLimit, nestedResultLimit,
#endif
                                    sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                    argumentLocation,
#endif
                                    ref localResult);
                            }

                            if (code == ReturnCode.Error)
                                result = localResult;

                            break;
                        }
                    case TokenType.Variable:
                    case TokenType.VariableNameOnly:
                        {
                            string varName = null;
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
#endif
                                        ref localResult);
                                }

                                if (code == ReturnCode.Ok)
                                    varIndex = localResult;
                            }

                            if (code == ReturnCode.Ok)
                            {
                                varName = text.Substring(
                                    parseState.Tokens[tokenIndex + 1].Start,
                                    parseState.Tokens[tokenIndex + 1].Length);

                                code = GetTokenVariableValue(interpreter,
                                    varName, varIndex, ref localResult);
                            }

                            switch (code)
                            {
                                case ReturnCode.Ok:       /* Got value */
                                    {
                                        break;
                                    }
                                case ReturnCode.Error:    /* Give error message to caller. */
                                    {
                                        result = localResult;
                                        break;
                                    }
                                case ReturnCode.Break:    /* Will not substitute anyway */
                                case ReturnCode.Continue: /* Will not substitute anyway */
                                    {
                                        break;
                                    }
                                default:
                                    {
                                        /*
                                         * All other return codes, we will subst the result from the
                                         * code-throwing evaluation.
                                         */
                                        break;
                                    }
                            }

                            tokenCount -= token.Components;
                            tokenIndex += token.Components;

                            break;
                        }
                    default:
                        {
                            result = String.Format(
                                "unexpected token type {0} for substitution",
                                token.Type);

                            return ReturnCode.Error;
                        }
                }

                if ((code == ReturnCode.Break) || (code == ReturnCode.Continue))
                {
                    /*
                     * Inhibit substitution.
                     */
                    continue;
                }

                //
                // NOTE: If there was no result, there is now.
                //
                if (substResult == null)
                    substResult = CommandBuilder.Create();

                if (localResult != null) // INTL: do not change to String.IsNullOrEmpty
                {
#if RESULT_LIMITS
                    if (!substResult.HaveEnoughCapacity(
                            nestedResultLimit, localResult, ref result))
                    {
                        return ReturnCode.Error;
                    }
#endif

                    substResult.Add(localResult);
                }
                else if (index != Index.Invalid)
                {
#if RESULT_LIMITS
                    if (!substResult.HaveEnoughCapacity(
                            nestedResultLimit, length, ref result))
                    {
                        return ReturnCode.Error;
                    }
#endif

                    substResult.Add(text, index, length);
                }
            }

            if (code != ReturnCode.Error)
            {
                if (substResult != null)
                {
                    result = Result.FromCommandBuilder(substResult);
                }
                else
                {
                    /* IGNORED */
                    ResetResult(interpreter, engineFlags, ref result);
                }
            }

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Substitution (String) Methods
        //
        // WARNING: This method creates and disposes its own "single-use"
        //          interpreter object.  Before using this method, make
        //          sure that is what you want.  This method is custom
        //          tailored to work from inside SQL Server.
        //
        /// <summary>
        /// This method performs substitution on the specified string using a
        /// private, single-use interpreter that it creates and disposes
        /// internally, with the default substitution flags.  It is a top-level
        /// entry point and is thread-safe and re-entrant.  This overload is
        /// custom tailored for hosting scenarios (for example, use from within
        /// SQL Server); most consumers should create an
        /// <see cref="Interpreter" /> and use its substitution methods instead.
        /// </summary>
        /// <param name="text">
        /// The string to perform substitution on.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// The integer value of the resulting <see cref="ReturnCode" />;
        /// <see cref="ReturnCode.Ok" /> (zero) indicates success.
        /// </returns>
        public static int /* ReturnCode */ SubstituteOneString(
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
                    code = SubstituteString(
                        interpreter, text, SubstitutionFlags.Default,
                        ref localResult);
                else
                    code = ReturnCode.Error;
            }

            result = localResult;
            return (int)code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method performs substitution on the specified string in the
        /// context of the given interpreter, using the supplied substitution
        /// flags and the default engine, event, and expression flags.  It is a
        /// top-level entry point and is thread-safe and re-entrant.  Most
        /// consumers should call the equivalent <see cref="Interpreter" />
        /// substitution method instead, which manages interpreter state on the
        /// caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="text">
        /// The string to perform substitution on.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteString(
            Interpreter interpreter,
            string text,
            SubstitutionFlags substitutionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return SubstituteString(
                interpreter, text, EngineFlags.None,
                substitutionFlags, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method performs substitution on the specified string in the
        /// context of the given interpreter, using the supplied engine,
        /// substitution, event, and expression flags.  It is a top-level entry
        /// point and is thread-safe and re-entrant.  It first resolves the
        /// current location (file name and line) for reporting purposes, then
        /// delegates to the core substitution routine.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.
        /// </param>
        /// <param name="text">
        /// The string to perform substitution on.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteString(
            Interpreter interpreter,
            string text,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
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

            return SubstituteString(
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
        /// This method is the core string-substitution routine.  It parses the
        /// specified string as if it were a double-quoted word and performs the
        /// requested variable, command, and backslash substitutions in the
        /// context of the given interpreter, honoring the supplied flags and
        /// limits.  On a parse error it substitutes the valid prefix before
        /// reporting the error.  It is thread-safe and re-entrant.  All of the
        /// other string substitution overloads ultimately delegate here.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  When
        /// this parameter is null, no substitution is performed.
        /// </param>
        /// <param name="fileName">
        /// The name of the file (or other origin) associated with the string,
        /// used for error and location reporting.  This parameter may be null.
        /// </param>
        /// <param name="currentLine">
        /// The line number at which the string begins.
        /// </param>
        /// <param name="text">
        /// The string to perform substitution on.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
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
        /// debugger during substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        private static ReturnCode SubstituteString(
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
            ReturnCode code;
            EngineFlags localEngineFlags = engineFlags;

            if (interpreter != null)
            {
                localEngineFlags = CombineFlags(
                    interpreter, engineFlags, true, true);

                if (!EngineFlagOps.HasNoSubstitute(localEngineFlags))
                {
                    bool noReady = EngineFlagOps.HasNoReady(localEngineFlags);

                    if (text != null) // INTL: do not change to String.IsNullOrEmpty
                    {
                        IParseState parseState = null;
                        Result error = null;
                        int index = 0;
                        int length = text.Length;

                        Parser.Initialize(
                            interpreter, fileName, currentLine, text,
                            index, length, localEngineFlags, substitutionFlags,
                            ref parseState);

                        interpreter.ResetForEngine(
                            localEngineFlags, GetCancelFlags(localEngineFlags),
                            ref result);

                        /*
                         * First parse the string rep of objPtr, as if it were enclosed as a
                         * "-quoted word in a normal Tcl command. Honor flags that selectively
                         * inhibit types of substitution.
                         */

                        if (Parser.ParseTokens(
                                interpreter, index, length, CharacterType.None,
                                parseState, noReady, ref result) != ReturnCode.Ok)
                        {
                            /*
                             * There was a parse error. Save the error message for possible
                             * reporting later.
                             */

                            error = result;

                            /*
                             * We need to re-parse to get the portion of the string we can [subst]
                             * before the parse error. Sadly, all the Tcl_Token's created by the
                             * first parse attempt are gone, freed according to the public spec
                             * for the Tcl_Parse* routines. The only clue we have is parse.term,
                             * which points to either the unmatched opener, or to characters that
                             * follow a close brace or close quote.
                             *
                             * Call ParseTokens again, working on the string up to parse.term.
                             * Keep repeating until we get a good parse on a prefix.
                             */

                            do
                            {
                                parseState.Tokens.Clear();
                                parseState.Characters = parseState.Terminator;
                                parseState.Incomplete = false;
                                parseState.ParseError = ParseError.Success;
                            }
                            while ((Parser.ParseTokens(
                                        interpreter, index, parseState.Characters,
                                        CharacterType.None, parseState,
                                        noReady, ref result) != ReturnCode.Ok)
                                    && !parseState.NotReady); // BUGFIX: Must be ready.

                            //
                            // BUGFIX: Make sure we completed all parsing successfully and were
                            //         not interrupted.
                            //
                            if (!parseState.NotReady)
                            {
                                /*
                                 * The good parse will have to be followed by {, (, or [.
                                 */

                                switch (text[parseState.Terminator])
                                {
                                    case Characters.OpenBrace:
                                        {
                                            /*
                                             * Parse error was a missing } in a ${varname} variable
                                             * substitution at the toplevel. We will subst everything up to
                                             * that broken variable substitution before reporting the parse
                                             * error. Substituting the leftover '$' will have no side-effects,
                                             * so the current token stream is fine.
                                             */
                                            break;
                                        }
                                    case Characters.OpenParenthesis:
                                        {
                                            /*
                                             * Parse error was during the parsing of the index part of an
                                             * array variable substitution at the toplevel.
                                             */

                                            if (text[parseState.Terminator - 1] == Characters.DollarSign)
                                            {
                                                /*
                                                 * Special case where removing the array index left us with
                                                 * just a dollar sign (array variable with name the empty
                                                 * string as its name), instead of with a scalar variable
                                                 * reference.
                                                 *
                                                 * As in the previous case, existing token stream is OK.
                                                 */
                                            }
                                            else
                                            {
                                                /*
                                                 * The current parse includes a successful parse of a scalar
                                                 * variable substitution where there should have been an array
                                                 * variable substitution. We remove that mistaken part of the
                                                 * parse before moving on. A scalar variable substitution is
                                                 * two tokens.
                                                 */

                                                if ((parseState.Tokens[parseState.Tokens.Last - 1].Type != TokenType.Variable) &&
                                                    (parseState.Tokens[parseState.Tokens.Last - 1].Type != TokenType.VariableNameOnly))
                                                {
                                                    result = String.Format(
                                                        "unexpected token type {0}", FormatOps.WrapOrNull(
                                                        parseState.Tokens[parseState.Tokens.Last - 1].Type));

                                                    code = ReturnCode.Error;

                                                    goto exit;
                                                }

                                                if (parseState.Tokens[parseState.Tokens.Last].Type != TokenType.Text)
                                                {
                                                    result = String.Format(
                                                        "unexpected token type {0}", FormatOps.WrapOrNull(
                                                        parseState.Tokens[parseState.Tokens.Last].Type));

                                                    code = ReturnCode.Error;

                                                    goto exit;
                                                }

                                                parseState.Tokens.RemoveAt(parseState.Tokens.Last, 2);
                                            }
                                            break;
                                        }
                                    case Characters.OpenBracket:
                                        {
                                            /*
                                             * Parse error occurred during parsing of a toplevel command
                                             * substitution.
                                             */

                                            parseState.Characters = index + length;
                                            index = parseState.Terminator + 1;
                                            length = parseState.Terminator - index;

                                            if (length == 0)
                                            {
                                                /*
                                                 * No commands, just an unmatched [. As in previous cases,
                                                 * existing token stream is OK.
                                                 */
                                            }
                                            else
                                            {
                                                /*
                                                 * We want to add the parsing of as many commands as we can
                                                 * within that substitution until we reach the actual parse
                                                 * error. We'll do additional parsing to determine what length
                                                 * to claim for the final TCL_TOKEN_COMMAND token.
                                                 */

                                                int lastTerminator = parseState.Terminator;

                                                IParseState nestedParseState = new ParseState(
                                                    localEngineFlags, substitutionFlags, parseState.FileName,
                                                    parseState.CurrentLine);

                                                while (Parser.ParseCommand(
                                                        interpreter, text, index,
                                                        length, noReady, nestedParseState,
                                                        false, ref result) == ReturnCode.Ok)
                                                {
                                                    index = nestedParseState.Terminator +
                                                        ConversionOps.ToInt(nestedParseState.Terminator < nestedParseState.Characters);

                                                    length = nestedParseState.Characters - index;

                                                    if ((length == 0) && (nestedParseState.Terminator == nestedParseState.Characters))
                                                    {
                                                        /*
                                                         * If we run out of string, blame the missing close
                                                         * bracket on the last command, and do not evaluate it
                                                         * during substitution.
                                                         */
                                                        break;
                                                    }

                                                    lastTerminator = nestedParseState.Terminator;
                                                }

                                                if (lastTerminator == parseState.Terminator)
                                                {
                                                    /*
                                                     * Parse error in first command. No commands to subst, add
                                                     * no more tokens.
                                                     */
                                                    break;
                                                }

                                                /*
                                                 * Create a command substitution token for whatever commands
                                                 * got parsed.
                                                 */

                                                IToken token = ParseToken.FromState(interpreter, parseState);

                                                token.Start = parseState.Terminator;
                                                token.Components = 0;
                                                token.Type = TokenType.Command;
                                                token.Length = lastTerminator - token.Start + 1;

                                                parseState.Tokens.Add(token);
                                            }
                                            break;
                                        }
                                    default:
                                        {
                                            result = String.Format(
                                                "bad parse in SubstituteString: {0}",
                                                text[parseState.Terminator]);

                                            code = ReturnCode.Error;

                                            goto exit;
                                        }
                                }
                            }
                            else
                            {
                                //
                                // NOTE: Not ready, canceled, etc.  The result already contains
                                //       the error message.
                                //
                                code = ReturnCode.Error;

                                goto exit;
                            }
                        }

                        /*
                         * Next, substitute the parsed tokens just as in normal Tcl evaluation.
                         */

                        int tokensLeft = parseState.Tokens.Count;
                        Result localResult = null;

                        code = SubstituteTokens(
                            interpreter, parseState,
                            parseState.Tokens.Count - tokensLeft,
#if RESULT_LIMITS
                            executeResultLimit,
                            nestedResultLimit,
#endif
                            sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                            argumentLocation,
#endif
                            ref tokensLeft, localEngineFlags,
                            substitutionFlags, eventFlags,
                            expressionFlags, ref localResult);

                        if (code == ReturnCode.Ok)
                        {
                            if (error != null) // INTL: do not change to String.IsNullOrEmpty
                            {
                                result = error;
                                code = ReturnCode.Error;

                                goto exit;
                            }

                            result = localResult;
                            code = ReturnCode.Ok; // NOTE: Redundant.

                            goto exit;
                        }

                        CommandBuilder substResult = CommandBuilder.Create();

                        while (true)
                        {
                            switch (code)
                            {
                                case ReturnCode.Error:
                                    {
                                        result = localResult;
                                        code = ReturnCode.Error;

                                        goto exit;
                                    }
                                case ReturnCode.Break:
                                    {
                                        tokensLeft = 0; /* Halt substitution */
                                        goto default; // FALL-THROUGH
                                    }
                                default:
                                    {
#if RESULT_LIMITS
                                        if (!substResult.HaveEnoughCapacity(
                                                nestedResultLimit, localResult,
                                                ref result))
                                        {
                                            code = ReturnCode.Error;
                                            goto exit;
                                        }
#endif

                                        substResult.Add(localResult);
                                        break;
                                    }
                            }

                            if (tokensLeft == 0)
                            {
                                if ((error != null) && (code != ReturnCode.Break)) // INTL: do not change to String.IsNullOrEmpty
                                {
                                    result = error;
                                    code = ReturnCode.Error;

                                    goto exit;
                                }

                                result = Result.FromCommandBuilder(substResult);
                                code = ReturnCode.Ok;

                                goto exit;
                            }

                            code = SubstituteTokens(
                                interpreter, parseState,
                                parseState.Tokens.Count - tokensLeft,
#if RESULT_LIMITS
                                executeResultLimit,
                                nestedResultLimit,
#endif
                                sameAppDomain,
#if DEBUGGER && DEBUGGER_BREAKPOINTS
                                argumentLocation,
#endif
                                ref tokensLeft, localEngineFlags,
                                substitutionFlags, eventFlags,
                                expressionFlags, ref localResult);
                        }
                    }
                    else
                    {
                        result = "invalid string";
                        code = ReturnCode.Error;
                    }
                }
                else
                {
                    result = "interpreter not accepting text to substitute";
                    code = ReturnCode.Error;
                }
            }
            else
            {
                result = "invalid interpreter";
                code = ReturnCode.Error;
            }

        exit:

#if (DEBUGGER && DEBUGGER_ENGINE) || NOTIFY
            return SubstituteExited(
                interpreter, fileName, currentLine,
                text, localEngineFlags, substitutionFlags,
                eventFlags, expressionFlags, ref code,
                ref result);
#else
            return code;
#endif
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Asynchronous Methods
        /// <summary>
        /// This method performs substitution on the specified string
        /// asynchronously in the context of the given interpreter, either on a
        /// newly created engine thread or via a queued work item.  It is a
        /// top-level entry point and is thread-safe, re-entrant, and
        /// asynchronous; it returns as soon as the work has been scheduled, and
        /// the optional callback is later invoked with the substitution
        /// outcome.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  If
        /// this parameter is null, an error is returned.
        /// </param>
        /// <param name="text">
        /// The string to perform substitution on.  If this parameter is null,
        /// an error is returned.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="callback">
        /// The callback to invoke when the asynchronous substitution completes.
        /// This parameter may be null for "fire-and-forget" scripts.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-defined data passed through to the callback.
        /// This parameter may be null.
        /// </param>
        /// <param name="error">
        /// Upon failure to schedule the asynchronous substitution, this
        /// contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the asynchronous substitution was
        /// successfully scheduled; otherwise, <see cref="ReturnCode.Error" />
        /// with details placed in <paramref name="error" />.
        /// </returns>
        public static ReturnCode SubstituteString(
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
                                EngineMode.SubstituteString, interpreter,
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
                                        EngineMode.SubstituteString, interpreter,
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

        #region Substitution (Stream) Methods
        /// <summary>
        /// This method performs substitution on the script obtained from the
        /// specified text reader in the context of the given interpreter, using
        /// the supplied flags.  It is a top-level entry point and is
        /// thread-safe and re-entrant.  This is the bridge between the stream
        /// substitution pipeline and the string substitution pipeline; the call
        /// frame management for that transition is performed here.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.
        /// </param>
        /// <param name="name">
        /// The name associated with the stream, used for error and location
        /// reporting.  This parameter may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader from which the string is read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index within the stream at which to begin
        /// substitution.
        /// </param>
        /// <param name="characters">
        /// The number of characters to substitute, or
        /// <see cref="Length.Invalid" /> to substitute to the end of the
        /// stream.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteStream(
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

                    code = SubstituteString(
                        interpreter, name, Parser.StartLine, text,
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
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Substitution (File) Methods
        /// <summary>
        /// This method performs substitution on the contents of the specified
        /// file in the context of the given interpreter, using the supplied
        /// substitution flags and the default engine, event, and expression
        /// flags.  It is a top-level entry point and is thread-safe and
        /// re-entrant.  Most consumers should call the equivalent
        /// <see cref="Interpreter" /> substitution method instead, which
        /// manages interpreter state on the caller's behalf.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  This
        /// parameter should not be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the file whose contents are substituted.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteFile(
            Interpreter interpreter,
            string fileName,
            SubstitutionFlags substitutionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return SubstituteFile(
                interpreter, fileName, EngineFlags.None,
                substitutionFlags, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method performs substitution on the contents of the specified
        /// file in the context of the given interpreter, using the supplied
        /// engine, substitution, event, and expression flags.  It is a
        /// top-level entry point and is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.
        /// </param>
        /// <param name="fileName">
        /// The name of the file whose contents are substituted.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteFile(
            Interpreter interpreter,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return SubstituteFile(
                interpreter, null, fileName, engineFlags,
                substitutionFlags, eventFlags, expressionFlags,
                ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method performs substitution on the contents of the specified
        /// file in the context of the given interpreter, reading the file using
        /// the supplied character encoding and substitution flags, with the
        /// default engine, event, and expression flags.  It is a top-level
        /// entry point and is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file whose contents are substituted.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            SubstitutionFlags substitutionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return SubstituteFile(
                interpreter, encoding, fileName, EngineFlags.None,
                substitutionFlags, EventFlags.Default,
                ExpressionFlags.Default, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core file-substitution routine.  It reads (or
        /// otherwise obtains) the contents of the specified file using the
        /// supplied character encoding, optionally pushes a dedicated engine
        /// call frame, tracks the script location, and then performs
        /// substitution on the file contents in the context of the given
        /// interpreter.  It is the bridge between the file substitution
        /// pipeline and the string substitution pipeline.  It is a top-level
        /// entry point and is thread-safe and re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  If
        /// this parameter is null, an error is returned.
        /// </param>
        /// <param name="encoding">
        /// The character encoding used to read the file, or null to use the
        /// default encoding.
        /// </param>
        /// <param name="fileName">
        /// The name of the file whose contents are substituted.  It may be
        /// adjusted while the file is read or obtained.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the substituted string.  Upon failure,
        /// this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// with details placed in <paramref name="result" />.
        /// </returns>
        public static ReturnCode SubstituteFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;

            if (interpreter != null)
            {
                string text = null;

                code = ReadOrGetScriptFile(
                    interpreter, encoding, ref fileName,
                    ref engineFlags, ref substitutionFlags,
                    ref eventFlags, ref expressionFlags, ref text,
                    ref result);

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

                            //
                            // FIXME: The [subst] parser does not know the line
                            //        where the error happened.
                            //
                            Interpreter.SetErrorLine(interpreter, 0);

                            code = SubstituteString(
                                interpreter, fileName, Parser.StartLine, text,
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

                            if (code == ReturnCode.Return)
                            {
                                code = UpdateReturnInformation(interpreter);
                            }
                            else if (code == ReturnCode.Error)
                            {
                                /* IGNORED */
                                AddErrorInformation(interpreter, result,
                                    String.Format("{0}    (file \"{1}\" line {2})",
                                        Environment.NewLine, FormatOps.Ellipsis(fileName),
                                        Interpreter.GetErrorLine(interpreter)));
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
            else
            {
                result = "invalid interpreter";
                code = ReturnCode.Error;
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Asynchronous Methods
        /// <summary>
        /// This method performs substitution on the contents of the specified
        /// file asynchronously in the context of the given interpreter, either
        /// on a newly created engine thread or via a queued work item.  It is a
        /// top-level entry point and is thread-safe, re-entrant, and
        /// asynchronous; it returns as soon as the work has been scheduled, and
        /// the optional callback is later invoked with the substitution
        /// outcome.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context in which to perform the substitution.  If
        /// this parameter is null, an error is returned.
        /// </param>
        /// <param name="fileName">
        /// The name of the file whose contents are substituted.  If this
        /// parameter is null or empty, an error is returned.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use for the substitution.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags that control which kinds of substitution are
        /// performed.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use for the substitution.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use for the substitution.
        /// </param>
        /// <param name="callback">
        /// The callback to invoke when the asynchronous substitution completes.
        /// This parameter may be null for "fire-and-forget" scripts.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-defined data passed through to the callback.
        /// This parameter may be null.
        /// </param>
        /// <param name="error">
        /// Upon failure to schedule the asynchronous substitution, this
        /// contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> if the asynchronous substitution was
        /// successfully scheduled; otherwise, <see cref="ReturnCode.Error" />
        /// with details placed in <paramref name="error" />.
        /// </returns>
        public static ReturnCode SubstituteFile(
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
                                EngineMode.SubstituteFile, interpreter,
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
                                        EngineMode.SubstituteFile, interpreter,
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
        #endregion
    }
}
