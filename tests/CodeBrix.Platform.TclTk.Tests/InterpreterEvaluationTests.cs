using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests that exercise core Tcl evaluation through the ported interpreter:
/// expressions, string/list commands, control flow, and procedures. These also
/// verify that the embedded (rebranded) script library is loaded and usable.
/// </summary>
public class InterpreterEvaluationTests
{
    [Theory]
    [InlineData("expr {2 + 2}", "4")]
    [InlineData("expr {6 * 7}", "42")]
    [InlineData("expr {10 / 4}", "2")]
    [InlineData("expr {10.0 / 4}", "2.5")]
    [InlineData("expr {2 ** 10}", "1024")]
    // Eagle evaluates relational/logical operators to typed booleans, whose string
    // form is "True"/"False" (a deliberate difference from stock Tcl's 1/0).
    [InlineData("expr {5 > 3}", "True")]
    [InlineData("expr {5 <= 3}", "False")]
    [InlineData("expr {1 && 0}", "False")]
    public void EvaluateScript_computes_expressions(string script, string expected)
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        string result = TclTkTest.Eval(interpreter, script);

        //Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EvaluateScript_set_returns_the_assigned_value()
        => TclTkTest.EvalOnce("set x hello").Should().Be("hello");

    [Fact]
    public void EvaluateScript_string_length_works()
        => TclTkTest.EvalOnce("string length \"hello world\"").Should().Be("11");

    [Fact]
    public void EvaluateScript_string_toupper_works()
        => TclTkTest.EvalOnce("string toupper hello").Should().Be("HELLO");

    [Fact]
    public void EvaluateScript_string_range_works()
        => TclTkTest.EvalOnce("string range abcdef 1 3").Should().Be("bcd");

    [Fact]
    public void EvaluateScript_builds_and_indexes_lists()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        string list = TclTkTest.Eval(interpreter, "list a b c d");
        string length = TclTkTest.Eval(interpreter, "llength {a b c d}");
        string third = TclTkTest.Eval(interpreter, "lindex {a b c d} 2");

        //Assert
        list.Should().Be("a b c d");
        length.Should().Be("4");
        third.Should().Be("c");
    }

    [Fact]
    public void EvaluateScript_lappend_extends_a_list()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        TclTkTest.Eval(interpreter, "set items {1 2 3}");
        string result = TclTkTest.Eval(interpreter, "lappend items 4 5");

        //Assert
        result.Should().Be("1 2 3 4 5");
    }

    [Fact]
    public void EvaluateScript_if_selects_the_true_branch()
        => TclTkTest.EvalOnce("if {5 > 3} { set r yes } else { set r no }").Should().Be("yes");

    [Fact]
    public void EvaluateScript_for_loop_accumulates()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        string sum = TclTkTest.Eval(interpreter,
            "set sum 0; for {set i 1} {$i <= 5} {incr i} { incr sum $i }; set sum");

        //Assert
        sum.Should().Be("15");
    }

    [Fact]
    public void EvaluateScript_foreach_iterates()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        string result = TclTkTest.Eval(interpreter,
            "set out {}; foreach x {a b c} { append out $x }; set out");

        //Assert
        result.Should().Be("abc");
    }

    [Fact]
    public void EvaluateScript_defines_and_calls_a_procedure()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        TclTkTest.Eval(interpreter, "proc double {x} { return [expr {$x * 2}] }");
        string result = TclTkTest.Eval(interpreter, "double 21");

        //Assert
        result.Should().Be("42");
    }

    [Fact]
    public void EvaluateScript_recursive_procedure_computes_factorial()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        TclTkTest.Eval(interpreter,
            "proc fact {n} { if {$n <= 1} { return 1 } return [expr {$n * [fact [expr {$n - 1}]]}] }");
        string result = TclTkTest.Eval(interpreter, "fact 5");

        //Assert
        result.Should().Be("120");
    }

    [Fact]
    public void EvaluateExpression_evaluates_a_bare_expression()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        Result result = null;

        //Act
        ReturnCode code = interpreter.EvaluateExpression("3 + 4 * 2", ref result);

        //Assert
        code.Should().Be(ReturnCode.Ok);
        result.ToString().Should().Be("11");
    }
}
