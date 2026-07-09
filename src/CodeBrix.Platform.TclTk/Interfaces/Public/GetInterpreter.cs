/*
 * GetInterpreter.cs --
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
    /// This interface is implemented by entities that expose read-only access
    /// to the interpreter they are associated with.
    /// </summary>
    [ObjectId("f1818462-b697-4504-bb49-cfe3c2dd6c68")]
    public interface IGetInterpreter
    {
        /// <summary>
        /// Gets the interpreter context associated with this entity.  This
        /// value may be null.
        /// </summary>
        //
        // TODO: Change this to use the IInterpreter type.
        //
        Interpreter Interpreter { get; }
    }
}
