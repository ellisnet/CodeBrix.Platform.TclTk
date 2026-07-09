/*
 * TypeDelegateDictionary.cs --
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
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Public;

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    System.Type, System.Delegate>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    System.Type, System.Delegate>;
#endif

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps types to the delegates
    /// associated with those types.
    /// </summary>
    [ObjectId("cd380ed7-7485-4daa-a212-3b11e35e3cf4")]
    internal sealed class TypeDelegateDictionary : SomeDictionary
    {
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public TypeDelegateDictionary()
            : base()
        {
            // do nothing.
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method builds a string representation of the type names in this
        /// dictionary, optionally filtered by a match pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern used to filter the type names.  This parameter may be
        /// null to include all names.
        /// </param>
        /// <param name="noCase">
        /// Non-zero to perform case-insensitive pattern matching.
        /// </param>
        /// <returns>
        /// The string representation of the (optionally filtered) type names in
        /// this dictionary.
        /// </returns>
        public string ToString(string pattern, bool noCase)
        {
            StringList list = new StringList(this.Keys);

            return ParserOps<string>.ListToString(list, Index.Invalid, Index.Invalid,
                ToStringFlags.None, Characters.SpaceString, pattern, noCase);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region System.Object Overrides
        /// <summary>
        /// This method builds a string representation of the type names in this
        /// dictionary.
        /// </summary>
        /// <returns>
        /// The string representation of the type names in this dictionary.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, false);
        }
        #endregion
    }
}
