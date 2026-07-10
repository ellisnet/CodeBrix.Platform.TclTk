using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the ported <c>trace</c> command. Every expected value in this
/// file was captured from real tclsh 8.6.16 (the development oracle),
/// except the two individually-commented documented divergences.
/// </summary>
public class TraceCommandTests
{
    #region Variable Traces
    [Fact]
    public void Trace_write_fires_with_name_and_op()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log w:$n1:$n2:$op };" +
            " trace add variable ::v1 write wcb; set ::v1 10; set ::log")
            .Should().Be("w:::v1::write");

    [Fact]
    public void Trace_write_fires_on_every_write()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log X };" +
            " trace add variable ::v1 write wcb; set ::v1 1; set ::v1 2;" +
            " llength $::log").Should().Be("2");

    [Fact]
    public void Trace_read_fires_before_value_is_returned()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc rcb {n1 n2 op} { lappend ::log r:$n1:$n2:$op };" +
            " set ::v2 5; trace add variable ::v2 read rcb;" +
            " list $::v2 $::log").Should().Be("5 r:::v2::read");

    [Fact]
    public void Trace_unset_fires_and_destroys_the_trace()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter,
            "set ::log {}; proc ucb {n1 n2 op} { lappend ::log u:$n1:$op };" +
            " trace add variable ::v3 unset ucb; set ::v3 1");

        //Act
        TclTkTest.Eval(interpreter, "unset ::v3");

        //Assert
        TclTkTest.Eval(interpreter, "set ::log").Should().Be("u:::v3:unset");
        TclTkTest.Eval(interpreter,
            "set ::v3 again; trace info variable ::v3").Should().Be("");
    }

    [Fact]
    public void Trace_multiple_operations_fire_in_sequence()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc cb {n1 n2 op} { lappend ::log $op };" +
            " trace add variable ::v4 {read write unset} cb;" +
            " set ::v4 1; set ::v4 2; set ::v4; unset ::v4; set ::log")
            .Should().Be("write write read unset");

    [Fact]
    public void Trace_info_variable_lists_most_recent_first()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace add variable ::v5 write wcb;" +
            " trace add variable ::v5 {read unset} {other cb};" +
            " trace info variable ::v5")
            .Should().Be("{{read unset} {other cb}} {write wcb}");

    [Fact]
    public void Trace_info_variable_on_untraced_name_is_empty()
        => TclTkTest.EvalOnce("trace info variable ::nothing").Should().Be("");

    [Fact]
    public void Trace_remove_variable_removes_the_matching_trace()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace add variable ::v5 write wcb;" +
            " trace add variable ::v5 {read unset} {other cb};" +
            " trace remove variable ::v5 write wcb;" +
            " trace info variable ::v5")
            .Should().Be("{{read unset} {other cb}}");

    [Fact]
    public void Trace_remove_variable_of_missing_trace_is_silent()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace remove variable ::v5 write wcb")
            .Should().Be("");

    [Fact]
    public void Trace_write_callback_can_override_the_value()
        => TclTkTest.EvalOnce(
            "proc fcb {n1 n2 op} { upvar 1 $n1 x; set x FORCED };" +
            " trace add variable ::v6 write fcb;" +
            " list [set ::v6 orig] [set ::v6X keep; set ::v6]")
            .Should().Be("FORCED FORCED");

    [Fact]
    public void Trace_read_callback_can_provide_the_value()
        => TclTkTest.EvalOnce(
            "proc dcb {n1 n2 op} { upvar 1 $n1 x; set x DEFAULT };" +
            " trace add variable ::v7 read dcb; set ::v7 REAL; set ::v7")
            .Should().Be("DEFAULT");

    [Fact]
    public void Trace_write_callback_error_aborts_the_set()
        => TclTkTest.EvalOnceError(
            "proc ecb {n1 n2 op} { error TRACEBOOM };" +
            " trace add variable ::v8 write ecb; set ::v8 val")
            .Should().Be("can't set \"::v8\": TRACEBOOM");

    [Fact]
    public void Trace_write_callback_error_keeps_the_value_on_existing_variable()
        => TclTkTest.EvalOnce(
            "proc ecb {n1 n2 op} { error TRACEBOOM }; set ::v9 preset;" +
            " trace add variable ::v9 write ecb; catch {set ::v9 val};" +
            " set ::v9").Should().Be("val");

    [Fact]
    public void Trace_write_callback_error_on_undefined_variable_rolls_back()
        // DIVERGENCE from stock Tcl: when a write trace fails on a variable
        // that never existed, stock Tcl leaves the half-written variable
        // set; this port rolls the creation back (the engine recycles the
        // undefined variable). Failing write traces on EXISTING variables
        // keep the new value, matching stock Tcl (see the test above).
        => TclTkTest.EvalOnce(
            "proc ecb {n1 n2 op} { error TRACEBOOM };" +
            " trace add variable ::v8 write ecb; catch {set ::v8 val};" +
            " info exists ::v8").Should().Be("0");

    [Fact]
    public void Trace_read_callback_error_aborts_the_read()
        => TclTkTest.EvalOnceError(
            "proc ecb {n1 n2 op} { error TRACEBOOM }; set ::v9 preset;" +
            " trace add variable ::v9 read ecb; set ::v9")
            .Should().Be("can't read \"::v9\": TRACEBOOM");

    [Fact]
    public void Trace_whole_array_trace_fires_per_element()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log w:$n1:$n2:$op };" +
            " trace add variable ::arr write wcb;" +
            " set ::arr(k) 5; set ::arr(j) 6; set ::log")
            .Should().Be("w:::arr:k:write w:::arr:j:write");

    [Fact]
    public void Trace_element_trace_fires_only_for_that_element()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log w:$n1:$n2:$op };" +
            " trace add variable ::arr2(one) write wcb;" +
            " set ::arr2(two) x; set first $::log; set ::arr2(one) y;" +
            " list $first $::log").Should().Be("{} w:::arr2:one:write");

    [Fact]
    public void Trace_array_operation_is_accepted_but_does_not_fire()
        // DIVERGENCE from stock Tcl (which fires the "array" operation for
        // [array] command access): this port has no engine hook for it, so
        // the operation parses and shows in [trace info] but never fires.
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log X };" +
            " trace add variable ::arr3 array wcb; array set ::arr3 {a 1};" +
            " list $::log [trace info variable ::arr3]")
            .Should().Be("{} {{array wcb}}");

    [Fact]
    public void Trace_local_variable_write_fires()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log w:$n1:$n2:$op };" +
            " proc ploc {} { trace add variable lv write ::wcb; set lv 5 };" +
            " list [ploc] $::log").Should().Be("5 w:lv::write");

    [Fact]
    public void Trace_local_variable_unset_fires_at_procedure_exit()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log u:$n1:$op };" +
            " proc ploc2 {} { trace add variable lv2 unset ::wcb; set lv2 5 };" +
            " ploc2; set ::log").Should().Be("u:lv2:unset");

    [Fact]
    public void Trace_nested_write_inside_own_trace_does_not_recurse()
        => TclTkTest.EvalOnce(
            "set ::n 0; proc scb {n1 n2 op} { incr ::n; set ::sv INSIDE };" +
            " set ::sv 0; trace add variable ::sv write scb;" +
            " set ::sv 5; list $::sv $::n").Should().Be("INSIDE 1");

    [Fact]
    public void Trace_variable_traces_fire_most_recent_first()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc c1 {n1 n2 op} { lappend ::log V1 };" +
            " proc c2 {n1 n2 op} { lappend ::log V2 }; set ::ov 0;" +
            " trace add variable ::ov write c1;" +
            " trace add variable ::ov write c2; set ::ov 1; set ::log")
            .Should().Be("V2 V1");
    #endregion

    #region Legacy Variable Trace Forms
    [Fact]
    public void Trace_legacy_variable_reports_letter_operations()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc wcb {n1 n2 op} { lappend ::log w:$n1:$n2:$op };" +
            " trace variable ::lv3 w wcb; set ::lv3 9; set ::log")
            .Should().Be("w:::lv3::w");

    [Fact]
    public void Trace_vinfo_renders_letter_operations()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace variable ::lv3 w wcb;" +
            " trace vinfo ::lv3").Should().Be("{w wcb}");

    [Fact]
    public void Trace_vinfo_renders_modern_traces_as_letters_too()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace add variable ::lv4 write wcb;" +
            " trace vinfo ::lv4").Should().Be("{w wcb}");

    [Fact]
    public void Trace_vdelete_removes_the_trace()
        => TclTkTest.EvalOnce(
            "proc wcb {n1 n2 op} {}; trace variable ::lv3 w wcb;" +
            " trace vdelete ::lv3 w wcb; trace vinfo ::lv3").Should().Be("");
    #endregion

    #region Command Traces
    [Fact]
    public void Trace_command_rename_and_delete_fire_with_qualified_names()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter,
            "set ::log {}; proc target {} { return T };" +
            " proc ccb {old new op} { lappend ::log c:$old:$new:$op };" +
            " trace add command target {rename delete} ccb");

        //Act
        TclTkTest.Eval(interpreter, "rename target target2");
        TclTkTest.Eval(interpreter, "rename target2 {}");

        //Assert
        TclTkTest.Eval(interpreter, "set ::log").Should().Be(
            "c:::target:::target2:rename c:::target2::delete");
    }

    [Fact]
    public void Trace_info_command_lists_operations_and_prefix()
        => TclTkTest.EvalOnce(
            "proc target {} {}; proc ccb {old new op} {};" +
            " trace add command target {rename delete} ccb;" +
            " trace info command target")
            .Should().Be("{{rename delete} ccb}");

    [Fact]
    public void Trace_command_on_missing_command_is_an_error()
        => TclTkTest.EvalOnceError(
            "trace add command nosuchcmd rename cb")
            .Should().Be("unknown command \"nosuchcmd\"");

    [Fact]
    public void Trace_info_command_on_missing_command_is_an_error()
        => TclTkTest.EvalOnceError("trace info command nosuchcmd")
            .Should().Be("unknown command \"nosuchcmd\"");
    #endregion

    #region Execution Traces
    [Fact]
    public void Trace_execution_enter_fires_with_command_string()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc extgt {a} { return \"got $a\" };" +
            " proc ecb {args} { lappend ::log e:$args };" +
            " trace add execution extgt enter ecb;" +
            " list [extgt 5] $::log")
            .Should().Be("{got 5} {{e:{extgt 5} enter}}");

    [Fact]
    public void Trace_execution_leave_fires_with_code_and_result()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc extgt {a} { return \"got $a\" };" +
            " proc ecb {args} { lappend ::log e:$args };" +
            " trace add execution extgt {enter leave} ecb;" +
            " list [extgt 7] $::log")
            .Should().Be("{got 7} {{e:{extgt 7} enter} {e:{extgt 7} 0 {got 7} leave}}");

    [Fact]
    public void Trace_execution_step_traces_fire_per_inner_command()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc scb {args} { lappend ::log s:$args };" +
            " proc stepped {} { set a 1; incr a };" +
            " trace add execution stepped {enterstep leavestep} scb;" +
            " list [stepped] $::log")
            .Should().Be("2 {{s:{set a 1} enterstep} {s:{set a 1} 0 1" +
                " leavestep} {s:{incr a} enterstep} {s:{incr a} 0 2" +
                " leavestep}}");

    [Fact]
    public void Trace_execution_enter_error_aborts_the_command()
        => TclTkTest.EvalOnceError(
            "proc ecb {args} { error ENTERBOOM }; proc etgt {} { return X };" +
            " trace add execution etgt enter ecb; etgt")
            .Should().Be("ENTERBOOM");

    [Fact]
    public void Trace_execution_remove_restores_normal_execution()
        => TclTkTest.EvalOnce(
            "proc ecb {args} { error ENTERBOOM }; proc etgt {} { return X };" +
            " trace add execution etgt enter ecb;" +
            " trace remove execution etgt enter ecb; etgt").Should().Be("X");

    [Fact]
    public void Trace_execution_traces_fire_most_recent_first()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc o1 {args} { lappend ::log ONE };" +
            " proc o2 {args} { lappend ::log TWO }; proc otgt {} { return O };" +
            " trace add execution otgt enter o1;" +
            " trace add execution otgt enter o2; otgt; set ::log")
            .Should().Be("TWO ONE");

    [Fact]
    public void Trace_info_execution_lists_most_recent_first()
        => TclTkTest.EvalOnce(
            "proc extgt {} {}; proc ecb {args} {};" +
            " trace add execution extgt enter ecb;" +
            " trace add execution extgt leave ecb;" +
            " trace info execution extgt")
            .Should().Be("{leave ecb} {enter ecb}");

    [Fact]
    public void Trace_execution_traces_follow_a_renamed_command()
        => TclTkTest.EvalOnce(
            "set ::log {}; proc extgt {} { return X };" +
            " proc ecb {args} { lappend ::log FIRED };" +
            " trace add execution extgt enter ecb;" +
            " rename extgt extgt2; extgt2; set ::log").Should().Be("FIRED");
    #endregion

    #region Ensemble Errors
    [Theory]
    [InlineData("trace",
        "wrong # args: should be \"trace option ?arg ...?\"")]
    [InlineData("trace add variable ::x foo cb",
        "bad operation \"foo\": must be array, read, unset, or write")]
    [InlineData("trace add execution nosuch foo cb",
        "unknown command \"nosuch\"")]
    [InlineData("trace add banana ::x write cb",
        "bad option \"banana\": must be execution, command, or variable")]
    [InlineData("trace add command banana foo cb",
        "unknown command \"banana\"")]
    [InlineData("trace variable ::x q cb",
        "bad operations \"q\": should be one or more of rwua")]
    public void Trace_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);

    [Fact]
    public void Trace_add_execution_bad_operation_is_an_error()
        => TclTkTest.EvalOnceError(
            "proc t {} {}; trace add execution t foo cb")
            .Should().Be(
                "bad operation \"foo\": must be enter, leave, enterstep, or leavestep");

    [Fact]
    public void Trace_add_command_bad_operation_is_an_error()
        => TclTkTest.EvalOnceError(
            "proc t {} {}; trace add command t foo cb")
            .Should().Be("bad operation \"foo\": must be delete or rename");
    #endregion
}
