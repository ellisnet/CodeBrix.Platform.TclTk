/*
 * LogicalXor.cs --
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
    /// This class implements the TclTk <c>^^</c> (logical exclusive-or)
    /// expression operator, which evaluates to a boolean that is true when
    /// exactly one of its two boolean operands is true.  The evaluation itself
    /// is provided by the <see cref="Logic" /> base class, selected by the
    /// <see cref="Lexeme.LogicalXor" /> lexeme.  See <c>core_language.md</c>
    /// for expression and operator semantics.
    /// </summary>
    [ObjectId("403adc7b-f116-4ad2-b5ec-c35307664f1b")]
    [OperatorFlags(
        OperatorFlags.NonStandard | OperatorFlags.Logical)]
    [Lexeme(Lexeme.LogicalXor)]
    [Operands(Arity.Binary)]
    [TypeListFlags(TypeListFlags.NumberTypes)]
    [ObjectGroup("logical")]
    [ObjectName(Operators.LogicalXor)]
    internal sealed class LogicalXor : Logic
    {
        #region Public Constructors
        /// <summary>
        /// Constructs an instance of the <c>^^</c> logical exclusive-or
        /// operator.
        /// </summary>
        /// <param name="operatorData">
        /// The data used to create and identify this operator, such as its
        /// name and flags.  This parameter may be null.
        /// </param>
        public LogicalXor(
            IOperatorData operatorData /* in */
            )
            : base(operatorData)
        {
            // do nothing.
        }
        #endregion
    }
}
