/*
 * Engine.Flags.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Private Constants, Private Data, and Engine Flags Methods" region group) so that no
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
        #region Private Constants
        //
        // NOTE: The maximum length used when adding the original command text that
        //       caused the current script error to the interpreter error info.
        //
        /// <summary>
        /// The maximum length of the original command text that is appended to
        /// the interpreter error information for the current script error.
        /// </summary>
        private const int ErrorInfoCommandLength = 150;

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: The maximum number of times to append to the errorInfo variable
        //       while unwinding the evaluation stack after a stack overflow.
        //
        /// <summary>
        /// The maximum number of times the error information variable is
        /// appended to while unwinding the evaluation stack after a stack
        /// overflow.
        /// </summary>
        private const int ErrorInfoStackOverflowFrames = 5;

        //
        // NOTE: The maximum level beyond which the errorInfo variable should not
        //       be appended to while unwinding the evaluation stack after a stack
        //       overflow.
        //
        /// <summary>
        /// The maximum level beyond which the error information variable is no
        /// longer appended to while unwinding the evaluation stack after a
        /// stack overflow.
        /// </summary>
        private const int ErrorInfoStackOverflowLevels = 5;

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: The flags used when the engine needs to set a script variable
        //       (typically to report extended error information).
        //
        /// <summary>
        /// The variable flags used when the engine needs to set a script
        /// variable, typically to report extended error information.
        /// </summary>
        private static readonly VariableFlags ErrorVariableFlags =
            VariableFlags.LibraryMask | VariableFlags.ViaEngine;

        //
        // NOTE: The flags used when the engine needs to set the "::errorCode"
        //       script variable.
        //
        /// <summary>
        /// The variable flags used when the engine needs to set the
        /// <c>::errorCode</c> script variable.
        /// </summary>
        internal static readonly VariableFlags ErrorCodeVariableFlags =
            ErrorVariableFlags |
#if FAST_ERRORCODE
            VariableFlags.FastTraceMask;
#else
            VariableFlags.None;
#endif

        //
        // NOTE: The flags used when the engine needs to set the "::errorInfo"
        //       script variable.
        //
        /// <summary>
        /// The variable flags used when the engine needs to set the
        /// <c>::errorInfo</c> script variable.
        /// </summary>
        internal static readonly VariableFlags ErrorInfoVariableFlags =
            ErrorVariableFlags |
#if FAST_ERRORINFO
            VariableFlags.FastTraceMask;
#else
            VariableFlags.None;
#endif

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: The default stack size for new threads when compiled or run
        //       on a Mono or Unix platform (or without native code support).
        //
        /// <summary>
        /// The default stack size, in bytes, for new threads when compiled or
        /// run on a Mono or Unix platform (or without native code support).
        /// </summary>
        private const int DefaultStackSize = 0x100000; // 1MB

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the error result used as a last resort when there is
        //       no more memory available.
        //
        /// <summary>
        /// The error result used as a last resort when there is no more memory
        /// available.
        /// </summary>
        private static readonly Result OutOfMemoryException = typeof(Engine) +
            ".Critical.OutOfMemoryException";

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the error result used as a last resort when there is
        //       no more stack space available.
        //
        /// <summary>
        /// The error result used as a last resort when there is no more stack
        /// space available.
        /// </summary>
        private static readonly Result StackOverflowException = typeof(Engine) +
            ".Critical.StackOverflowException";

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the error result used when its thread is interrupted
        //       via the Thread.Interrupt() method.
        //
        /// <summary>
        /// The error result used when its thread is interrupted via the
        /// <c>Thread.Interrupt</c> method.
        /// </summary>
        private static readonly Result ThreadInterruptedException = typeof(Engine) +
            ".Critical.ThreadInterruptedException";

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the error result used when its thread is aborted via
        //       the Thread.Abort() method.
        //
        /// <summary>
        /// The error result used when its thread is aborted via the
        /// <c>Thread.Abort</c> method.
        /// </summary>
        private static readonly Result ThreadAbortException = typeof(Engine) +
            ".Critical.ThreadAbortException";

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the error message returned if the interpreter is
        //       somehow unusable (i.e. it may have been disposed, deleted,
        //       etc).
        //
        /// <summary>
        /// The error message returned when the interpreter is somehow unusable
        /// (e.g. it may have been disposed or deleted).
        /// </summary>
        internal static readonly Result InterpreterUnusableError =
            "interpreter is unusable (it may have been disposed)";

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The error message returned when script evaluation is canceled.
        /// </summary>
        internal static readonly Result EvalCanceledError = "eval canceled";
        /// <summary>
        /// The error message returned when script evaluation is canceled and
        /// the evaluation stack is unwound.
        /// </summary>
        internal static readonly Result EvalUnwoundError = "eval unwound";

        /// <summary>
        /// The error message returned when script evaluation is canceled due to
        /// a timeout.
        /// </summary>
        internal static readonly Result EvalCanceledTimeoutError = "eval canceled due to timeout";
        /// <summary>
        /// The error message returned when script evaluation is unwound due to
        /// a timeout.
        /// </summary>
        internal static readonly Result EvalUnwoundTimeoutError = "eval unwound due to timeout";

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The error message returned when the interpreter has been halted.
        /// </summary>
        internal static readonly Result HaltedError = "halted";
        /// <summary>
        /// The prominent error message used to indicate that the interpreter
        /// has been halted.
        /// </summary>
        internal static readonly Result InterpreterHaltedError = "INTERPRETER HALTED";

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: This is the number of bytes to attempt to read at once from
        //       after the "soft" end-of-file.
        //
        // HACK: This is purposely not read-only.
        //
        /// <summary>
        /// The number of bytes to attempt to read at once after the "soft"
        /// end-of-file when reading a script.
        /// </summary>
        private static int ReadPostScriptBufferSize = 262144; /* 256K */

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // HACK: These are purposely not read-only.
        //
        /// <summary>
        /// When non-zero, blocking channel flags are used for the <c>fcopy</c>
        /// command.
        /// </summary>
        internal static bool BlockingFlagsForFcopy = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used for process
        /// redirection; this must remain false because the operation cannot
        /// block.
        /// </summary>
        internal static bool BlockingFlagsForProcess = false; // BUGFIX: Cannot block.

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // HACK: These are purposely not read-only.
        //
        /// <summary>
        /// When non-zero, blocking channel flags are used when evaluating a
        /// script.
        /// </summary>
        private static bool BlockingFlagsForEvaluate = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used when executing a
        /// command.
        /// </summary>
        private static bool BlockingFlagsForExecute = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used when reading a
        /// script.
        /// </summary>
        private static bool BlockingFlagsForRead = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used when reading the
        /// bytes of a script.
        /// </summary>
        private static bool BlockingFlagsForReadBytes = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used when reading a script
        /// from a file.
        /// </summary>
        private static bool BlockingFlagsForReadFile = true; // COMPAT: TclTk beta.
        /// <summary>
        /// When non-zero, blocking channel flags are used when reading a script
        /// from a stream.
        /// </summary>
        private static bool BlockingFlagsForReadStream = true; // COMPAT: TclTk beta.
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Private Data
        #region Global Engine Lock
        //
        // NOTE: This is only used to protect global engine data that is not
        //       read-only.  The only data currently in this category is the
        //       global throw-on-disposed flag.
        //
        /// <summary>
        /// The object used to synchronize access to the global engine data that
        /// is not read-only (currently only the global throw-on-disposed flag).
        /// </summary>
        private static readonly object syncRoot = new object();
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Global Throw-On-Disposed Flag
        //
        // NOTE: The default value here should always be "true", use the
        //       "NoThrowOnDisposed" environment variable to override.
        //
        /// <summary>
        /// When non-zero, an exception is thrown when a disposed interpreter is
        /// accessed.  The default is true and may be overridden via the
        /// <c>NoThrowOnDisposed</c> environment variable.
        /// </summary>
        private static bool ThrowOnDisposed = true;
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Global Extra (Reserved) Stack Space
#if NATIVE
        /// <summary>
        /// The amount of extra stack space, in bytes, reserved when performing
        /// native stack checks.
        /// </summary>
        private static ulong ExtraStackSpace = 0;
