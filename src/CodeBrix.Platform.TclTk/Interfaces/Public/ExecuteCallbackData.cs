/*
 * ExecuteCallbackData.cs --
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
    /// This interface represents the data associated with a dynamic execute
    /// callback.  It is an aggregate that composes the identifier naming
    /// (<see cref="IIdentifierName" />), wrapper metadata
    /// (<see cref="IWrapperData" />), associated client data
    /// (<see cref="IHaveClientData" />), and the dynamic-execute callback
    /// itself (<see cref="IDynamicExecuteCallback" />).
    /// </summary>
    [ObjectId("4fe571c6-48a0-4ddf-bf28-dd0892b930ea")]
    public interface IExecuteCallbackData : IIdentifierName, IWrapperData, IHaveClientData, IDynamicExecuteCallback
    {
        // nothing.
    }
}
