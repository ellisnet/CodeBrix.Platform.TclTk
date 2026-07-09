/*
 * ComplaintList.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

#if SERIALIZATION
using System;
#endif

using System.Collections.Generic;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Interfaces.Public;

using ComplaintTriplet = CodeBrix.Platform.TclTk._Components.Public.AnyTriplet<
    long, long, CodeBrix.Platform.TclTk._Components.Public.Result>;

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a list of complaints, where each element is a
    /// triplet that pairs a pair of long integer values with the associated
    /// result.  It extends the standard generic list with conversion to the
    /// TclTk string list format, including optional pattern matching.
    /// </summary>
#if SERIALIZATION
    [Serializable()]
#endif
    [ObjectId("b553a263-7efc-4cc3-975c-f852d65c3235")]
    internal sealed class ComplaintList : List<ComplaintTriplet>
    {
        #region Public Constructors
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public ComplaintList()
            : base()
        {
            // do nothing.
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ToString Methods
        /// <summary>
        /// Converts this list to a string in the TclTk list format, optionally
        /// including only those elements matching the specified pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern that each element must match in order to be included in
        /// the resulting string.  This parameter may be null, in which case all
        /// elements are included.
        /// </param>
        /// <param name="noCase">
        /// Non-zero if pattern matching should be case-insensitive.
        /// </param>
        /// <returns>
        /// The string representation of this list.
        /// </returns>
        public string ToString(
            string pattern,
            bool noCase
            )
        {
            return ParserOps<ComplaintTriplet>.ListToString(
                this, Index.Invalid, Index.Invalid, ToStringFlags.None,
                Characters.SpaceString, pattern, noCase);
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region System.Object Overrides
        /// <summary>
        /// Converts this list to a string in the TclTk list format.
        /// </summary>
        /// <returns>
        /// The string representation of this list.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, false);
        }
        #endregion
    }
}
