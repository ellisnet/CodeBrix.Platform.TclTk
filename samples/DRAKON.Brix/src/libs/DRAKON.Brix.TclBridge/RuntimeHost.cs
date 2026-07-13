using System;

using CodeBrix.Platform.TkCanvas.Hosting;

namespace DRAKON.Brix.Drakon;

/// <summary>
/// The application-facing owner of the DRAKON Tcl runtime. UI code holds one of
/// these and drives its <see cref="Start"/>/<see cref="Dispose"/> lifecycle, so
/// the code-behind never touches <see cref="DrakonRuntime"/> directly — the
/// application starts it one way, tests can drive it another.
/// </summary>
public sealed class RuntimeHost : IDisposable
{
    private DrakonRuntime _runtime;

    /// <summary>
    /// Creates and starts the DRAKON runtime inside the given host view. Call
    /// once, from the UI thread, after the host has loaded (its tree and
    /// dispatcher exist). Subsequent calls are ignored.
    /// </summary>
    /// <param name="host">The loaded Tk host view.</param>
    public void Start(TkHostView host)
    {
        if (host == null) { throw new ArgumentNullException(nameof(host)); }
        if (_runtime != null) { return; }

        _runtime = new DrakonRuntime();
        _runtime.Start(host);
    }

    /// <summary>
    /// Stops the Tcl thread and disposes the runtime. Safe to call more than
    /// once, and safe to call when <see cref="Start"/> was never called.
    /// </summary>
    public void Dispose()
    {
        DrakonRuntime runtime = _runtime;
        _runtime = null;
        if (runtime != null) { runtime.Dispose(); }
    }
}
