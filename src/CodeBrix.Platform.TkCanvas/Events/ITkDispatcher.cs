using System;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// The seam between the toolkit and its host UI framework's dispatcher.
/// TkCanvas itself has no Uno/WinUI dependency; the host application (or the
/// host-integration layer) supplies an implementation bridging to its UI
/// thread — e.g. Uno's DispatcherQueue. Headless scenarios and tests run
/// without one: the <see cref="TkScheduler"/> then relies on manual pumping.
/// </summary>
public interface ITkDispatcher
{
    /// <summary>Queues work onto the UI thread.</summary>
    /// <param name="action">The work to run.</param>
    void Post(Action action);

    /// <summary>
    /// Starts a one-shot timer that invokes <paramref name="callback"/> on
    /// the UI thread after <paramref name="milliseconds"/>.
    /// </summary>
    /// <param name="milliseconds">The delay.</param>
    /// <param name="callback">The work to run when due.</param>
    /// <returns>An opaque handle for <see cref="CancelTimer"/>.</returns>
    object StartTimer(int milliseconds, Action callback);

    /// <summary>Cancels a timer started by <see cref="StartTimer"/> (no-op when already fired).</summary>
    /// <param name="handle">The timer handle.</param>
    void CancelTimer(object handle);

    /// <summary>
    /// Synchronously processes the host's own pending UI work, as far as the
    /// host supports it — the full-<c>update</c> hook. Hosts that cannot
    /// pump re-entrantly may implement this as a no-op; the toolkit's own
    /// idle queue and due timers are drained by the scheduler regardless.
    /// </summary>
    void PumpPendingWork();
}

/// <summary>
/// The scheduler's clock, swappable for deterministic tests (the analogue of
/// Tcl's time epoch for <c>after</c> timers).
/// </summary>
public interface ITkTimeSource
{
    /// <summary>A monotonic timestamp in milliseconds.</summary>
    long NowMilliseconds { get; }
}
