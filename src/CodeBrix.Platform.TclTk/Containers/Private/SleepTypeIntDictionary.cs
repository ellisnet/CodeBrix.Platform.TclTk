/*
 * SleepTypeIntDictionary.cs --
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

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    CodeBrix.Platform.TclTk._Components.Public.SleepType, int>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    CodeBrix.Platform.TclTk._Components.Public.SleepType, int>;
#endif

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps sleep type values
    /// (<see cref="SleepType" />) to their associated integer values.
    /// </summary>
    [ObjectId("29f6c8d2-8288-4447-88da-730f3a8f927d")]
    internal sealed class SleepTypeIntDictionary : SomeDictionary
    {
        #region Public Constructors
        /// <summary>
        /// Constructs an empty sleep type dictionary.
        /// </summary>
        public SleepTypeIntDictionary()
            : base()
        {
            // do nothing.
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Methods
        /// <summary>
        /// This method produces a string containing the keys of the dictionary
        /// that match the specified pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern used to filter the keys that are included in the result.
        /// This parameter may be null, in which case all keys are included.
        /// </param>
        /// <param name="noCase">
        /// Non-zero if the pattern matching should be performed in a
        /// case-insensitive manner.
        /// </param>
        /// <returns>
        /// The list of matching keys formatted as a string.
        /// </returns>
        public string ToString(
            string pattern,
            bool noCase
            )
        {
            IList<SleepType> list = new List<SleepType>(this.Keys);

            return ParserOps<SleepType>.ListToString(
                list, Index.Invalid, Index.Invalid, ToStringFlags.None,
                Characters.SpaceString, pattern, noCase);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region System.Object Overrides
        /// <summary>
        /// This method produces a string containing all of the keys of the
        /// dictionary.
        /// </summary>
        /// <returns>
        /// The keys of the dictionary formatted as a string.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, false);
        }
        #endregion
    }
}
