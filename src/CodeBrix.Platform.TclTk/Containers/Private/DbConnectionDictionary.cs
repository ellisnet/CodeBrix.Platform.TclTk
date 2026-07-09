/*
 * DbConnectionDictionary.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using System.Collections.Generic;
using System.Data;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Public;

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    string, System.Data.IDbConnection>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    string, System.Data.IDbConnection>;
#endif

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps string names to open
    /// database connections.  It extends the underlying generic dictionary with
    /// conversion of its keys to the TclTk string list format, including
    /// optional pattern matching.
    /// </summary>
    [ObjectId("4539b54e-1800-40ca-aa07-8048857f8d36")]
    internal sealed class DbConnectionDictionary : SomeDictionary
    {
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public DbConnectionDictionary()
            : base()
        {
            // do nothing.
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Converts the keys of this dictionary to a string in the TclTk list
        /// format, optionally including only those keys matching the specified
        /// pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern that each key must match in order to be included in the
        /// resulting string.  This parameter may be null, in which case all keys
        /// are included.
        /// </param>
        /// <param name="noCase">
        /// Non-zero if pattern matching should be case-insensitive.
        /// </param>
        /// <returns>
        /// The string representation of the keys of this dictionary.
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
        /// Converts the keys of this dictionary to a string in the TclTk list
        /// format.
        /// </summary>
        /// <returns>
        /// The string representation of the keys of this dictionary.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, false);
        }
        #endregion
    }
}
