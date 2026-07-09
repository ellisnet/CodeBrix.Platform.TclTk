/*
 * CoreEncoding.cs --
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

using System.Text;
using CodeBrix.Platform.TclTk._Attributes;

namespace CodeBrix.Platform.TclTk._Encodings //was previously: Eagle._Encodings;
{
    /// <summary>
    /// This class serves as the abstract base for the custom text encodings
    /// provided by TclTk, extending the standard
    /// <see cref="Encoding" /> class with a common, TclTk-specific
    /// registered (IANA) name.
    /// </summary>
#if SERIALIZATION
    [Serializable()]
#endif
    [ObjectId("9113b6ac-63fe-4d92-a0e9-3722e2059bc3")]
    public abstract class CoreEncoding : Encoding
    {
        #region Private Constants
        /// <summary>
        /// The registered (IANA) name reported for this encoding.
        /// </summary>
        private static readonly string webName = "Core";
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region System.Text.Encoding Overrides
        /// <summary>
        /// Gets the registered (IANA) name for this encoding.
        /// </summary>
        public override string WebName
        {
            get { return webName; }
        }
        #endregion
    }
}
