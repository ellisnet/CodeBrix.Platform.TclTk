/*
 * ReturnCodeDictionary.cs --
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

#if SERIALIZATION
using System.Runtime.Serialization;
#endif

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Public;

#if FAST_DICTIONARY
using SomeDictionary = CodeBrix.Platform.TclTk._Containers.Public.FastDictionary<
    CodeBrix.Platform.TclTk._Components.Public.ReturnCode, string>;
#else
using SomeDictionary = System.Collections.Generic.Dictionary<
    CodeBrix.Platform.TclTk._Components.Public.ReturnCode, string>;
#endif

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Containers.Private //was previously: Eagle._Containers.Private;
{
    /// <summary>
    /// This class represents a dictionary that maps
    /// <see cref="ReturnCode" /> values to their associated strings.  It
    /// extends the underlying generic dictionary with a helper for producing a
    /// filtered string form of its keys.
    /// </summary>
#if SERIALIZATION
    [Serializable()]
#endif
    [ObjectId("163bbc1c-f0c4-43a9-ad0e-cbb29f067575")]
    internal sealed class ReturnCodeDictionary : SomeDictionary
    {
        #region Public Constructors
        /// <summary>
        /// Constructs an empty instance of this class.
        /// </summary>
        public ReturnCodeDictionary()
            : base()
        {
            // do nothing.
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Protected Constructors
#if SERIALIZATION
        /// <summary>
        /// Constructs an instance of this class from previously serialized data.
        /// This constructor is used during deserialization.
        /// </summary>
        /// <param name="info">
        /// The object that holds the serialized data for the dictionary.
        /// </param>
        /// <param name="context">
        /// The streaming context that describes the source of the serialized
        /// data.
        /// </param>
        private ReturnCodeDictionary(
            SerializationInfo info,
            StreamingContext context
            )
            : base(info, context)
        {
            // do nothing.
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region ToString Methods
        /// <summary>
        /// This method produces a string containing the keys of the dictionary
        /// that match the specified pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern used to filter the keys that are included in the result.
        /// This parameter may be null, in which case all keys are included.
        /// </param>
        /// <param name="noCase">
        /// Non-zero if pattern matching should be case-insensitive.
        /// </param>
        /// <returns>
        /// The matching keys formatted as a string.
        /// </returns>
        public string ToString(
            string pattern,
            bool noCase
            )
        {
            ReturnCodeList list = new ReturnCodeList(this.Keys);

            return ParserOps<ReturnCode>.ListToString(
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
