/*
 * Engine.Status.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Interpreter Status Methods" region group) so that no
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
        #region Interpreter Status Methods
        #region Interpreter "Usability" Methods
        /// <summary>
        /// This method determines whether the specified interpreter is
        /// currently usable (i.e. it is non-null and has not been disposed).
        /// This overload discards any associated error message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for usability.
        /// </param>
        /// <returns>
        /// Non-zero if the interpreter is usable; otherwise, zero.
        /// </returns>
        internal static bool IsUsableNoLock(
            Interpreter interpreter
            )
        {
            Result error = null;

            return IsUsableNoLock(interpreter, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether the specified interpreter is
        /// currently usable (i.e. it is non-null and has not been disposed).
        /// The caller must hold the appropriate interpreter lock prior to
        /// calling this method.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for usability.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message that
        /// explains why the interpreter is not usable.
        /// </param>
        /// <returns>
        /// Non-zero if the interpreter is usable; otherwise, zero.
        /// </returns>
        private static bool IsUsableNoLock(
            Interpreter interpreter,
            ref Result error
            )
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return false;
            }

            if (interpreter.Disposed)
            {
                error = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);

                return false;
            }

            return true;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

#if PROFILER
        /// <summary>
        /// This method determines whether the specified profiler is currently
        /// usable, taking into account a previously computed usability flag
        /// for the associated interpreter.
        /// </summary>
        /// <param name="profiler">
        /// The profiler to check for usability.
        /// </param>
        /// <param name="usable">
        /// Non-zero if the associated interpreter is known to be usable.  When
        /// this is zero, the profiler is always considered unusable.
        /// </param>
        /// <returns>
        /// Non-zero if the profiler is usable; otherwise, zero.
        /// </returns>
        private static bool IsUsableNoLock(
            IProfilerState profiler,
            bool usable
            )
        {
            if (!usable)
                return false;

            if (profiler == null)
                return false;

            if (profiler.Disposed)
                return false;

            return true;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Interpreter Deletion Methods
        /// <summary>
        /// This method determines whether the specified interpreter has been
        /// marked as deleted, optionally acquiring the necessary interpreter
        /// lock and firing the associated interrupt callback.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for deletion.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how the
        /// result and interrupt callback are handled.
        /// </param>
        /// <param name="result">
        /// Upon failure, this parameter will receive an error message; this may
        /// also receive a message indicating the interpreter has been deleted.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> if the interpreter has not been deleted;
        /// otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode IsDeleted(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            ReturnCode code;

            bool fireCallback = true;
            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    code = interpreter.InternalDeleted ?
                        ReturnCode.Error : ReturnCode.Ok;

                    if (code == ReturnCode.Error)
                    {
                        bool needResult = FlagOps.HasFlags(
                            cancelFlags, CancelFlags.NeedResult, true);

                        if (needResult)
                            result = "attempt to call eval in deleted interpreter";
                    }
                }
                else
                {
                    TraceOps.LockTrace(
                        "IsDeleted",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    fireCallback = false; /* TODO: Nothing was done? */

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

            if (fireCallback && (code == ReturnCode.Error))
            {
                bool waitForLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.WaitForLock, true);

                ReturnCode fireCode;
                Result fireError = null;

                fireCode = interpreter.FireInterruptCallback(
                    InterruptType.Deleted, ClientData.Empty,
                    waitForLock, ref fireError);

                if (fireCode != ReturnCode.Ok)
                    DebugOps.Complain(interpreter, fireCode, fireError);
            }

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Interpreter Halting Methods
        /// <summary>
        /// This method determines whether evaluation in the specified
        /// interpreter has been halted, acquiring the necessary interpreter
        /// lock and firing the associated interrupt callback as needed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for the halted state.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how the
        /// result and interrupt callback are handled.
        /// </param>
        /// <param name="result">
        /// Upon failure, this parameter will receive an error message or the
        /// reason why evaluation was halted.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> if evaluation has not been halted; otherwise,
        /// <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode InternalIsHalted(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            CancelFlags cancelFlags,
            ref Result result
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            //
            // BUGFIX: Acquire the interpreter lock here;
            //         however, do not use the property
            //         because the interpreter may be
            //         disposed at this point.  We do not
            //         want to throw exceptions here
            //         primarily because we are called by
            //         Interpreter.Ready and that method
            //         checks for interpreter disposal
            //         already (although, not always at
            //         the right time to avoid a race
            //         condition).
            //
            // BUGBUG: This may not be the right fix for
            //         this issue.  It might be better to
            //         grab the interpreter lock inside of
            //         the Ready method while calling this
            //         method and only after checking if
            //         the interpreter has been disposed.
            //
            ReturnCode code;

            bool fireCallback = true;
            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref result))
                        return ReturnCode.Error;

                    code = interpreter.InternalIsHalted(
#if THREADING
                        engineContext,
#endif
                        cancelFlags, ref result);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalIsHalted",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    fireCallback = false; /* TODO: Nothing was done? */

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

            if (fireCallback && (code == ReturnCode.Error))
            {
                bool waitForLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.WaitForLock, true);

                ReturnCode fireCode;
                Result fireError = null;

                fireCode = interpreter.FireInterruptCallback(
                    InterruptType.Halted, ClientData.Empty,
                    waitForLock, ref fireError);

                if (fireCode != ReturnCode.Ok)
                    DebugOps.Complain(interpreter, fireCode, fireError);
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

