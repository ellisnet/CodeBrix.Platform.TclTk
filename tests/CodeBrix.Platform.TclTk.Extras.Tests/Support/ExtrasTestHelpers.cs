using System;
using System.IO;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras;
using SilverAssertions;

namespace CodeBrix.Platform.TclTk.Extras.Tests.Support;

/// <summary>
/// Shared plumbing for the Extras tests: interpreter creation with the Extras
/// commands registered, script evaluation helpers, per-test temp folders, and
/// discovery of the test assets (a TrueType font; the DRAKON example .drn files).
/// </summary>
internal static class ExtrasTestHelpers
{
    /// <summary>Creates an interpreter with BOTH Extras command sets registered.</summary>
    public static Interpreter CreateInterpreter()
    {
        Result result = null;
        Interpreter interpreter = Interpreter.Create(ref result);

        interpreter.Should().NotBeNull(
            "interpreter creation should succeed; failure result: " + AsString(result));

        Result error = null;
        ReturnCode code = TclTkExtras.RegisterAll(interpreter, ref error);

        code.Should().Be(ReturnCode.Ok,
            "extras registration should succeed; error: " + AsString(error));

        return interpreter;
    }

    /// <summary>Evaluates a script that is expected to succeed and returns its result text.</summary>
    public static string Eval(Interpreter interpreter, string script)
    {
        Result result = null;
        ReturnCode code = interpreter.EvaluateScript(script, ref result);

        code.Should().Be(ReturnCode.Ok,
            "script should succeed: <" + script + ">; result: " + AsString(result));

        return AsString(result);
    }

    /// <summary>Evaluates a script and returns the raw code and result text, for error-path tests.</summary>
    public static (ReturnCode Code, string Result) TryEval(Interpreter interpreter, string script)
    {
        Result result = null;
        ReturnCode code = interpreter.EvaluateScript(script, ref result);
        return (code, AsString(result));
    }

    /// <summary>Creates a unique writable folder for one test.</summary>
    public static string CreateTempFolder()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "CodeBrix.TclTk.Extras.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Best-effort removal of a test temp folder.</summary>
    public static void DeleteTempFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The folder holding this test project's VENDORED fixtures, resolved next to
    /// the test assembly (copied there at build time). Nothing here reaches outside
    /// this repository, so the tests never depend on another repo's layout.
    /// </summary>
    private static string AssetsDir
        => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>
    /// Returns the vendored monospaced TrueType font (Liberation Mono, SIL OFL 1.1)
    /// used by the pdf4tcl font tests. The file lives in this repo under
    /// <c>Assets/fonts</c> and is copied next to the test assembly, so it is always
    /// present regardless of host-installed fonts.
    /// </summary>
    public static string RequireMonospaceTtf()
    {
        string path = Path.Combine(AssetsDir, "fonts", "LiberationMono-Regular.ttf");

        File.Exists(path).Should().BeTrue(
            "the vendored test font should be copied next to the test assembly: " + path);

        return path;
    }

    /// <summary>
    /// Returns a copy of the vendored real-stock-DRAKON <c>.drn</c> sample used by
    /// the <c>.drn</c>-compatibility tests, placed in <paramref name="destinationFolder" />.
    /// The source lives in this repo under <c>Assets/sample.drn</c> (a genuine
    /// DRAKON Editor example database), so no other repository is referenced.
    /// </summary>
    public static string CopyDrakonSampleDrn(string destinationFolder)
    {
        string source = Path.Combine(AssetsDir, "sample.drn");

        File.Exists(source).Should().BeTrue(
            "the vendored sample .drn should be copied next to the test assembly: " + source);

        string copy = Path.Combine(destinationFolder, "example.drn");
        File.Copy(source, copy);
        return copy;
    }

    private static string AsString(Result result)
        => (result != null) ? result.ToString() : "<null>";
}
