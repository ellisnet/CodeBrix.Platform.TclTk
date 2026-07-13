/*
 * Engine.Features.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Feature Support Methods" region group) so that no
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
        #region Feature Support Methods
        #region Throw-On-Disposed Support Methods
        /// <summary>
        /// This method determines whether an exception should be thrown when a
        /// disposed interpreter is accessed, considering both the global setting
        /// and the per-interpreter creation flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose creation flags should be considered.  This
        /// parameter may be null.
        /// </param>
        /// <param name="all">
        /// Non-zero to require both the global setting and the per-interpreter
        /// flag to be set; zero to require either one; null to select the
        /// behavior automatically based on whether an interpreter was supplied.
        /// </param>
        /// <returns>
        /// Non-zero if an exception should be thrown when a disposed
        /// interpreter is accessed; otherwise, zero.
        /// </returns>
        public static bool IsThrowOnDisposed(
            Interpreter interpreter,
            bool? all
            )
        {
            CreateFlags createFlags = CreateFlags.None;

            //
            // BUGFIX: Avoid ever taking the interpreter lock while in this
            //         method, as we have no idea under what conditions it
            //         could be [legitimately] called.
            //
            if (interpreter != null)
                createFlags = interpreter.CreateFlagsNoLock;

            lock (syncRoot) /* ENGINE-LOCK */
            {
                bool newAll;

                if (all != null)
                    newAll = (bool)all;
                else if (interpreter != null)
                    newAll = true;
                else
                    newAll = false;

                ///////////////////////////////////////////////////////////////

                if (newAll)
                {
                    return ThrowOnDisposed && FlagOps.HasFlags(
                        createFlags, CreateFlags.ThrowOnDisposed, true);
                }
                else
                {
                    return ThrowOnDisposed || FlagOps.HasFlags(
                        createFlags, CreateFlags.ThrowOnDisposed, true);
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method configures whether an exception should be thrown when a
        /// disposed interpreter is accessed, updating the per-interpreter
        /// creation flags and optionally the global setting.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose creation flags should be updated.  This
        /// parameter may be null.
        /// </param>
        /// <param name="throwOnDisposed">
        /// Non-zero to throw an exception when a disposed interpreter is
        /// accessed.
        /// </param>
        /// <param name="all">
        /// Non-zero to also update the global setting; the global setting is
        /// always updated when no interpreter is supplied.
        /// </param>
        public static void SetThrowOnDisposed(
            Interpreter interpreter,
            bool throwOnDisposed,
            bool all
            )
        {
            if (interpreter != null)
            {
                if (throwOnDisposed)
                    interpreter.CreateFlags |= CreateFlags.ThrowOnDisposed;
                else
                    interpreter.CreateFlags &= ~CreateFlags.ThrowOnDisposed;
            }

            if (all || (interpreter == null))
            {
                lock (syncRoot) /* ENGINE-LOCK */
                {
                    ThrowOnDisposed = throwOnDisposed;
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Stack Space Methods
        /// <summary>
        /// This method determines the stack size, in bytes, to use when
        /// creating a new thread, preferring the specified size, then the
        /// interpreter setting, and finally a platform-appropriate default.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose configured thread stack size should be used,
        /// if any.  This parameter may be null.
        /// </param>
        /// <param name="maxStackSize">
        /// The requested maximum stack size, in bytes, or zero to determine it
        /// automatically.
        /// </param>
        /// <returns>
        /// The stack size, in bytes, to use for the new thread.
        /// </returns>
        private static int GetNewThreadStackSize(
            Interpreter interpreter,
            int maxStackSize
            )
        {
            if (maxStackSize != 0)
                return maxStackSize;

            if (interpreter != null)
            {
                maxStackSize = interpreter.InternalThreadStackSize;

                if (maxStackSize != 0)
                    return maxStackSize;
            }

            ///////////////////////////////////////////////////////////////////

            bool isMono = CommonOps.Runtime.IsMono();

#if NATIVE
            if (!isMono)
            {
                //
                // NOTE: When running under the .NET Framework on Windows, use
                //       the native stack checking code to determine the proper
                //       stack size for new threads; otherwise, just use one of
                //       our "fail-safe" defaults.
                //
                return ConversionOps.ToInt(
                    NativeStack.GetNewThreadNativeStackSize());
            }
            else
#endif
            {
                //
                // HACK: *MONO* Use the process-wide default.  Apparently, if
                //       we are running on Mono we must use a non-zero stack
                //       size or thread creation will fail.
                //
                return isMono ? DefaultStackSize : 0;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

#if NATIVE
        /// <summary>
        /// This method returns the amount of extra stack space, in bytes,
        /// reserved when performing native stack checks.
        /// </summary>
        /// <returns>
        /// The extra reserved stack space, in bytes.
        /// </returns>
        internal static ulong GetExtraStackSpace()
        {
            lock (syncRoot)
            {
                return ExtraStackSpace;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets the amount of extra stack space, in bytes, reserved
        /// when performing native stack checks.
        /// </summary>
        /// <param name="extraSpace">
        /// The extra stack space, in bytes, to reserve.
        /// </param>
        internal static void SetExtraStackSpace(
            ulong extraSpace
            )
        {
            lock (syncRoot)
            {
                ExtraStackSpace = extraSpace;
            }
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Threading Support Methods
        #region Thread Creation Methods
        /// <summary>
        /// This method creates a new managed thread that runs the specified
        /// thread start routine within the script engine.  This overload uses
        /// no interpreter.
        /// </summary>
        /// <param name="start">
        /// The thread start routine to run on the new thread.
        /// </param>
        /// <param name="maxStackSize">
        /// The maximum stack size, in bytes, for the new thread, or zero to
        /// determine it automatically.
        /// </param>
        /// <param name="userInterface">
        /// Non-zero if the new thread will be used for a user interface, in
        /// which case it uses a single-threaded apartment.
        /// </param>
        /// <param name="isBackground">
        /// Non-zero to create the thread as a background thread.
        /// </param>
        /// <param name="useActiveStack">
        /// Non-zero to share the active stack of the calling thread.
        /// </param>
        /// <returns>
        /// The newly created thread, or null if it could not be created.
        /// </returns>
        public static Thread CreateThread(
            ThreadStart start,
            int maxStackSize,
            bool userInterface,
            bool isBackground,
            bool useActiveStack
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return CreateThread(
                null, start, maxStackSize, userInterface, isBackground,
                useActiveStack);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method creates a new managed thread that runs the specified
        /// thread start routine within the script engine, using the thread host
        /// of the specified interpreter when one is available.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose thread host should be used to create the
        /// thread, if available.  This parameter may be null.
        /// </param>
        /// <param name="start">
        /// The thread start routine to run on the new thread.
        /// </param>
        /// <param name="maxStackSize">
        /// The maximum stack size, in bytes, for the new thread, or zero to
        /// determine it automatically.
        /// </param>
        /// <param name="userInterface">
        /// Non-zero if the new thread will be used for a user interface, in
        /// which case it uses a single-threaded apartment.
        /// </param>
        /// <param name="isBackground">
        /// Non-zero to create the thread as a background thread.
        /// </param>
        /// <param name="useActiveStack">
        /// Non-zero to share the active stack of the calling thread.
        /// </param>
        /// <returns>
        /// The newly created thread, or null if it could not be created.
        /// </returns>
        public static Thread CreateThread(
            Interpreter interpreter,
            ThreadStart start,
            int maxStackSize,
            bool userInterface,
            bool isBackground,
            bool useActiveStack
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            Thread thread = null;

            EngineThread engineThread = EngineThread.Create(
                interpreter, start, useActiveStack);

            if (engineThread == null)
                return thread;

#if NOTIFY
            EngineFlags engineFlags = EngineFlags.None;
#endif
            IThreadHost threadHost = null;

            if (interpreter != null)
            {
#if NOTIFY
                engineFlags = interpreter.EngineFlagsNoLock;
#endif

                threadHost = GetThreadHost(interpreter);
            }

            try
            {
                maxStackSize = GetNewThreadStackSize(interpreter, maxStackSize);

                if ((threadHost != null) &&
                    FlagOps.HasFlags(threadHost.GetHostFlags(), HostFlags.Thread, true))
                {
                    ReturnCode code;
                    Result error = null;

                    code = threadHost.CreateThread(
                        engineThread.ThreadStart, maxStackSize, userInterface,
                        isBackground, useActiveStack, ref thread, ref error);

                    engineThread.SetThread(thread);

                    if (code != ReturnCode.Ok)
                        DebugOps.Complain(interpreter, code, error);
                }
                else
                {
                    //
                    // NOTE: It is highly recommended that external users of the script engine
                    //       should use at LEAST this value when creating threads that will be
                    //       using this class (the script engine).
                    //
                    thread = new Thread(
                        engineThread.ThreadStart, maxStackSize);

                    engineThread.SetThread(thread);

                    if (userInterface)
                        thread.SetApartmentState(ApartmentState.STA);

                    if (thread.IsBackground != isBackground)
                        thread.IsBackground = isBackground;
                }
            }
            catch (Exception e)
            {
                TraceOps.DebugTrace(
                    e, typeof(Engine).Name,
                    TracePriority.ThreadError);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectList(start, engineThread, maxStackSize,
                        userInterface, isBackground, thread), interpreter,
                        null, null, e);
                }
#endif
            }

            return thread;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method creates a new managed thread that runs the specified
        /// parameterized thread start routine within the script engine.  This
        /// overload uses no interpreter.
        /// </summary>
        /// <param name="start">
        /// The parameterized thread start routine to run on the new thread.
        /// </param>
        /// <param name="maxStackSize">
        /// The maximum stack size, in bytes, for the new thread, or zero to
        /// determine it automatically.
        /// </param>
        /// <param name="userInterface">
        /// Non-zero if the new thread will be used for a user interface, in
        /// which case it uses a single-threaded apartment.
        /// </param>
        /// <param name="isBackground">
        /// Non-zero to create the thread as a background thread.
        /// </param>
        /// <param name="useActiveStack">
        /// Non-zero to share the active stack of the calling thread.
        /// </param>
        /// <returns>
        /// The newly created thread, or null if it could not be created.
        /// </returns>
        public static Thread CreateThread(
            ParameterizedThreadStart start,
            int maxStackSize,
            bool userInterface,
            bool isBackground,
            bool useActiveStack
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            return CreateThread(
                null, start, maxStackSize, userInterface, isBackground,
                useActiveStack);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method creates a new managed thread that runs the specified
        /// parameterized thread start routine within the script engine, using
        /// the thread host of the specified interpreter when one is available.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose thread host should be used to create the
        /// thread, if available.  This parameter may be null.
        /// </param>
        /// <param name="start">
        /// The parameterized thread start routine to run on the new thread.
        /// </param>
        /// <param name="maxStackSize">
        /// The maximum stack size, in bytes, for the new thread, or zero to
        /// determine it automatically.
        /// </param>
        /// <param name="userInterface">
        /// Non-zero if the new thread will be used for a user interface, in
        /// which case it uses a single-threaded apartment.
        /// </param>
        /// <param name="isBackground">
        /// Non-zero to create the thread as a background thread.
        /// </param>
        /// <param name="useActiveStack">
        /// Non-zero to share the active stack of the calling thread.
        /// </param>
        /// <returns>
        /// The newly created thread, or null if it could not be created.
        /// </returns>
        public static Thread CreateThread(
            Interpreter interpreter,
            ParameterizedThreadStart start,
            int maxStackSize,
            bool userInterface,
            bool isBackground,
            bool useActiveStack
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            Thread thread = null;

            EngineThread engineThread = EngineThread.Create(
                interpreter, start, useActiveStack);

            if (engineThread == null)
                return thread;

#if NOTIFY
            EngineFlags engineFlags = EngineFlags.None;
#endif
            IThreadHost threadHost = null;

            if (interpreter != null)
            {
#if NOTIFY
                engineFlags = interpreter.EngineFlagsNoLock;
#endif

                threadHost = GetThreadHost(interpreter);
            }

            try
            {
                maxStackSize = GetNewThreadStackSize(interpreter, maxStackSize);

                if ((threadHost != null) &&
                    FlagOps.HasFlags(threadHost.GetHostFlags(), HostFlags.Thread, true))
                {
                    ReturnCode code;
                    Result error = null;

                    code = threadHost.CreateThread(
                        engineThread.ParameterizedThreadStart, maxStackSize,
                        userInterface, isBackground, useActiveStack, ref thread,
                        ref error);

                    engineThread.SetThread(thread);

                    if (code != ReturnCode.Ok)
                        DebugOps.Complain(interpreter, code, error);
                }
                else
                {
                    //
                    // NOTE: It is highly recommended that external users of the script engine
                    //       should use at LEAST this value when creating threads that will be
                    //       using this class (the script engine).
                    //
                    thread = new Thread(
                        engineThread.ParameterizedThreadStart, maxStackSize);

                    engineThread.SetThread(thread);

                    if (userInterface)
                        thread.SetApartmentState(ApartmentState.STA);

                    if (thread.IsBackground != isBackground)
                        thread.IsBackground = isBackground;
                }
            }
            catch (Exception e)
            {
                TraceOps.DebugTrace(
                    e, typeof(Engine).Name,
                    TracePriority.ThreadError);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectList(start, engineThread, maxStackSize,
                        userInterface, isBackground, thread), interpreter,
                        null, null, e);
                }
#endif
            }

            return thread;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Thread Queue Methods
        //
        // WARNING: This method is only for use by the CodeBrix.Platform.TclTk._Hosts.Engine
        //          class.
        //
        /// <summary>
        /// This method queues the specified callback to run on a thread-pool
        /// thread.
        /// </summary>
        /// <param name="callBack">
        /// The callback to run.
        /// </param>
        /// <param name="flags">
        /// The flags that control how the work item is queued.
        /// </param>
        /// <returns>
        /// Non-zero if the work item was successfully queued; otherwise, zero.
        /// </returns>
        internal static bool QueueWorkItem(
            ThreadStart callBack,
            QueueFlags flags
            ) /* throw */
        {
            return ThreadOps.QueueUserWorkItem(
                callBack, FlagOps.HasFlags(flags,
                QueueFlags.WaitForStart, true)); /* throw */
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // WARNING: This method is only for use by the CodeBrix.Platform.TclTk._Hosts.Engine
        //          class, the CodeBrix.Platform.TclTk._Components.Public.ScriptThread class,
        //          and the CodeBrix.Platform.TclTk._Components.Public.ScriptThread class.
        //
        /// <summary>
        /// This method queues the specified callback, along with its state, to
        /// run on a thread-pool thread.
        /// </summary>
        /// <param name="callBack">
        /// The callback to run.
        /// </param>
        /// <param name="state">
        /// The state object passed to the callback.  This parameter may be
        /// null.
        /// </param>
        /// <param name="flags">
        /// The flags that control how the work item is queued.
        /// </param>
        /// <returns>
        /// Non-zero if the work item was successfully queued; otherwise, zero.
        /// </returns>
        internal static bool QueueWorkItem(
            WaitCallback callBack,
            object state,
            QueueFlags flags
            ) /* throw */
        {
            return ThreadOps.QueueUserWorkItem(
                callBack, state, FlagOps.HasFlags(flags,
                QueueFlags.WaitForStart, true)); /* throw */
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method queues the specified callback to run on a thread-pool
        /// thread, using the thread host of the specified interpreter when one
        /// is available.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose thread host should be used to queue the work
        /// item, if available.  This parameter may be null.
        /// </param>
        /// <param name="start">
        /// The callback to run.
        /// </param>
        /// <param name="flags">
        /// The flags that control how the work item is queued.
        /// </param>
        /// <returns>
        /// Non-zero if the work item was successfully queued; otherwise, zero.
        /// </returns>
        public static bool QueueWorkItem(
            Interpreter interpreter,
            ThreadStart start,
            QueueFlags flags
            ) /* throw */
        {
#if NOTIFY
            EngineFlags engineFlags = EngineFlags.None;
#endif
            IThreadHost threadHost = null;

            if (interpreter != null)
            {
#if NOTIFY
                engineFlags = interpreter.EngineFlagsNoLock;
#endif

                threadHost = GetThreadHost(interpreter);
            }

            try
            {
                if ((threadHost != null) &&
                    FlagOps.HasFlags(threadHost.GetHostFlags(), HostFlags.WorkItem, true))
                {
                    ReturnCode code;
                    Result error = null;

                    code = threadHost.QueueWorkItem(start, flags, ref error);

                    if (code == ReturnCode.Ok)
                        return true;
                    else
                        DebugOps.Complain(interpreter, code, error);
                }
                else
                {
                    if (QueueWorkItem(start, flags)) /* throw */
                        return true;
                    else
                        DebugOps.Complain(interpreter,
                            ReturnCode.Error, "could not queue work item");
                }
            }
            catch (Exception e)
            {
                TraceOps.DebugTrace(
                    e, typeof(Engine).Name,
                    TracePriority.ThreadError);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectPair(start, null), interpreter,
                        null, null, e);
                }
#endif
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method queues the specified parameterized callback, along with
        /// its state, to run on a thread-pool thread, using the thread host of
        /// the specified interpreter when one is available.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose thread host should be used to queue the work
        /// item, if available.  This parameter may be null.
        /// </param>
        /// <param name="start">
        /// The parameterized callback to run.
        /// </param>
        /// <param name="obj">
        /// The state object passed to the callback.  This parameter may be
        /// null.
        /// </param>
        /// <param name="flags">
        /// The flags that control how the work item is queued.
        /// </param>
        /// <returns>
        /// Non-zero if the work item was successfully queued; otherwise, zero.
        /// </returns>
        public static bool QueueWorkItem(
            Interpreter interpreter,
            ParameterizedThreadStart start,
            object obj,
            QueueFlags flags
            ) /* throw */
        {
#if NOTIFY
            EngineFlags engineFlags = EngineFlags.None;
#endif
            IThreadHost threadHost = null;

            if (interpreter != null)
            {
#if NOTIFY
                engineFlags = interpreter.EngineFlagsNoLock;
#endif

                threadHost = GetThreadHost(interpreter);
            }

            try
            {
                if ((threadHost != null) &&
                    FlagOps.HasFlags(threadHost.GetHostFlags(), HostFlags.WorkItem, true))
                {
                    ReturnCode code;
                    Result error = null;

                    code = threadHost.QueueWorkItem(
                        new WaitCallback(start), obj, flags, ref error);

                    if (code == ReturnCode.Ok)
                        return true;
                    else
                        DebugOps.Complain(interpreter, code, error);
                }
                else
                {
                    if (QueueWorkItem(
                            new WaitCallback(start), obj, flags)) /* throw */
                    {
                        return true;
                    }
                    else
                    {
                        DebugOps.Complain(interpreter,
                            ReturnCode.Error, "could not queue work item");
                    }
                }
            }
            catch (Exception e)
            {
                TraceOps.DebugTrace(
                    e, typeof(Engine).Name,
                    TracePriority.ThreadError);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectPair(start, obj), interpreter,
                        null, null, e);
                }
#endif
            }

            return false;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Private Thread Support Methods
        /// <summary>
        /// This method returns the thread host for the specified interpreter,
        /// provided the interpreter belongs to the current application domain
        /// and its host is not a transparent proxy.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose thread host is requested.  This parameter may
        /// be null.
        /// </param>
        /// <returns>
        /// The thread host for the interpreter, or null if one is not
        /// available.
        /// </returns>
        private static IThreadHost GetThreadHost(
            Interpreter interpreter
            )
        {
            if ((interpreter != null) && AppDomainOps.IsSame(interpreter))
            {
                IThreadHost threadHost = interpreter.InternalHost;

                if (!AppDomainOps.IsTransparentProxy(threadHost))
                    return threadHost;
            }

            return null;
        }
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Asynchronous Support Methods
        /// <summary>
        /// This method is the thread entry point used to perform an
        /// asynchronous engine operation.  It extracts the operation context,
        /// performs the requested evaluation or substitution, and then either
        /// invokes the completion callback or handles any error as a background
        /// error.
        /// </summary>
        /// <param name="obj">
        /// The asynchronous context describing the operation to perform; this
        /// is expected to implement <see cref="IAsynchronousContext" />.  This
        /// parameter may be null.
        /// </param>
        private static void EngineThreadStart(
            object obj
            ) /* System.Threading.ParameterizedThreadStart */
        {
#if NATIVE
            RuntimeOps.RefreshNativeStackPointers(true);
#endif

            IAsynchronousContext context = obj as IAsynchronousContext;

            if (context != null)
            {
                Interpreter interpreter = context.Interpreter;
                EngineMode engineMode = context.EngineMode;
                string text = context.Text;
                EngineFlags engineFlags = context.EngineFlags;
                SubstitutionFlags substitutionFlags = context.SubstitutionFlags;
                EventFlags eventFlags = context.EventFlags;
                ExpressionFlags expressionFlags = context.ExpressionFlags;
                AsynchronousCallback callback = context.Callback;

                bool bgError = !FlagOps.HasFlags(
                    eventFlags, EventFlags.NoBgError, true);

                bool disposeThread = FlagOps.HasFlags(
                    eventFlags, EventFlags.DisposeThread, true);

                try
                {
                    ReturnCode code;
                    Result result = null;
                    int errorLine = 0;

                    switch (engineMode)
                    {
                        case EngineMode.None:
                            {
                                //
                                // NOTE: Do nothing.
                                //
                                code = ReturnCode.Ok;

                                break;
                            }
                        case EngineMode.EvaluateExpression:
                            {
                                code = EvaluateExpression(
                                    interpreter, text, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref result);

                                break;
                            }
                        case EngineMode.EvaluateScript:
                            {
                                code = EvaluateScript(
                                    interpreter, text, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref result, ref errorLine);

                                break;
                            }
                        case EngineMode.EvaluateFile:
                            {
                                code = EvaluateFile(
                                    interpreter, text, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref result, ref errorLine);

                                break;
                            }
                        case EngineMode.SubstituteString:
                            {
                                code = SubstituteString(
                                    interpreter, text, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref result);

                                break;
                            }
                        case EngineMode.SubstituteFile:
                            {
                                code = SubstituteFile(
                                    interpreter, text, engineFlags, substitutionFlags,
                                    eventFlags, expressionFlags, ref result);

                                break;
                            }
                        default:
                            {
                                result = String.Format(
                                    "invalid engine mode {0}",
                                    engineMode);

                                code = ReturnCode.Error;
                                break;
                            }
                    }

                    if (callback != null)
                    {
                        //
                        // NOTE: Modify the context to include the result of
                        //       the script evaluation.
                        //
                        context.SetResult(code, result, errorLine);

                        //
                        // NOTE: Notify the callback that the script has
                        //       completed.  We do not care at this point if
                        //       the script succeeded or generated an error.
                        //       The callback should take whatever action is
                        //       appropriate based on the result contained in
                        //       the context.
                        //
                        callback(context); /* throw */
                    }
                    else if (code == ReturnCode.Error)
                    {
                        //
                        // NOTE: The script generated an error and no callback
                        //       was specified; therefore, attempt to handle
                        //       this as a background error.
                        //
                        if (bgError && EventOps.HandleBackgroundError(
                                interpreter, code, result) != ReturnCode.Ok)
                        {
                            //
                            // NOTE: For some reason, that failed; therefore,
                            //       just complain about it to the interpreter
                            //       host.
                            //
                            DebugOps.Complain(interpreter, code, result);
                        }
                    }
                }
                catch (ThreadAbortException e)
                {
                    Thread.ResetAbort();

                    TraceOps.DebugTrace(
                        e, typeof(Engine).Name,
                        TracePriority.ThreadError2);
                }
                catch (ThreadInterruptedException e)
                {
                    TraceOps.DebugTrace(
                        e, typeof(Engine).Name,
                        TracePriority.ThreadError2);
                }
                catch (Exception e)
                {
                    //
                    // NOTE: Nothing we can do here except log the failure.
                    //
                    TraceOps.DebugTrace(
                        e, typeof(Engine).Name,
                        TracePriority.ThreadError);

#if NOTIFY
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Engine, NotifyFlags.Exception,
                            new ObjectPair(obj, context), interpreter,
                            null, null, e);
                    }
#endif
                }
                finally
                {
                    if (disposeThread && (interpreter != null))
                    {
                        /* IGNORED */
                        interpreter.MaybeDisposeThread();
                    }
                }
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Callback Queue Support Methods
#if CALLBACK_QUEUE
        /// <summary>
        /// This method returns the command name (the first argument) from the
        /// specified argument list.
        /// </summary>
        /// <param name="arguments">
        /// The argument list whose first element is the command name.  This
        /// parameter may be null.
        /// </param>
        /// <returns>
        /// The command name, or null if the argument list is null or empty.
        /// </returns>
        private static string GetCommandName(
            StringList arguments
            )
        {
            return ((arguments != null) && (arguments.Count > 0)) ? arguments[0] : null;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes all callbacks currently queued on the
        /// specified interpreter, after first confirming that the interpreter
        /// is usable.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose callback queue should be executed.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use when executing each queued callback.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use when executing each queued callback.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use when executing each queued callback.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use when executing each queued callback.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size permitted for the result of each queued callback.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result of the last executed
        /// callback.  Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.
        /// </returns>
        internal static ReturnCode ExecuteCallbackQueue(
            Interpreter interpreter,
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

            return ExecuteCallbackQueue(
                interpreter, engineFlags, substitutionFlags, eventFlags,
                expressionFlags,
#if RESULT_LIMITS
                executeResultLimit,
#endif
                ref usable, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method executes all callbacks currently queued on the
        /// specified interpreter.  It has snapshot semantics; any callbacks
        /// queued while it is running are not executed until the next call.  If
        /// execution stops early, the remaining callbacks are re-enqueued.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose callback queue should be executed.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to use when executing each queued callback.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to use when executing each queued callback.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to use when executing each queued callback.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to use when executing each queued callback.
        /// </param>
        /// <param name="executeResultLimit">
        /// The maximum size permitted for the result of each queued callback.
        /// </param>
        /// <param name="usable">
        /// Upon return, indicates whether the interpreter is still usable;
        /// execution stops if the interpreter becomes unusable.
        /// </param>
        /// <param name="result">
        /// Upon success, this contains the result of the last executed
        /// callback.  Upon failure, this contains an appropriate error message.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise a non-Ok value
        /// (for example, <see cref="ReturnCode.Error" />) with details placed
        /// in <paramref name="result" />.
        /// </returns>
        private static ReturnCode ExecuteCallbackQueue(
            Interpreter interpreter,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
#if RESULT_LIMITS
            int executeResultLimit,
#endif
            ref bool usable,
            ref Result result
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }

            //
            // NOTE: Attempt to dequeue all the previously queued callbacks
            //       and process them, in order, until we either encounter
            //       an error or we run out of callbacks.  This method now
            //       has snapshot semantics; any callbacks queued during
            //       process will not be executed until the next time this
            //       method is called.
            //
            ReturnCode code;
            CommandCallback[] callbacks = null;

            code = interpreter.DequeueAllCallbacks(ref callbacks, ref result);

            if (code != ReturnCode.Ok)
                return code;

            //
            // NOTE: If the dequeue operation was successful and there are no
            //       callbacks, just skip everything else, returning success.
            //
            if (callbacks == null)
                return code;

            int nextIndex = Index.Invalid; /* First callback to re-enqueue. */
            int length = callbacks.Length; /* Total callbacks. */

            for (int index = 0; index < length; index++)
            {
                CommandCallback callback = callbacks[index];

                //
                // NOTE: Make sure the callback is valid; otherwise,
                //       just skip it.
                //
                if (callback == null)
                    continue;

                //
                // NOTE: Grab the arguments for the callback so that
                //       we can extract the command name.  The other
                //       arguments needed (if any) are already part
                //       of the command callback object itself.
                //
                string name = GetCommandName(callback.Arguments);

                //
                // NOTE: Execute the callback.  If we encounter an
                //       error, the loop will bail out.
                //
                code = Execute(
                    name, callback, interpreter,
                    GetClientData(
                        interpreter, null, false),
                    null, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags,
#if RESULT_LIMITS
                    executeResultLimit,
#endif
                    ref usable, ref result);

                //
                // NOTE: We need to bail out if there is an error
                //       -OR- the interpreter is no longer usable.
                //
                if ((code != ReturnCode.Ok) || !usable)
                {
                    //
                    // NOTE: Save the index of the next callback
                    //       that would have been executed, if any.
                    //       This will be used to re-enqueue the
                    //       callbacks to the callback queue that
                    //       we did not even attempt to execute.
                    //
                    if ((index + 1) < length)
                        nextIndex = index + 1;

                    break;
                }
            }

            //
            // NOTE: Are there any callbacks left that need to be
            //       [re-]enqueued to the interpreter?  This cannot
            //       be done if the interpreter is no longer usable
            //       (i.e. disposed).  However, that doesn't really
            //       matter because the remaining callbacks would
            //       never be executed anyway.
            //
            if (usable && (nextIndex != Index.Invalid))
            {
                ReturnCode enqueueCode;
                Result enqueueError = null;

                enqueueCode = interpreter.EnqueueSomeCallbacks(
                    nextIndex, ref callbacks, ref enqueueError);

                if (enqueueCode != ReturnCode.Ok)
                {
                    DebugOps.Complain(
                        interpreter, enqueueCode, enqueueError);
                }
            }

            return code;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Debugging Support Methods
#if DEBUGGER
#if DEBUGGER_ARGUMENTS
        #region Debugger Notification Methods
        /// <summary>
        /// This method gets the saved command argument list associated with the
        /// debugger for the specified interpreter, acquiring the engine lock for
        /// the duration of the query.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger argument list is being queried.  If
        /// this is null, no value is returned.
        /// </param>
        /// <returns>
        /// The saved command argument list, or null if the interpreter is
        /// invalid, the engine lock cannot be acquired, no debugger is present,
        /// or no argument list has been saved.
        /// </returns>
        internal static ArgumentList GetDebuggerExecuteArguments(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return null;

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter))
                        return null;

                    IDebugger debugger = interpreter.Debugger;

                    if (debugger == null)
                        return null;

                    return debugger.ExecuteArguments;
                }
                else
                {
                    TraceOps.LockTrace(
                        "GetDebuggerExecuteArguments",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    return null;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked); /* TRANSACTIONAL */
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // WARNING: This method is used in the critical path within the script
        //          evaluation engine and must be as simple as possible.
        //
        /// <summary>
        /// This method saves the specified command argument list onto the
        /// debugger for the specified interpreter, acquiring the engine lock for
        /// the duration of the update.  This method is used in the critical path
        /// within the script evaluation engine.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger argument list is being set.  If this
        /// is null, the operation fails.
        /// </param>
        /// <param name="arguments">
        /// The command argument list to save onto the debugger.
        /// </param>
        /// <returns>
        /// Non-zero if the argument list was saved, zero if the interpreter is
        /// invalid, the engine lock cannot be acquired, or no debugger is
        /// present.
        /// </returns>
        private static bool SetDebuggerExecuteArguments(
            Interpreter interpreter,
            ArgumentList arguments
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

                    IDebugger debugger = interpreter.Debugger;

                    if (debugger == null)
                        return false;

                    debugger.ExecuteArguments = arguments;
                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "SetDebuggerExecuteArguments",
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
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Debugger Checking Methods
        /// <summary>
        /// This method conditionally resets the pending "debugger exiting" flag
        /// for the specified interpreter, acquiring the engine lock for the
        /// duration of the operation.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose pending "debugger exiting" flag is being
        /// checked and possibly reset.  If this is null, the operation fails.
        /// </param>
        /// <returns>
        /// Non-zero if the flag was checked, zero if the interpreter is invalid
        /// or the engine lock cannot be acquired.
        /// </returns>
        private static bool CheckIsDebuggerExiting(
            Interpreter interpreter
            )
        {
            if (interpreter == null)
                return false;

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked);

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter))
                        return false;

                    interpreter.MaybeResetIsDebuggerExiting();
                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "CheckIsDebuggerExiting",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    return false;
                }
            }
            finally
            {
                interpreter.InternalExitLock(
                    ref locked);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves its associated (isolated) debugging
        /// interpreter.  This is a convenience overload that does not return the
        /// debugger itself.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugInterpreter">
        /// Upon success, receives the debugger's associated debugging
        /// interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger and its debugging interpreter were
        /// found, zero otherwise.
        /// </returns>
        internal static bool CheckDebuggerInterpreter(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref Interpreter debugInterpreter,
            ref Result error
            )
        {
            IDebugger debugger = null;

            return CheckDebugger(interpreter, ignoreEnabled,
                ref debugger, ref debugInterpreter, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks whether the specified interpreter has a usable
        /// debugger.  This is a convenience overload that discards all of the
        /// resolved debugger state and any error message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled
            )
        {
            IDebugger debugger = null;
            bool enabled = false;
            HeaderFlags headerFlags = HeaderFlags.None;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves both the debugger and its associated
        /// (isolated) debugging interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="debugInterpreter">
        /// Upon success, receives the debugger's associated debugging
        /// interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger and its debugging interpreter were
        /// found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref Interpreter debugInterpreter,
            ref Result error
            )
        {
            bool enabled = false;
            HeaderFlags headerFlags = HeaderFlags.None;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags, ref debugInterpreter, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger, its enabled state, its
        /// header display flags, and its associated (isolated) debugging
        /// interpreter.  This is a convenience overload that discards any error
        /// message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="enabled">
        /// Upon success, receives whether the debugger is currently enabled.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <param name="debugInterpreter">
        /// Upon success, receives the debugger's associated debugging
        /// interpreter.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger and its debugging interpreter were
        /// found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref bool enabled,
            ref HeaderFlags headerFlags,
            ref Interpreter debugInterpreter
            )
        {
            Result error = null;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags, ref debugInterpreter, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger, its enabled state, its
        /// header display flags, and its associated (isolated) debugging
        /// interpreter.  This overload performs the additional resolution of the
        /// debugging interpreter on behalf of the other overloads.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="enabled">
        /// Upon success, receives whether the debugger is currently enabled.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <param name="debugInterpreter">
        /// Upon success, receives the debugger's associated debugging
        /// interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger and its debugging interpreter were
        /// found, zero otherwise.
        /// </returns>
        private static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref bool enabled,
            ref HeaderFlags headerFlags,
            ref Interpreter debugInterpreter,
            ref Result error
            )
        {
            if (CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                    ref enabled, ref headerFlags, ref error))
            {
                debugInterpreter = debugger.Interpreter;

                if (debugInterpreter != null)
                    return true;
                else
                    error = "debugger interpreter not available";
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger.  This is a convenience
        /// overload that discards the enabled state and header display flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref Result error
            )
        {
            bool enabled = false;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger and its enabled state.  This
        /// is a convenience overload that discards the header display flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="enabled">
        /// Upon success, receives whether the debugger is currently enabled.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref bool enabled,
            ref Result error
            )
        {
            HeaderFlags headerFlags = HeaderFlags.None;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger and its header display
        /// flags.  This is a convenience overload that discards the enabled
        /// state and any error message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        private static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref HeaderFlags headerFlags
            )
        {
            bool enabled = false;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger and its header display
        /// flags.  This is a convenience overload that discards the enabled
        /// state.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref HeaderFlags headerFlags,
            ref Result error
            )
        {
            bool enabled = false;

            return CheckDebugger(interpreter, ignoreEnabled, ref debugger,
                ref enabled, ref headerFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method checks for a usable debugger on the specified interpreter
        /// and, upon success, resolves the debugger, its enabled state, and its
        /// header display flags.  This is a convenience overload that discards
        /// any error message.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="enabled">
        /// Upon success, receives whether the debugger is currently enabled.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero otherwise.
        /// </returns>
        private static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref bool enabled,
            ref HeaderFlags headerFlags
            )
        {
            Result error = null;

            return CheckDebugger(interpreter, ignoreEnabled,
                ref debugger, ref enabled, ref headerFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method performs the core debugger check for the specified
        /// interpreter, acquiring the engine lock for the duration of the query.
        /// It verifies that the interpreter is usable and not halted, fetches
        /// the debugger and header display flags, and (unless the enabled state
        /// is being ignored) requires the debugger to be enabled.  All other
        /// debugger checking overloads ultimately delegate to this method.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being checked.  If this is null,
        /// the operation fails.
        /// </param>
        /// <param name="ignoreEnabled">
        /// Non-zero to succeed even when a debugger is present but not currently
        /// enabled.
        /// </param>
        /// <param name="debugger">
        /// Upon success, receives the debugger associated with the interpreter.
        /// </param>
        /// <param name="enabled">
        /// Upon success, receives whether the debugger is currently enabled.
        /// </param>
        /// <param name="headerFlags">
        /// Upon success, receives the header display flags for the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the check
        /// failed.
        /// </param>
        /// <returns>
        /// Non-zero if a usable debugger was found, zero if the interpreter is
        /// invalid, the engine lock cannot be acquired, the interpreter is
        /// halted, no debugger is present, or the debugger is not enabled and
        /// the enabled state is not being ignored.
        /// </returns>
        internal static bool CheckDebugger(
            Interpreter interpreter,
            bool ignoreEnabled,
            ref IDebugger debugger,
            ref bool enabled,
            ref HeaderFlags headerFlags,
            ref Result error
            )
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return false;
            }

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter, ref error))
                        return false;

                    if (interpreter.GlobalHalt)
                    {
                        error = "halted";
                        return false;
                    }

                    debugger = interpreter.Debugger;
                    headerFlags = interpreter.HeaderFlags;

                    if (debugger == null)
                    {
                        error = "debugger not available";
                        return false;
                    }

                    enabled = debugger.Enabled;

                    if (ignoreEnabled || enabled)
                    {
                        return true;
                    }
                    else
                    {
                        error = "debugger not enabled";
                        return false;
                    }
                }
                else
                {
                    TraceOps.LockTrace(
                        "CheckDebugger",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    error = "unable to acquire lock";
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

        #region Debugger Setup Methods
        /// <summary>
        /// This method creates or tears down the debugger for the specified
        /// interpreter.  This is a convenience overload that discards the
        /// resolved debugger and the flag indicating whether the interpreter
        /// debugger field was modified.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being created or torn down.
        /// </param>
        /// <param name="culture">
        /// The culture name to use when creating the debugger and (when
        /// isolated) its debugging interpreter.
        /// </param>
        /// <param name="createFlags">
        /// The interpreter creation flags to use when creating the debugger.
        /// </param>
        /// <param name="hostCreateFlags">
        /// The host creation flags to use when creating the debugger.
        /// </param>
        /// <param name="initializeFlags">
        /// The initialization flags to use when creating the debugger.
        /// </param>
        /// <param name="scriptFlags">
        /// The script flags to use when creating the debugger.
        /// </param>
        /// <param name="interpreterFlags">
        /// The interpreter flags to use when creating the debugger.
        /// </param>
        /// <param name="pluginFlags">
        /// The plugin flags to use when creating the debugger.
        /// </param>
        /// <param name="appDomain">
        /// The application domain to use when creating the debugger.
        /// </param>
        /// <param name="host">
        /// The host to use when creating the debugger.
        /// </param>
        /// <param name="libraryPath">
        /// The script library path to use when creating the debugger.
        /// </param>
        /// <param name="autoPathList">
        /// The list of automatic package search paths to use when creating the
        /// debugger.
        /// </param>
        /// <param name="ignoreModifiable">
        /// Non-zero to skip the check that verifies the interpreter is allowed
        /// to be modified.
        /// </param>
        /// <param name="setup">
        /// Non-zero to create (set up) the debugger; zero to tear down and
        /// dispose any existing debugger.
        /// </param>
        /// <param name="isolated">
        /// Non-zero to create an isolated debugging interpreter for the
        /// debugger.
        /// </param>
        /// <param name="result">
        /// Upon success, receives result information; upon failure, receives an
        /// error message.
        /// </param>
        /// <returns>
        /// Non-zero on success, zero on failure.
        /// </returns>
        internal static bool SetupDebugger(
            Interpreter interpreter,
            string culture,
            CreateFlags createFlags,
            HostCreateFlags hostCreateFlags,
            InitializeFlags initializeFlags,
            ScriptFlags scriptFlags,
            InterpreterFlags interpreterFlags,
            PluginFlags pluginFlags,
            AppDomain appDomain,
            IHost host,
            string libraryPath,
            StringList autoPathList,
            bool ignoreModifiable,
            bool setup,
            bool isolated,
            ref Result result
            )
        {
            IDebugger debugger = null;
            bool modified = false;

            return SetupDebugger(
                interpreter, culture, createFlags, hostCreateFlags,
                initializeFlags, scriptFlags, interpreterFlags,
                pluginFlags, appDomain, host, libraryPath,
                autoPathList, ignoreModifiable, setup, isolated,
                ref debugger, ref modified, ref result);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method creates or tears down the debugger for the specified
        /// interpreter, acquiring the engine lock for the duration of the
        /// operation.  When setting up, an existing debugger is reused (and an
        /// isolated debugging interpreter is created if requested and missing);
        /// otherwise a new debugger is created.  When tearing down, any existing
        /// debugging interpreter and debugger are disposed and cleared.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose debugger is being created or torn down.  If
        /// this is null, the operation fails.
        /// </param>
        /// <param name="culture">
        /// The culture name to use when creating the debugger and (when
        /// isolated) its debugging interpreter.
        /// </param>
        /// <param name="createFlags">
        /// The interpreter creation flags to use when creating the debugger.
        /// </param>
        /// <param name="hostCreateFlags">
        /// The host creation flags to use when creating the debugger.
        /// </param>
        /// <param name="initializeFlags">
        /// The initialization flags to use when creating the debugger.
        /// </param>
        /// <param name="scriptFlags">
        /// The script flags to use when creating the debugger.
        /// </param>
        /// <param name="interpreterFlags">
        /// The interpreter flags to use when creating the debugger.
        /// </param>
        /// <param name="pluginFlags">
        /// The plugin flags to use when creating the debugger.
        /// </param>
        /// <param name="appDomain">
        /// The application domain to use when creating the debugger.
        /// </param>
        /// <param name="host">
        /// The host to use when creating the debugger.
        /// </param>
        /// <param name="libraryPath">
        /// The script library path to use when creating the debugger.
        /// </param>
        /// <param name="autoPathList">
        /// The list of automatic package search paths to use when creating the
        /// debugger.
        /// </param>
        /// <param name="ignoreModifiable">
        /// Non-zero to skip the check that verifies the interpreter is allowed
        /// to be modified.
        /// </param>
        /// <param name="setup">
        /// Non-zero to create (set up) the debugger; zero to tear down and
        /// dispose any existing debugger.
        /// </param>
        /// <param name="isolated">
        /// Non-zero to create an isolated debugging interpreter for the
        /// debugger.
        /// </param>
        /// <param name="debugger">
        /// Upon return, receives the debugger for the interpreter, which will be
        /// null after a successful tear down.
        /// </param>
        /// <param name="modified">
        /// Upon return, indicates whether the interpreter debugger field was
        /// changed by this call.
        /// </param>
        /// <param name="result">
        /// Upon success, receives result information; upon failure, receives an
        /// error message.
        /// </param>
        /// <returns>
        /// Non-zero on success, zero on failure.
        /// </returns>
        internal static bool SetupDebugger(
            Interpreter interpreter,
            string culture,
            CreateFlags createFlags,
            HostCreateFlags hostCreateFlags,
            InitializeFlags initializeFlags,
            ScriptFlags scriptFlags,
            InterpreterFlags interpreterFlags,
            PluginFlags pluginFlags,
            AppDomain appDomain,
            IHost host,
            string libraryPath,
            StringList autoPathList,
            bool ignoreModifiable,
            bool setup,
            bool isolated,
            ref IDebugger debugger,
            ref bool modified,
            ref Result result
            )
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return false;
            }

            bool locked = false;

            try
            {
                interpreter.InternalEngineTryLock(
                    ref locked); /* TRANSACTIONAL */

                if (locked)
                {
                    if (!IsUsableNoLock(interpreter, ref result))
                        return false;

                    //
                    // NOTE: We intend to modify the interpreter state, make
                    //       sure this is not forbidden.
                    //
                    if (!ignoreModifiable &&
                        !interpreter.IsModifiable(false, ref result))
                    {
                        return false;
                    }

                    debugger = interpreter.Debugger;

                    if (setup)
                    {
                        if (debugger == null)
                        {
                            //
                            // NOTE: Create a new debugger using the
                            //       creation arguments provided by
                            //       the caller.
                            //
                            debugger = DebuggerOps.Create(
                                isolated, culture, createFlags, hostCreateFlags,
                                initializeFlags, scriptFlags, interpreterFlags,
                                pluginFlags, appDomain, host, libraryPath,
                                autoPathList);

                            //
                            // NOTE: Now, initialize the debugger field
                            //       for the interpreter.
                            //
                            interpreter.Debugger = debugger; modified = true;
                        }

                        if (isolated)
                        {
                            Interpreter debugInterpreter = debugger.Interpreter;

                            if (debugInterpreter == null)
                            {
                                debugInterpreter = DebuggerOps.CreateInterpreter(
                                    culture, createFlags, hostCreateFlags,
                                    initializeFlags, scriptFlags, interpreterFlags,
                                    pluginFlags, appDomain, host, libraryPath,
                                    autoPathList, ref result);

                                if (debugInterpreter == null)
                                    return false;

                                debugger.Interpreter = debugInterpreter;
                            }
                        }
                    }
                    else if (debugger != null)
                    {
                        Interpreter debugInterpreter = debugger.Interpreter;

                        if (debugInterpreter != null)
                        {
                            debugInterpreter.Dispose();
                            debugInterpreter = null;

                            debugger.Interpreter = null;
                        }

                        IDisposable disposable = debugger as IDisposable;

                        if (disposable != null)
                        {
                            disposable.Dispose();
                            disposable = null;
                        }

                        debugger = null;

                        //
                        // NOTE: Finally, clear out the debugger field
                        //       for the interpreter.
                        //
                        interpreter.Debugger = null; modified = true;
                    }

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "SetupDebugger",
                        typeof(Engine).Name, false,
                        TracePriority.LockError,
                        interpreter.MaybeWhoHasLock());

                    result = "unable to acquire lock";
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

        #region Breakpoint Support Methods
        #region Generic Execute Breakpoint Methods
        /// <summary>
        /// This method determines whether either the specified executable entity
        /// or the specified execute argument entity has a breakpoint set on it.
        /// </summary>
        /// <param name="execute">
        /// The executable entity (e.g. command or procedure) to check.
        /// </param>
        /// <param name="executeArgument">
        /// The execute argument entity (e.g. function or operator) to check.
        /// </param>
        /// <returns>
        /// Non-zero if either entity has a breakpoint set, zero otherwise.
        /// </returns>
        private static bool HasAnyBreakpoint(
            IExecute execute,
            IExecuteArgument executeArgument
            )
        {
            return HasExecuteBreakpoint(execute) ||
                HasExecuteArgumentBreakpoint(executeArgument);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether the specified executable entity has a
        /// breakpoint set on it.  Only procedures and commands are considered.
        /// </summary>
        /// <param name="execute">
        /// The executable entity to check.  If this is null, or is neither a
        /// procedure nor a command, the result is zero.
        /// </param>
        /// <returns>
        /// Non-zero if the entity has a breakpoint set, zero otherwise.
        /// </returns>
        internal static bool HasExecuteBreakpoint(
            IExecute execute
            )
        {
            if (execute != null)
            {
                IProcedure procedure = execute as IProcedure;

                if (procedure != null)
                    return EntityOps.HasBreakpoint(procedure);

                ICommand command = execute as ICommand;

                if (command != null)
                    return EntityOps.HasBreakpoint(command);
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether the specified execute argument entity
        /// has a breakpoint set on it.  Only functions and operators are
        /// considered.
        /// </summary>
        /// <param name="executeArgument">
        /// The execute argument entity to check.  If this is null, or is neither
        /// a function nor an operator, the result is zero.
        /// </param>
        /// <returns>
        /// Non-zero if the entity has a breakpoint set, zero otherwise.
        /// </returns>
        internal static bool HasExecuteArgumentBreakpoint(
            IExecuteArgument executeArgument
            )
        {
            if (executeArgument != null)
            {
                IFunction function = executeArgument as IFunction;

                if (function != null)
                    return EntityOps.HasBreakpoint(function);

                IOperator @operator = executeArgument as IOperator;

                if (@operator != null)
                    return EntityOps.HasBreakpoint(@operator);
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets or clears the breakpoint on the specified executable
        /// entity.  Only procedures and commands are supported.  It is an error
        /// if the requested state already matches the current state.
        /// </summary>
        /// <param name="execute">
        /// The executable entity (procedure or command) whose breakpoint is
        /// being changed.  If this is null, the operation fails.
        /// </param>
        /// <param name="enable">
        /// Non-zero to set the breakpoint, zero to clear it.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the
        /// breakpoint could not be changed.
        /// </param>
        /// <returns>
        /// Non-zero if the breakpoint state was changed, zero otherwise.
        /// </returns>
        internal static bool SetExecuteBreakpoint(
            IExecute execute,
            bool enable,
            ref Result error
            )
        {
            if (execute != null)
            {
                IProcedure procedure = execute as IProcedure;

                if (procedure != null)
                {
                    bool enabled = EntityOps.HasBreakpoint(procedure);

                    if (enable != enabled)
                    {
                        /* IGNORED */
                        EntityOps.SetBreakpoint(procedure, enable);

                        return true;
                    }
                    else
                    {
                        error = String.Format(
                            "procedure {0} breakpoint is already {1}",
                            FormatOps.DisplayName(EntityOps.GetNameNoThrow(
                            procedure)), enable ? "set" : "unset");
                    }

                    return false;
                }

                ICommand command = execute as ICommand;

                if (command != null)
                {
                    bool enabled = EntityOps.HasBreakpoint(command);

                    if (enable != enabled)
                    {
                        /* IGNORED */
                        EntityOps.SetBreakpoint(command, enable);

                        return true;
                    }
                    else
                    {
                        error = String.Format(
                            "command {0} breakpoint is already {1}",
                            FormatOps.DisplayName(EntityOps.GetNameNoThrow(
                            command)), enable ? "set" : "unset");
                    }

                    return false;
                }

                error = "not a command or procedure";
            }
            else
            {
                error = "invalid execute";
            }

            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets or clears the breakpoint on the specified execute
        /// argument entity.  Only functions and operators are supported.  It is
        /// an error if the requested state already matches the current state.
        /// </summary>
        /// <param name="executeArgument">
        /// The execute argument entity (function or operator) whose breakpoint
        /// is being changed.  If this is null, the operation fails.
        /// </param>
        /// <param name="enable">
        /// Non-zero to set the breakpoint, zero to clear it.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives an error message that describes why the
        /// breakpoint could not be changed.
        /// </param>
        /// <returns>
        /// Non-zero if the breakpoint state was changed, zero otherwise.
        /// </returns>
        internal static bool SetExecuteArgumentBreakpoint(
            IExecuteArgument executeArgument,
            bool enable,
            ref Result error
            )
        {
            if (executeArgument != null)
            {
                IFunction function = executeArgument as IFunction;

                if (function != null)
                {
                    bool enabled = EntityOps.HasBreakpoint(function);

                    if (enable != enabled)
                    {
                        /* IGNORED */
                        EntityOps.SetBreakpoint(function, enable);

                        return true;
                    }
                    else
                    {
                        error = String.Format(
                            "function {0} breakpoint is already {1}",
                            FormatOps.DisplayName(EntityOps.GetNameNoThrow(
                            function)), enable ? "set" : "unset");
                    }

                    return false;
                }

                IOperator @operator = executeArgument as IOperator;

                if (@operator != null)
                {
                    bool enabled = EntityOps.HasBreakpoint(@operator);

                    if (enable != enabled)
                    {
                        /* IGNORED */
                        EntityOps.SetBreakpoint(@operator, enable);

                        return true;
                    }
                    else
                    {
                        error = String.Format(
                            "operator {0} breakpoint is already {1}",
                            FormatOps.DisplayName(EntityOps.GetNameNoThrow(
                            @operator)), enable ? "set" : "unset");
                    }

                    return false;
                }

                error = "not a function or operator";
            }
            else
            {
                error = "invalid execute argument";
            }

            return false;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Token Breakpoint Methods
#if DEBUGGER_BREAKPOINTS
        /// <summary>
        /// This method determines whether the specified token has a breakpoint
        /// associated with it, either via its own token flags or by matching one
        /// of the breakpoints registered with the debugger.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter used when matching the token against the debugger
        /// breakpoints.
        /// </param>
        /// <param name="debugger">
        /// The debugger whose registered breakpoints are matched against the
        /// token.
        /// </param>
        /// <param name="token">
        /// The token to check.  If this is null, the result is zero.
        /// </param>
        /// <returns>
        /// Non-zero if the token has a breakpoint set or matches a registered
        /// breakpoint, zero otherwise.
        /// </returns>
        private static bool HasTokenBreakpoint(
            Interpreter interpreter,
            IDebugger debugger,
            IToken token
            )
        {
            if (token != null)
            {
                if (FlagOps.HasFlags(token.Flags, TokenFlags.Breakpoint, true))
                {
                    return true;
                }
                else
                {
                    bool match = false;

                    if (debugger.MatchBreakpoint(
                            interpreter, token, ref match) == ReturnCode.Ok)
                    {
                        return match;
                    }
                }
            }

            return false;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Test Breakpoint Methods (for [test1] and [test2])
        /// <summary>
        /// This method determines whether the specified interpreter has a test
        /// breakpoint registered for the given name, as used by the
        /// <c>[test1]</c> and <c>[test2]</c> commands.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to query.  If this is null, the result is zero.
        /// </param>
        /// <param name="name">
        /// The name of the test breakpoint to look for.
        /// </param>
        /// <returns>
        /// Non-zero if a matching test breakpoint is registered, zero otherwise.
        /// </returns>
        private static bool HasTestBreakpoint(
            Interpreter interpreter,
            string name
            )
        {
            if (interpreter != null)
                return interpreter.HasTestBreakpoint(name);

            return false;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Breakpoint Methods
        /// <summary>
        /// This method evaluates the current execution point against the active
        /// debugger to determine whether a breakpoint should be triggered.  It
        /// considers single-step and multiple-step state, identifier (command,
        /// procedure, function, and operator), cancellation, unwind, exit,
        /// error, return, test, and token breakpoint criteria.  When at least
        /// one criterion is met, it dispatches any associated notifications and
        /// enters the interactive debugger loop.
        /// </summary>
        /// <param name="code">
        /// The return code of the operation that reached this execution point.
        /// </param>
        /// <param name="breakpointType">
        /// The kind(s) of breakpoint applicable to this execution point.
        /// </param>
        /// <param name="breakpointName">
        /// The name associated with this execution point, used for test
        /// breakpoint matching.
        /// </param>
        /// <param name="token">
        /// The script token at this execution point, used for token breakpoint
        /// matching.
        /// </param>
        /// <param name="traceInfo">
        /// The trace information associated with this execution point, if any.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect for the current operation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current operation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect for the current operation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current operation.
        /// </param>
        /// <param name="execute">
        /// The executable entity (command or procedure) being executed at this
        /// point, used for identifier breakpoint matching.
        /// </param>
        /// <param name="executeArgument">
        /// The execute argument entity (function or operator) being executed at
        /// this point, used for identifier breakpoint matching.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter associated with the current operation.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the current operation.
        /// </param>
        /// <param name="arguments">
        /// The argument list associated with the current operation.
        /// </param>
        /// <param name="result">
        /// Upon return, receives the result produced by handling the breakpoint,
        /// which may be modified by the interactive debugger loop.
        /// </param>
        /// <returns>
        /// The (possibly updated) return code after any breakpoint handling.
        /// </returns>
        internal static ReturnCode CheckBreakpoints(
            ReturnCode code,
            BreakpointType breakpointType,
            string breakpointName,
            IToken token,
            ITraceInfo traceInfo,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            IExecute execute,
            IExecuteArgument executeArgument,
            Interpreter interpreter,
            IClientData clientData,
            ArgumentList arguments,
            ref Result result
            )
        {
            IDebugger debugger = null;
            HeaderFlags headerFlags = HeaderFlags.None;
            bool breakpoint = false;

            if (CheckDebugger(interpreter, false, ref debugger, ref headerFlags))
            {
                if (FlagOps.HasFlags(debugger.Types, breakpointType, true))
                {
                    BreakpointType newBreakpointType = BreakpointType.None;

                    if (debugger.SingleStep)
                        newBreakpointType |= BreakpointType.SingleStep;

                    if (debugger.MaybeNextStep())
                        newBreakpointType |= BreakpointType.MultipleStep;

                    if (debugger.BreakOnExecute &&
                        HasAnyBreakpoint(execute, executeArgument))
                    {
                        newBreakpointType |= BreakpointType.Identifier;
                    }

                    if ((FlagOps.HasFlags(
                            breakpointType, BreakpointType.Cancel, true) ||
                        FlagOps.HasFlags(
                            breakpointType, BreakpointType.Unwind, true)) &&
                        debugger.BreakOnCancel)
                    {
                        //
                        // NOTE: This will be "Cancel" and possibly "Unwind".
                        //
                        newBreakpointType |= breakpointType;
                    }

                    if (FlagOps.HasFlags(
                            breakpointType, BreakpointType.Exit, true) &&
                        debugger.BreakOnExit)
                    {
                        //
                        // NOTE: This will be "Exit" and either "Evaluate" or
                        //       "Substitute".
                        //
                        newBreakpointType |= breakpointType;
                    }

                    if ((code == ReturnCode.Error) && debugger.BreakOnError)
                        newBreakpointType |= BreakpointType.Error;

                    if ((code == ReturnCode.Return) && debugger.BreakOnReturn)
                        newBreakpointType |= BreakpointType.Return;

                    if (debugger.BreakOnTest &&
                        HasTestBreakpoint(interpreter, breakpointName))
                    {
                        newBreakpointType |= BreakpointType.Test;
                    }

#if DEBUGGER_BREAKPOINTS
                    if (debugger.BreakOnToken &&
                        HasTokenBreakpoint(interpreter, debugger, token))
                    {
                        newBreakpointType |= BreakpointType.Token;
                    }
#endif

                    //
                    // NOTE: Did we meet at least one criteria for a
                    //       breakpoint at this point in the script?
                    //
                    if (newBreakpointType != BreakpointType.None)
                    {
                        breakpointType |= newBreakpointType;
                        breakpoint = true;
                    }
                }
            }

            if (breakpoint)
            {
#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Debugger, NotifyFlags.PreBreakpoint,
                        new ObjectList(code, breakpointType, breakpointName,
                        token, traceInfo, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, execute, executeArgument),
                        interpreter, clientData, arguments, null, ref result);
                }
#endif

                //
                // BUGFIX: Do not show full debugger info for a simple breakpoint (unless
                //         the default header display flags have been overridden by the
                //         user).
                //
                if (FlagOps.HasFlags(headerFlags, HeaderFlags.User, true))
                    headerFlags |= HeaderFlags.Breakpoint;
                else
                    headerFlags = HeaderFlags.Breakpoint;

                code = DebuggerOps.Breakpoint(
                    debugger, interpreter, new InteractiveLoopData(code, breakpointType,
                    breakpointName, token, traceInfo, engineFlags, substitutionFlags,
                    eventFlags, expressionFlags, headerFlags, clientData, arguments),
                    ref result);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Debugger, NotifyFlags.Breakpoint,
                        new ObjectList(code, breakpointType, breakpointName,
                        token, traceInfo, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, execute, executeArgument),
                        interpreter, clientData, arguments, null, ref result);
                }
#endif
            }

            return code;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Watchpoint Methods
#if DEBUGGER_VARIABLE
        /// <summary>
        /// This method evaluates the current variable operation against the
        /// active debugger to determine whether a variable watchpoint should be
        /// triggered.  When the debugger is watching for the given breakpoint
        /// type, it dispatches any associated notifications and enters the
        /// interactive debugger loop for the duration of an entered watchpoint
        /// level.
        /// </summary>
        /// <param name="code">
        /// The return code of the operation that reached this watchpoint.
        /// </param>
        /// <param name="breakpointType">
        /// The kind(s) of watchpoint (variable access) applicable to this
        /// operation.
        /// </param>
        /// <param name="breakpointName">
        /// The name associated with this watchpoint operation.
        /// </param>
        /// <param name="token">
        /// The script token associated with this watchpoint operation, if any.
        /// </param>
        /// <param name="traceInfo">
        /// The trace information associated with this watchpoint operation, if
        /// any.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect for the current operation.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect for the current operation.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect for the current operation.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect for the current operation.
        /// </param>
        /// <param name="interpreter">
        /// The interpreter associated with the current operation.
        /// </param>
        /// <param name="result">
        /// Upon return, receives the result produced by handling the watchpoint,
        /// which may be modified by the interactive debugger loop.
        /// </param>
        /// <returns>
        /// The (possibly updated) return code after any watchpoint handling.
        /// </returns>
        internal static ReturnCode CheckWatchpoints(
            ReturnCode code,
            BreakpointType breakpointType,
            string breakpointName,
            IToken token,
            ITraceInfo traceInfo,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags,
            EventFlags eventFlags,
            ExpressionFlags expressionFlags,
            Interpreter interpreter,
            ref Result result
            )
        {
            IDebugger debugger = null;
            HeaderFlags headerFlags = HeaderFlags.None;
            bool watchpoint = false;

            if (CheckDebugger(interpreter, false, ref debugger, ref headerFlags))
            {
                if (FlagOps.HasFlags(debugger.Types, breakpointType, true))
                {
                    watchpoint = true;
                }
            }

            if (watchpoint)
            {
                //
                // BUGFIX: Do not show full debugger info for a variable watch (unless
                //         the default header display flags have been overridden by the
                //         user).
                //
                if (FlagOps.HasFlags(headerFlags, HeaderFlags.User, true))
                    headerFlags |= HeaderFlags.Watchpoint;
                else
                    headerFlags = HeaderFlags.Watchpoint;

                if (interpreter != null)
                    /* IGNORED */
                    interpreter.EnterWatchpointLevel();

                try
                {
#if NOTIFY
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Debugger, NotifyFlags.PreWatchpoint,
                            new ObjectList(code, breakpointType, breakpointName,
                            token, traceInfo, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags), interpreter, null,
                            null, null, ref result);
                    }
#endif

                    code = DebuggerOps.Watchpoint(
                        debugger, interpreter, new InteractiveLoopData(code, breakpointType,
                        breakpointName, token, traceInfo, engineFlags, substitutionFlags,
                        eventFlags, expressionFlags, headerFlags), ref result);

#if NOTIFY
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Debugger, NotifyFlags.Watchpoint,
                            new ObjectList(code, breakpointType, breakpointName,
                            token, traceInfo, engineFlags, substitutionFlags,
                            eventFlags, expressionFlags), interpreter, null,
                            null, null, ref result);
                    }
#endif
                }
                finally
                {
                    if (interpreter != null)
                        /* IGNORED */
                        interpreter.ExitWatchpointLevel();
                }
            }

            return code;
        }
#endif
        #endregion
        #endregion
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Event Processing Support Methods
        /// <summary>
        /// This method verifies that the interpreter is ready for use and then,
        /// unless events have been disabled, processes any pending asynchronous
        /// events (which may run arbitrary script code) before re-verifying that
        /// the interpreter is still ready.  This method is thread-safe and
        /// re-entrant.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose readiness is checked and whose pending events
        /// are processed.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect, which control whether the readiness
        /// checks and event processing are performed.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect.  This parameter is not used.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags used when processing any pending asynchronous events.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect.  This parameter is not used.
        /// </param>
        /// <param name="result">
        /// Upon failure, receives an error message that describes why the check
        /// or event processing failed.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise, the return code
        /// from the readiness check or event processing that failed.
        /// </returns>
        internal static ReturnCode CheckEvents(
            Interpreter interpreter,
            EngineFlags engineFlags,
            SubstitutionFlags substitutionFlags, /* NOT USED */
            EventFlags eventFlags,
            ExpressionFlags expressionFlags, /* NOT USED */
            ref Result result
            ) /* THREAD-SAFE, RE-ENTRANT */
        {
            ReturnCode code = ReturnCode.Ok;

            if (!EngineFlagOps.HasNoReady(engineFlags))
            {
                //
                // NOTE: Check if the interpreter is still valid and ready for use.
                //
                code = Interpreter.EngineReady(
                    interpreter, null, GetReadyFlags(engineFlags), ref result);

                if (code != ReturnCode.Ok)
                    return code;
            }

            //
            // NOTE: Skip event processing if events have been disabled.
            //
            if (!EngineFlagOps.HasNoEvent(engineFlags))
            {
                //
                // NOTE: Process any pending asynchronous events.  This could cause
                //       almost anything to happen (including script evaluation).
                //
                code = EventOps.ProcessEvents(
                    interpreter, eventFlags, EventPriority.CheckEvents, null, 0,
                    true, false, ref result);

                if (code != ReturnCode.Ok)
                    return code;

                if (!EngineFlagOps.HasNoReady(engineFlags))
                {
                    //
                    // NOTE: Now, re-check if the interpreter is still valid and
                    //       ready for use because the asynchronous events, if any,
                    //       could have invalidated the interpreter in some way.
                    //
                    code = Interpreter.EngineReady(
                        interpreter, null, GetReadyFlags(engineFlags), ref result);

                    if (code != ReturnCode.Ok)
                        return code;
                }
            }

            return code;
        }
        #endregion
        #endregion
    }
}
