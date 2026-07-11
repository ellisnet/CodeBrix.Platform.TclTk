using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for <see cref="Interpreter.BooleanResultMode" />: the
/// opt-in switch that renders a boolean-valued <c>[expr]</c> result as the
/// canonical Tcl string <c>1</c>/<c>0</c> (<c>TclshCompat</c>) instead of the
/// engine's default <c>True</c>/<c>False</c> (<c>EagleCompat</c>). Expected
/// values were probed on real tclsh 8.6.16.
/// </summary>
public class BooleanResultModeTests
{
    private static Interpreter Interp(BooleanResultMode mode)
    {
        //The mode is chosen once, at creation — it can never be changed after
        //  (the property is read-only), which is the whole point of the design.
        Result result = null;
        Interpreter interpreter = Interpreter.Create(ref result, mode);
        interpreter.Should().NotBeNull(
            "interpreter creation should succeed; failure result: " +
            (result == null ? "<null>" : result.ToString()));
        return interpreter;
    }

    [Fact]
    public void Default_mode_is_EagleCompat()
    {
        //The compatMode parameter defaults to EagleCompat. Create directly
        //  (not via the shared helper, which honors TCLTK_TEST_BOOLEAN_MODE)
        //  so this asserts the genuine library default regardless of any
        //  diagnostic env var forcing the rest of the suite into TclshCompat.
        Result result = null;
        using Interpreter interpreter = Interpreter.Create(ref result);
        interpreter.Should().NotBeNull();
        interpreter.BooleanResultMode
            .Should().Be(BooleanResultMode.EagleCompat);
    }

    [Fact]
    public void Mode_is_fixed_at_creation_and_reported_by_the_property()
    {
        using (Interpreter tcl = Interp(BooleanResultMode.TclshCompat))
        {
            tcl.BooleanResultMode
                .Should().Be(BooleanResultMode.TclshCompat);
        }
        using (Interpreter eagle = Interp(BooleanResultMode.EagleCompat))
        {
            eagle.BooleanResultMode
                .Should().Be(BooleanResultMode.EagleCompat);
        }
    }

    [Theory]
    [InlineData("expr {1 && 1}", "True")]
    [InlineData("expr {2 > 1}", "True")]
    [InlineData("expr {5 == 5}", "True")]
    [InlineData("expr {1 < 0}", "False")]
    [InlineData("expr {1 || 0}", "True")]
    public void EagleCompat_renders_booleans_as_True_False(string script, string expected)
    {
        using Interpreter interpreter = Interp(BooleanResultMode.EagleCompat);
        TclTkTest.Eval(interpreter, script).Should().Be(expected);
    }

    [Theory]
    [InlineData("expr {1 && 1}", "1")]
    [InlineData("expr {2 > 1}", "1")]
    [InlineData("expr {5 == 5}", "1")]
    [InlineData("expr {1 < 0}", "0")]
    [InlineData("expr {1 || 0}", "1")]
    public void TclshCompat_renders_booleans_as_1_0(string script, string expected)
    {
        //Matches real tclsh 8.6.16 byte-for-byte.
        using Interpreter interpreter = Interp(BooleanResultMode.TclshCompat);
        TclTkTest.Eval(interpreter, script).Should().Be(expected);
    }

