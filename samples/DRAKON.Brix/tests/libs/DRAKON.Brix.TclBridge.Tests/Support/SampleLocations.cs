using System;
using System.IO;

namespace DRAKON.Brix.TclBridge.Tests;

/// <summary>
/// Resolves paths inside the DRAKON.Brix sample by walking up from the test
/// assembly to the sample root (identified by <c>DRAKON.Brix.slnx</c>). The
/// gold-master example <c>.drn</c> files and the DRAKON <c>Assets</c> tree are
/// read from source — never copied into the test project — so there is a single
/// source of truth and no build-copy of the whole vendored Tcl tree.
/// </summary>
internal static class SampleLocations
{
    /// <summary>The DRAKON.Brix sample root directory.</summary>
    public static string SampleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DRAKON.Brix.slnx"))) { return dir.FullName; }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the DRAKON.Brix sample root (DRAKON.Brix.slnx) above " +
            AppContext.BaseDirectory);
    }

    /// <summary>The read-only gold-master example <c>.drn</c> directory.</summary>
    public static string ExamplesDir(string root) => Path.Combine(root, "examples");

    /// <summary>The DRAKON <c>Assets</c> directory (bootstrap.tcl + drakon/ tree).</summary>
    public static string AssetsDir(string root) =>
        Path.Combine(root, "src", "DRAKON.Brix.Core", "Assets");
}
