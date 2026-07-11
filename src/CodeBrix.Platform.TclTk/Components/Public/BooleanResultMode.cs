/*
 * BooleanResultMode.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using CodeBrix.Platform.TclTk._Attributes;

namespace CodeBrix.Platform.TclTk._Components.Public //was previously: Eagle._Components.Public;
{
    /// <summary>
    /// Selects how the interpreter renders a boolean RESULT as a STRING — both a
    /// boolean-valued <c>[expr]</c> (e.g. <c>[expr {1 &amp;&amp; 1}]</c>) and the
    /// boolean-returning commands (<c>string equal</c>, <c>info complete</c>,
    /// <c>info default</c>, <c>interp exists</c>, <c>interp issafe</c>,
    /// <c>dict exists</c>, <c>package vsatisfies</c>, <c>eof</c>,
    /// <c>fblocked</c>, …). It affects only the rendered string; boolean
    /// CONTEXTS (<c>if</c>/<c>while</c> conditions, <c>&amp;&amp;</c>/<c>||</c>
    /// short-circuiting, the <c>?:</c> ternary) coerce the value to an actual
    /// boolean and behave identically under either mode.
    /// </summary>
    [ObjectId("6f3c2b41-9d5a-4e77-8c1a-2b0d7f4e9a63")]
    public enum BooleanResultMode
    {
        /// <summary>
        /// The default. A boolean result renders as the .NET-style string
        /// <c>True</c> or <c>False</c> (the historical behavior of this engine).
        /// Boolean-aware operators still coerce these correctly, but code that
        /// treats the result as a literal string (e.g. <c>[switch]</c>,
        /// string-identity comparison, interpolation, or storage) sees
        /// <c>True</c>/<c>False</c>.
        /// </summary>
        EagleCompat = 0,

        /// <summary>
        /// A boolean result — from <c>[expr]</c> or a boolean-returning command
        /// — renders as the canonical Tcl string <c>1</c> or <c>0</c>, matching
        /// real <c>tclsh</c> byte-for-byte. Choose this at interpreter creation
        /// for full stock-Tcl compatibility.
        /// </summary>
        TclshCompat = 1
    }
}
