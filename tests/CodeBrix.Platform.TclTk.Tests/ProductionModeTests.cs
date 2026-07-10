using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the <see cref="Interpreter.ProductionMode" /> opt-in: the property
/// round-trips, and script evaluation produces the same results with it on as
/// with the default (fully-instrumented) engine flags.
/// </summary>
public class ProductionModeTests
{
    [Fact]
    public void ProductionMode_is_off_by_default()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Assert
        interpreter.ProductionMode.Should().BeFalse();
    }

    [Fact]
    public void ProductionMode_round_trips_on_and_off()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act + Assert
        interpreter.ProductionMode = true;
        interpreter.ProductionMode.Should().BeTrue();

        interpreter.ProductionMode = false;
        interpreter.ProductionMode.Should().BeFalse();
    }

    [Theory]
    [InlineData("expr {6 * 7}")]
    [InlineData("string toupper hello")]
    [InlineData("lindex {a b c d} 2")]
    [InlineData("proc double {x} { return [expr {$x * 2}] }; double 21")]
    [InlineData("set total 0; foreach n {1 2 3 4 5} { incr total $n }; set total")]
    public void ProductionMode_scripts_produce_the_same_results(string script)
    {
        //Arrange
        using Interpreter defaultInterpreter = TclTkTest.CreateInterpreter();
        using Interpreter productionInterpreter = TclTkTest.CreateInterpreter();
        productionInterpreter.ProductionMode = true;

        //Act
        string defaultResult = TclTkTest.Eval(defaultInterpreter, script);
        string productionResult = TclTkTest.Eval(productionInterpreter, script);

        //Assert
        productionResult.Should().Be(defaultResult);
    }

    [Fact]
    public void ProductionMode_can_be_enabled_mid_session()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set x before");

        //Act
        interpreter.ProductionMode = true;
        string result = TclTkTest.Eval(interpreter, "set y [string toupper $x]");

        //Assert
        result.Should().Be("BEFORE");
    }

    [Fact]
    public void ProductionMode_errors_still_report_correctly()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        interpreter.ProductionMode = true;

        //Act
        (ReturnCode code, string value) = TclTkTest.TryEval(
            interpreter, "expr {1 /");

        //Assert
        code.Should().Be(ReturnCode.Error);
        value.Should().NotBeNullOrEmpty();
    }
}
