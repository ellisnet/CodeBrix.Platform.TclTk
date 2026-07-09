/*
 * ViaScript.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Public;

namespace CodeBrix.Platform.TclTk._Interfaces.Private //was previously: Eagle._Interfaces.Private;
{
    /// <summary>
    /// This interface is implemented by entities that need to indicate whether
    /// they originated from, or are being driven by, a script (as opposed to
    /// being created or invoked directly from native code).
    /// </summary>
    [ObjectId("8ec9cf8f-7237-4a5a-b73b-2c1c32cda6e2")]
    internal interface IViaScript
    {
        /// <summary>
        /// Gets a value indicating whether this entity originated from, or is
        /// being driven by, a script.
        /// </summary>
        bool IsViaScript { get; }
    }
}
