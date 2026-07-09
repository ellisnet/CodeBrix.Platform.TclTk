/*
 * NotEqual.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Interfaces.Private;

namespace CodeBrix.Platform.TclTk._Operators //was previously: Eagle._Operators;
{
    /// <summary>
    /// This class implements the TclTk <c>!=</c> (not-equal) expression
    /// operator, which compares its two operands and yields a true result
    /// when they are not equal.  The evaluation itself is provided by the
    /// <see cref="MaybeString" /> base class, selected by the
    /// <see cref="Lexeme.NotEqual" /> lexeme; operands are compared either
    /// numerically or as strings depending on their types.  See
    /// <c>core_language.md</c> for expression and operator semantics.
    /// </summary>
    [ObjectId("ab93e922-b5e8-48b2-a5ec-6d576dac8ba8")]
    [OperatorFlags(
        OperatorFlags.Standard | OperatorFlags.Relational)]
    [Lexeme(Lexeme.NotEqual)]
    [Operands(Arity.Binary)]
    [TypeListFlags(TypeListFlags.AllTypes)]
    [ObjectGroup("inequation")]
    [ObjectName(Operators.NotEqual)]
    internal sealed class NotEqual : MaybeString
    {
        #region Public Constructors
        /// <summary>
        /// Constructs an instance of the <c>!=</c> not-equal operator.
        /// </summary>
        /// <param name="operatorData">
        /// The data used to create and identify this operator, such as its
        /// name and flags.  This parameter may be null.
        /// </param>
        public NotEqual(
            IOperatorData operatorData /* in */
            )
            : base(operatorData)
        {
            // do nothing.
        }
        #endregion
    }
}
