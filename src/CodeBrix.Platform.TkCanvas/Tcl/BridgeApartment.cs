using System;
using System.Collections.Concurrent;
using System.Threading;

using CodeBrix.Platform.TkCanvas.Events;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The bridge's threading model. In DIRECT mode (headless, tests) everything
/// runs inline on the calling thread. In HOSTED mode the interpreter lives on
/// a dedicated Tcl worker thread: Tk command bodies marshal synchronously to
/// the UI thread through the tree's <see cref="ITkDispatcher"/>, while
/// UI-side callbacks (bind scripts, -command scripts, after scripts) are
/// fire-and-forget posts onto the Tcl work queue — the UI thread never blocks
/// on the Tcl thread, which is what makes modal waits (tk_messageBox,
/// tk_dialog, file pickers) safe: the Tcl thread parks on the result signal
/// while the UI keeps pumping.
/// </summary>
internal sealed class BridgeApartment : IDisposable
{
    private readonly ITkDispatcher _dispatcher;
    private readonly BlockingCollection<Action> _tclQueue;
    private readonly Thread _tclThread;
    private bool _disposed;

    /// <summary>Creates the apartment. A null dispatcher selects DIRECT mode.</summary>
    internal BridgeApartment(ITkDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        if (dispatcher != null)
        {
            _tclQueue = new BlockingCollection<Action>();
            _tclThread = new Thread(TclThreadLoop)
            {
                Name = "TkTclBridge",
                IsBackground = true
            };
            _tclThread.Start();
        }
    }

    /// <summary>True when running the dedicated-Tcl-thread (hosted) model.</summary>
    internal bool IsHosted
    {
        get { return _dispatcher != null; }
    }

    /// <summary>
    /// Raised when work posted to the Tcl thread throws (the analogue of
    /// Tk's <c>bgerror</c>). The exception message is the Tcl error text.
    /// </summary>
    internal event Action<Exception> BackgroundError;

    /// <summary>
    /// Optional auto-responder for modal commands when no interactive host
    /// can answer (DIRECT mode tests, scripted runs): receives the dialog
    /// kind and the argument words, returns the result string, or null to
    /// decline (which raises a Tcl error).
    /// </summary>
    internal Func<string, string[], string> ModalAutoResponder { get; set; }

    private void TclThreadLoop()
    {
        foreach (Action work in _tclQueue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Action<Exception> handler = BackgroundError;
                if (handler != null) { handler(ex); }
            }
        }
    }

    /// <summary>
    /// Runs a Tk command body that must execute on the UI thread and returns
    /// its result. Called from the Tcl thread (hosted) or inline (direct).
    /// <see cref="TkTclError"/> thrown by the body propagates unchanged.
    /// </summary>
    internal string RunOnUi(Func<string> body)
    {
        if (_dispatcher == null) { return body(); }

        string result = null;
        Exception failure = null;
        using (var done = new ManualResetEventSlim(false))
        {
            _dispatcher.Post(() =>
            {
                try { result = body(); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            });
            done.Wait();
        }

        if (failure != null) { throw failure; }
        return result;
    }

    /// <summary>
    /// Queues work for the Tcl thread (script evaluation, interpreter
    /// access). Fire-and-forget from the UI side. DIRECT mode runs it
    /// inline — which makes bind/-command scripts synchronous in tests,
    /// matching real Tk's inline dispatch.
    /// </summary>
    internal void PostToTcl(Action work)
    {
        if (work == null) { throw new ArgumentNullException(nameof(work)); }

        if (_tclQueue == null)
        {
            work();
            return;
        }

        if (!_tclQueue.IsAddingCompleted) { _tclQueue.Add(work); }
    }

    /// <summary>
    /// Runs a modal interaction: <paramref name="show"/> executes on the UI
    /// thread and receives a completion callback; the calling (Tcl) thread
    /// blocks until that callback delivers the result string. In DIRECT mode
    /// the completion must arrive synchronously (a test auto-responder or a
    /// pre-answered dialog) or the command fails rather than deadlocking.
    /// </summary>
    internal string WaitForModal(Action<Action<string>> show)
    {
        if (_dispatcher == null)
        {
            string directResult = null;
            bool completed = false;
            show(value =>
            {
                directResult = value;
                completed = true;
            });
            if (!completed)
            {
                throw new TkTclError(
                    "cannot wait for a modal answer without a host dispatcher");
            }
            return directResult;
        }

        string result = null;
        using (var done = new ManualResetEventSlim(false))
        {
            Exception failure = null;
            _dispatcher.Post(() =>
            {
                try
                {
                    show(value =>
                    {
                        result = value;
                        done.Set();
                    });
                }
                catch (Exception ex)
                {
                    failure = ex;
                    done.Set();
                }
            });
            done.Wait();
            if (failure != null) { throw failure; }
        }

        return result;
    }

    /// <summary>Stops the Tcl worker thread (hosted mode).</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_tclQueue != null) { _tclQueue.CompleteAdding(); }
    }
}
