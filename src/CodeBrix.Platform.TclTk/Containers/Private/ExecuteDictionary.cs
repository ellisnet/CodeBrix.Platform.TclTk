/*
 * ExecuteDictionary.cs --
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
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    string, CodeBrix.Platform.TclTk._Interfaces.Public.IExecute>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    string, CodeBrix.Platform.TclTk._Interfaces.Public.IExecute>;
#endif

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps string keys to executable
    /// entity values.  It extends the underlying generic dictionary of
    /// <see cref="IExecute" /> objects with a helper for producing a filtered
    /// string form of its keys.
    /// </summary>
    [ObjectId("0d1fbbf8-b4e1-4d7f-af3b-b4e08f085ac3")]
    internal sealed class ExecuteDictionary : SomeDictionary
    {
        /// <summary>
        /// Constructs an empty execute dictionary.
        /// </summary>
        public ExecuteDictionary()
            : base()
        {
            // do nothing.
        }

        ///////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method produces a string containing the keys of the dictionary
        /// that match the specified pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern used to select which keys are included.  This parameter
        /// may be null to include all keys.
        /// </param>
        /// <param name="noCase">
        /// Non-zero if pattern matching should be case-insensitive.
        /// </param>
        /// <returns>
        /// The list of matching keys formatted as a string.
        /// </returns>
        public string ToString(string pattern, bool noCase)
        {
            StringList list = new StringList(this.Keys);

            return ParserOps<string>.ListToString(
                list, Index.Invalid, Index.Invalid, ToStringFlags.None,
                Characters.SpaceString, pattern, noCase);
        }

        ///////////////////////////////////////////////////////////////////////

        #region System.Object Overrides
        /// <summary>
        /// This method produces a string containing all the keys of the
        /// dictionary.
        /// </summary>
        /// <returns>
        /// The list of keys formatted as a string.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, false);
        }
        #endregion
    }
}
