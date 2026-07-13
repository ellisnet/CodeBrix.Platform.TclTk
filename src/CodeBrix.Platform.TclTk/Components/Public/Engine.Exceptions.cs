/*
 * Engine.Exceptions.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Script Exception Methods" region group) so that no
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
        #region Script Exception Methods
        #region Script Exception Flag Methods
        /// <summary>
        /// This method sets or clears the flag that indicates the error code
        /// has been set for the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error code flag should be modified.
        /// </param>
        /// <param name="errorCodeSet">
        /// Non-zero to indicate the error code has been set; zero to clear that
        /// indication.
        /// </param>
        /// <returns>
        /// Non-zero if the flag was modified; otherwise, zero (e.g. when the
        /// interpreter is null).
        /// </returns>
        internal static bool SetErrorCodeSet( /* FOR [error], [exec], [return] USE ONLY */
            Interpreter interpreter,
            bool errorCodeSet
            )
        {
            if (interpreter != null)
            {
                if (errorCodeSet)
                    interpreter.ContextEngineFlags |= EngineFlags.ErrorCodeSet;
                else
                    interpreter.ContextEngineFlags &= ~EngineFlags.ErrorCodeSet;

                return true;
            }
            else
            {
                return false;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets or clears the flag that indicates an error is in
        /// progress for the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error-in-progress flag should be modified.
        /// </param>
        /// <param name="errorInProgress">
        /// Non-zero to indicate an error is in progress; zero to clear that
        /// indication.
        /// </param>
        /// <returns>
        /// Non-zero if the flag was modified; otherwise, zero (e.g. when the
        /// interpreter is null).
        /// </returns>
        internal static bool SetErrorInProgress( /* FOR [return] USE ONLY */
            Interpreter interpreter,
            bool errorInProgress
            )
        {
            if (interpreter != null)
            {
                if (errorInProgress)
                    interpreter.ContextEngineFlags |= EngineFlags.ErrorInProgress;
                else
                    interpreter.ContextEngineFlags &= ~EngineFlags.ErrorInProgress;

                return true;
            }
            else
            {
                return false;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets or clears the flag that indicates error
        /// information has already been logged for the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error-already-logged flag should be modified.
        /// </param>
        /// <param name="errorAlreadyLogged">
        /// Non-zero to indicate error information has already been logged; zero
        /// to clear that indication.
        /// </param>
        /// <returns>
        /// Non-zero if the flag was modified; otherwise, zero (e.g. when the
        /// interpreter is null).
        /// </returns>
        internal static bool SetErrorAlreadyLogged( /* FOR [error] USE ONLY */
            Interpreter interpreter,
            bool errorAlreadyLogged
            )
        {
            if (interpreter == null)
                return false;

            if (errorAlreadyLogged)
                interpreter.ContextEngineFlags |= EngineFlags.ErrorAlreadyLogged;
            else
                interpreter.ContextEngineFlags &= ~EngineFlags.ErrorAlreadyLogged;

            return true;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets or clears the flag that prevents the error state
        /// from being reset for the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose no-reset-error flag should be modified.
        /// </param>
        /// <param name="noResetError">
        /// Non-zero to prevent the error state from being reset; zero to allow
        /// it to be reset.
        /// </param>
        /// <returns>
        /// Non-zero if the flag was modified; otherwise, zero (e.g. when the
        /// interpreter is null).
        /// </returns>
        internal static bool SetNoResetError( /* FOR [try] USE ONLY */
            Interpreter interpreter,
            bool noResetError
            )
        {
            if (interpreter != null)
            {
                if (noResetError)
                    interpreter.ContextEngineFlags |= EngineFlags.NoResetError;
                else
                    interpreter.ContextEngineFlags &= ~EngineFlags.NoResetError;

                return true;
            }
            else
            {
                return false;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method clears all of the error-related engine flags for the
        /// specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error flags should be cleared.
        /// </param>
        /// <returns>
        /// Non-zero if the flags were cleared; otherwise, zero (e.g. when the
        /// interpreter is null).
        /// </returns>
        private static bool ResetErrorFlags(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return false;

            EngineFlags engineFlags = interpreter.ContextEngineFlags;

            engineFlags &= ~EngineFlags.ErrorMask;

            interpreter.ContextEngineFlags = engineFlags;
            return true;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Script Exception Stack Trace Methods
        /// <summary>
        /// This method checks for and resets a pending stack overflow
        /// condition for the specified interpreter, appending the appropriate
        /// error information when one is detected.  This method acquires the
        /// necessary interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for a stack overflow condition.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero (e.g. when the interpreter is
        /// null, unusable, or its lock cannot be acquired).
        /// </returns>
        private static bool CheckStackOverflow(
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
                    //
                    // NOTE: There is not much point in checking for a stack
                    //       overflow if the interpreter is disposed.
                    //
                    if (!IsUsableNoLock(interpreter))
                        return false;

                    //
                    // HACK: Reset the stack overflow flag now that we are at
                    //       the outermost evaluation level.
                    //
                    if (interpreter.StackOverflow)
                    {
                        string errorInfo = String.Format(
                            "{0}    ... truncated ..." +
                            "{0}    (stack overflow line {1})",
                            Environment.NewLine,
                            Interpreter.GetErrorLine(interpreter));

                        /* IGNORED */
                        interpreter.SetVariableValue( /* EXEMPT */
                            ErrorInfoVariableFlags | VariableFlags.AppendValue,
                            TclVars.Core.ErrorInfo, errorInfo, null);

                        interpreter.StackOverflow = false;
                    }

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "CheckStackOverflow",
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
        /// This method resets the error code and error information for the
        /// specified interpreter, including their associated Tcl variables.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error information should be reset.
        /// </param>
        /// <param name="waitForLock">
        /// Non-zero to wait for the interpreter lock to be acquired; zero to
        /// attempt a non-blocking ("soft") lock.
        /// </param>
        /// <param name="failOnError">
        /// Non-zero to fail if one of the associated variables cannot be reset;
        /// zero to ignore such failures.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode ResetErrorInformation(
            Interpreter interpreter,
            bool waitForLock,
            bool failOnError,
            ref Result error
            )
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool locked = false;

            try
            {
                if (waitForLock)
                {
                    interpreter.InternalEngineTryLock(
                        ref locked); /* TRANSACTIONAL */
                }
                else
                {
                    interpreter.InternalSoftTryLock(
                        ref locked); /* TRANSACTIONAL */
                }

                if (locked)
                {
                    Result localError = null; /* REUSED */

                    if (!IsUsableNoLock(interpreter, ref localError))
                    {
                        error = localError;
                        return ReturnCode.Error;
                    }

                    interpreter.ErrorCode = null;

                    localError = null;

                    if ((interpreter.SetVariableValue( /* EXEMPT */
                            ErrorCodeVariableFlags,
                            TclVars.Core.ErrorCode,
                            interpreter.ErrorCode, null,
                            ref localError) != ReturnCode.Ok) &&
                        failOnError)
                    {
                        error = localError;
                        return ReturnCode.Error;
                    }

                    interpreter.ErrorInfo = null;

                    localError = null;

                    if ((interpreter.SetVariableValue( /* EXEMPT */
                            ErrorInfoVariableFlags,
                            TclVars.Core.ErrorInfo,
                            interpreter.ErrorInfo, null,
                            ref localError) != ReturnCode.Ok) &&
                        failOnError)
                    {
                        error = localError;
                        return ReturnCode.Error;
                    }

                    return ReturnCode.Ok;
                }
                else
                {
                    TraceOps.LockTrace(
                        "ResetErrorInformation",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
                    return ReturnCode.Error;
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
        /// This method sets the error code for the specified interpreter based
        /// on the supplied exception.  This overload supplies no argument list,
        /// member information, or result.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error code should be set.
        /// </param>
        /// <param name="exception">
        /// The exception used to derive the error code.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode SetExceptionErrorCode(
            Interpreter interpreter,
            Exception exception
            )
        {
            return SetExceptionErrorCode(
                interpreter, exception, null, null, null);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets the error code for the specified interpreter based
        /// on the supplied exception, also packing the supplied argument list,
        /// member information, and result into the saved exception.  Any
        /// failure is traced for diagnostic purposes.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error code should be set.
        /// </param>
        /// <param name="exception">
        /// The exception used to derive the error code.
        /// </param>
        /// <param name="arguments">
        /// The argument list to associate with the saved exception, if any.
        /// </param>
        /// <param name="memberInfo">
        /// The member information to associate with the saved exception, if
        /// any.
        /// </param>
        /// <param name="result">
        /// The result to associate with the saved exception, if any.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode SetExceptionErrorCode(
            Interpreter interpreter,
            Exception exception,
            ArgumentList arguments,
            MemberInfo memberInfo,
            Result result
            )
        {
            ReturnCode code;
            Result error = null;

            code = SetExceptionErrorCode(
                interpreter, exception, arguments, memberInfo, result,
                ref error);

            if (code != ReturnCode.Ok)
            {
                TraceOps.DebugTrace(String.Format(
                    "SetExceptionErrorCode: failed, interpreter = {0}, " +
                    "exception = {1}, arguments = {2}, memberInfo = {3}, " +
                    "result = {4}, code = {5}, error = {6}",
                    FormatOps.InterpreterNoThrow(interpreter),
                    FormatOps.WrapOrNull(exception),
                    FormatOps.WrapOrNull(arguments),
                    FormatOps.WrapOrNull(memberInfo),
                    FormatOps.WrapOrNull(result),
                    FormatOps.WrapOrNull(code),
                    FormatOps.WrapOrNull(error)),
                    typeof(Engine).Name, TracePriority.EngineError);
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets the error code for the specified interpreter based
        /// on the supplied exception, saving the original exception into the
        /// per-thread state and setting the error code variable to describe
        /// the root cause.  This method acquires the necessary interpreter
        /// lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error code should be set.
        /// </param>
        /// <param name="exception">
        /// The exception used to derive the error code.
        /// </param>
        /// <param name="arguments">
        /// The argument list to associate with the saved exception, if any.
        /// </param>
        /// <param name="memberInfo">
        /// The member information to associate with the saved exception, if
        /// any.
        /// </param>
        /// <param name="result">
        /// The result to associate with the saved exception, if any.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode SetExceptionErrorCode(
            Interpreter interpreter,
            Exception exception,
            ArgumentList arguments,
            MemberInfo memberInfo,
            Result result,
            ref Result error
            )
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            if (exception == null)
            {
                error = "invalid exception";
                return ReturnCode.Error;
            }

            //
            // BUGFIX: Acquire the interpreter lock here; however, do not use
            //         the property because the interpreter may be disposed at
            //         this point.  We do not want to throw exceptions here
            //         primarily because we are called after a command has been
            //         executed (i.e. which may have arbitrary side-effects,
            //         including disposal of the interpreter).
            //
            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    //
                    // NOTE: If the interpreter is unusable, we cannot continue.
                    //
                    if (!IsUsableNoLock(interpreter, ref error))
                        return ReturnCode.Error;

                    //
                    // NOTE: Figure out the extra data to pack into the exception
                    //       saved into the interpreter, if any.
                    //
                    ResultList results = null;

                    if (result != null)
                    {
                        if (results == null)
                            results = new ResultList();

                        results.Add(result);
                    }

                    if (memberInfo != null)
                    {
                        if (results == null)
                            results = new ResultList();

                        results.Add(memberInfo.ToString());
                    }

                    //
                    // NOTE: First, save the original exception that was seen into
                    //       the per-thread state.
                    //
                    interpreter.Exception = new ScriptException(arguments,
                        ReturnCode.Exception, results, exception); /* per-thread */

                    //
                    // TODO: Fetch the innermost (i.e. the "root cause") exception.
                    //       At some point, there might be a need to report other
                    //       exceptions [from along the way]; however, for now this
                    //       should provide some good error context information.
                    //
                    Exception baseException = ScriptOps.GetBaseException(exception);

                    //
                    // NOTE: *WARNING* This code currently assumes that this method
                    //       is called for the "innermost" try/catch blocks inside
                    //       the engine [and related dispatch mechanisms] only.  As
                    //       such, it does not check if the error code has already
                    //       been set by some other means.
                    //
                    /* IGNORED */
                    interpreter.SetVariableValue( /* EXEMPT */
                        ErrorCodeVariableFlags, TclVars.Core.ErrorCode,
                        StringList.MakeList("EXCEPTION", baseException.GetType(),
                        FormatOps.ExceptionMethod(baseException, false)),
                        null);

                    SetErrorCodeSet(interpreter, true);
                    return ReturnCode.Ok;
                }
                else
                {
                    TraceOps.LockTrace(
                        "SetExceptionErrorCode",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
                    return ReturnCode.Error;
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
        /// This method copies the error code and error information from one
        /// interpreter to another, appending it to the supplied result.
        /// </summary>
        /// <param name="sourceInterpreter">
        /// The interpreter to copy the error information from.
        /// </param>
        /// <param name="targetInterpreter">
        /// The interpreter to copy the error information to.
        /// </param>
        /// <param name="result">
        /// The result that the error information should be appended to.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        internal static bool CopyErrorInformation(
            Interpreter sourceInterpreter,
            Interpreter targetInterpreter,
            Result result
            )
        {
            if ((sourceInterpreter == null) || (targetInterpreter == null))
                return false;

            Result localResult = null;

            if (sourceInterpreter.InternalCopyErrorInformation(
                    VariableFlags.None, false, ref localResult) == ReturnCode.Ok)
            {
                if (localResult != null)
                {
                    string errorInfo = localResult.ErrorInfo;

                    if (!String.IsNullOrEmpty(errorInfo))
                    {
                        int startIndex = errorInfo.IndexOf(
                            Environment.NewLine);

                        if (startIndex != Index.Invalid)
                        {
                            //
                            // HACK: Skip the initial error message as
                            //       that will already be present in the
                            //       passed result.
                            //
                            errorInfo = errorInfo.Substring(startIndex);
                        }
                        else
                        {
                            //
                            // HACK: The call below assumes there is a
                            //       new line at the start of the error
                            //       information; therefore, make sure
                            //       it does.
                            //
                            errorInfo = String.Format("{0}{1}",
                                Environment.NewLine, errorInfo);
                        }
                    }

                    string errorCode = localResult.ErrorCode;

                    return AddErrorInformation(
                        targetInterpreter, result, errorCode, errorInfo);
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method appends the specified error information to the error
        /// state of the interpreter.  This overload supplies no error code.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error information should be updated.
        /// </param>
        /// <param name="result">
        /// The result that represents the current error message.
        /// </param>
        /// <param name="errorInfo">
        /// The error information to append.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        public static bool AddErrorInformation(
            Interpreter interpreter,
            Result result,
            string errorInfo
            )
        {
            return AddErrorInformation(
                interpreter, result, null, errorInfo);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method appends the specified error code and error information
        /// to the error state of the interpreter, acquiring the necessary
        /// interpreter lock and using its current engine flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error information should be updated.
        /// </param>
        /// <param name="result">
        /// The result that represents the current error message.
        /// </param>
        /// <param name="errorCode">
        /// The error code to set, or null to use the default.
        /// </param>
        /// <param name="errorInfo">
        /// The error information to append.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        private static bool AddErrorInformation(
            Interpreter interpreter,
            Result result,
            string errorCode,
            string errorInfo
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

                    return AddErrorInformation(interpreter,
                        interpreter.EngineFlagsNoLock,
                        result, errorCode, errorInfo);
                }
                else
                {
                    TraceOps.LockTrace(
                        "AddErrorInformation(1)",
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
        /// This method appends the specified error code and error information
        /// to the error state of the interpreter, using the supplied engine
        /// flags to determine whether an error is already in progress.  This
        /// method acquires the necessary interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error information should be updated.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that describe the current error state.
        /// </param>
        /// <param name="result">
        /// The result that represents the current error message.
        /// </param>
        /// <param name="errorCode">
        /// The error code to set, or null to use the default.
        /// </param>
        /// <param name="errorInfo">
        /// The error information to append.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        private static bool AddErrorInformation(
            Interpreter interpreter,
            EngineFlags engineFlags,
            Result result,
            string errorCode,
            string errorInfo
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

                    if (!EngineFlagOps.HasErrorInProgress(engineFlags))
                    {
                        SetErrorInProgress(interpreter, true);

                        /* IGNORED */
                        interpreter.SetVariableValue( /* EXEMPT */
                            ErrorInfoVariableFlags,
                            TclVars.Core.ErrorInfo, result, null);

                        if (!EngineFlagOps.HasErrorCodeSet(engineFlags))
                        {
                            if (errorCode == null)
                                errorCode = "NONE"; /* COMPAT: Tcl. */

                            /* IGNORED */
                            interpreter.SetVariableValue( /* EXEMPT */
                                ErrorCodeVariableFlags,
                                TclVars.Core.ErrorCode, errorCode, null);
                        }
                    }

                    //
                    // HACK: *PERF* Skip excessive appending to the errorInfo
                    //       variable when unwinding from a stack overflow.
                    //
                    if (interpreter.StackOverflow &&
                        ((interpreter.InternalLevels - interpreter.PreviousLevels)
                            >= ErrorInfoStackOverflowLevels) &&
                        (interpreter.ErrorFrames >= ErrorInfoStackOverflowFrames))
                    {
                        return true; /* SUCCESS: UNNECESSARY */
                    }

                    /* IGNORED */
                    interpreter.SetVariableValue( /* EXEMPT */
                        ErrorInfoVariableFlags | VariableFlags.AppendValue,
                        TclVars.Core.ErrorInfo, errorInfo, null);

                    interpreter.ErrorFrames++;
                    return true; /* SUCCESS: DONE */
                }
                else
                {
                    TraceOps.LockTrace(
                        "AddErrorInformation(2)",
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
        /// This method calculates and records the line number where the
        /// current error occurred for the specified interpreter, based on its
        /// current parse state.  This method acquires the necessary
        /// interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error line number should be set.
        /// </param>
        /// <param name="force">
        /// Non-zero to record the error line number even when it is zero;
        /// otherwise, the line number is only recorded when it is non-zero.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        internal static bool SetErrorLine( /* NOTE: For use by [error] only. */
            Interpreter interpreter,
            bool force
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

                    IParseState parseState = interpreter.ParseState;

                    if (parseState == null)
                        return false;

                    int errorLine = 0;

                    CalculateErrorLine(parseState.Text,
                        parseState.CommandStart, ref errorLine);

                    if (force || (errorLine != 0))
                    {
                        Interpreter.SetErrorLine(
                            interpreter, errorLine);
                    }

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "SetErrorLine",
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
        /// This method calculates the one-based line number of a command
        /// within the specified script text by counting the line terminators
        /// that precede it.
        /// </summary>
        /// <param name="text">
        /// The script text containing the command.
        /// </param>
        /// <param name="commandStart">
        /// The character offset within the script text where the command
        /// begins.
        /// </param>
        /// <param name="errorLine">
        /// Upon return, this parameter will receive the calculated one-based
        /// line number.
        /// </param>
        private static void CalculateErrorLine(
            string text,      /* in */
            int commandStart, /* in */
            ref int errorLine /* out */
            )
        {
            if (text == null)
                return;

            int localErrorLine = 1;
            int length = Math.Min(commandStart, text.Length);

            for (int index = 0; index < length; index++)
                if (Parser.IsLineTerminator(text[index]))
                    localErrorLine++;

            errorLine = localErrorLine;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method records error information for the command currently
        /// being executed, including the calculated error line and a formatted
        /// excerpt of the command text.  This method acquires the necessary
        /// interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose error information should be updated.
        /// </param>
        /// <param name="text">
        /// The script text containing the command.
        /// </param>
        /// <param name="commandStart">
        /// The character offset within the script text where the command
        /// begins.
        /// </param>
        /// <param name="commandLength">
        /// The length, in characters, of the command text; a negative value
        /// means the remainder of the script text following the command start.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that describe the current error state.
        /// </param>
        /// <param name="result">
        /// The result that represents the current error message.
        /// </param>
        /// <param name="errorLine">
        /// Upon return, this parameter will receive the calculated one-based
        /// line number where the command begins.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        private static bool LogCommandInformation(
            Interpreter interpreter,
            string text,
            int commandStart,
            int commandLength,
            EngineFlags engineFlags,
            Result result,
            ref int errorLine
            )
        {
            if (interpreter == null)
                return false;

            //
            // NOTE: Already checked by [only] caller.
            //
            // if (EngineFlagOps.HasErrorAlreadyLogged(engineFlags))
            //     return false;
            //
            CalculateErrorLine(text, commandStart, ref errorLine);

            if (commandLength < 0)
                commandLength = text.Length - commandStart;

            string format;

            if (!EngineFlagOps.HasErrorInProgress(engineFlags))
                format = "{0}    while executing{0}\"{1}\"";
            else
                format = "{0}    invoked from within{0}\"{1}\"";

            string errorInfo = String.Format(format,
                Environment.NewLine, FormatOps.Ellipsis(
                    text, commandStart, commandLength,
                    ErrorInfoCommandLength, false));

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter))
                        return false;

                    /* IGNORED */
                    AddErrorInformation(
                        interpreter, engineFlags, result, null,
                        errorInfo);

                    /* IGNORED */
                    SetErrorAlreadyLogged(interpreter, false);

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "LogCommandInformation",
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

        #region Script Exception Return Code Methods
        /// <summary>
        /// This method conditionally resets the return code carried by the
        /// specified result.  This overload discards the reset indicator and
        /// any associated error message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter associated with the result.
        /// </param>
        /// <param name="result">
        /// The result whose return code should be reset.
        /// </param>
        /// <param name="force">
        /// Non-zero to reset the return code regardless of the current
        /// evaluation level.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode ResetReturnCode(
            Interpreter interpreter,
            Result result,
            bool force
            )
        {
            bool reset = false;
            Result error = null;

            return ResetReturnCode(interpreter, result, force, ref reset, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method conditionally resets the return code carried by the
        /// specified result, reporting whether it was actually changed.  The
        /// return code is reset when forced or when the interpreter is at its
        /// outermost evaluation level.  This method acquires the necessary
        /// interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter associated with the result.
        /// </param>
        /// <param name="result">
        /// The result whose return code should be reset.
        /// </param>
        /// <param name="force">
        /// Non-zero to reset the return code regardless of the current
        /// evaluation level, even when the interpreter has been disposed.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the return code
        /// was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode ResetReturnCode(
            Interpreter interpreter,
            Result result,
            bool force,
            ref bool reset,
            ref Result error
            ) /* THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    //
                    // BUGFIX: If the interpreter has been disposed, skip
                    //         checking its levels.
                    //
                    int levels;
                    Result localError = null;

                    if (IsUsableNoLock(interpreter, ref localError))
                    {
                        //
                        // NOTE: The interpreter is not disposed, query its
                        //       levels.
                        //
                        levels = interpreter.InternalLevels;
                    }
                    else if (force)
                    {
                        //
                        // NOTE: The interpreter is disposed and the force
                        //       flag is set, just use zero since the value
                        //       will be ignored.
                        //
                        levels = 0;
                    }
                    else
                    {
                        //
                        // NOTE: The interpreter is disposed and the force
                        //       flag is not set, fail.
                        //
                        error = localError;
                        return ReturnCode.Error;
                    }

                    //
                    // BUGFIX: Cannot reset a null result.
                    //
                    if ((result != null) && (force || (levels == 0)))
                    {
                        ReturnCode returnCode = result.ReturnCode;

                        result.ReturnCode = ReturnCode.Ok;

                        reset = (returnCode != ReturnCode.Ok);
                    }
                    else
                    {
                        reset = false;
                    }

                    return ReturnCode.Ok;
                }
                else
                {
                    TraceOps.LockTrace(
                        "ResetReturnCode",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
                    return ReturnCode.Error;
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
        /// This method retrieves the return code associated with the
        /// "exception" semantics of the specified interpreter, resets it to
        /// <c>ReturnCode.Ok</c>, and, when that return code indicates an error,
        /// updates the error code and error information variables accordingly.
        /// This method acquires the necessary interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose return information should be updated.
        /// </param>
        /// <returns>
        /// The return code that was associated with the interpreter; this will
        /// be <c>ReturnCode.Ok</c> when the interpreter is null or unusable, or
        /// <c>ReturnCode.Error</c> when the interpreter lock cannot be
        /// acquired.
        /// </returns>
        internal static ReturnCode UpdateReturnInformation(
            Interpreter interpreter
            )
        {
            //
            // TODO: Figure out why the return code here defaults to "Ok".
            //
            ReturnCode code = ReturnCode.Ok;

            //
            // NOTE: Get the ReturnCode value used by the "exception"
            //       semantics and then reset it to Ok.
            //
            if (interpreter == null)
                return code;

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    //
                    // BUGFIX: If the interpreter has been disposed, skip
                    //         setting the return code.
                    //
                    if (!IsUsableNoLock(interpreter))
                        return code;

                    code = interpreter.ReturnCode;
                    interpreter.ReturnCode = ReturnCode.Ok;

                    if (code == ReturnCode.Error)
                    {
                        if (interpreter.ErrorCode != null)
                        {
                            /* IGNORED */
                            interpreter.SetVariableValue( /* EXEMPT */
                                ErrorCodeVariableFlags, TclVars.Core.ErrorCode,
                                interpreter.ErrorCode, null);

                            SetErrorCodeSet(interpreter, true);
                        }

                        if (interpreter.ErrorInfo != null)
                        {
                            /* IGNORED */
                            interpreter.SetVariableValue( /* EXEMPT */
                                ErrorInfoVariableFlags, TclVars.Core.ErrorInfo,
                                interpreter.ErrorInfo, null);

                            SetErrorInProgress(interpreter, true);
                        }
                    }

                    return code;
                }
                else
                {
                    TraceOps.LockTrace(
                        "UpdateReturnInformation",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    return ReturnCode.Error;
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

        #region Script Exception/Result Reset Methods
        /// <summary>
        /// This method resets the result of the specified interpreter.  This
        /// overload uses no additional engine flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose result should be reset.
        /// </param>
        /// <param name="result">
        /// Upon return, this parameter will be reset to null.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        public static bool ResetResult(
            Interpreter interpreter,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return ResetResult(interpreter, EngineFlags.None, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the result of the specified interpreter,
        /// optionally clearing the error flags as well, subject to the
        /// supplied and current engine flags.  This method acquires the
        /// necessary interpreter lock.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose result should be reset.
        /// </param>
        /// <param name="engineFlags">
        /// Additional engine flags that influence whether the result and error
        /// flags are reset.
        /// </param>
        /// <param name="result">
        /// Upon return, this parameter will be reset to null.
        /// </param>
        /// <returns>
        /// Non-zero on success; otherwise, zero.
        /// </returns>
        private static bool ResetResult(
            Interpreter interpreter,
            EngineFlags engineFlags,
            ref Result result
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
                    //
                    // BUGFIX: If the interpreter has already been disposed,
                    //         just reset the string result and return.
                    //
                    if (!IsUsableNoLock(interpreter))
                    {
                        result = null;
                        return false;
                    }

                    EngineFlags localEngineFlags = CombineFlags(
                        interpreter, engineFlags, false, false);

                    if (!EngineFlagOps.HasNoResetResult(localEngineFlags))
                    {
                        //
                        // NOTE: Reset the string result.  This used to be
                        //       String.Empty; however, that does not seem
                        //       to be necessary here.
                        //
                        result = null;

                        if (!EngineFlagOps.HasNoResetError(localEngineFlags))
                        {
                            /* IGNORED */
                            ResetErrorFlags(interpreter);
                        }
                    }

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "ResetResult",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    //
                    // BUGFIX: Just reset the string result and return;
                    //         i.e. as the alternative may be a deadlock.
                    //
                    result = null;
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
        #endregion
    }
}
