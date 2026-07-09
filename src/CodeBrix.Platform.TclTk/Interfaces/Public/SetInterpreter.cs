/*
 * SetInterpreter.cs --
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
    /// This interface is implemented by entities that allow their associated
    /// interpreter to be set after they have been created.
    /// </summary>
    [ObjectId("07bf3e68-f141-4ebb-9e2a-14ca2a8f722b")]
    public interface ISetInterpreter
    {
        /// <summary>
        /// Sets the interpreter context associated with this object.  This
        /// value may be null.
        /// </summary>
        //
        // TODO: Change this to use the IInterpreter type.
        //
        Interpreter Interpreter { set; }
    }
}
