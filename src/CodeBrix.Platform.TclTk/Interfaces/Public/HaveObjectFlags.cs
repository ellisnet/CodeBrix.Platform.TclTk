/*
 * HaveObjectFlags.cs --
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

namespace CodeBrix.Platform.TclTk._Interfaces.Public //was previously: Eagle._Interfaces.Public;
{
    /// <summary>
    /// This interface is implemented by entities that carry a set of
    /// <see cref="ObjectFlags" /> describing optional behaviors and state
    /// for an opaque object handle.
    /// </summary>
    [ObjectId("2471d451-241c-4a65-94fd-e483d4bb494a")]
    public interface IHaveObjectFlags
    {
        /// <summary>
        /// Gets or sets the <see cref="ObjectFlags" /> associated with this
        /// entity.
        /// </summary>
        ObjectFlags ObjectFlags { get; set; }
    }
}
