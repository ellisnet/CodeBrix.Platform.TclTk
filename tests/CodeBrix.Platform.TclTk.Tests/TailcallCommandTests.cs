using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the ported <c>tailcall</c> command. Every expected value in
/// this file was captured from real tclsh 8.6.16 (the development oracle):
/// a tailcall is recorded on the procedure frame, fires only when the
/// procedure returns normally (even through an intercepting <c>catch</c>),
/// resolves its target in the current namespace, executes at the caller's
/// level, and chains without growing the stack.
/// </summary>
public class TailcallCommandTests
{
    [Fact]
    public void Tailcall_replaces_the_procedure_and_skips_following_code()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { tailcall lappend ::log A; lappend ::log NEVER };" +
            " list [t] $::log").Should().Be("A A");

    [Fact]
    public void Tailcall_result_becomes_the_procedure_result()
        => TclTkTest.EvalOnce(
            "proc t {} { tailcall string toupper abc }; t").Should().Be("ABC");

    [Fact]
    public void Tailcall_bare_returns_empty()
        => TclTkTest.EvalOnce("proc t {} { tailcall }; t").Should().Be("");

    [Fact]
    public void Tailcall_bare_when_caught_lets_the_procedure_continue()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { catch {tailcall}; lappend ::log AFTER; return RR };" +
            " list [t] $::log").Should().Be("RR AFTER");

    [Fact]
    public void Tailcall_deep_self_recursion_does_not_grow_the_stack()
        => TclTkTest.EvalOnce(
            "proc count {n} { if {$n == 0} { return done };" +
            " tailcall count [expr {$n - 1}] }; count 200000")
            .Should().Be("done");

    [Fact]
    public void Tailcall_deep_mutual_recursion_does_not_grow_the_stack()
        => TclTkTest.EvalOnce(
            "proc even? {n} { if {$n == 0} { return yes };" +
            " tailcall odd? [expr {$n - 1}] };" +
            " proc odd? {n} { if {$n == 0} { return no };" +
            " tailcall even? [expr {$n - 1}] }; even? 100001")
            .Should().Be("no");

    [Fact]
    public void Tailcall_executes_at_the_callers_level()
        => TclTkTest.EvalOnce(
            "proc lvl {} { tailcall info level }; proc wrap {} { lvl };" +
            " list [info level] [wrap]").Should().Be("0 1");

    [Fact]
    public void Tailcall_target_upvar_sees_the_original_caller()
        => TclTkTest.EvalOnce(
            "proc inner {} { upvar 1 v v; set v TOUCHED };" +
            " proc outer {} { set v ORIG; tailcall inner };" +
            " set v CALLER; outer; set v").Should().Be("TOUCHED");

    [Fact]
    public void Tailcall_caught_by_catch_still_fires_on_normal_return()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { set r [catch {tailcall lappend ::log C} m];" +
            " lappend ::log caught:$r:$m; return RET }; list [t] $::log")
            .Should().Be("{caught:2: C} {caught:2: C}");

    [Fact]
    public void Tailcall_error_return_discards_the_pending_tailcall()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { catch {tailcall lappend ::log FIRED}; error BOOM };" +
            " list [catch {t} m] $m $::log").Should().Be("1 BOOM {}");

    [Fact]
    public void Tailcall_break_return_discards_the_pending_tailcall()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { catch {tailcall lappend ::log FIRED};" +
            " return -code break }; list [catch {t} m] $m $::log")
            .Should().Be("3 {} {}");

    [Fact]
    public void Tailcall_second_call_replaces_the_first()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { catch {tailcall lappend ::log ONE};" +
            " tailcall lappend ::log TWO }; list [t] $::log")
            .Should().Be("TWO TWO");

    [Fact]
    public void Tailcall_works_inside_eval()
        => TclTkTest.EvalOnce(
            "proc t {} { eval {tailcall string toupper ev} }; t")
            .Should().Be("EV");

    [Fact]
    public void Tailcall_works_inside_a_while_body()
        => TclTkTest.EvalOnce(
            "set ::log {};" +
            " proc t {} { while 1 { tailcall lappend ::log W };" +
            " lappend ::log NEVER }; list [t] $::log").Should().Be("W W");

    [Fact]
    public void Tailcall_resolves_the_target_in_the_current_namespace()
        => TclTkTest.EvalOnce(
            "namespace eval myns { proc helper {} { return NS-HELPER };" +
            " proc go {} { tailcall helper } }; myns::go")
            .Should().Be("NS-HELPER");

    [Fact]
    public void Tailcall_works_from_a_lambda()
        => TclTkTest.EvalOnce(
            "apply {{} { tailcall string toupper lam }}").Should().Be("LAM");

    [Fact]
    public void Tailcall_chains_through_procedures()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc a {} { tailcall b };" +
            " proc b {} { lappend ::log IND; return R2 }; list [a] $::log")
            .Should().Be("R2 IND");

    [Fact]
    public void Tailcall_propagates_target_errors()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "proc t {} { tailcall error BOOM }");

        //Act
        (ReturnCode code, string value) = TclTkTest.TryEval(interpreter, "t");

        //Assert
        code.Should().Be(ReturnCode.Error);
        value.Should().Be("BOOM");
    }

    [Theory]
    [InlineData("tailcall set x 1")]
    [InlineData("proc t {} { uplevel 1 {tailcall set x 1} }; t")]
    public void Tailcall_outside_a_procedure_is_an_error(string script)
        => TclTkTest.EvalOnceError(script)
            .Should().Be("tailcall can only be called from a proc, lambda or method");

    [Fact]
    public void Tailcall_to_a_missing_command_is_an_error()
        => TclTkTest.EvalOnceError(
            "proc t {} { tailcall no_such_cmd_xyz }; t")
            .Should().Be("invalid command name \"no_such_cmd_xyz\"");
}
