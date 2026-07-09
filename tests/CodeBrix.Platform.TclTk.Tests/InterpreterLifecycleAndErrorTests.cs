using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for interpreter lifecycle (creation, disposal, isolation between
/// instances) and error handling / return codes.
/// </summary>
public class InterpreterLifecycleAndErrorTests
{
    [Fact]
    public void can_create_interpreter()
    {
        //Arrange
        Result result = null;

        //Act
        using Interpreter interpreter = Interpreter.Create(ref result);

        //Assert
        interpreter.Should().NotBeNull();
    }

    [Fact]
    public void interpreter_is_disposable()
    {
        //Arrange / Act
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Assert
        interpreter.Should().BeAssignableTo<System.IDisposable>();
    }

    [Fact]
    public void interpreter_reports_disposed_after_dispose()
    {
        //Arrange
        Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        interpreter.Dispose();

        //Assert
        interpreter.Disposed.Should().BeTrue();
    }

    [Fact]
    public void evaluating_on_a_disposed_interpreter_throws()
    {
        //Arrange
        Interpreter interpreter = TclTkTest.CreateInterpreter();
        interpreter.Dispose();
        Result result = null;

        //Act
        System.Action act = () => interpreter.EvaluateScript("expr {1 + 1}", ref result);

        //Assert
        act.Should().Throw<System.Exception>();
    }

    [Fact]
    public void separate_interpreters_have_isolated_state()
    {
        //Arrange
        using Interpreter first = TclTkTest.CreateInterpreter();
        using Interpreter second = TclTkTest.CreateInterpreter();

        //Act
        TclTkTest.Eval(first, "set shared first");
        Result value = null;
        Result error = null;
        ReturnCode code = second.GetVariableValue("shared", ref value, ref error);

        //Assert
        code.Should().Be(ReturnCode.Error, "the variable set in the first interpreter must not leak into the second");
    }

    [Fact]
    public void EvaluateScript_returns_error_for_an_unknown_command()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        (ReturnCode code, _) = TclTkTest.TryEval(interpreter, "this_is_not_a_command");

        //Assert
        code.Should().Be(ReturnCode.Error);
    }

    [Fact]
    public void EvaluateScript_returns_error_for_a_syntax_error()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        (ReturnCode code, _) = TclTkTest.TryEval(interpreter, "expr {1 + }");

        //Assert
        code.Should().Be(ReturnCode.Error);
    }

    [Fact]
    public void EvaluateScript_error_message_is_reported_in_the_result()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        (ReturnCode code, string message) = TclTkTest.TryEval(interpreter, "error {custom failure}");

        //Assert
        code.Should().Be(ReturnCode.Error);
        message.Should().Be("custom failure");
    }

    [Fact]
    public void catch_traps_a_script_error_and_yields_nonzero()
        => TclTkTest.EvalOnce("catch { error boom }").Should().Be("1");
}
