/*
 * NewHostCallback.cs --
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

namespace CodeBrix.Platform.TclTk._Interfaces.Public //was previously: Eagle._Interfaces.Public;
{
    /// <summary>
    /// This interface is implemented by an entity that is able to create a new
    /// interpreter host on demand.  It provides the single factory method,
    /// <see cref="NewHost" />, used to construct a host from its host data.
    /// </summary>
    [ObjectId("54578e21-6a4c-440a-8435-61efb9ec2a06")]
    public interface INewHostCallback
    {
        /// <summary>
        /// Creates a new interpreter host using the specified host data.
        /// </summary>
        /// <param name="hostData">
        /// The data used to configure the newly created host.  This parameter
        /// may be null.
        /// </param>
        /// <returns>
        /// The newly created host, or null if one could not be created.
        /// </returns>
        IHost NewHost(IHostData hostData);
    }
}
