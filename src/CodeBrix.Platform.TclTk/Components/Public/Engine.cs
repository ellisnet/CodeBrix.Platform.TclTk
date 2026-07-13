/*
 * Engine.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
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

///////////////////////////////////////////////////////////////////////////////////////////////
// *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING*
//
// Please do not add any non-static members to this class.  It is not allowed to maintain any
// kind of state information because all script state information is stored in the Interpreter
// object(s).
//
// *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING* *WARNING*
///////////////////////////////////////////////////////////////////////////////////////////////

namespace CodeBrix.Platform.TclTk._Components.Public //was previously: Eagle._Components.Public;
{
    /// <summary>
    /// This static class is TclTk's central execution engine; essentially all
    /// script execution flows through it.  It evaluates scripts, files,
    /// expressions, and streams; reads and parses script text; performs
    /// substitution; dispatches commands, procedures, functions, and
    /// operators; and manages cancellation, halting, error and exception
    /// information, debugging breakpoints and watchpoints, threading, and the
    /// asynchronous callback queue.  It is a pure static utility -- it has no
    /// instances and holds no per-interpreter state; the <see cref="Interpreter" />
    /// holds that state and delegates the actual work to this class.  Most
    /// consumers should call the equivalent <see cref="Interpreter" /> methods
    /// (for example, the interpreter's own script-evaluation methods) rather
    /// than calling this class directly, since those manage interpreter state
    /// on the caller's behalf.  See <c>engine_vs_interpreter.md</c> for
    /// guidance on choosing between the two.
    /// </summary>
    [ObjectId("204a6f65-204d-6973-7461-63686b696e20")]
    public static partial class Engine /* unique */
    {
    }
}
