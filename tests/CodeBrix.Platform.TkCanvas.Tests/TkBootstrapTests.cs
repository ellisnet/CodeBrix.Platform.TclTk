using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.10b coverage: the interpreter-side bootstrap (§4.5 of the port plan) —
/// after <see cref="TkBootstrap.Register"/>, the version globals satisfy the
/// hard <c>$tcl_version &gt;= 8.6</c>/<c>$tk_version &gt;= 8.6</c> check
/// DRAKON runs on line 1, and <c>package require Tk</c>/<c>Img</c> succeed.
/// </summary>
public class TkBootstrapTests
{
    [Fact]
    public void Register_sets_version_globals_and_provides_packages()
    {
        Result result = null;
        using (Interpreter interpreter = Interpreter.Create(ref result, BooleanModeForTests.Mode))
        {
            interpreter.Should().NotBeNull();

            Result error = null;
            TkBootstrap.Register(interpreter, ref error).Should().Be(ReturnCode.Ok);

            ReturnCode code = interpreter.EvaluateScript("set ::tcl_version", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("8.6");

            code = interpreter.EvaluateScript("set ::tk_version", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("8.6");

            code = interpreter.EvaluateScript("set ::tk_patchLevel", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("8.6.16");

            code = interpreter.EvaluateScript("package require Tk", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("8.6");

            code = interpreter.EvaluateScript("package require Img", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("1.4.13");
        }
    }

    [Fact]
    public void Drakon_version_gate_passes_after_register()
    {
        Result result = null;
        using (Interpreter interpreter = Interpreter.Create(ref result, BooleanModeForTests.Mode))
        {
            Result error = null;
            TkBootstrap.Register(interpreter, ref error).Should().Be(ReturnCode.Ok);

            // The gate drakon_editor.tcl runs before doing anything, in the
            // condition position it actually uses. (NOTE: a bare
            // [expr {a && b}] string-rep is "True" in the engine where real
            // Tcl says "1" — a known .TclTk divergence, flagged separately;
            // condition contexts like this one behave correctly.)
            ReturnCode code = interpreter.EvaluateScript(
                    "if { $tcl_version >= 8.6 && $tk_version >= 8.6 } "
                    + "{ set gate pass } else { set gate fail }", ref result);
            code.Should().Be(ReturnCode.Ok);
            result.ToString().Should().Be("pass");
        }
    }
}
