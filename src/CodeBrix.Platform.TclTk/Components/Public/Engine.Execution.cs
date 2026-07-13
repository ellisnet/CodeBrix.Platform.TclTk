/*
 * Engine.Execution.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Execution Methods" region group) so that no
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
        #region Execution Methods
        #region ClientData Methods
        /// <summary>
        /// This method determines the effective client data to use for an
        /// execution.  When an explicit value is supplied, it is used;
        /// otherwise, the per-context client data of the interpreter is used,
        /// when present.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose context client data may be used as a
        /// fallback; this may be null.
        /// </param>
        /// <param name="clientData">
        /// The explicit client data to use, if any; this may be null.
        /// </param>
        /// <param name="useEmpty">
        /// Non-zero to return the empty client data instance instead of null
        /// when no client data can be determined.
        /// </param>
        /// <returns>
        /// The effective client data, the empty client data instance, or
        /// null.
        /// </returns>
        internal static IClientData GetClientData(
            Interpreter interpreter,
            IClientData clientData,
            bool useEmpty
            )
        {
            if (clientData != null)
                return clientData;

            if (interpreter != null)
            {
                IClientData localClientData =
                    interpreter.ContextClientData;

                if (localClientData != null)
                    return localClientData;
            }

            return useEmpty ? ClientData.Empty : null;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Profiler Methods
#if PROFILER
        /// <summary>
        /// This method obtains the profiler associated with the interpreter
        /// and starts it, so that the elapsed time of a subsequent execution
        /// can be measured.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose profiler should be obtained and started;
        /// this may be null.
        /// </param>
        /// <returns>
        /// The started profiler, or null if there is no usable profiler.
        /// </returns>
        private static IProfilerState GetProfilerAndStart(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return null;

            IProfilerState profiler = interpreter.Profiler;

            if (profiler != null)
            {
                if (profiler.Disposed)
                    return null; /* IMPOSSIBLE? */

                profiler.Start();
            }

            return profiler;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Result Limit Methods
