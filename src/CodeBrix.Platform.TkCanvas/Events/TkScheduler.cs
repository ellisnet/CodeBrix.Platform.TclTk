using System;
using System.Collections.Generic;
using System.Diagnostics;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// An <c>after</c> timer registration handle (the token <c>after cancel</c>
/// operates on).
/// </summary>
public sealed class AfterHandle
{
    internal AfterHandle(long dueAt, Action callback, bool isIdle)
    {
        DueAt = dueAt;
        Callback = callback;
        IsIdle = isIdle;
    }

    internal long DueAt { get; }

    internal Action Callback { get; }

    internal bool IsIdle { get; }

    internal bool Cancelled { get; set; }

    internal object HostTimer { get; set; }
}

/// <summary>
/// The per-tree work scheduler: Tk's idle-callback queue
/// (<c>Tcl_DoWhenIdle</c>), the <c>after ms</c>/<c>after idle</c> timer
/// bridge, coalesced relayout scheduling, and — most important for fidelity —
/// the SYNCHRONOUS-FLUSH semantics of the Tk <c>update</c> command:
/// <see cref="Update"/> / <see cref="UpdateIdleTasks"/> run all pending
/// relayout, repaint, and due timer work NOW, on the calling thread, so
/// geometry reads immediately afterwards see final values. DRAKON's
/// pervasive measure-then-place sequences depend on exactly this.
/// </summary>
public sealed class TkScheduler
{
    private sealed class StopwatchTimeSource : ITkTimeSource
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public long NowMilliseconds
        {
            get { return _stopwatch.ElapsedMilliseconds; }
        }
    }

    private readonly TkWindow _root;
    private readonly List<Action> _idleQueue = new List<Action>();
    private readonly List<AfterHandle> _timers = new List<AfterHandle>();
    private bool _relayoutPending;
    private bool _repaintPending;
    private bool _draining;
    private bool _runningLayout;

    internal TkScheduler(TkWindow root)
    {
        _root = root;
        TimeSource = new StopwatchTimeSource();
    }

    /// <summary>
    /// The host dispatcher bridge, or null when running headless. With a
    /// host attached, scheduled idle work and timers also wake the host so
    /// they run without anyone calling <see cref="Update"/>.
    /// </summary>
    public ITkDispatcher Host { get; set; }

    /// <summary>The scheduler's clock (swappable for deterministic tests).</summary>
    public ITkTimeSource TimeSource { get; set; }

    /// <summary>
    /// Raised when the tree needs repainting (geometry changed, a widget
    /// invalidated itself). The host subscribes and invalidates its Skia
    /// canvas; headless runs just observe it.
    /// </summary>
    public event Action RepaintRequested;

    /// <summary>Whether a coalesced relayout is scheduled and not yet run.</summary>
    public bool IsRelayoutPending
    {
        get { return _relayoutPending; }
    }

    /// <summary>
    /// Queues one-shot idle work — the analogue of <c>Tcl_DoWhenIdle</c>.
    /// Idle work runs on the next <see cref="UpdateIdleTasks"/> /
    /// <see cref="Update"/> drain (or when the host goes idle).
    /// </summary>
    /// <param name="callback">The work to run.</param>
    public void ScheduleIdle(Action callback)
    {
        if (callback == null) { throw new ArgumentNullException(nameof(callback)); }
        _idleQueue.Add(callback);
        WakeHost();
    }

    /// <summary>
    /// Schedules the tree's coalesced relayout: any number of geometry
    /// changes before the next drain produce ONE layout pass. Called
    /// automatically by geometry mutations; harmless to call repeatedly.
    /// </summary>
    public void ScheduleRelayout()
    {
        if (_relayoutPending) { return; }
        _relayoutPending = true;
        WakeHost();
    }

    /// <summary>
    /// Requests a repaint (coalesced until the next drain or host paint).
    /// </summary>
    public void ScheduleRepaint()
    {
        if (_repaintPending) { return; }
        _repaintPending = true;
        WakeHost();
    }

    /// <summary>
    /// Registers <c>after MILLISECONDS callback</c>.
    /// </summary>
    /// <param name="milliseconds">The delay (values below 0 are clamped to 0).</param>
    /// <param name="callback">The work to run when due.</param>
    /// <returns>The registration, for <see cref="CancelAfter"/>.</returns>
    public AfterHandle After(int milliseconds, Action callback)
    {
        if (callback == null) { throw new ArgumentNullException(nameof(callback)); }
        if (milliseconds < 0) { milliseconds = 0; }

        var handle = new AfterHandle(TimeSource.NowMilliseconds + milliseconds, callback, false);
        _timers.Add(handle);

        if (Host != null)
        {
            handle.HostTimer = Host.StartTimer(milliseconds, () => FireTimer(handle));
        }
        return handle;
    }

    /// <summary>
    /// Registers <c>after idle callback</c> — runs with the idle queue.
    /// </summary>
    /// <param name="callback">The work to run.</param>
    /// <returns>The registration, for <see cref="CancelAfter"/>.</returns>
    public AfterHandle AfterIdle(Action callback)
    {
        if (callback == null) { throw new ArgumentNullException(nameof(callback)); }
        var handle = new AfterHandle(0, callback, true);
        _idleQueue.Add(() => FireTimer(handle));
        WakeHost();
        return handle;
    }

    /// <summary>
    /// Cancels an <c>after</c> registration — <c>after cancel</c>. Cancelling
    /// one that already fired is a no-op.
    /// </summary>
    /// <param name="handle">The registration.</param>
    public void CancelAfter(AfterHandle handle)
    {
        if (handle == null) { return; }
        handle.Cancelled = true;
        _timers.Remove(handle);
        if (handle.HostTimer != null && Host != null)
        {
            Host.CancelTimer(handle.HostTimer);
            handle.HostTimer = null;
        }
    }

    /// <summary>
    /// The Tk <c>update idletasks</c> semantic: synchronously drains the
    /// idle queue (including the coalesced relayout and repaint) until it is
    /// empty. Timer callbacks that are not yet due do NOT run.
    /// </summary>
    public void UpdateIdleTasks()
    {
        Drain(false);
    }

    /// <summary>
    /// The Tk <c>update</c> semantic: synchronously processes everything
    /// runnable now — the host's pending work (when a host is attached), all
    /// DUE <c>after</c> timers, and the idle queue including relayout and
    /// repaint. After it returns, geometry reads see final values.
    /// </summary>
    public void Update()
    {
        Drain(true);
    }

    private void Drain(bool full)
    {
        if (_draining) { return; }
        _draining = true;
        try
        {
            if (full && Host != null)
            {
                Host.PumpPendingWork();
            }

            bool progressed = true;
            while (progressed)
            {
                progressed = false;

                if (full && RunDueTimers()) { progressed = true; }

                if (_idleQueue.Count > 0)
                {
                    Action[] batch = _idleQueue.ToArray();
                    _idleQueue.Clear();
                    foreach (Action callback in batch)
                    {
                        callback();
                    }
                    progressed = true;
                }

                if (_relayoutPending)
                {
                    _relayoutPending = false;
                    _runningLayout = true;
                    try
                    {
                        TkLayout.Update(_root);
                    }
                    finally
                    {
                        _runningLayout = false;
                    }
                    _repaintPending = true;
                    progressed = true;
                }
            }

            if (_repaintPending)
            {
                _repaintPending = false;
                Action handler = RepaintRequested;
                if (handler != null) { handler(); }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private bool RunDueTimers()
    {
        long now = TimeSource.NowMilliseconds;
        List<AfterHandle> due = null;
        foreach (AfterHandle timer in _timers)
        {
            if (timer.DueAt <= now)
            {
                if (due == null) { due = new List<AfterHandle>(); }
                due.Add(timer);
            }
        }
        if (due == null) { return false; }

        // Fire in due-time order (stable for equal times: registration order).
        due.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        foreach (AfterHandle timer in due)
        {
            FireTimer(timer);
        }
        return true;
    }

    private void FireTimer(AfterHandle handle)
    {
        if (handle.Cancelled) { return; }
        handle.Cancelled = true;
        _timers.Remove(handle);
        if (handle.HostTimer != null && Host != null)
        {
            Host.CancelTimer(handle.HostTimer);
            handle.HostTimer = null;
        }
        handle.Callback();
    }

    /// <summary>
    /// Whether the scheduler is inside the LAYOUT PASS itself. Geometry
    /// mutations made by the pass (request propagation) must not re-schedule
    /// endlessly — but mutations from timer/idle callbacks during a drain
    /// legitimately schedule more work, which the same drain then flushes.
    /// </summary>
    internal bool IsRunningLayout
    {
        get { return _runningLayout; }
    }

    private void WakeHost()
    {
        if (Host == null || _draining) { return; }
        Host.Post(() => Drain(false));
    }
}
