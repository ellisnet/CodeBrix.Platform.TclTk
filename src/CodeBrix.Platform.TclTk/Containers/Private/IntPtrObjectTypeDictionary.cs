/*
 * IntPtrObjectTypeDictionary.cs --
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
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Interfaces.Public;

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    System.IntPtr, CodeBrix.Platform.TclTk._Interfaces.Public.IObjectType>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    System.IntPtr, CodeBrix.Platform.TclTk._Interfaces.Public.IObjectType>;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps native pointer values to
    /// instances of objects that implement the <see cref="IObjectType" />
    /// interface.
    /// </summary>
    [ObjectId("8442b84d-f17b-44e2-8a78-ae1ec7caed74")]
    internal sealed class IntPtrObjectTypeDictionary : SomeDictionary
    {
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public IntPtrObjectTypeDictionary()
            : base()
        {
            // do nothing.
        }
    }
}
