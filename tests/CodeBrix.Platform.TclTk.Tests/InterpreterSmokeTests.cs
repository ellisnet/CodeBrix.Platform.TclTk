using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Smoke tests that verify the ported interpreter starts up (loads its embedded
/// script library and resolves its own types after the Eagle-&gt;TclTk rebrand)
/// and evaluates basic Tcl.
/// </summary>
public class InterpreterSmokeTests
{
    [Fact]
    public void can_create_interpreter_and_evaluate_expression()
    {
        //Arrange
        Result result = null;

        //Act
        Interpreter interpreter = Interpreter.Create(ref result);

        //Assert
        interpreter.Should().NotBeNull(
            "interpreter creation should succeed; failure result: " +
            (result == null ? "<null>" : result.ToString()));

        using (interpreter)
        {
            ReturnCode code = interpreter.EvaluateScript("expr {2 + 2}", ref result);

            code.Should().Be(ReturnCode.Ok,
                "evaluation should succeed; result: " +
                (result == null ? "<null>" : result.ToString()));
            result.ToString().Should().Be("4");
        }
    }
}