#if SHELL
        /// <summary>
        /// This method determines whether evaluation in the specified
        /// interpreter has been halted, using the cancellation flags
        /// appropriate for the interactive loop.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for the halted state.
        /// </param>
        /// <param name="result">
        /// Upon failure, this parameter will receive an error message or the
        /// reason why evaluation was halted.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> if evaluation has not been halted; otherwise,
        /// <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode InteractiveIsHalted(
            Interpreter interpreter,
            ref Result result
            )
        {
#if THREADING
            IEngineContext engineContext = null;

            if (interpreter != null)
                engineContext = interpreter.GetEngineContextNoCreate();
#endif

            return InternalIsHalted(interpreter,
#if THREADING
                engineContext,
#endif
                CancelFlags.InteractiveIsHalted, ref result);
        }
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the halted state of the specified interpreter.
        /// This overload discards any associated error message and reset
        /// indicator.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose halted state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode ResetHalt(
            Interpreter interpreter,
            CancelFlags cancelFlags
            )
        {
            Result error = null;

            return ResetHalt(interpreter, cancelFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the halted state of the specified interpreter.
        /// This overload discards the reset indicator.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose halted state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode ResetHalt(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref Result error
            )
        {
            bool reset = false;

            return ResetHalt(interpreter, cancelFlags, ref reset, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the halted state of the specified interpreter,
        /// reporting whether the state was actually changed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose halted state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the halted state
        /// was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode ResetHalt(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref bool reset,
            ref Result error
            )
        {
            Result haltedResults = null;
            bool halted = false;

            return InternalResetHalt(interpreter,
#if THREADING
                null,
#endif
                cancelFlags, ref haltedResults, ref halted, ref reset,
                ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the halted state of the specified interpreter,
        /// acquiring the necessary interpreter lock and optionally raising a
        /// notification.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose halted state should be reset.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="haltedResults">
        /// Upon success, this parameter will receive the result(s) that were
        /// associated with the halted state, if any.
        /// </param>
        /// <param name="halted">
        /// Upon success, this parameter will be non-zero if the interpreter
        /// was in the halted state prior to the reset.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the halted state
        /// was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode InternalResetHalt(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            CancelFlags cancelFlags,
            ref Result haltedResults,
            ref bool halted,
            ref bool reset,
            ref Result error
            ) /* THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            ReturnCode code;

#if NOTIFY
            bool notify = FlagOps.HasFlags(
                cancelFlags, CancelFlags.Notify, true);
#endif

            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref error))
                        return ReturnCode.Error;

                    code = interpreter.InternalResetHalt(
#if THREADING
                        engineContext,
#endif
                        cancelFlags, ref haltedResults,
                        ref halted, ref reset, ref error);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalResetHalt",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

#if NOTIFY
            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
#if DEBUGGER
                    NotifyType.Debugger,
#else
                    NotifyType.Script,
#endif
                    NotifyFlags.Reset | NotifyFlags.Halted,
                    new ObjectTriplet(code, cancelFlags, reset),
                    interpreter, null, null, null, ref error);
            }
#endif

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

#if SHELL
        /// <summary>
        /// This method resets the halted state of the specified interpreter,
        /// using the cancellation flags appropriate for the interactive loop.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose halted state should be reset.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the halted state
        /// was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode InteractiveResetHalt(
            Interpreter interpreter,
            ref bool reset,
            ref Result error
            )
        {
            return ResetHalt(
                interpreter, CancelFlags.InteractiveAutomaticResetHalt,
                ref reset, ref error);
        }
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method halts any script evaluation that is in progress within
        /// the specified interpreter, associating the supplied result with the
        /// halted state.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script evaluation should be halted.
        /// </param>
        /// <param name="result">
        /// The result to associate with the halted state.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode HaltEvaluate(
            Interpreter interpreter,
            Result result,
            CancelFlags cancelFlags,
            ref Result error
            ) /* THREAD-SAFE */
        {
            return InternalHaltEvaluate(
                interpreter,
#if THREADING
                null,
#endif
                result, cancelFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method halts any script evaluation that is in progress within
        /// the specified interpreter, acquiring the necessary interpreter lock
        /// and optionally raising notifications before and after the operation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script evaluation should be halted.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="result">
        /// The result to associate with the halted state.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode InternalHaltEvaluate(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            Result result,
            CancelFlags cancelFlags,
            ref Result error
            ) /* THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            ReturnCode code;

#if NOTIFY
            bool notify = FlagOps.HasFlags(
                cancelFlags, CancelFlags.Notify, true);

            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
#if DEBUGGER
                    NotifyType.Debugger,
#else
                    NotifyType.Script,
#endif
                    NotifyFlags.PreHalted,
                    new ObjectPair(result, cancelFlags), interpreter,
                    null, null, null, ref error);
            }
#endif

            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref error))
                        return ReturnCode.Error;

                    code = interpreter.InternalHaltEvaluate(
#if THREADING
                        engineContext,
#endif
                        result, cancelFlags, ref error);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalHaltEvaluate",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

#if NOTIFY
            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
#if DEBUGGER
                    NotifyType.Debugger,
#else
                    NotifyType.Script,
#endif
                    NotifyFlags.Halted,
                    new ObjectTriplet(code, result, cancelFlags),
                    interpreter, null, null, null, ref error);
            }
#endif

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Interpreter Cancellation Methods
        /// <summary>
        /// This method determines whether script evaluation in the specified
        /// interpreter has been canceled.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for script cancellation.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how the
        /// result and interrupt callback are handled.
        /// </param>
        /// <param name="result">
        /// Upon failure, this parameter will receive an error message or the
        /// reason why script evaluation was canceled.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> if script evaluation has not been canceled;
        /// otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode IsCanceled(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return InternalIsCanceled(interpreter,
#if THREADING
                null,
#endif
                cancelFlags, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether script evaluation in the specified
        /// interpreter has been canceled, acquiring the necessary interpreter
        /// lock and firing the associated interrupt callback as needed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to check for script cancellation.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how the
        /// result and interrupt callback are handled.
        /// </param>
        /// <param name="result">
        /// Upon failure, this parameter will receive an error message or the
        /// reason why script evaluation was canceled.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> if script evaluation has not been canceled;
        /// otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode InternalIsCanceled(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            CancelFlags cancelFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            InterruptType interruptType = InterruptType.None;

#if DEBUGGER
            BreakpointType breakpointType = BreakpointType.None;
#endif

            //
            // BUGFIX: Acquire the interpreter lock here;
            //         however, do not use the property
            //         because the interpreter may be
            //         disposed at this point.  We do not
            //         want to throw exceptions here
            //         primarily because we are called by
            //         Interpreter.Ready and that method
            //         checks for interpreter disposal
            //         already (although, not always at
            //         the right time to avoid a race
            //         condition).
            //
            // BUGBUG: This may not be the right fix for
            //         this issue.  It might be better to
            //         grab the interpreter lock inside of
            //         the Ready method while calling this
            //         method and only after checking if
            //         the interpreter has been disposed.
            //
            ReturnCode code;

            bool fireCallback = true;
            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref result))
                        return ReturnCode.Error;

                    code = interpreter.InternalIsCanceled(
#if THREADING
                        engineContext,
#endif
                        cancelFlags, ref interruptType,
#if DEBUGGER
                        ref breakpointType,
#endif
                        ref result);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalIsCanceled",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    fireCallback = false; /* TODO: Nothing was done? */

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

            if (fireCallback && (code == ReturnCode.Error))
            {
                bool waitForLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.WaitForLock, true);

                ReturnCode fireCode;
                Result fireError = null;

                fireCode = interpreter.FireInterruptCallback(
                    interruptType, ClientData.Empty, waitForLock,
                    ref fireError);

                if (fireCode != ReturnCode.Ok)
                    DebugOps.Complain(interpreter, fireCode, fireError);
            }

#if DEBUGGER && DEBUGGER_ENGINE
            //
            // HACK: Use the "fireCallback" bit here as well, because
            //       this breakpoint type should only fire when script
            //       cancellation was actually checked and found to be
            //       true.
            //
            if (fireCallback && (code == ReturnCode.Error))
            {
                EngineFlags engineFlags = EngineFlags.None;

                bool noBreakpoint = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoBreakpoint, true);

                if (noBreakpoint)
                    engineFlags |= EngineFlags.NoDebuggerMask;

                if (DebuggerOps.CanHitBreakpoints(interpreter,
                        engineFlags, breakpointType))
                {
                    code = interpreter.CheckBreakpoints(
                        code, breakpointType, null,
                        null, null, null, null, null,
                        null, ref result);
                }
            }
#endif

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the script cancellation state of the specified
        /// interpreter.  This overload discards any associated error message
        /// and reset indicator.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script cancellation state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode ResetCancel(
            Interpreter interpreter,
            CancelFlags cancelFlags
            )
        {
            Result error = null;

            return ResetCancel(interpreter, cancelFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the script cancellation state of the specified
        /// interpreter.  This overload discards the reset indicator.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script cancellation state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode ResetCancel(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref Result error
            )
        {
            bool reset = false;

            return ResetCancel(interpreter, cancelFlags, ref reset, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the script cancellation state of the specified
        /// interpreter, reporting whether the state was actually changed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script cancellation state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the script
        /// cancellation state was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode ResetCancel(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref bool reset,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            Result canceledResults = null;
            bool canceled = false;
            bool unwound = false;

            return ResetCancel(
                interpreter, cancelFlags, ref canceledResults, ref canceled,
                ref unwound, ref reset, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the script cancellation state of the specified
        /// interpreter, reporting the previous cancellation and unwind states
        /// as well as whether the state was actually changed.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script cancellation state should be reset.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="canceledResults">
        /// Upon success, this parameter will receive the result(s) that were
        /// associated with the cancellation state, if any.
        /// </param>
        /// <param name="canceled">
        /// Upon success, this parameter will be non-zero if script evaluation
        /// was in the canceled state prior to the reset.
        /// </param>
        /// <param name="unwound">
        /// Upon success, this parameter will be non-zero if the interpreter
        /// was being unwound prior to the reset.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the script
        /// cancellation state was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode ResetCancel(
            Interpreter interpreter,
            CancelFlags cancelFlags,
            ref Result canceledResults,
            ref bool canceled,
            ref bool unwound,
            ref bool reset,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return InternalResetCancel(interpreter,
#if THREADING
                null,
#endif
                cancelFlags, ref canceledResults, ref canceled,
                ref unwound, ref reset, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method resets the script cancellation state of the specified
        /// interpreter, acquiring the necessary interpreter lock and
        /// optionally raising a notification.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script cancellation state should be reset.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="canceledResults">
        /// Upon success, this parameter will receive the result(s) that were
        /// associated with the cancellation state, if any.
        /// </param>
        /// <param name="canceled">
        /// Upon success, this parameter will be non-zero if script evaluation
        /// was in the canceled state prior to the reset.
        /// </param>
        /// <param name="unwound">
        /// Upon success, this parameter will be non-zero if the interpreter
        /// was being unwound prior to the reset.
        /// </param>
        /// <param name="reset">
        /// Upon success, this parameter will be non-zero if the script
        /// cancellation state was actually reset.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        private static ReturnCode InternalResetCancel(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            CancelFlags cancelFlags,
            ref Result canceledResults,
            ref bool canceled,
            ref bool unwound,
            ref bool reset,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            ReturnCode code;

#if NOTIFY
            bool notify = FlagOps.HasFlags(
                cancelFlags, CancelFlags.Notify, true);
#endif

            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref error))
                        return ReturnCode.Error;

                    code = interpreter.InternalResetCancel(
#if THREADING
                        engineContext,
#endif
                        cancelFlags, ref canceledResults,
                        ref canceled, ref unwound, ref reset,
                        ref error);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalResetCancel",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

#if NOTIFY
            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
                    NotifyType.Script, NotifyFlags.Reset | NotifyFlags.Canceled,
                    new ObjectList(code, cancelFlags, canceledResults, canceled, unwound, reset),
                    interpreter, null, null, null, ref error);
            }
#endif

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method cancels any script evaluation that is in progress
        /// within the specified interpreter, associating the supplied result
        /// with the cancellation state.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script evaluation should be canceled.
        /// </param>
        /// <param name="result">
        /// The result to associate with the cancellation state.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        public static ReturnCode CancelEvaluate(
            Interpreter interpreter,
            Result result,
            CancelFlags cancelFlags,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return InternalCancelEvaluate(
                interpreter,
#if THREADING
                null,
#endif
                result, cancelFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method cancels any script evaluation that is in progress
        /// within the specified interpreter, acquiring the necessary
        /// interpreter lock and optionally raising notifications before and
        /// after the operation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose script evaluation should be canceled.
        /// </param>
        /// <param name="engineContext">
        /// The per-thread engine context associated with the interpreter, if
        /// any.
        /// </param>
        /// <param name="result">
        /// The result to associate with the cancellation state.
        /// </param>
        /// <param name="cancelFlags">
        /// Flags that control how the interpreter lock is acquired and how
        /// notifications are handled.
        /// </param>
        /// <param name="error">
        /// Upon failure, this parameter will receive an error message.
        /// </param>
        /// <returns>
        /// <c>ReturnCode.Ok</c> on success; otherwise, <c>ReturnCode.Error</c>.
        /// </returns>
        internal static ReturnCode InternalCancelEvaluate(
            Interpreter interpreter,
#if THREADING
            IEngineContext engineContext,
#endif
            Result result,
            CancelFlags cancelFlags,
            ref Result error
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            ReturnCode code;

#if NOTIFY
            bool notify = FlagOps.HasFlags(
                cancelFlags, CancelFlags.Notify, true);

            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
                    NotifyType.Script, NotifyFlags.PreCanceled,
                    new ObjectPair(result, cancelFlags), interpreter,
                    null, null, null, ref error);
            }
#endif

            bool locked = false;

            try
            {
                bool noLock = FlagOps.HasFlags(
                    cancelFlags, CancelFlags.NoLock, true);

                if (!noLock)
                {
                    bool tryLock = FlagOps.HasFlags(
                        cancelFlags, CancelFlags.TryLock, true);

                    if (tryLock)
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                    else
                    {
                        interpreter.InternalLock(
                            ref locked); /* TRANSACTIONAL */
                    }
                }

                if (noLock || locked)
                {
                    if (!IsUsableNoLock(interpreter, ref error))
                        return ReturnCode.Error;

                    code = interpreter.InternalCancelEvaluate(
#if THREADING
                        engineContext,
#endif
                        result, cancelFlags, ref error);
                }
                else
                {
                    TraceOps.LockTrace(
                        "InternalCancelEvaluate",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

#if NOTIFY
            if (notify)
            {
                /* IGNORED */
                interpreter.CheckNotification(
                    NotifyType.Script, NotifyFlags.Canceled,
                    new ObjectTriplet(code, result, cancelFlags),
                    interpreter, null, null, null, ref error);
            }
#endif

            return code;
        }
        #endregion
        #endregion
    }
}
