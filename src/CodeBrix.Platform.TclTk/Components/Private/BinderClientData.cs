/*
 * BinderClientData.cs --
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
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Components.Private //was previously: Eagle._Components.Private;
{
    /// <summary>
    /// This class stores the extra state passed along to the TclTk reflection
    /// binder, namely the set of options that are in effect together with any
    /// caller-supplied client data.
    /// </summary>
    [ObjectId("df8adcbd-138a-4045-9a34-806af8bc3aae")]
    internal sealed class BinderClientData : ClientData, IHaveClientData
    {
        #region Private Constructors
        /// <summary>
        /// Constructs an instance of this class wrapping the specified data.
        /// </summary>
        /// <param name="data">
        /// The opaque, application-specific data to associate with this
        /// instance.  This parameter may be null.
        /// </param>
        private BinderClientData(
            object data
            )
            : base(data)
        {
            // do nothing.
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Constructors
        /// <summary>
        /// Constructs an instance of this class wrapping the specified data,
        /// options, and client data.
        /// </summary>
        /// <param name="data">
        /// The opaque, application-specific data to associate with this
        /// instance.  This parameter may be null.
        /// </param>
        /// <param name="options">
        /// The dictionary of options that are in effect.  This parameter may
        /// be null.
        /// </param>
        /// <param name="clientData">
        /// The caller-supplied client data to associate with this instance.
        /// This parameter may be null.
        /// </param>
        public BinderClientData(
            object data,
            OptionDictionary options,
            IClientData clientData
            )
            : this(data)
        {
            this.options = options;
            this.clientData = clientData;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region Public Properties
        /// <summary>
        /// Stores the dictionary of options that are in effect.
        /// </summary>
        private OptionDictionary options;
        /// <summary>
        /// Gets or sets the dictionary of options that are in effect.
        /// </summary>
        public OptionDictionary Options
        {
            get { return options; }
            set { options = value; }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////

        #region IGetClientData / ISetClientData Members
        /// <summary>
        /// Stores the caller-supplied client data associated with this
        /// instance.
        /// </summary>
        private IClientData clientData;
        /// <summary>
        /// Gets or sets the caller-supplied client data associated with this
        /// instance.
        /// </summary>
        public IClientData ClientData
        {
            get { return clientData; }
            set { clientData = value; }
        }
        #endregion
    }
}
