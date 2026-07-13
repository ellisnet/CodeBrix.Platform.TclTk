using System;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Events;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The Tcl command bridge: registers the classic Tk command surface —
/// widget creation commands (classic and <c>ttk::</c> forms), the
/// <c>pack</c>/<c>grid</c> geometry managers, <c>bind</c>, <c>wm</c>/
/// <c>winfo</c>/<c>toplevel</c>/<c>grab</c>, <c>image</c>/<c>font</c>/
/// <c>clipboard</c>, <c>option</c>/<c>ttk::style</c> and the theme applier
/// commands, the standard dialogs, and the <c>update</c>/<c>after</c> event
/// loop — on a CodeBrix.Platform.TclTk interpreter, driving a
/// <see cref="WindowTree"/>. This is what lets an unmodified Tcl/Tk
/// application run on the
/// toolkit.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> <see cref="Register"/> creates a DIRECT bridge:
/// everything runs on the calling thread (headless use, tests — bind and
/// -command scripts run inline, like real Tk). <see cref="RegisterHosted"/>
/// creates the hosted apartment: the interpreter runs on a dedicated Tcl
/// worker thread, Tk commands marshal synchronously to the UI thread, and
/// UI events post their callback scripts back to the Tcl thread — so modal
/// commands (<c>tk_messageBox</c>, the file pickers) can block the script
/// while the UI stays live.</para>
/// <para>After registering, run the application's startup script through
/// <see cref="Post"/> (hosted) or plain <c>EvaluateScript</c> (direct).</para>
/// </remarks>
public sealed class TkTclBridge : IDisposable
{
    private readonly BridgeContext _context;
    private readonly BridgeApartment _apartment;

    private TkTclBridge(BridgeContext context, BridgeApartment apartment)
    {
        _context = context;
        _apartment = apartment;
        _apartment.BackgroundError += ex =>
        {
            Action<string> handler = BackgroundError;
            if (handler != null) { handler(ex.Message); }
        };
    }

    /// <summary>
    /// Registers the Tk command surface in DIRECT mode: all work runs on
    /// the calling thread (headless hosts and tests).
    /// </summary>
    /// <param name="interpreter">The interpreter to register on.</param>
    /// <param name="tree">The widget tree to drive.</param>
    /// <returns>The bridge instance.</returns>
    public static TkTclBridge Register(Interpreter interpreter, WindowTree tree)
    {
        return RegisterCore(interpreter, tree, null);
    }

    /// <summary>
    /// Registers the Tk command surface in HOSTED mode: the interpreter
    /// lives on a dedicated Tcl thread and Tk commands marshal to the UI
    /// thread through the tree's scheduler dispatcher (which must be set —
    /// <c>Hosting.TkHostView</c> does this).
    /// </summary>
    /// <param name="interpreter">The interpreter to register on.</param>
    /// <param name="tree">The widget tree to drive.</param>
    /// <returns>The bridge instance.</returns>
    public static TkTclBridge RegisterHosted(Interpreter interpreter, WindowTree tree)
    {
        if (tree == null) { throw new ArgumentNullException(nameof(tree)); }

        ITkDispatcher dispatcher = tree.Scheduler.Host;
        if (dispatcher == null)
        {
            throw new InvalidOperationException(
                "RegisterHosted needs the tree's scheduler dispatcher (tree.Scheduler.Host); " +
                "use Register for headless/direct operation.");
        }

        return RegisterCore(interpreter, tree, dispatcher);
    }

    private static TkTclBridge RegisterCore(
        Interpreter interpreter, WindowTree tree, ITkDispatcher dispatcher)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }
        if (tree == null) { throw new ArgumentNullException(nameof(tree)); }

        var apartment = new BridgeApartment(dispatcher);
        var context = new BridgeContext(interpreter, tree, apartment);
        var bridge = new TkTclBridge(context, apartment);

        WidgetCommands.Register(context);
        GeometryCommands.Register(context);
        BindCommands.Register(context);
        EventLoopCommands.Register(context);
        WindowCommands.Register(context);
        ResourceCommands.Register(context);
        DialogCommands.Register(context);

        return bridge;
    }

    /// <summary>
    /// Raised when a callback script (bind handler, -command, after script)
    /// fails — the analogue of Tk's <c>bgerror</c>. The payload is the Tcl
    /// error message.
    /// </summary>
    public event Action<string> BackgroundError;

    /// <summary>
    /// The host's file/folder picker implementation (the only native-escape
    /// dialogs). Null when headless: the picker commands then raise Tcl
    /// errors.
    /// </summary>
    public ITkFileDialogProvider FileDialogs
    {
        get { return _context.FileDialogs; }
        set { _context.FileDialogs = value; }
    }

    /// <summary>
    /// Optional auto-responder for modal dialog commands, for scripted and
    /// headless runs: receives the command name (e.g. "tk_messageBox") and
    /// its argument words, returns the result string, or null to decline.
    /// </summary>
    public Func<string, string[], string> ModalAutoResponder
    {
        get { return _apartment.ModalAutoResponder; }
        set { _apartment.ModalAutoResponder = value; }
    }

    /// <summary>
    /// Queues a script for evaluation on the Tcl thread (runs inline in
    /// DIRECT mode). Fire-and-forget; failures raise
    /// <see cref="BackgroundError"/>.
    /// </summary>
    /// <param name="script">The script to evaluate.</param>
    public void PostScript(string script)
    {
        _context.EvalCallbackScript(script);
    }

    /// <summary>
    /// Queues arbitrary interpreter work onto the Tcl thread (runs inline
    /// in DIRECT mode) — the way a host runs its bootstrap + source
    /// sequence without touching the interpreter from the UI thread.
    /// </summary>
    /// <param name="work">The work; receives the interpreter.</param>
    public void Post(Action<Interpreter> work)
    {
        if (work == null) { throw new ArgumentNullException(nameof(work)); }
        _apartment.PostToTcl(() => work(_context.Interpreter));
    }

    /// <summary>Stops the Tcl worker thread (hosted mode).</summary>
    public void Dispose()
    {
        _apartment.Dispose();
    }
}
