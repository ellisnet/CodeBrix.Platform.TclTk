using System;

using CodeBrix.Platform.TkCanvas.Events;

using Microsoft.UI.Dispatching;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// The <see cref="ITkDispatcher"/> seam implemented over the CodeBrix.Platform
/// UI dispatcher (<see cref="DispatcherQueue"/>): posts toolkit work and
/// <c>after</c> timers onto the UI thread. Created by
/// <see cref="TkHostView"/>; also usable standalone by hosts that embed the
/// toolkit without the ready-made view.
/// </summary>
public sealed class TkHostDispatcher : ITkDispatcher
{
    private readonly DispatcherQueue _queue;

    /// <summary>Creates the dispatcher bridge over a UI-thread queue.</summary>
    /// <param name="queue">The UI thread's dispatcher queue.</param>
    public TkHostDispatcher(DispatcherQueue queue)
    {
        if (queue == null) { throw new ArgumentNullException(nameof(queue)); }
        _queue = queue;
    }

    /// <inheritdoc/>
    public void Post(Action action)
    {
        _queue.TryEnqueue(() => action());
    }

    /// <inheritdoc/>
    public object StartTimer(int milliseconds, Action callback)
    {
        DispatcherQueueTimer timer = _queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => callback();
        timer.Start();
        return timer;
    }

    /// <inheritdoc/>
    public void CancelTimer(object handle)
    {
        var timer = handle as DispatcherQueueTimer;
        if (timer != null) { timer.Stop(); }
    }

    /// <inheritdoc/>
    public void PumpPendingWork()
    {
        // The platform dispatcher cannot be pumped re-entrantly; the
        // toolkit scheduler drains its own idle queue and due timers
        // regardless (the R1 flush stays exact for toolkit-side work).
    }
}
