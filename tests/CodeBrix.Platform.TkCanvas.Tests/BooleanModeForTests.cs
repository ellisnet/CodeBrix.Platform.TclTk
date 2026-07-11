using System;

using CodeBrix.Platform.TclTk._Components.Public;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The boolean-result mode interpreters in this test project are created
/// with. Normally the engine default
/// (<see cref="BooleanResultMode.EagleCompat" />); a diagnostic run can set
/// <c>TCLTK_TEST_BOOLEAN_MODE=TclshCompat</c> to force the suite into
/// TclshCompat, to surface any real functional regression under that mode.
/// </summary>
internal static class BooleanModeForTests
{
    internal static readonly BooleanResultMode Mode =
        string.Equals(
            Environment.GetEnvironmentVariable("TCLTK_TEST_BOOLEAN_MODE"),
            "TclshCompat", StringComparison.OrdinalIgnoreCase)
            ? BooleanResultMode.TclshCompat
            : BooleanResultMode.EagleCompat;
}