#if RESULT_LIMITS
        /// <summary>
        /// This method checks the size of a pending result against the
        /// configured per-execution result limits for the interpreter.  This
        /// overload assumes there are no extra length or count values to
        /// account for.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose result limits should be enforced; this may
        /// be null.
        /// </param>
        /// <param name="length">
        /// The length, in characters, of the result to check.
        /// </param>
        /// <param name="count">
        /// The number of result items to check.
        /// </param>
        /// <param name="code">
        /// Upon failure, this is set to <see cref="ReturnCode.Error" />.
        /// </param>
        /// <param name="result">
        /// Upon failure, this receives an error message describing the limit
        /// that was exceeded.
        /// </param>
        internal static void CheckResultAgainstLimits(
            Interpreter interpreter,
            int length,
            int count,
            ref ReturnCode code,
            ref Result result
            )
        {
            /* NO RESULT */
            CheckResultAgainstLimits(
                interpreter, length, 0, count, 0, ref code, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks the combined size of a pending result against
        /// the configured per-execution result limits for the interpreter.
        /// The base and extra length and count values are summed, and their
        /// product is also checked, prior to comparison against the limit.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose result limits should be enforced; this may
        /// be null.
        /// </param>
        /// <param name="baseLength">
        /// The base length, in characters, of the result to check.
        /// </param>
        /// <param name="extraLength">
        /// The additional length, in characters, to add to the base length.
        /// </param>
        /// <param name="baseCount">
        /// The base number of result items to check.
        /// </param>
        /// <param name="extraCount">
        /// The additional number of result items to add to the base count.
        /// </param>
        /// <param name="code">
        /// Upon failure, this is set to <see cref="ReturnCode.Error" />.
        /// </param>
        /// <param name="result">
        /// Upon failure, this receives an error message describing the limit
        /// that was exceeded.
        /// </param>
        internal static void CheckResultAgainstLimits(
            Interpreter interpreter,
            int baseLength,
            int extraLength,
            int baseCount,
            int extraCount,
            ref ReturnCode code,
            ref Result result
            )
        {
            if (interpreter == null)
                return;

            if ((baseLength == 0) && (extraLength == 0))
                return;

            if ((baseCount == 0) && (extraCount == 0))
                return;

            int executeResultLimit;
            int badLength;
            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    executeResultLimit = interpreter.InternalExecuteResultLimit;

                    if (executeResultLimit != Limits.Unlimited)
                    {
                        int length = baseLength + extraLength;

                        if ((length < 0) || (length > executeResultLimit))
                        {
                            badLength = length;
                            goto error;
                        }

                        int count = baseCount + extraCount;

                        if ((count < 0) || (count > executeResultLimit))
                        {
                            badLength = count; /* HACK: Kinda makes sense. */
                            goto error;
                        }

                        int totalLength = length * count;

                        if ((totalLength < 0) ||
                            (totalLength > executeResultLimit))
                        {
                            badLength = totalLength;
                            goto error;
                        }
                    }
                }
                else
                {
                    TraceOps.LockTrace(
                        "CheckResultAgainstLimits",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    result = "unable to acquire lock";
                    code = ReturnCode.Error;
                }

                return;
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }

        error:

            result = String.Format(
                "cannot exceed length of {0} characters ({1})",
                executeResultLimit, badLength);

            code = ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks an already-produced result against the
        /// specified maximum result length.  When the limit is exceeded, the
        /// result is reset, memory is reclaimed, and an error is produced.
        /// </summary>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result;
        /// <see cref="Limits.Unlimited" /> disables the check.
        /// </param>
        /// <param name="code">
        /// Upon failure, this is set to <see cref="ReturnCode.Error" />.
        /// </param>
        /// <param name="result">
        /// The result to check; upon failure, it is reset and receives an
        /// error message describing the limit that was exceeded.
        /// </param>
        private static void CheckResultAgainstLimits(
            int executeResultLimit,
            ref ReturnCode code,
            ref Result result
            )
        {
            if ((executeResultLimit != Limits.Unlimited) && (result != null))
            {
                int length = result.Length;

                if (length > executeResultLimit)
                {
                    result.Reset(ResultFlags.ResetObject);

                    ObjectOps.CollectGarbage(); /* NOTE: Force memory cleanup. */

                    result = String.Format(
                        "maximum result length of {0} characters exceeded ({1})",
                        executeResultLimit, length);

                    code = ReturnCode.Error;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks an already-produced argument value against the
        /// specified maximum result length.  When the limit is exceeded, the
        /// value and any error are reset, memory is reclaimed, and an error
        /// is produced.
        /// </summary>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the value;
        /// <see cref="Limits.Unlimited" /> disables the check.
        /// </param>
        /// <param name="code">
        /// Upon failure, this is set to <see cref="ReturnCode.Error" />.
        /// </param>
        /// <param name="value">
        /// The value to check; upon failure, it is reset to its empty, zero
        /// state.
        /// </param>
        /// <param name="error">
        /// Upon failure, this is reset and receives an error message
        /// describing the limit that was exceeded.
        /// </param>
        private static void CheckResultAgainstLimits(
            int executeResultLimit,
            ref ReturnCode code,
            ref Argument value,
            ref Result error
            )
        {
            if ((executeResultLimit != Limits.Unlimited) && (value != null))
            {
                int length = value.Length;

                if (length > executeResultLimit)
                {
                    value.Reset(ArgumentFlags.ResetWithZero);

                    if (error != null)
                        error.Reset(ResultFlags.ResetObject);

                    ObjectOps.CollectGarbage(); /* NOTE: Force memory cleanup. */

                    error = String.Format(
                        "maximum result length of {0} characters exceeded ({1})",
                        executeResultLimit, length);

                    code = ReturnCode.Error;
                }
            }
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Execution Statistics Methods
        /// <summary>
        /// This method enforces the operation and command usage quotas for
        /// the interpreter by incrementing its operation and command counts.
        /// For a "safe" interpreter, these counts are typically constrained
        /// by a configured quota.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose usage quotas should be enforced; this may
        /// be null.
        /// </param>
        /// <param name="usageData">
        /// The entity being executed; when it is an
        /// <see cref="ICommand" />, the command count is also incremented.
        /// This may be null.
        /// </param>
        /// <param name="code">
        /// Upon failure (i.e. a quota was exceeded), this is set to an error
        /// return code.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message describing the quota
        /// that was exceeded.
        /// </param>
        private static void CheckUsageAgainstLimits(
            Interpreter interpreter, /* in: OPTIONAL */
            IUsageData usageData,    /* in: OPTIONAL */
            ref ReturnCode code,     /* in, out */
            ref Result error         /* in, out */
            )
        {
            //
            // NOTE: Keep track of how many operations and commands
            //       are executed in this interpreter.  For "safe"
            //       interpreters, these will typically be (quite)
            //       limited by a (default or configured) quota.
            //
            if (interpreter != null)
            {
                interpreter.IncrementOperationAndCommandCount(
                    usageData is ICommand, ref code, ref error);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method updates the usage statistics for the executed entity,
        /// recording either the elapsed profiled time or a single use.  When
        /// usage data tracking is disabled via the engine flags, this method
        /// does nothing.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter associated with the execution; this may be null.
        /// </param>
        /// <param name="usageData">
        /// The entity whose usage statistics should be updated; this may be
        /// null.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control whether usage data is recorded.
        /// </param>
        /// <param name="microseconds">
        /// The elapsed execution time, in microseconds; when zero, a single
        /// use is counted instead.
        /// </param>
        /// <param name="code">
        /// Reserved for future use; this is not currently modified.
        /// </param>
        /// <param name="error">
        /// Reserved for future use; this is not currently modified.
        /// </param>
        private static void UpdateStatistics(
            Interpreter interpreter, /* in: OPTIONAL */
            IUsageData usageData,    /* in: OPTIONAL */
            EngineFlags engineFlags, /* in */
            long microseconds,       /* in */
            ref ReturnCode code,     /* in, out: NOT YET USED */
            ref Result error         /* in, out: NOT YET USED */
            )
        {
            //
            // NOTE: Keep track of how many times this particular
            //       entity has been used.
            //
            if ((usageData != null) &&
                !EngineFlagOps.HasNoUsageData(engineFlags))
            {
                if (microseconds != 0)
                {
                    /* IGNORED */
                    usageData.ProfileUsage(ref microseconds);
                }
                else
                {
                    long count = 1;

                    /* IGNORED */
                    usageData.CountUsage(ref count);
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Delegate Execution Methods
        /// <summary>
        /// This method dynamically invokes the specified delegate with the
        /// supplied arguments, capturing its return value or any exception
        /// that is thrown.
        /// </summary>
        /// <param name="delegate">
        /// The delegate to invoke; this may be null.
        /// </param>
        /// <param name="args">
        /// The array of arguments to pass to the delegate; this may be null.
        /// </param>
        /// <param name="returnValue">
        /// Upon success, this receives the value returned by the delegate.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message or the exception
        /// that was caught.
        /// </param>
        /// <returns>
        /// Returns <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ExecuteDelegate(
            Delegate @delegate,
            object[] args,
            ref object returnValue,
            ref Result error
            )
        {
            if (@delegate != null)
            {
                try
                {
                    returnValue = @delegate.DynamicInvoke(args);

                    return ReturnCode.Ok;
                }
                catch (Exception e)
                {
                    error = e;
                }
            }
            else
            {
                error = "invalid delegate";
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method invokes the specified delegate using the values of
        /// the supplied arguments and, upon success, converts its return
        /// value into a result.
        /// </summary>
        /// <param name="delegate">
        /// The delegate to invoke; this may be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments whose values are passed to the delegate;
        /// this may be null.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the delegate return value converted
        /// to a result; upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// Returns <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ExecuteDelegate(
            Delegate @delegate,
            ArgumentList arguments,
            ref Result result
            )
        {
            object[] args = null;

            if (arguments != null)
            {
                int count = arguments.Count;

                if (count > 0)
                {
                    args = new object[count];

                    for (int index = 0; index < count; index++)
                    {
                        Argument argument = arguments[index];

                        args[index] = (argument != null) ?
                            argument.Value : null;
                    }
                }
            }

            object returnValue = null;

            if (ExecuteDelegate(
                    @delegate, args, ref returnValue,
                    ref result) == ReturnCode.Ok)
            {
                result = Result.FromObject(
                    returnValue, true, false, false);

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Member Execution Methods
        /// <summary>
        /// This method resolves a framework type or object instance and then
        /// invokes the named member on it via reflection, returning the
        /// value produced by the invocation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter providing the binder, culture, and framework
        /// lookup; this may not be null.
        /// </param>
        /// <param name="id">
        /// The optional identifier used to select the framework type or
        /// object; this may be null.
        /// </param>
        /// <param name="frameworkFlags">
        /// The flags that control how the framework type or object is
        /// located.
        /// </param>
        /// <param name="bindingFlags">
        /// The reflection binding flags that control how the member is
        /// invoked.
        /// </param>
        /// <param name="memberName">
        /// The name of the member to invoke.
        /// </param>
        /// <param name="args">
        /// The array of arguments to pass to the member; this may be null.
        /// </param>
        /// <param name="clientData">
        /// Reserved for future use; this is not currently used.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the value produced by the member
        /// invocation; upon failure, it receives an error message or the
        /// exception that was caught.
        /// </param>
        /// <returns>
        /// Returns <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ExecuteMember(
            Interpreter interpreter,       /* in */
            Guid? id,                      /* in */
            FrameworkFlags frameworkFlags, /* in */
            BindingFlags bindingFlags,     /* in */
            string memberName,             /* in */
            object[] args,                 /* in */
            ref IClientData clientData,    /* in, out: NOT USED, RESERVED */
            ref Result result              /* out */
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            Result localResult = null; /* REUSED */

            if (interpreter.GetFramework(
                    id, frameworkFlags,
                    ref localResult) != ReturnCode.Ok)
            {
                result = localResult;
                return ReturnCode.Error;
            }

            object target;
            Type type;

            if (localResult.Value is Type)
            {
                target = null;
                type = (Type)localResult.Value;
            }
            else
            {
                target = localResult.Value;
                type = target.GetType();
            }

            IBinder binder = interpreter.InternalBinder;
            CultureInfo cultureInfo = interpreter.InternalCultureInfo;

            localResult = null;

            try
            {
                localResult = Result.FromObject(type.InvokeMember(
                    memberName, bindingFlags, binder as Binder,
                    target, args, cultureInfo), true, false,
                    false); /* throw */

                result = localResult;
                return ReturnCode.Ok;
            }
            catch (Exception e)
            {
                result = e;
                return ReturnCode.Error;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region IExecute Execution Methods
        #region Private IExecute Execution Methods
        /// <summary>
        /// This method executes the specified <see cref="IExecute" /> entity
        /// in the context of the interpreter, enforcing usage and result
        /// limits and updating the associated statistics.  Any exception
        /// thrown during execution is caught and converted into an error
        /// result.  Entities executed via this method do not increment the
        /// command count for the interpreter.
        /// </summary>
        /// <param name="execute">
        /// The executable entity to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; this is not used by this
        /// method.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; this is not used by this method.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteIExecute(
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags, /* NOT USED */
            EventFlags eventFlags,
            ExpressionFlags expressionFlags, /* NOT USED */
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (execute != null)
                {
                    //
                    // NOTE: Execute the IExecute in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, null, ref code, ref result);

                    if (code != ReturnCode.Ok)
                        return code;

#if PERFORMANCE_DIAGNOSIS
                    long __probeTs = Diagnostics.PerfProbe.Now;
#endif
                    try
                    {
                        //
                        // NOTE: Execute the IExecute in the context of the interpreter.
                        //       Commands executed via this method do NOT increment the
                        //       command count for the interpreter.  This behavior is by
                        //       design.
                        //
                        code = execute.Execute(
                            interpreter, clientData, arguments,
                            ref result); /* throw */
                    }
                    finally
                    {
#if PERFORMANCE_DIAGNOSIS
                        if (Diagnostics.PerfProbe.Enabled && arguments != null && arguments.Count > 0)
                        { Diagnostics.PerfProbe.Add("X:" + (string)arguments[0], __probeTs); }
#endif
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref result);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, null, engineFlags,
                            microseconds, ref code, ref result);

#if NOTIFY && NOTIFY_EXECUTE
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.IExecute, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.IExecute, NotifyFlags.Executed,
                                new ObjectList(execute, code, engineFlags,
                                substitutionFlags, eventFlags, expressionFlags),
                                interpreter, clientData, arguments, null,
                                ref result);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    result = "invalid execute";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing: ",
                    arguments, TracePriority.DisposedError);
#endif

                result = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing: ",
                    arguments, TracePriority.GeneralError);
#endif

                result = String.Format(
                    "caught exception while executing: {0}",
                    e);

                result.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            execute, interpreter,
                            clientData, arguments, e, ref result);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes the specified <see cref="IExecute" /> entity,
        /// checking for any applicable before and after breakpoints and then
        /// delegating the actual execution to <c>PrivateExecuteIExecute</c>.
        /// </summary>
        /// <param name="execute">
        /// The executable entity to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode ExecuteIExecute(
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;

#if DEBUGGER && DEBUGGER_EXECUTE
            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    engineFlags, BreakpointType.BeforeIExecute))
            {
                code = CheckBreakpoints(
                    code, BreakpointType.BeforeIExecute, null,
                    null, null, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, execute, null,
                    interpreter, clientData, arguments,
                    ref result);
            }
#endif

            if (code == ReturnCode.Ok)
            {
                code = PrivateExecuteIExecute(
                    execute, interpreter, clientData, arguments, engineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref exception, ref result);

#if DEBUGGER && DEBUGGER_EXECUTE
                if (usable && DebuggerOps.CanHitBreakpoints(interpreter,
                        engineFlags, BreakpointType.AfterIExecute))
                {
                    code = CheckBreakpoints(
                        code, BreakpointType.AfterIExecute, null,
                        null, null, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, execute, null,
                        interpreter, clientData, arguments,
                        ref result);
                }
#endif
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region SubCommand Execution Methods
        #region Private SubCommand Execution Methods
        /// <summary>
        /// This method executes the specified sub-command in the context of
        /// the interpreter, preferring its execute callback when one is
        /// present, enforcing usage and result limits and updating the
        /// associated statistics.  Any exception thrown during execution is
        /// caught and converted into an error result.
        /// </summary>
        /// <param name="subCommand">
        /// The sub-command to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the sub-command is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the sub-command; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the sub-command.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the sub-command;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteSubCommand(
            ISubCommand subCommand,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (subCommand != null)
                {
                    //
                    // NOTE: Execute the sub-command in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PERFORMANCE_DIAGNOSIS
                    long __probeTs = 0;
#endif
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, subCommand, ref code, ref result);

                    if (code != ReturnCode.Ok)
                        return code;

                    try
                    {
                        ExecuteCallback callback = subCommand.Callback;

#if PERFORMANCE_DIAGNOSIS
                        __probeTs = Diagnostics.PerfProbe.Now;
#endif
                        if (callback != null)
                        {
                            code = callback(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                        else
                        {
                            code = subCommand.Execute(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                    }
                    finally
                    {
#if PERFORMANCE_DIAGNOSIS
                        if (Diagnostics.PerfProbe.Enabled && arguments != null && arguments.Count > 0)
                        { Diagnostics.PerfProbe.Add("S:" + (string)arguments[0], __probeTs); }
#endif
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref result);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, subCommand, engineFlags,
                            microseconds, ref code, ref result);

#if NOTIFY && NOTIFY_EXECUTE
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.SubCommand, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.SubCommand, NotifyFlags.Executed,
                                new ObjectList(subCommand, code, engineFlags,
                                substitutionFlags, eventFlags, expressionFlags),
                                interpreter, clientData, arguments, null,
                                ref result);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    result = "invalid sub-command";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing sub-command: ",
                    arguments, TracePriority.DisposedError);
#endif

                result = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing sub-command: ",
                    arguments, TracePriority.GeneralError);
#endif

                result = String.Format(
                    "caught exception while executing sub-command: {0}",
                    e);

                result.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            subCommand, interpreter,
                            clientData, arguments, e, ref result);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes the specified sub-command, checking for any
        /// applicable before and after breakpoints, resetting the
        /// interpreter return code as appropriate, and delegating the actual
        /// execution to <c>PrivateExecuteSubCommand</c>.  When the
        /// sub-command requests a return, the interpreter return information
        /// is updated.
        /// </summary>
        /// <param name="subCommand">
        /// The sub-command to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the sub-command is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the sub-command; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the sub-command.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the sub-command;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode ExecuteSubCommand(
            ISubCommand subCommand,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;

#if DEBUGGER && DEBUGGER_EXECUTE
            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    engineFlags, BreakpointType.BeforeSubCommand))
            {
                code = CheckBreakpoints(
                    code, BreakpointType.BeforeSubCommand, null,
                    null, null, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, subCommand,
                    null, interpreter, clientData, arguments,
                    ref result);
            }
#endif

            if (code == ReturnCode.Ok)
            {
                if ((interpreter != null) && (subCommand.Command == null))
                    interpreter.ReturnCode = ReturnCode.Ok;

                code = PrivateExecuteSubCommand(
                    subCommand, interpreter, clientData, arguments, engineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref exception, ref result);

                if (usable)
                {
#if DEBUGGER && DEBUGGER_EXECUTE
                    if (DebuggerOps.CanHitBreakpoints(interpreter,
                            engineFlags, BreakpointType.AfterSubCommand))
                    {
                        code = CheckBreakpoints(
                            code, BreakpointType.AfterSubCommand, null,
                            null, null, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags, subCommand,
                            null, interpreter, clientData, arguments,
                            ref result);
                    }
#endif

                    if ((code == ReturnCode.Return) &&
                        (interpreter != null) && (subCommand.Command == null))
                    {
                        bool locked = false;

                        try
                        {
                            interpreter.InternalEngineTryLock(
                                ref locked); /* TRANSACTIONAL */

                            if (locked)
                            {
                                if (!interpreter.InternalIsBusy)
                                    code = UpdateReturnInformation(interpreter);
                            }
                            else
                            {
                                TraceOps.LockTrace(
                                    "ExecuteSubCommand",
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
                    }
                }
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Command Execution Methods
        #region Private Command Execution Methods
        /// <summary>
        /// This method executes the specified command in the context of the
        /// interpreter, preferring its execute callback when one is present,
        /// enforcing usage and result limits and updating the associated
        /// statistics.  Any exception thrown during execution is caught and
        /// converted into an error result.
        /// </summary>
        /// <param name="command">
        /// The command to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the command is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the command; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the command.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the command;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteCommand(
            ICommand command,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (command != null)
                {
                    //
                    // NOTE: Execute the command in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PERFORMANCE_DIAGNOSIS
                    long __probeTs = 0;
#endif
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, command, ref code, ref result);

                    if (code != ReturnCode.Ok)
                        return code;

                    try
                    {
                        ExecuteCallback callback = command.Callback;

#if PERFORMANCE_DIAGNOSIS
                        __probeTs = Diagnostics.PerfProbe.Now;
#endif
                        if (callback != null)
                        {
                            code = callback(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                        else
                        {
                            code = command.Execute(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                    }
                    finally
                    {
#if PERFORMANCE_DIAGNOSIS
                        if (Diagnostics.PerfProbe.Enabled && arguments != null && arguments.Count > 0)
                        { Diagnostics.PerfProbe.Add("C:" + (string)arguments[0], __probeTs); }
#endif
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref result);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, command, engineFlags,
                            microseconds, ref code, ref result);

#if NOTIFY && NOTIFY_EXECUTE
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.Command, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Command, NotifyFlags.Executed,
                                new ObjectList(command, code, engineFlags,
                                substitutionFlags, eventFlags, expressionFlags),
                                interpreter, clientData, arguments, null,
                                ref result);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    result = "invalid command";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing command: ",
                    arguments, TracePriority.DisposedError);
#endif

                result = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing command: ",
                    arguments, TracePriority.GeneralError);
#endif

                result = String.Format(
                    "caught exception while executing command: {0}",
                    e);

                result.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            command, interpreter,
                            clientData, arguments, e, ref result);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes the specified command, checking for any
        /// applicable before and after breakpoints, resetting the
        /// interpreter return code, and delegating the actual execution to
        /// <c>PrivateExecuteCommand</c>.  When the command requests a
        /// return, the interpreter return information is updated.
        /// </summary>
        /// <param name="command">
        /// The command to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the command is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the command; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the command.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the command;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode ExecuteCommand(
            ICommand command,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;

#if DEBUGGER && DEBUGGER_EXECUTE
            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    engineFlags, BreakpointType.BeforeCommand))
            {
                code = CheckBreakpoints(
                    code, BreakpointType.BeforeCommand, null,
                    null, null, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, command, null,
                    interpreter, clientData, arguments,
                    ref result);
            }
#endif

            if (code == ReturnCode.Ok)
            {
                if (interpreter != null)
                    interpreter.ReturnCode = ReturnCode.Ok;

                code = PrivateExecuteCommand(
                    command, interpreter, clientData, arguments, engineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref exception, ref result);

                if (usable)
                {
#if DEBUGGER && DEBUGGER_EXECUTE
                    if (DebuggerOps.CanHitBreakpoints(interpreter,
                            engineFlags, BreakpointType.AfterCommand))
                    {
                        code = CheckBreakpoints(
                            code, BreakpointType.AfterCommand, null,
                            null, null, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags, command, null,
                            interpreter, clientData, arguments,
                            ref result);
                    }
#endif

                    if ((code == ReturnCode.Return) && (interpreter != null))
                    {
                        bool locked = false;

                        try
                        {
                            interpreter.InternalEngineTryLock(
                                ref locked); /* TRANSACTIONAL */

                            if (locked)
                            {
                                if (!interpreter.InternalIsBusy)
                                    code = UpdateReturnInformation(interpreter);
                            }
                            else
                            {
                                TraceOps.LockTrace(
                                    "ExecuteCommand",
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
                    }
                }
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Procedure Execution Methods
        #region Private Procedure Execution Methods
        /// <summary>
        /// This method executes the specified procedure in the context of
        /// the interpreter, preferring its execute callback when one is
        /// present, enforcing usage and result limits and updating the
        /// associated statistics.  Any exception thrown during execution is
        /// caught and converted into an error result.
        /// </summary>
        /// <param name="procedure">
        /// The procedure to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the procedure is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the procedure; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the procedure.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the procedure;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteProcedure(
            IProcedure procedure,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (procedure != null)
                {
                    //
                    // NOTE: Execute the procedure in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, procedure, ref code, ref result);

                    if (code != ReturnCode.Ok)
                        return code;

                    try
                    {
                        ExecuteCallback callback = procedure.Callback;

                        if (callback != null)
                        {
                            code = callback(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                        else
                        {
                            code = procedure.Execute(
                                interpreter, clientData, arguments,
                                ref result); /* throw */
                        }
                    }
                    finally
                    {
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref result);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, procedure, engineFlags,
                            microseconds, ref code, ref result);

#if NOTIFY && NOTIFY_EXECUTE
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.Procedure, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Procedure, NotifyFlags.Executed,
                                new ObjectList(procedure, code, engineFlags,
                                substitutionFlags, eventFlags, expressionFlags),
                                interpreter, clientData, arguments, null,
                                ref result);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    result = "invalid procedure";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing procedure: ",
                    arguments, TracePriority.DisposedError);
#endif

                result = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing procedure: ",
                    arguments, TracePriority.GeneralError);
#endif

                result = String.Format(
                    "caught exception while executing procedure: {0}",
                    e);

                result.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            procedure, interpreter,
                            clientData, arguments, e, ref result);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes the specified procedure, checking for any
        /// applicable before and after breakpoints, and delegating the
        /// actual execution to <c>PrivateExecuteProcedure</c>.
        /// </summary>
        /// <param name="procedure">
        /// The procedure to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the procedure is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the procedure; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the procedure.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the procedure;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode ExecuteProcedure(
            IProcedure procedure,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Result result
            )
        {
            ReturnCode code = ReturnCode.Ok;

#if DEBUGGER && DEBUGGER_EXECUTE
            if (DebuggerOps.CanHitBreakpoints(interpreter,
                    engineFlags, BreakpointType.BeforeProcedure))
            {
                code = CheckBreakpoints(
                    code, BreakpointType.BeforeProcedure, null,
                    null, null, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, procedure,
                    null, interpreter, clientData, arguments,
                    ref result);
            }
#endif

            if (code == ReturnCode.Ok)
            {
                code = PrivateExecuteProcedure(
                    procedure, interpreter, clientData, arguments, engineFlags,
                    substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref exception, ref result);

#if DEBUGGER && DEBUGGER_EXECUTE
                if (usable && DebuggerOps.CanHitBreakpoints(interpreter,
                        engineFlags, BreakpointType.AfterProcedure))
                {
                    code = CheckBreakpoints(
                        code, BreakpointType.AfterProcedure, null,
                        null, null, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, procedure,
                        null, interpreter, clientData, arguments,
                        ref result);
                }
#endif
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Function Execution Methods
        #region Private Function Execution Methods
        /// <summary>
        /// This method executes the specified function in the context of the
        /// interpreter, enforcing usage and result limits and updating the
        /// associated statistics.  Any exception thrown during execution is
        /// caught and converted into an error.
        /// </summary>
        /// <param name="function">
        /// The function to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the function is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the function; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the function.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the value.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="value">
        /// Upon success, this receives the value produced by the function.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message or the exception
        /// that was caught.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteFunction(
            IFunction function,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Argument value,
            ref Result error
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (function != null)
                {
                    //
                    // NOTE: Execute the function in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, function, ref code, ref error);

                    if (code != ReturnCode.Ok)
                        return code;

                    try
                    {
                        code = function.Execute(
                            interpreter, clientData, arguments,
                            ref value, ref error); /* throw */
                    }
                    finally
                    {
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref value, ref error);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, function, engineFlags,
                            microseconds, ref code, ref error);

#if NOTIFY && NOTIFY_EXPRESSION
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.Function, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Function, NotifyFlags.Executed,
                                new ObjectList(function, code, value,
                                engineFlags, substitutionFlags, eventFlags,
                                expressionFlags), interpreter, clientData,
                                arguments, null, ref error);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    error = "invalid function";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing function: ",
                    arguments, TracePriority.DisposedError);
#endif

                error = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing function: ",
                    arguments, TracePriority.GeneralError);
#endif

                error = String.Format(
                    "caught exception while executing function: {0}",
                    e);

                error.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            function, interpreter,
                            clientData, arguments, e, ref error);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Public Function Execution Methods
        /// <summary>
        /// This method executes the specified function, verifying that it is
        /// enabled and permitted for the interpreter, checking for any
        /// applicable before and after breakpoints, and delegating the
        /// actual execution to <c>PrivateExecuteFunction</c>.
        /// </summary>
        /// <param name="function">
        /// The function to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the function is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the function; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the function.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the value.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="value">
        /// Upon success, this receives the value produced by the function.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message or the exception
        /// that was caught.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        internal static ReturnCode ExecuteFunction(
            IFunction function,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Argument value,
            ref Result error
            )
        {
            ReturnCode code = ReturnCode.Ok;

            if (!EntityOps.IsDisabled(function))
            {
                bool isSafe = false;

                if (EngineFlagOps.HasNoSafeFunction(engineFlags) ||
                    EntityOps.IsSafe(function) ||
                    !(isSafe = interpreter.InternalIsSafe()))
                {
#if DEBUGGER && DEBUGGER_EXPRESSION
                    if (DebuggerOps.CanHitBreakpoints(interpreter,
                            engineFlags, BreakpointType.BeforeFunction))
                    {
                        code = CheckBreakpoints(
                            code, BreakpointType.BeforeFunction, null,
                            null, null, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags, null, function,
                            interpreter, clientData, new ArgumentList(
                            arguments, value), ref error);
                    }
#endif

                    if (code == ReturnCode.Ok)
                    {
                        code = PrivateExecuteFunction(
                            function, interpreter, clientData, arguments, engineFlags,
                            substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                            executeResultLimit,
#endif
                            ref usable, ref exception, ref value, ref error);

#if DEBUGGER && DEBUGGER_EXPRESSION
                        if (usable && DebuggerOps.CanHitBreakpoints(interpreter,
                                engineFlags, BreakpointType.AfterFunction))
                        {
                            code = CheckBreakpoints(
                                code, BreakpointType.AfterFunction, null,
                                null, null, engineFlags, substitutionFlags,
                                eventFlags, expressionFlags, null, function,
                                interpreter, clientData, new ArgumentList(
                                arguments, value), ref error);
                        }
#endif
                    }
                }
                else
                {
                    error = String.Format(
                        "permission denied: {0}interpreter cannot use function {1}",
                        isSafe ? "safe " : String.Empty, FormatOps.DisplayName(
                        function));

                    code = ReturnCode.Error;
                }
            }
            else
            {
                error = String.Format(
                    "invalid function name {0}",
                    FormatOps.DisplayName(function));

                code = ReturnCode.Error;
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Operator Execution Methods
        #region Private Operator Execution Methods
        /// <summary>
        /// This method executes the specified operator in the context of the
        /// interpreter, enforcing usage and result limits and updating the
        /// associated statistics.  Any exception thrown during execution is
        /// caught and converted into an error.
        /// </summary>
        /// <param name="operator">
        /// The operator to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the operator is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the operator; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the operator.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the value.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="value">
        /// Upon success, this receives the value produced by the operator.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message or the exception
        /// that was caught.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode PrivateExecuteOperator(
            IOperator @operator,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Argument value,
            ref Result error
            )
        {
            bool interpreterPushed = GlobalState.MaybePushActiveInterpreter(
                interpreter); /* NOTE: Skipped when already the active one. */

            try
            {
                if (@operator != null)
                {
                    //
                    // NOTE: Execute the operator in the context of the interpreter.
                    //
                    long microseconds = 0;
#if PROFILER
                    IProfilerState profiler = GetProfilerAndStart(interpreter);
#endif
                    ReturnCode code = ReturnCode.Ok;

                    /* NO RESULT */
                    CheckUsageAgainstLimits(
                        interpreter, @operator, ref code, ref error);

                    if (code != ReturnCode.Ok)
                        return code;

                    try
                    {
                        //
                        // NOTE: Execute the operator in the context of the interpreter.
                        //
                        code = @operator.Execute(
                            interpreter, clientData, arguments,
                            ref value, ref error); /* throw */
                    }
                    finally
                    {
                        usable = IsUsableNoLock(interpreter);

#if PROFILER
                        if (IsUsableNoLock(profiler, usable))
                        {
                            microseconds = ConversionOps.ToLong(
                                Math.Round(profiler.Stop()));
                        }
#endif
                    }

                    if (usable)
                    {
#if RESULT_LIMITS
                        /* NO RESULT */
                        CheckResultAgainstLimits(
                            executeResultLimit, ref code, ref value, ref error);
#endif

                        /* NO RESULT */
                        UpdateStatistics(
                            interpreter, @operator, engineFlags,
                            microseconds, ref code, ref error);

#if NOTIFY && NOTIFY_EXPRESSION
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags) &&
                            interpreter.ShouldMaybeFireNotification(
                                NotifyType.Operator, NotifyFlags.Executed))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Operator, NotifyFlags.Executed,
                                new ObjectList(@operator, code, value,
                                engineFlags, substitutionFlags, eventFlags,
                                expressionFlags), interpreter, clientData,
                                arguments, null, ref error);
                        }
#endif
                    }

                    return code;
                }
                else
                {
                    error = "invalid operator";
                }
            }
#if DEBUG || FORCE_TRACE
            catch (InterpreterDisposedException e)
#else
            catch (InterpreterDisposedException)
#endif
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "interpreter was disposed while executing operator: ",
                    arguments, TracePriority.DisposedError);
#endif

                error = Result.Copy(
                    InterpreterUnusableError, ResultFlags.CopyValue);
            }
            catch (Exception e)
            {
                exception = true;

#if DEBUG || FORCE_TRACE
                TraceOps.DebugTrace(e, typeof(Engine).Name,
                    "caught exception while executing operator: ",
                    arguments, TracePriority.GeneralError);
#endif

                error = String.Format(
                    "caught exception while executing operator: {0}",
                    e);

                error.Exception = e;

                if (usable)
                {
                    SetExceptionErrorCode(interpreter, e);

#if NOTIFY && NOTIFY_EXCEPTION
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            @operator, interpreter,
                            clientData, arguments, e, ref error);
                    }
#endif
                }
            }
            finally
            {
                if (interpreterPushed)
                {
                    /* IGNORED */
                    GlobalState.PopActiveInterpreter();
                }
            }

            return ReturnCode.Error;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Public Operator Execution Methods
        /// <summary>
        /// This method executes the specified operator, verifying that it is
        /// enabled, checking for any applicable before and after
        /// breakpoints, and delegating the actual execution to
        /// <c>PrivateExecuteOperator</c>.
        /// </summary>
        /// <param name="operator">
        /// The operator to execute; this may not be null.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the operator is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the operator; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the operator.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when issuing any execution notifications.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the value.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="exception">
        /// Upon return, this is non-zero if an exception was caught during
        /// execution.
        /// </param>
        /// <param name="value">
        /// Upon success, this receives the value produced by the operator.
        /// </param>
        /// <param name="error">
        /// Upon failure, this receives an error message or the exception
        /// that was caught.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        internal static ReturnCode ExecuteOperator(
            IOperator @operator,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref bool exception,
            ref Argument value,
            ref Result error
            )
        {
            ReturnCode code = ReturnCode.Ok;

            if (!EntityOps.IsDisabled(@operator))
            {
#if DEBUGGER && DEBUGGER_EXPRESSION
                if (DebuggerOps.CanHitBreakpoints(interpreter,
                        engineFlags, BreakpointType.BeforeOperator))
                {
                    code = CheckBreakpoints(
                        code, BreakpointType.BeforeOperator, null,
                        null, null, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, null, @operator,
                        interpreter, clientData, new ArgumentList(
                        arguments, value), ref error);
                }
#endif

                if (code == ReturnCode.Ok)
                {
                    code = PrivateExecuteOperator(
                        @operator, interpreter, clientData, arguments, engineFlags,
                        substitutionFlags, eventFlags, expressionFlags,
#if RESULT_LIMITS
                        executeResultLimit,
#endif
                        ref usable, ref exception, ref value, ref error);

#if DEBUGGER && DEBUGGER_EXPRESSION
                    if (usable && DebuggerOps.CanHitBreakpoints(interpreter,
                            engineFlags, BreakpointType.AfterOperator))
                    {
                        code = CheckBreakpoints(
                            code, BreakpointType.AfterOperator, null,
                            null, null, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags, null, @operator,
                            interpreter, clientData, new ArgumentList(
                            arguments, value), ref error);
                    }
#endif
                }
            }
            else
            {
                error = String.Format(
                    "invalid operator name {0}",
                    FormatOps.DisplayName(@operator));

                code = ReturnCode.Error;
            }

            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region External Execution Methods
        /// <summary>
        /// This method executes the specified <see cref="IExecute" /> entity
        /// after verifying that the interpreter is usable.  It is a
        /// convenience wrapper that resolves the effective client data and
        /// forwards to the core execution method.
        /// </summary>
        /// <param name="name">
        /// The name of the entity to execute, used for diagnostic messages.
        /// </param>
        /// <param name="execute">
        /// The executable entity to execute.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when processing any asynchronous events.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        internal static ReturnCode Execute(
            string name,
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref Result result
            )
        {
            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

            return Execute(
                name, execute, interpreter,
                GetClientData(
                    interpreter, clientData, false),
                arguments, engineFlags,
                substitutionFlags, eventFlags,
                expressionFlags,
#if RESULT_LIMITS
                executeResultLimit,
#endif
                ref usable, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the central execution entry point used by both the
        /// evaluation engine core and external callers; any semantics that
        /// must apply to every command, sub-command, procedure, or other
        /// executable entity are applied here.  It cooperatively processes
        /// any pending asynchronous events, enforces the hidden, disabled,
        /// and policy checks, dispatches to the appropriate type-specific
        /// execution method, and rebalances the call stack if an exception
        /// occurs.
        /// </summary>
        /// <param name="name">
        /// The name of the entity to execute, used for diagnostic messages.
        /// </param>
        /// <param name="execute">
        /// The executable entity to execute.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed,
        /// including the hidden and policy handling.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when processing any asynchronous events.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode Execute(
            string name,
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            //
            // NOTE: Script-level execution traces (see the "trace" command
            //       and ScriptTraceOps): the state reference is null until
            //       the first trace is added, so this check is a single
            //       field read on the hot path.
            //
            ScriptTraceState traceState =
                ScriptTraceOps.GetActiveState(interpreter);

            if (traceState == null)
            {
                return ExecuteCore(
                    name, execute, interpreter, clientData, arguments,
                    engineFlags, substitutionFlags, eventFlags,
                    expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref result);
            }

            ReturnCode traceCode;
            int stepCount = 0;

            traceCode = ScriptTraceOps.FireEnterTraces(
                traceState, interpreter, name, arguments, ref stepCount,
                ref result);

            if (traceCode != ReturnCode.Ok)
            {
                //
                // NOTE: Deactivate any step traces activated before the
                //       failure and abort the command.
                //
                if (stepCount > 0)
                {
                    traceState.ActiveStepTraces.RemoveRange(
                        traceState.ActiveStepTraces.Count - stepCount,
                        stepCount);
                }

                return traceCode;
            }

            traceCode = ExecuteCore(
                name, execute, interpreter, clientData, arguments,
                engineFlags, substitutionFlags, eventFlags,
                expressionFlags,
#if RESULT_LIMITS
                executeResultLimit,
#endif
                ref usable, ref result);

            if (usable)
            {
                ScriptTraceOps.FireLeaveTraces(
                    traceState, interpreter, name, arguments, stepCount,
                    ref traceCode, ref result);
            }

            return traceCode;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core implementation behind the central
        /// execution entry point above; it must only be called by that
        /// method, which layers the script-level execution traces on top.
        /// </summary>
        private static ReturnCode ExecuteCore(
            string name,
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code;

            ///////////////////////////////////////////////////////////////////////////////////
            //
            // NOTE: This function is called by the evaluation engine core and by external
            //       callers that have some kind of IExecute compatible object that needs to be
            //       executed.  Any semantic changes that need to be applied to each and every
            //       script command and procedure execution should be done in the function and
            //       ONLY in this function.
            //
            ///////////////////////////////////////////////////////////////////////////////////

            //
            // NOTE: Did we succeed at finding something to execute?
            //
            if (execute != null)
            {
                if (interpreter != null)
                {
                    bool locked; /* REUSED */
                    ICallFrame peekFrame = null;

                    locked = false;

                    try
                    {
                        interpreter.InternalEngineTryLock(
                            ref locked); /* TRANSACTIONAL */

                        if (locked)
                        {
                            if (interpreter.CanPeekCallFrame())
                                peekFrame = interpreter.PeekCallFrame();
                        }
                        else
                        {
                            TraceOps.LockTrace(
                                "Execute",
                                typeof(Engine).Name, false,
                                TracePriority.LockError,
                                interpreter.MaybeWhoHasLock());

                            result = "unable to acquire lock";
                            return ReturnCode.Error;
                        }
                    }
                    finally
                    {
                        interpreter.InternalExitLock(
                            ref locked); /* TRANSACTIONAL */
                    }

                    bool exception = false;

                    GlobalState.PushActiveInterpreter(interpreter);

                    try
                    {
                        //
                        // NOTE: Cooperatively check for any pending asynchronous
                        //       events for this interpreter.  If an asynchronous
                        //       event returns an error, other events are skipped
                        //       and we also skip executing the command.
                        //
                        // WARNING: Please do not add calls to this function after
                        //          any kind of IExecute has been executed (here or
                        //          elsewhere) because the state of the interpreter
                        //          can no longer be relied upon and any processed
                        //          events could potentially mask the real results
                        //          of the IExecute.
                        //
                        code = CheckEvents(
                            interpreter, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags, ref result);

                        //
                        // NOTE: If all the asynchronous events were processed
                        //       successfully (or there were none), attempt to
                        //       execute the command.
                        //
                        if (code == ReturnCode.Ok)
                        {
                            bool isSafe = false; /* REUSED */

                            if (execute is ICommand)
                            {
                                ICommand command = (ICommand)execute;

                                if (!EntityOps.IsDisabled(command))
                                {
                                    bool ignoreHidden = EngineFlagOps.HasIgnoreHidden(engineFlags);
                                    bool invokeHidden = EngineFlagOps.HasInvokeHidden(engineFlags);
                                    bool isHidden = EntityOps.IsHidden(command);

                                    if (ignoreHidden || (invokeHidden == isHidden))
                                    {
                                        code = ExecuteCommand(command, interpreter,
                                            (clientData != null) ? clientData : command.ClientData,
                                            arguments, engineFlags, substitutionFlags, eventFlags,
                                            expressionFlags,
#if RESULT_LIMITS
                                            executeResultLimit,
#endif
                                            ref usable, ref exception, ref result);
                                    }
                                    else if (isHidden)
                                    {
                                        //
                                        // NOTE: *POLICY* See if hidden command is allowed to be
                                        //       executed, based on whatever criteria the current
                                        //       policies evaluate.  However, if the interpreter is
                                        //       not "safe", the command was purposely hidden and
                                        //       will not be executed.
                                        //
                                        ReturnCode? commandCode = null;
                                        PolicyDecision commandDecision = interpreter.CommandInitialDecision;
                                        Result commandPolicyResult = null;

                                        if (!EngineFlagOps.HasNoPolicy(engineFlags) &&
                                            (isSafe = interpreter.InternalIsSafe()) &&
                                            ((commandCode = interpreter.CheckCommandPolicies(
                                                PolicyFlags.EngineBeforeCommand, command, arguments, null,
                                                ref commandDecision, ref commandPolicyResult)) == ReturnCode.Ok) &&
                                            PolicyContext.IsApproved(commandDecision))
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeCommand, commandCode,
                                                commandDecision);

                                            code = ExecuteCommand(command, interpreter,
                                                (clientData != null) ? clientData : command.ClientData,
                                                arguments, engineFlags, substitutionFlags, eventFlags,
                                                expressionFlags,
#if RESULT_LIMITS
                                                executeResultLimit,
#endif
                                                ref usable, ref exception, ref result);
                                        }
                                        else
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeCommand, commandCode,
                                                commandDecision);

                                            if (commandPolicyResult != null)
                                            {
                                                result = commandPolicyResult;
                                            }
                                            else
                                            {
                                                result = String.Format(
                                                    "permission denied: {0}interpreter cannot use {1}command {2}",
                                                    isSafe ? "safe " : String.Empty, !isSafe && isHidden ? "hidden " :
                                                    String.Empty, FormatOps.DisplayName(command, interpreter, arguments));
                                            }

                                            code = ReturnCode.Error;
                                        }

#if POLICY_TRACE
                                        TraceOps.MaybeWritePolicyTrace("Execute", interpreter,
                                            !PolicyContext.GetForceTraceFull(), "name", name,
                                            "execute", execute, "clientData", clientData,
                                            "arguments", arguments, "engineFlags", engineFlags,
                                            "substitutionFlags", substitutionFlags, "eventFlags",
                                            eventFlags, "expressionFlags", expressionFlags,
                                            "ignoreHidden", ignoreHidden, "invokeHidden",
                                            invokeHidden, "isHidden", isHidden, "code", code,
                                            "commandDecision", commandDecision,
                                            "commandPolicyResult", commandPolicyResult, "usable",
                                            usable, "exception", exception, "result", result);
#endif
                                    }
                                    else
                                    {
                                        result = String.Format(
                                            "command {0} is {1}hidden",
                                            FormatOps.DisplayName(name),
                                            isHidden ? String.Empty : "not ");

                                        code = ReturnCode.Error;
                                    }
                                }
                                else
                                {
                                    result = String.Format(
                                        "command {0} is disabled",
                                        FormatOps.DisplayName(name));

                                    code = ReturnCode.Error;
                                }
                            }
                            else if (execute is ISubCommand)
                            {
                                ISubCommand subCommand = (ISubCommand)execute;

                                if (!EntityOps.IsDisabled(subCommand))
                                {
                                    bool ignoreHidden = EngineFlagOps.HasIgnoreHidden(engineFlags);
                                    bool invokeHidden = EngineFlagOps.HasInvokeHidden(engineFlags);
                                    bool isHidden = EntityOps.IsHidden(subCommand);

                                    if (ignoreHidden || (invokeHidden == isHidden))
                                    {
                                        code = ExecuteSubCommand(subCommand, interpreter,
                                            (clientData != null) ? clientData : subCommand.ClientData,
                                            arguments, engineFlags, substitutionFlags, eventFlags,
                                            expressionFlags,
#if RESULT_LIMITS
                                            executeResultLimit,
#endif
                                            ref usable, ref exception, ref result);
                                    }
                                    else if (isHidden)
                                    {
                                        //
                                        // NOTE: *POLICY* See if hidden sub-command is allowed to be
                                        //       executed, based on whatever criteria the current
                                        //       policies evaluate.  However, if the interpreter is
                                        //       not "safe", the sub-command was purposely hidden and
                                        //       will not be executed.
                                        //
                                        ReturnCode? commandCode = null;
                                        PolicyDecision commandDecision = interpreter.CommandInitialDecision;
                                        Result commandPolicyResult = null;

                                        if (!EngineFlagOps.HasNoPolicy(engineFlags) &&
                                            (isSafe = interpreter.InternalIsSafe()) &&
                                            ((commandCode = interpreter.CheckCommandPolicies(
                                                PolicyFlags.EngineBeforeSubCommand, subCommand, arguments,
                                                null, ref commandDecision, ref commandPolicyResult)) == ReturnCode.Ok) &&
                                            PolicyContext.IsApproved(commandDecision))
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeSubCommand, commandCode,
                                                commandDecision);

                                            code = ExecuteSubCommand(subCommand, interpreter,
                                                (clientData != null) ? clientData : subCommand.ClientData,
                                                arguments, engineFlags, substitutionFlags, eventFlags,
                                                expressionFlags,
#if RESULT_LIMITS
                                                executeResultLimit,
#endif
                                                ref usable, ref exception, ref result);
                                        }
                                        else
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeSubCommand, commandCode,
                                                commandDecision);

                                            if (commandPolicyResult != null)
                                            {
                                                result = commandPolicyResult;
                                            }
                                            else
                                            {
                                                result = String.Format(
                                                    "permission denied: {0}interpreter cannot use {1}sub-command {2}",
                                                    isSafe ? "safe " : String.Empty, !isSafe && isHidden ? "hidden " :
                                                    String.Empty, FormatOps.DisplayName(subCommand, interpreter, arguments));
                                            }

                                            code = ReturnCode.Error;
                                        }

#if POLICY_TRACE
                                        TraceOps.MaybeWritePolicyTrace("Execute", interpreter,
                                            !PolicyContext.GetForceTraceFull(), "name", name,
                                            "execute", execute, "clientData", clientData,
                                            "arguments", arguments, "engineFlags", engineFlags,
                                            "substitutionFlags", substitutionFlags, "eventFlags",
                                            eventFlags, "expressionFlags", expressionFlags,
                                            "ignoreHidden", ignoreHidden, "invokeHidden",
                                            invokeHidden, "isHidden", isHidden, "code", code,
                                            "commandDecision", commandDecision,
                                            "commandPolicyResult", commandPolicyResult, "usable",
                                            usable, "exception", exception, "result", result);
#endif
                                    }
                                    else
                                    {
                                        result = String.Format(
                                            "sub-command {0} is {1}hidden",
                                            FormatOps.DisplayName(name),
                                            isHidden ? String.Empty : "not ");

                                        code = ReturnCode.Error;
                                    }
                                }
                                else
                                {
                                    result = String.Format(
                                        "sub-command {0} is disabled",
                                        FormatOps.DisplayName(name));

                                    code = ReturnCode.Error;
                                }
                            }
                            else if (execute is IProcedure)
                            {
                                IProcedure procedure = (IProcedure)execute;

                                if (!EntityOps.IsDisabled(procedure))
                                {
                                    bool ignoreHidden = EngineFlagOps.HasIgnoreHidden(engineFlags);
                                    bool invokeHidden = EngineFlagOps.HasInvokeHidden(engineFlags);
                                    bool isHidden = EntityOps.IsHidden(procedure);

                                    if (ignoreHidden || (invokeHidden == isHidden))
                                    {
                                        code = ExecuteProcedure(procedure, interpreter,
                                            (clientData != null) ? clientData : procedure.ClientData,
                                            arguments, engineFlags, substitutionFlags, eventFlags,
                                            expressionFlags,
#if RESULT_LIMITS
                                            executeResultLimit,
#endif
                                            ref usable, ref exception, ref result);
                                    }
                                    else if (isHidden)
                                    {
                                        //
                                        // NOTE: *POLICY* See if hidden procedure is allowed to be
                                        //       executed, based on whatever criteria the current
                                        //       policies evaluate.  However, if the interpreter is
                                        //       not "safe", the command was purposely hidden and
                                        //       will not be executed.
                                        //
                                        ReturnCode? commandCode = null;
                                        PolicyDecision commandDecision = interpreter.CommandInitialDecision;
                                        Result commandPolicyResult = null;

                                        if (!EngineFlagOps.HasNoPolicy(engineFlags) &&
                                            (isSafe = interpreter.InternalIsSafe()) &&
                                            ((commandCode = interpreter.CheckCommandPolicies(
                                                PolicyFlags.EngineBeforeProcedure, procedure, arguments,
                                                null, ref commandDecision, ref commandPolicyResult)) == ReturnCode.Ok) &&
                                            PolicyContext.IsApproved(commandDecision))
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeProcedure, commandCode,
                                                commandDecision);

                                            code = ExecuteProcedure(procedure, interpreter,
                                                (clientData != null) ? clientData : procedure.ClientData,
                                                arguments, engineFlags, substitutionFlags, eventFlags,
                                                expressionFlags,
#if RESULT_LIMITS
                                                executeResultLimit,
#endif
                                                ref usable, ref exception, ref result);
                                        }
                                        else
                                        {
                                            interpreter.CommandFinalDecision = PolicyOps.FinalDecision(
                                                PolicyFlags.EngineBeforeProcedure, commandCode,
                                                commandDecision);

                                            if (commandPolicyResult != null)
                                            {
                                                result = commandPolicyResult;
                                            }
                                            else
                                            {
                                                result = String.Format(
                                                    "permission denied: {0}interpreter cannot use {1}procedure {2}",
                                                    isSafe ? "safe " : String.Empty, !isSafe && isHidden ? "hidden " :
                                                    String.Empty, FormatOps.DisplayName(name));
                                            }

                                            code = ReturnCode.Error;
                                        }

#if POLICY_TRACE
                                        TraceOps.MaybeWritePolicyTrace("Execute", interpreter,
                                            !PolicyContext.GetForceTraceFull(), "name", name,
                                            "execute", execute, "clientData", clientData,
                                            "arguments", arguments, "engineFlags", engineFlags,
                                            "substitutionFlags", substitutionFlags, "eventFlags",
                                            eventFlags, "expressionFlags", expressionFlags,
                                            "ignoreHidden", ignoreHidden, "invokeHidden",
                                            invokeHidden, "isHidden", isHidden, "code", code,
                                            "commandDecision", commandDecision,
                                            "commandPolicyResult", commandPolicyResult, "usable",
                                            usable, "exception", exception, "result", result);
#endif
                                    }
                                    else
                                    {
                                        result = String.Format(
                                            "procedure {0} is {1}hidden",
                                            FormatOps.DisplayName(name),
                                            isHidden ? String.Empty : "not ");

                                        code = ReturnCode.Error;
                                    }
                                }
                                else
                                {
                                    result = String.Format(
                                        "procedure {0} is disabled",
                                        FormatOps.DisplayName(name));

                                    code = ReturnCode.Error;
                                }
                            }
                            //
                            // NOTE: The IExecute interface is a strict sub-set of the
                            //       other interfaces; therefore, it must be last one
                            //       to be checked.
                            //
                            else if (execute is IExecute)
                            {
                                code = ExecuteIExecute(
                                    execute, interpreter, clientData, arguments,
                                    engineFlags, substitutionFlags, eventFlags,
                                    expressionFlags,
#if RESULT_LIMITS
                                    executeResultLimit,
#endif
                                    ref usable, ref exception, ref result);
                            }
                            else
                            {
                                result = String.Format(
                                    "unknown execution type for {0}",
                                    FormatOps.DisplayName(name));

                                code = ReturnCode.Error;
                            }
                        }

                        if (usable)
                        {
                            //
                            // NOTE: If the execution above succeeded, re-check the readiness
                            //       of the interpreter in case they executed [interp cancel]
                            //       or something similar that changed the state of the
                            //       interpreter.  There is not much point here in checking
                            //       if the interpreter is ready if we have just exited.
                            //
                            if ((code == ReturnCode.Ok) &&
                                !interpreter.ExitNoThrow &&
#if DEBUGGER
                                !interpreter.IsDebuggerExiting &&
#endif
                                !EngineFlagOps.HasNoReady(engineFlags))
                            {
                                code = Interpreter.EngineReady(
                                    interpreter, null, GetReadyFlags(engineFlags),
                                    ref result);
                            }
                        }
                    }
                    catch
                    {
                        exception = true;
                        throw;
                    }
                    finally
                    {
                        //
                        // NOTE: If an exception was thrown while executing
                        //       something, it may have cause the call stack
                        //       to be imbalanced.  Technically, the call
                        //       stack could be imbalanced even if an exception
                        //       was not thrown; however, that may simply be a
                        //       misuse of the library and the engine is not
                        //       designed to automatically correct anything in
                        //       that case.
                        //
                        if (usable && exception) /* RARE */
                        {
                            locked = false;

                            try
                            {
                                interpreter.InternalEngineTryLock(
                                    ref locked); /* TRANSACTIONAL */

                                if (locked)
                                {
                                    //
                                    // NOTE: Keep popping 'automatic' call frames until
                                    //       the call stack is balanced again.  In the
                                    //       general case, there should be exactly one
                                    //       iteration of this loop.
                                    //
                                    // BUGFIX: Stop if (any) call frame encountered is
                                    //         ever unsable (i.e. disposed).  This can
                                    //         act as a "fail-safe" in case threading
                                    //         rules are not followed.
                                    //
                                    while (Interpreter.ShouldPopAutomaticCallFrame(
                                            interpreter, peekFrame))
                                    {
                                        /* IGNORED */
                                        Interpreter.PopAutomaticCallFrame(
                                            interpreter, ref usable);

                                        if (!usable)
                                            break;
                                    }
                                }
                                else
                                {
                                    //
                                    // WARNING: It should be (almost?) impossible to get
                                    //          here, i.e. without using custom timeouts
                                    //          and many threads competing to acquire an
                                    //          interpreter lock; however, if this point
                                    //          is reached, the thread call stack may be
                                    //          (permanently) imbalanced.
                                    //
                                    TraceOps.LockTrace(
                                        "Execute",
                                        typeof(Engine).Name, false,
                                        TracePriority.LockError3,
                                        interpreter.MaybeWhoHasLock());
                                }
                            }
                            finally
                            {
                                interpreter.InternalExitLock(
                                    ref locked); /* TRANSACTIONAL */
                            }
                        }

                        /* IGNORED */
                        GlobalState.PopActiveInterpreter();
                    }
                }
                else
                {
                    result = "invalid interpreter";
                    code = ReturnCode.Error;
                }
            }
            else
            {
                result = "invalid execute";
                code = ReturnCode.Error;
            }

            return code;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        #region ExternalExecuteWithFrame Methods
        //
        // WARNING: This method is now obsolete.  Use the new one below.
        //
        /// <summary>
        /// This method is obsolete; use the overload that accepts nullable
        /// flags and the <c>useInterpreterFlags</c> parameter instead.  It
        /// forwards to that overload without augmenting the supplied flags
        /// from the interpreter.
        /// </summary>
        /// <param name="name">
        /// The name of the entity to execute, used for diagnostic messages.
        /// </param>
        /// <param name="execute">
        /// The executable entity to execute.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how execution is performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when processing any asynchronous events.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        [Obsolete()]
        public static ReturnCode ExternalExecuteWithFrame( // COMPAT: TclTk beta.
            string name,
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            return ExternalExecuteWithFrame(name,
                execute, interpreter, clientData, arguments, engineFlags,
                substitutionFlags, eventFlags, expressionFlags, false,
                ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: These methods are somewhat special.  It is the only public method that can
        //       directly execute any executable entity in the core library without going
        //       through the evaluation engine (e.g. EvaluateScript).  Great care must be
        //       taken in this method to prevent exceptions from escaping.  Also, it must
        //       make sure that the call stack is balanced upon exit and that the previous
        //       engine flags are restored.
        //
        /// <summary>
        /// This method is the only public entry point that can directly
        /// execute any executable entity in the core library without going
        /// through the evaluation engine.  It pushes a tracking call frame,
        /// enables the external execution engine flags, executes the entity,
        /// and then restores the saved flags and rebalances the call stack.
        /// Great care is taken to prevent exceptions from escaping.
        /// </summary>
        /// <param name="name">
        /// The name of the entity to execute, used for diagnostic messages.
        /// </param>
        /// <param name="execute">
        /// The executable entity to execute.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter in whose context the entity is executed; this
        /// may not be null.
        /// </param>
        /// <param name="clientData">
        /// The opaque, caller-specific data to pass to the entity; this may
        /// be null.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments to pass to the entity.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use, or null to use the default flags.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use, or null to use the default flags.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use, or null to use the default flags.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use, or null to use the default flags.
        /// </param>
        /// <param name="useInterpreterFlags">
        /// Non-zero to augment the supplied flags with the corresponding
        /// flags from the interpreter.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the entity;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        public static ReturnCode ExternalExecuteWithFrame( /* EXTERNAL USE ONLY */
            string name,
            IExecute execute,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            EngineFlags? engineFlags,
            SubstitutionFlags? substitutionFlags,
            EventFlags? eventFlags,
            ExpressionFlags? expressionFlags,
            bool useInterpreterFlags,
            ref Result result
            ) /* ENTRY-POINT, THREAD-SAFE, RE-ENTRANT */
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            EngineFlags localEngineFlags = EngineFlags.None;

            if (engineFlags != null)
                localEngineFlags = (EngineFlags)engineFlags;

            SubstitutionFlags localSubstitutionFlags = SubstitutionFlags.Default;

            if (substitutionFlags != null)
                localSubstitutionFlags = (SubstitutionFlags)substitutionFlags;

            EventFlags localEventFlags = EventFlags.Default;

            if (eventFlags != null)
                localEventFlags = (EventFlags)eventFlags;

            ExpressionFlags localExpressionFlags = ExpressionFlags.Default;

            if (expressionFlags != null)
                localExpressionFlags = (ExpressionFlags)expressionFlags;

            if (useInterpreterFlags && !TryAugmentAllFlags(
                    interpreter, BlockingFlagsForExecute,
                    ref localEngineFlags, ref localSubstitutionFlags,
                    ref localEventFlags, ref localExpressionFlags,
                    ref result))
            {
                return ReturnCode.Error;
            }

            //
            // NOTE: Is the interpreter usable at this point?  If not, return
            //       an error now.
            //
            bool usable = IsUsableNoLock(interpreter, ref result);

            if (!usable)
                return ReturnCode.Error;

#if RESULT_LIMITS
            int executeResultLimit = interpreter.InternalExecuteResultLimit;
#endif

            ICallFrame frame = interpreter.NewTrackingCallFrame(
                StringList.MakeList("external", name),
                CallFrameFlags.External);

            ReturnCode code;
            EngineFlags savedEngineFlags = EngineFlags.None;

            //
            // NOTE: Push a new call frame linked to the current one.  This
            //       call frame can be used to detect the external command
            //       execution in progress.  It will be automatically popped
            //       before returning from this method.
            //
            interpreter.PushCallFrame(frame);

            try
            {
                //
                // NOTE: Save the current engine flags and then enable the
                //       external execution flags.
                //
                savedEngineFlags = interpreter.BeginExternalExecution();

                try
                {
                    //
                    // NOTE: Execute the command using the engine flags having
                    //       been modified to include the flags necessary for
                    //       external command execution (i.e. command execution
                    //       outside of the engine).
                    //
                    code = Execute(
                        name, execute, interpreter,
                        GetClientData(
                            interpreter, clientData, false),
                        arguments, localEngineFlags,
                        localSubstitutionFlags, localEventFlags,
                        localExpressionFlags,
#if RESULT_LIMITS
                        executeResultLimit,
#endif
                        ref usable, ref result);
                }
                finally
                {
                    if (usable)
                    {
                        //
                        // NOTE: Restore the saved engine flags, masking off
                        //       the external execution flags as necessary.
                        //
                        /* IGNORED */
                        interpreter.EndAndCleanupExternalExecution(
                            savedEngineFlags);
                    }
                }
            }
            finally
            {
                if (usable)
                {
                    //
                    // NOTE: Pop the original call frame that we pushed
                    //       above and any intervening scope call frames
                    //       that may be leftover (i.e. they were not
                    //       explicitly closed).
                    //
                    /* IGNORED */
                    interpreter.PopScopeCallFramesAndOneMore();
                }
            }

            //
            // NOTE: Return the results to the caller.
            //
            return code;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Argument List Execution Methods
        /// <summary>
        /// This method attempts to retrieve a cached <see cref="IExecute" />
        /// entity directly from the supplied argument, avoiding a full
        /// command resolution.  It also reports the entity name and whether
        /// argument-based caching is enabled for the interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose argument caching configuration is
        /// consulted; this may be null.
        /// </param>
        /// <param name="argument">
        /// The argument that may carry the entity name and a cached entity;
        /// this may be null.
        /// </param>
        /// <param name="viaArgument">
        /// Upon return, this is non-zero if argument-based caching is
        /// enabled for the interpreter.
        /// </param>
        /// <param name="executeName">
        /// Upon return, this receives the entity name taken from the
        /// argument, if any.
        /// </param>
        /// <param name="execute">
        /// Upon return, this receives the cached entity, if one was found.
        /// </param>
        /// <returns>
        /// Non-zero if a cached entity was found; otherwise, zero.
        /// </returns>
        private static bool MaybeGetIExecuteViaArgument(
            Interpreter interpreter,
            Argument argument,
            out bool viaArgument,
            out string executeName,
            out IExecute execute
            )
        {
            viaArgument = (interpreter != null) ?
                interpreter.HasCacheViaArgument() : false;

            executeName = null;
            execute = null;

            if (argument != null)
            {
                executeName = argument; /* CONVERSION */

                if (viaArgument)
                {
                    execute = argument.GetCacheValue(
                        interpreter, false) as IExecute;

                    if (execute != null)
                        return true;
                }
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method attempts to store the resolved <see cref="IExecute" />
        /// entity in the supplied argument so that it can be reused on
        /// subsequent executions, but only when the name is absolute or the
        /// entity resides in the global namespace.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter associated with the argument cache; this may be
        /// null.
        /// </param>
        /// <param name="argument">
        /// The argument in which to cache the entity; this may be null.
        /// </param>
        /// <param name="viaArgument">
        /// Non-zero if argument-based caching is enabled.
        /// </param>
        /// <param name="execute">
        /// The resolved entity to cache.
        /// </param>
        /// <returns>
        /// Non-zero if the entity was cached; otherwise, zero.
        /// </returns>
        private static bool MaybeCacheIExecuteViaArgument(
            Interpreter interpreter,
            Argument argument,
            bool viaArgument,
            IExecute execute
            )
        {
            if (viaArgument && (argument != null))
            {
                if (NamespaceOps.IsAbsoluteName(argument) ||
                    NamespaceOps.IsGlobal(execute))
                {
                    return argument.SetCacheValue(
                        interpreter, execute, false);
                }
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is called directly by the evaluation engine core to
        /// execute a command represented by a list of arguments.  It
        /// resolves the command (or procedure) named by the first argument,
        /// optionally consulting the argument cache and the unknown command
        /// handler, and then dispatches it via the core execution method,
        /// pushing and popping the global call frame as required.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter in whose context the command is executed.
        /// </param>
        /// <param name="arguments">
        /// The list of arguments; the first argument is the name of the
        /// command to resolve and execute.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how command resolution and
        /// execution are performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current evaluation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when processing any asynchronous events.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current evaluation.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum length, in characters, permitted for the result.
        /// This parameter is only present when result limits are enabled at
        /// compile-time.
        /// </param>
        /// <param name="usable">
        /// Upon return, this is non-zero if the interpreter remains usable
        /// (i.e. was not disposed) after execution.
        /// </param>
        /// <param name="result">
        /// Upon success, this receives the result produced by the command;
        /// upon failure, it receives an error message.
        /// </param>
        /// <returns>
        /// The return code produced by the execution;
        /// <see cref="ReturnCode.Ok" /> indicates success.
        /// </returns>
        private static ReturnCode ExecuteArguments(
            Interpreter interpreter,
            ArgumentList arguments,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ///////////////////////////////////////////////////////////////////////////////////
            //
            // NOTE: This function is called directly by the evaluation engine core.
            //
            ///////////////////////////////////////////////////////////////////////////////////

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

            ReturnCode code = ReturnCode.Ok;

            if (arguments.Count == 0) // no command name or arguments.
                return code;

            //
            // NOTE: Fetch the first argument now; it should contain the
            //       name of the command to be looked up.
            //
#if PERFORMANCE_DIAGNOSIS
            long __probeEaTs = Diagnostics.PerfProbe.Enabled ?
                Diagnostics.PerfProbe.Now : 0;
#endif

            Argument firstArgument = arguments[0];

            //
            // NOTE: Figure out what our new (local) command resolution
            //       EngineFlags are.
            //
            engineFlags = GetResolveFlags(engineFlags, false);

            //
            // NOTE: Was the global call frame pushed by this method?
            //
            bool shouldPush = EngineFlagOps.HasEvaluateGlobal(engineFlags);
            bool didPush = false;

            if (shouldPush)
            {
                interpreter.PushGlobalCallFrame(true);
                didPush = true;
            }

            try
            {
                //
                // NOTE: Is [unknown] being used to locate the command?
                //
                bool useUnknown = false;

                //
                // HACK: Cheat, attempt to read the cached command from
                //       the Argument object itself?
                //
                bool viaArgument;
                string executeName;
                IExecute execute;

                if (MaybeGetIExecuteViaArgument(interpreter,
                        firstArgument, out viaArgument,
                        out executeName, out execute))
                {
                    goto execute;
                }

                bool ambiguous = false;
                Result error = null;

                //
                // NOTE: Resolve the command or procedure to execute.
                //
                code = interpreter.InternalGetIExecuteViaResolvers(
                    engineFlags | EngineFlags.ToExecute,
                    executeName, arguments, LookupFlags.EngineDefault,
                    ref ambiguous, ref execute, ref error);

                //
                // NOTE: See if there is a callback configured to deal
                //       with unknown commands.
                //
                bool skipUnknown = false;

                if (code == ReturnCode.Ok)
                {
                    /* IGNORED */
                    MaybeCacheIExecuteViaArgument(
                        interpreter, firstArgument, viaArgument, execute);
                }
                else
                {
                    UnknownCallback unknownCallback = interpreter.UnknownCallback;

                    if (unknownCallback != null)
                    {
                        code = unknownCallback(
                            interpreter, engineFlags | EngineFlags.ToExecute,
                            executeName, arguments, LookupFlags.EngineDefault,
                            ref ambiguous, ref execute, ref error);

                        if (code == ReturnCode.Ok)
                        {
                            //
                            // NOTE: Command may have been changed.
                            //       Refresh its name just in case.
                            //
                            if ((arguments != null) &&
                                (arguments.Count > 1))
                            {
                                executeName = firstArgument;
                            }
                            else
                            {
                                //
                                // BUGBUG: No command name?  This is
                                //         not a great situation.
                                //
                                executeName = null;
                            }
                        }
                        else if (code == ReturnCode.Break)
                        {
                            //
                            // NOTE: Command is effectively disabled,
                            //       do nothing -AND- return success.
                            //
                            return ReturnCode.Ok;
                        }
                        else if (code == ReturnCode.Continue)
                        {
                            //
                            // NOTE: Command is effectively disabled,
                            //       do nothing -AND- return failure,
                            //       while skipping further [unknown]
                            //       processing.
                            //
                            skipUnknown = true;

                            code = ReturnCode.Error;
                        }
                    }
                }

                //
                // NOTE: Did we fail to find the command (or procedure)?
                //
                if (code != ReturnCode.Ok)
                {
#if NOTIFY
                    if (!EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Resolver, NotifyFlags.NotFound,
                            new ObjectList(engineFlags | EngineFlags.ToExecute,
                            executeName, ambiguous, execute, error), interpreter,
                            null, arguments, null, ref result);
                    }
#endif

                    if (!skipUnknown)
                    {
                        //
                        // NOTE: If the command name was not ambiguous (i.e.
                        //       ambiguous commands are not considered to be
                        //       "unknown") and we are not explicitly forbidden
                        //       from using the unknown command handler, try to
                        //       look it up now.
                        //
                        // BUGFIX: Prevent infinite recursion via unknown (i.e.
                        //         if it ends up trying to call a nonexistent
                        //         command).
                        //
                        if (!ambiguous &&
                            !EngineFlagOps.HasNoUnknown(engineFlags))
                        {
                            code = interpreter.AttemptToUseUnknown(
                                code, engineFlags, LookupFlags.EngineNoVerbose,
                                ref arguments, ref execute, ref useUnknown);

                            if (code != ReturnCode.Ok)
                                result = error;
                        }
                        else
                        {
                            //
                            // NOTE: If we cannot find an unknown command handler
                            //       or we cannot use it then just give the caller
                            //       back the original invalid command name error.
                            //
                            result = error;
                        }
                    }
                    else
                    {
                        //
                        // NOTE: Make sure the caller has access to the command
                        //       resolution error message.
                        //
                        result = error;
                    }
                }

            execute:

#if PERFORMANCE_DIAGNOSIS
                if (Diagnostics.PerfProbe.Enabled)
                { Diagnostics.PerfProbe.Add("ea.resolve", __probeEaTs); }
#endif

                if (code == ReturnCode.Ok)
                {
                    //
                    // NOTE: Did we succeed at finding some command (or procedure)
                    //       to execute (unknown or otherwise)?
                    //
                    if (useUnknown)
                        interpreter.EnterUnknownLevel();

                    try
                    {
                        //
                        // NOTE: Call the primary external execution entry point
                        //       so that we get all the necessary handling.
                        //
                        code = Execute(
                            executeName, execute, interpreter,
                            GetClientData(
                                interpreter, null, false),
                            arguments, engineFlags,
                            substitutionFlags,
                            eventFlags, expressionFlags,
#if RESULT_LIMITS
                            executeResultLimit,
#endif
                            ref usable, ref result);
                    }
                    finally
                    {
                        if (usable && useUnknown)
                            interpreter.ExitUnknownLevel();
                    }
                }
            }
            finally
            {
                //
                // NOTE: If we previously pushed the global call frame (above),
                //       we also need to pop any leftover scope call frames now;
                //       otherwise, the call stack will be imbalanced.
                //
                if (shouldPush && didPush && usable)
                    interpreter.PopGlobalCallFrame(true);
            }

            return code;
        }
        #endregion
        #endregion
    }
}
