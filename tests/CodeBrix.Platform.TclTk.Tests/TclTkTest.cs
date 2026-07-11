using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Shared helpers for exercising a <see cref="Interpreter" /> in tests.
/// </summary>
internal static class TclTkTest
{
    /// <summary>
    /// The boolean-result mode every helper-created interpreter uses. Normally
    /// <see cref="BooleanResultMode.EagleCompat" /> (the engine default), but a
    /// diagnostic run can set the environment variable
    /// <c>TCLTK_TEST_BOOLEAN_MODE=TclshCompat</c> to force the ENTIRE suite
    /// into TclshCompat mode — the point is to surface any test that breaks for
    /// a reason OTHER than merely asserting the old <c>True</c>/<c>False</c>
    /// rendering (i.e. a real functional regression under TclshCompat).
    /// </summary>
    public static readonly BooleanResultMode BooleanMode =
        string.Equals(
            System.Environment.GetEnvironmentVariable("TCLTK_TEST_BOOLEAN_MODE"),
            "TclshCompat", System.StringComparison.OrdinalIgnoreCase)
            ? BooleanResultMode.TclshCompat
            : BooleanResultMode.EagleCompat;

    /// <summary>
    /// Creates and initializes an interpreter, asserting that creation succeeded.
    /// </summary>
    public static Interpreter CreateInterpreter()
    {
        Result result = null;
        Interpreter interpreter = Interpreter.Create(ref result, BooleanMode);
        interpreter.Should().NotBeNull(
            "interpreter creation should succeed; failure result: " +
            (result == null ? "<null>" : result.ToString()));
        return interpreter;
    }

    /// <summary>
    /// Evaluates a script, asserting an Ok return code, and returns the string result.
    /// </summary>
    public static string Eval(Interpreter interpreter, string script)
    {
        Result result = null;
        ReturnCode code = interpreter.EvaluateScript(script, ref result);
        code.Should().Be(ReturnCode.Ok,
            "script should evaluate successfully: <" + script + "> -> " +
            (result == null ? "<null>" : result.ToString()));
        return result == null ? null : result.ToString();
    }

    /// <summary>
    /// Creates a fresh interpreter, evaluates a script, disposes the interpreter,
    /// and returns the string result. Use for one-off evaluations so no interpreter
    /// is leaked.
    /// </summary>
    public static string EvalOnce(string script)
    {
        using Interpreter interpreter = CreateInterpreter();
        return Eval(interpreter, script);
    }

    /// <summary>
    /// Evaluates a script and returns the (code, result-string) pair without asserting.
    /// </summary>
    public static (ReturnCode Code, string Value) TryEval(Interpreter interpreter, string script)
    {
        Result result = null;
        ReturnCode code = interpreter.EvaluateScript(script, ref result);
        return (code, result == null ? null : result.ToString());
    }

    /// <summary>
    /// Creates a fresh interpreter, evaluates a script that is expected to fail,
    /// asserts the Error return code, and returns the error message.
    /// </summary>
    public static string EvalOnceError(string script)
    {
        using Interpreter interpreter = CreateInterpreter();
        (ReturnCode code, string value) = TryEval(interpreter, script);
        code.Should().Be(ReturnCode.Error,
            "script should fail: <" + script + "> -> " +
            (value ?? "<null>"));
        return value;
    }
}
