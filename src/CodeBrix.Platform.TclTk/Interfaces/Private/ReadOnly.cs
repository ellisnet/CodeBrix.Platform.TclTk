/*
 * ReadOnly.cs --
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
    /// This interface is implemented by entities that can report whether they
    /// are currently read-only, i.e. whether their state is allowed to be
    /// modified.
    /// </summary>
    [ObjectId("eb1581f0-2d25-4f61-87af-c8de7982ad5b")]
    internal interface IReadOnly
    {
        /// <summary>
        /// Gets a value indicating whether this entity is read-only.  True if
        /// the entity is read-only and may not be modified; otherwise, false.
        /// </summary>
        bool IsReadOnly { get; }
    }
}
