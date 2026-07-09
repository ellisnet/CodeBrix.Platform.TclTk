/*
 * CallbackQueue.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using System.Collections.Generic;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a first-in, first-out queue of command callback
    /// objects.
    /// </summary>
    [ObjectId("376abeb8-61de-44d6-9a86-b4bdc8c8c48a")]
    internal sealed class CallbackQueue : Queue<CommandCallback>
    {
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public CallbackQueue()
            : base()
        {
            // do nothing.
        }
    }
}
