using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for getting and setting Tcl variables from managed code, and the
/// interop between managed variable access and script evaluation.
/// </summary>
public class InterpreterVariableTests
{
    [Fact]
    public void SetVariableValue_makes_the_value_visible_to_scripts()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        Result error = null;

        //Act
        ReturnCode code = interpreter.SetVariableValue("name", "world", ref error);

        //Assert
        code.Should().Be(ReturnCode.Ok);
        TclTkTest.Eval(interpreter, "set greeting \"hello, $name\"").Should().Be("hello, world");
    }

    [Fact]
    public void GetVariableValue_reads_a_value_set_by_a_script()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set answer 42");
        Result value = null;
        Result error = null;

        //Act
        ReturnCode code = interpreter.GetVariableValue("answer", ref value, ref error);

        //Assert
        code.Should().Be(ReturnCode.Ok);
        value.ToString().Should().Be("42");
    }

    [Fact]
    public void SetVariableValue_then_GetVariableValue_round_trips()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        Result error = null;
        Result value = null;

        //Act
        interpreter.SetVariableValue("pi", "3.14159", ref error);
        interpreter.GetVariableValue("pi", ref value, ref error);

        //Assert
        value.ToString().Should().Be("3.14159");
    }

    [Fact]
    public void GetVariableValue_returns_error_for_an_unknown_variable()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        Result value = null;
        Result error = null;

        //Act
        ReturnCode code = interpreter.GetVariableValue("does_not_exist", ref value, ref error);

        //Assert
        code.Should().Be(ReturnCode.Error);
    }

    [Fact]
    public void incr_updates_a_managed_set_variable()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        Result error = null;
        interpreter.SetVariableValue("counter", "10", ref error);

        //Act
        string result = TclTkTest.Eval(interpreter, "incr counter 5");

        //Assert
        result.Should().Be("15");
    }
}