    [Fact]
    public void TclshCompat_makes_stored_expr_results_string_faithful()
    {
        //The §21.4 concern: DRAKON stores an [expr] result and later treats it
        //  as a literal string. Because TclshCompat makes the STORED value "1"
        //  (not "True"), everything downstream of that value matches stock
        //  tclsh: an expr string-identity test, switch dispatch, length, and
        //  interpolation/output.
        using Interpreter interpreter = Interp(BooleanResultMode.TclshCompat);

        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; expr {$x eq \"1\"}").Should().Be("1");
        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; switch -- $x {1 {set _ one} 0 {set _ zero} default {set _ other}}")
            .Should().Be("one");
        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; string length $x").Should().Be("1");
        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; return \"count=$x\"").Should().Be("count=1");
    }


    [Fact]
    public void EagleCompat_leaves_stored_expr_results_as_True_False()
    {
        //The historical behavior is preserved when the mode is off.
        using Interpreter interpreter = Interp(BooleanResultMode.EagleCompat);

        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; return \"count=$x\"").Should().Be("count=True");
        TclTkTest.Eval(interpreter,
            "set x [expr {2 > 1}]; string length $x").Should().Be("4");
    }

    [Theory]
    [InlineData(BooleanResultMode.EagleCompat)]
    [InlineData(BooleanResultMode.TclshCompat)]
    public void Boolean_contexts_are_unaffected_by_the_mode(BooleanResultMode mode)
    {
        //if/while/&&/||/ternary consume the boolean as a VALUE, not a string,
        //  so both modes behave identically — the scope guarantee.
        using Interpreter interpreter = Interp(mode);

        TclTkTest.Eval(interpreter,
            "if {[expr {2 > 1}]} {return yes} else {return no}").Should().Be("yes");
        TclTkTest.Eval(interpreter,
            "if {2 > 1} {return yes} else {return no}").Should().Be("yes");
        TclTkTest.Eval(interpreter,
            "set n 0; while {$n < 3} {incr n}; set n").Should().Be("3");
        TclTkTest.Eval(interpreter,
            "expr {1 && 1 ? 5 : 9}").Should().Be("5");
    }

    [Theory]
    // Standard Tcl commands that return a boolean. In TclshCompat they render
    // the canonical "1"/"0" (probed on real tclsh 8.6.16); the mode covers
    // commands, not just [expr].
    [InlineData("string equal a a", "1")]
    [InlineData("string equal a b", "0")]
    [InlineData("info complete {set x}", "1")]
    [InlineData("interp exists {}", "1")]
    [InlineData("interp exists nope", "0")]
    [InlineData("interp issafe", "0")]
    [InlineData("dict exists {a 1 b 2} a", "1")]
    [InlineData("dict exists {a 1 b 2} z", "0")]
    [InlineData("package vsatisfies 8.6 8.5", "1")]
    [InlineData("package vsatisfies 8.6 9.0", "0")]
    [InlineData("eof stdin", "0")]
    public void TclshCompat_normalizes_boolean_returning_commands(string script, string expected)
    {
        using Interpreter interpreter = Interp(BooleanResultMode.TclshCompat);
        TclTkTest.Eval(interpreter, script).Should().Be(expected);
    }

    [Theory]
    // The DEFAULT (EagleCompat) rendering for those same commands is unchanged
    // -- historical "True"/"False" -- so nothing existing (DRAKON, etc.) shifts.
    [InlineData("string equal a a", "True")]
    [InlineData("string equal a b", "False")]
    [InlineData("info complete {set x}", "True")]
    [InlineData("interp exists {}", "True")]
    [InlineData("dict exists {a 1 b 2} a", "True")]
    [InlineData("package vsatisfies 8.6 8.5", "True")]
    [InlineData("eof stdin", "False")]
    public void EagleCompat_leaves_boolean_returning_commands_as_True_False(
        string script, string expected)
    {
        using Interpreter interpreter = Interp(BooleanResultMode.EagleCompat);
        TclTkTest.Eval(interpreter, script).Should().Be(expected);
    }

    [Fact]
    public void TclshCompat_string_equal_result_is_now_usable_as_a_literal()
    {
        //The boundary that motivated covering commands: [string equal] used to
        //  render "True", so [string equal $x 1] and downstream string uses
        //  diverged. Now, in TclshCompat, it is the canonical "1".
        using Interpreter interpreter = Interp(BooleanResultMode.TclshCompat);

        TclTkTest.Eval(interpreter, "set x [expr {2 > 1}]; string equal $x 1").Should().Be("1");
        TclTkTest.Eval(interpreter,
            "switch -- [string equal a a] {1 {set _ yes} 0 {set _ no}}").Should().Be("yes");
    }

    [Fact]
    public void Non_boolean_expr_results_are_unchanged_in_both_modes()
    {
        //The mode only touches a boolean final result; numbers/strings are
        //  identical either way.
        using (Interpreter eagle = Interp(BooleanResultMode.EagleCompat))
        {
            TclTkTest.Eval(eagle, "expr {3 + 4}").Should().Be("7");
            TclTkTest.Eval(eagle, "expr {10 / 4.0}").Should().Be("2.5");
        }
        using (Interpreter tcl = Interp(BooleanResultMode.TclshCompat))
        {
            TclTkTest.Eval(tcl, "expr {3 + 4}").Should().Be("7");
            TclTkTest.Eval(tcl, "expr {10 / 4.0}").Should().Be("2.5");
        }
    }
}