#endif
        #endregion
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Engine Flags Methods
        /// <summary>
        /// This method computes the resulting engine flags by applying the
        /// changes between the old and new flags, permitting only those
        /// additions allowed by the add mask and those removals allowed by the
        /// remove mask.
        /// </summary>
        /// <param name="oldFlags">
        /// The original engine flags.
        /// </param>
        /// <param name="newFlags">
        /// The proposed engine flags.
        /// </param>
        /// <param name="addMask">
        /// The mask of engine flags that are permitted to be added.
        /// </param>
        /// <param name="removeMask">
        /// The mask of engine flags that are permitted to be removed.
        /// </param>
        /// <returns>
        /// The resulting engine flags after the permitted additions and
        /// removals have been applied.
        /// </returns>
        internal static EngineFlags CombineFlagsWithMasks(
            EngineFlags oldFlags,
            EngineFlags newFlags,
            EngineFlags addMask,
            EngineFlags removeMask
            )
        {
            //
            // NOTE: What flags were added (between the old and new flags)?
            //
            EngineFlags addedFlags = ~oldFlags & newFlags;

            //
            // NOTE: What flags were removed (between the old and new flags)?
            //
            EngineFlags removedFlags = oldFlags & ~newFlags;

            //
            // NOTE: For the flags that were added, just mask off the ones
            //       that are not permitted.
            //
            addedFlags &= addMask;

            //
            // NOTE: For the flags that were removed, mask off the ones that
            //       are not permitted.
            //
            removedFlags &= removeMask;

            //
            // NOTE: Start with the old flags.
            //
            EngineFlags result = oldFlags;

            //
            // NOTE: Add flags that were added -AND- that were permitted to
            //       be added.
            //
            result |= addedFlags;

            //
            // NOTE: Remove flags that were removed -AND- that were permitted
            //       to be removed.
            //
            result &= ~removedFlags;

            //
            // NOTE: Return the final resulting flags.
            //
            return result;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method combines the specified engine flags with any engine
        /// flags set in the interpreter, optionally adding the native stack
        /// checking flags and optionally masking off the error handling flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter whose engine flags should be honored, if any.  This
        /// parameter may be null.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags supplied by the caller.
        /// </param>
        /// <param name="checkStack">
        /// Non-zero to add the native stack space checking flags.
        /// </param>
        /// <param name="errorMask">
        /// Non-zero to remove the error handling flags from the result.
        /// </param>
        /// <returns>
        /// The combined engine flags.
        /// </returns>
        private static EngineFlags CombineFlags(
            Interpreter interpreter,
            EngineFlags engineFlags,
            bool checkStack,
            bool errorMask
            )
        {
            EngineFlags result = engineFlags;

            //
            // NOTE: Make sure that we honor any flags set in the interpreter,
            //       if available, in addition to the ones passed by the caller.
            //
            if (interpreter != null)
            {
                //
                // HACK: The interpreter lock is not being held here, due to
                //       this being within the very hot path.
                //
                result |= interpreter.EngineFlagsNoLock; /* EXEMPT */
            }

            //
            // BUGFIX: If requested, make sure the native stack space checking
            //         flag is set from within the engine itself.
            //
            if (checkStack)
                result |= EngineFlags.BaseStackMask;

            //
            // BUGFIX: Make sure the error handling flags are not copied.  The
            //         error handling flags will be removed from the interpreter
            //         itself by the ResetResult method; however, that method
            //         will not affect the flags in this local variable.
            //
            if (errorMask)
                result &= ~EngineFlags.ErrorMask;

            return result;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method builds the script cancellation flags that correspond to
        /// the specified engine flags.
        /// </summary>
        /// <param name="engineFlags">
        /// The engine flags to examine.
        /// </param>
        /// <returns>
        /// The cancellation flags corresponding to the specified engine flags.
        /// </returns>
        private static CancelFlags GetCancelFlags(
            EngineFlags engineFlags
            )
        {
            CancelFlags cancelFlags = CancelFlags.Default;

            if (EngineFlagOps.HasResetCancel(engineFlags))
                cancelFlags |= CancelFlags.IgnorePending;

            return cancelFlags | CancelFlags.Engine;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method temporarily enables the stack checking engine flags,
        /// returning the previous engine flags so they may later be restored.
        /// Setting these flags avoids native stack overflows during deeply
        /// nested execution that does not pass through the evaluator.
        /// </summary>
        /// <param name="engineFlags">
        /// The engine flags to augment with the stack checking flags.
        /// </param>
        /// <returns>
        /// The engine flags as they were prior to this call, for use with
        /// <c>RemoveStackCheckFlags</c>.
        /// </returns>
        internal static EngineFlags AddStackCheckFlags(
            ref EngineFlags engineFlags
            )
        {
            //
            // NOTE: The stack checking flags must be temporarily set in order
            //       to avoid native stack overflows in the case of deeply
            //       nested execution that does not go through the evaluator.
            //
            EngineFlags savedEngineFlags = engineFlags;
            engineFlags |= EngineFlags.FullStackMask;

            return savedEngineFlags;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method restores the stack checking engine flags to their
        /// previous state, removing any of those flags that were not set in the
        /// saved engine flags.
        /// </summary>
        /// <param name="savedEngineFlags">
        /// The engine flags captured prior to the matching call to
        /// <c>AddStackCheckFlags</c>.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to restore.
        /// </param>
        internal static void RemoveStackCheckFlags(
            EngineFlags savedEngineFlags,
            ref EngineFlags engineFlags
            )
        {
            //
            // NOTE: Check the individual engine flags that we are responsible
            //       for.  Remove them from the current engine flags if they
            //       were not previously set (i.e. restore the engine flags to
            //       their previous state).
            //
            if (!EngineFlagOps.HasCheckStack(savedEngineFlags))
                engineFlags &= ~EngineFlags.CheckStack;

            if (!EngineFlagOps.HasForceStack(savedEngineFlags))
                engineFlags &= ~EngineFlags.ForceStack;

            if (!EngineFlagOps.HasForcePoolStack(savedEngineFlags))
                engineFlags &= ~EngineFlags.ForcePoolStack;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method derives the engine flags used for command resolution,
        /// enabling all resolution sources when none are specified and
        /// optionally requiring an exact match.
        /// </summary>
        /// <param name="engineFlags">
        /// The engine flags to examine.
        /// </param>
        /// <param name="exact">
        /// Non-zero to require an exact match during resolution.
        /// </param>
        /// <returns>
        /// The engine flags to be used for resolution.
        /// </returns>
        internal static EngineFlags GetResolveFlags(
            EngineFlags engineFlags,
            bool exact
            )
        {
            EngineFlags result = engineFlags;

            if (!EngineFlagOps.HasUseIExecutes(result) &&
                !EngineFlagOps.HasUseCommands(result) &&
                !EngineFlagOps.HasUseProcedures(result))
            {
                //
                // NOTE: If none of these flags are set, use the default
                //       (i.e. set them all).
                //
                result |= EngineFlags.UseAllMask;
            }

            if (exact)
                result |= EngineFlags.ExactMatch;

            return result;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method translates the specified engine flags into the
        /// corresponding readiness check flags.
        /// </summary>
        /// <param name="engineFlags">
        /// The engine flags to examine.
        /// </param>
        /// <returns>
        /// The readiness flags corresponding to the specified engine flags.
        /// </returns>
        internal static ReadyFlags GetReadyFlags(
            EngineFlags engineFlags
            )
        {
            ReadyFlags readyFlags = ReadyFlags.None;

            if (EngineFlagOps.HasCheckStack(engineFlags))
                readyFlags |= ReadyFlags.CheckStack;

            if (EngineFlagOps.HasForceStack(engineFlags))
                readyFlags |= ReadyFlags.ForceStack;

            if (EngineFlagOps.HasForcePoolStack(engineFlags))
                readyFlags |= ReadyFlags.ForcePoolStack;

#if false
            if (EngineFlagOps.HasNoReady(engineFlags))
                readyFlags |= ReadyFlags.Disabled;
#endif

            if (EngineFlagOps.HasNoCancel(engineFlags))
                readyFlags |= ReadyFlags.NoCancel;

            if (EngineFlagOps.HasNoGlobalCancel(engineFlags))
                readyFlags |= ReadyFlags.NoGlobalCancel;

            return readyFlags;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method initializes the engine, substitution, and expression
        /// flags to their default values.
        /// </summary>
        /// <param name="engineFlags">
        /// Upon return, receives the default engine flags.
        /// </param>
        /// <param name="substitutionFlags">
        /// Upon return, receives the default substitution flags.
        /// </param>
        /// <param name="expressionFlags">
        /// Upon return, receives the default expression flags.
        /// </param>
        internal static void InitializeAllFlags(
            out EngineFlags engineFlags,             /* out */
            out SubstitutionFlags substitutionFlags, /* out */
            out ExpressionFlags expressionFlags      /* out */
            )
        {
            EventFlags eventFlags; /* NOT USED */

            InitializeAllFlags(
                out engineFlags, out substitutionFlags,
                out eventFlags, out expressionFlags);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method initializes the engine, substitution, event, and
        /// expression flags to their default values.
        /// </summary>
        /// <param name="engineFlags">
        /// Upon return, receives the default engine flags.
        /// </param>
        /// <param name="substitutionFlags">
        /// Upon return, receives the default substitution flags.
        /// </param>
        /// <param name="eventFlags">
        /// Upon return, receives the default event flags.
        /// </param>
        /// <param name="expressionFlags">
        /// Upon return, receives the default expression flags.
        /// </param>
        private static void InitializeAllFlags(
            out EngineFlags engineFlags,             /* out */
            out SubstitutionFlags substitutionFlags, /* out */
            out EventFlags eventFlags,               /* out */
            out ExpressionFlags expressionFlags      /* out */
            )
        {
            engineFlags = EngineFlags.None;
            substitutionFlags = SubstitutionFlags.Default;
            eventFlags = EventFlags.Default;
            expressionFlags = ExpressionFlags.Default;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method attempts to query the engine, substitution, and
        /// expression flags from the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to query.
        /// </param>
        /// <param name="blocking">
        /// Non-zero to wait for the interpreter lock; otherwise, a non-blocking
        /// lock attempt is made.
        /// </param>
        /// <param name="engineFlags">
        /// Upon success, receives the engine flags from the interpreter.
        /// </param>
        /// <param name="substitutionFlags">
        /// Upon success, receives the substitution flags from the interpreter.
        /// </param>
        /// <param name="expressionFlags">
        /// Upon success, receives the expression flags from the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error.
        /// </param>
        /// <returns>
        /// Non-zero if the flags were successfully queried; otherwise, zero.
        /// </returns>
        internal static bool TryQueryAllFlags(
            Interpreter interpreter,                 /* in */
            bool blocking,                           /* in */
            out EngineFlags engineFlags,             /* out */
            out SubstitutionFlags substitutionFlags, /* out */
            out ExpressionFlags expressionFlags,     /* out */
            ref Result error                         /* out */
            )
        {
            EventFlags eventFlags;

            return TryQueryAllFlags(
                interpreter, blocking, out engineFlags,
                out substitutionFlags, out eventFlags,
                out expressionFlags, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method attempts to query the engine, substitution, event, and
        /// expression flags from the specified interpreter.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to query.
        /// </param>
        /// <param name="blocking">
        /// Non-zero to wait for the interpreter lock; otherwise, a non-blocking
        /// lock attempt is made.
        /// </param>
        /// <param name="engineFlags">
        /// Upon success, receives the engine flags from the interpreter.
        /// </param>
        /// <param name="substitutionFlags">
        /// Upon success, receives the substitution flags from the interpreter.
        /// </param>
        /// <param name="eventFlags">
        /// Upon success, receives the event flags from the interpreter.
        /// </param>
        /// <param name="expressionFlags">
        /// Upon success, receives the expression flags from the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error.
        /// </param>
        /// <returns>
        /// Non-zero if the flags were successfully queried; otherwise, zero.
        /// </returns>
        private static bool TryQueryAllFlags(
            Interpreter interpreter,                 /* in */
            bool blocking,                           /* in */
            out EngineFlags engineFlags,             /* out */
            out SubstitutionFlags substitutionFlags, /* out */
            out EventFlags eventFlags,               /* out */
            out ExpressionFlags expressionFlags,     /* out */
            ref Result error                         /* out */
            )
        {
            InitializeAllFlags(
                out engineFlags, out substitutionFlags,
                out eventFlags, out expressionFlags);

            if (interpreter == null)
            {
                error = "invalid interpreter";
                return false;
            }

            bool locked = false;

            try
            {
                if (blocking)
                {
                    interpreter.InternalLock(
                        ref locked); /* TRANSACTIONAL */
                }
                else
                {
                    interpreter.InternalEngineTryLock(
                        ref locked); /* TRANSACTIONAL */
                }

                if (locked)
                {
                    engineFlags = interpreter.EngineFlagsNoLock;
                    substitutionFlags = interpreter.SubstitutionFlagsNoLock;
                    eventFlags = interpreter.EngineEventFlagsNoLock;
                    expressionFlags = interpreter.ExpressionFlagsNoLock;

                    return true;
                }
                else
                {
                    TraceOps.LockTrace(
                        "TryQueryAllFlags",
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

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method queries the engine, substitution, event, and expression
        /// flags from the specified interpreter and merges them into the
        /// supplied flags.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter to query.
        /// </param>
        /// <param name="blocking">
        /// Non-zero to wait for the interpreter lock; otherwise, a non-blocking
        /// lock attempt is made.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags to augment with those from the interpreter.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags to augment with those from the interpreter.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags to augment with those from the interpreter.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags to augment with those from the interpreter.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error.
        /// </param>
        /// <returns>
        /// Non-zero if the flags were successfully queried and merged;
        /// otherwise, zero.
        /// </returns>
        private static bool TryAugmentAllFlags(
            Interpreter interpreter,                 /* in */
            bool blocking,                           /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref Result error                         /* out */
            )
        {
            EngineFlags localEngineFlags;
            SubstitutionFlags localSubstitutionFlags;
            EventFlags localEventFlags;
            ExpressionFlags localExpressionFlags;

            if (!TryQueryAllFlags(
                    interpreter, blocking, out localEngineFlags,
                    out localSubstitutionFlags, out localEventFlags,
                    out localExpressionFlags, ref error))
            {
                return false;
            }

            engineFlags |= localEngineFlags;
            substitutionFlags |= localSubstitutionFlags;
            eventFlags |= localEventFlags;
            expressionFlags |= localExpressionFlags;

            return true;
        }
        #endregion
    }
}
