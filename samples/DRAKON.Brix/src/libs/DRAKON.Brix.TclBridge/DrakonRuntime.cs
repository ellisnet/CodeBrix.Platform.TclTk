using System;
using System.IO;
using System.Threading.Tasks;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras;
using CodeBrix.Platform.TkCanvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Windowing;

using DRAKON.Brix.Drakon.Commands;

namespace DRAKON.Brix.Drakon;

/// <summary>
/// Boots the unmodified DRAKON Editor Tcl on the managed interpreter and the
/// TkCanvas toolkit: creates the interpreter, registers the Extras shims
/// (sqlite3, pdf4tcl), the Tk bootstrap, and the Tcl command bridge, then
/// sources the vendored bootstrap.tcl glue followed by drakon_editor.tcl.
/// <para>The application boots in HOSTED mode (<see cref="Start"/>) — the
/// interpreter on a dedicated Tcl thread marshalling to a live UI thread. Tests
/// boot in DIRECT mode (<see cref="StartDirect"/>) — everything inline on the
/// calling thread against a headless root window. Both funnel through the same
/// <see cref="Boot"/> sequence, so the Tcl that runs (and therefore the file
/// open path) is identical.</para>
/// </summary>
internal sealed class DrakonRuntime : IDisposable
{
    private Interpreter _interpreter;
    private TkTclBridge _bridge;
    private TkWindow _directRoot;
    private bool _started;

    /// <summary>Raised with diagnostic text (startup failures, bgerror).</summary>
    public event Action<string> Diagnostic;

    /// <summary>
    /// Starts DRAKON in HOSTED mode inside the given host view — the way the
    /// application runs it. Call once, from the UI thread, after the host has
    /// loaded (its tree and dispatcher exist).
    /// </summary>
    /// <param name="host">The loaded Tk host view.</param>
    public void Start(TkHostView host)
    {
        if (host == null) { throw new ArgumentNullException(nameof(host)); }
        if (_started) { return; }
        _started = true;

        WindowTree tree = host.Tree;
        string assets = Path.Combine(AppContext.BaseDirectory, "Assets");

        Task.Run(() =>
        {
            try
            {
                if (!Boot(tree, hosted: true, code => Environment.Exit(code), new TkHostFileDialogs(), assets))
                {
                    return;
                }
                RunHostedDiagnostics();
            }
            catch (Exception ex)
            {
                Report("startup exception: " + ex);
            }
        });
    }

    /// <summary>
    /// Starts DRAKON in DIRECT mode against a headless root window, inline on the
    /// calling thread — the way tests run it. Runs the SAME registration and
    /// sourcing sequence as <see cref="Start"/>, so the Tcl open path is
    /// identical; only the marshalling differs (inline vs. a dedicated thread).
    /// A no-op quit action is used so a stray <c>exit</c> cannot end the test
    /// host. After this returns, evaluate Tcl through <see cref="EvaluateScriptForTest"/>.
    /// </summary>
    /// <param name="assetsDirectory">
    /// The directory holding <c>bootstrap.tcl</c> and <c>drakon/drakon_editor.tcl</c>
    /// (the application's <c>Assets</c> folder, in source or output).
    /// </param>
    internal void StartDirect(string assetsDirectory)
    {
        if (assetsDirectory == null) { throw new ArgumentNullException(nameof(assetsDirectory)); }
        if (_started) { return; }
        _started = true;

        _directRoot = TkWindow.CreateRoot();
        _directRoot.SetForcedSize(1024, 768);

        Boot(_directRoot.Tree, hosted: false, code => { }, null, assetsDirectory);
    }

    /// <summary>
    /// The shared boot sequence. In HOSTED mode every interpreter interaction is
    /// marshalled to the Tcl thread via <c>bridge.Post</c>; in DIRECT mode it
    /// runs inline on the calling thread. Returns false when a synchronous setup
    /// step fails (the failure is reported).
    /// </summary>
    private bool Boot(
        WindowTree tree, bool hosted, Action<int> onQuit,
        TkHostFileDialogs fileDialogs, string assetsDirectory)
    {
        Result createResult = null;
        _interpreter = Interpreter.Create(ref createResult);
        if (_interpreter == null)
        {
            Report("interpreter creation failed: " + createResult);
            return false;
        }

        //DRAKON re-draws diagrams by re-running the same Tcl procedure bodies
        //thousands of times per file open; cache their parsed form so each
        //body is tokenized once per interpreter instead of on every execution.
        _interpreter.CacheParsedScripts = true;

        //ProductionMode skips optional per-command engine work (readiness
        //checks, previous-result tracking, usage counters, ...) for another
        //large speedup; results are byte-identical. Trade-off: [interp cancel]
        //cannot interrupt a running script promptly — DRAKON never uses script
        //cancellation, so that is acceptable here. Remove this line if prompt
        //cancellation ever becomes necessary.
        _interpreter.ProductionMode = true;

        Result error = null;
        if (TkBootstrap.Register(_interpreter, ref error) != ReturnCode.Ok)
        {
            Report("TkBootstrap failed: " + error);
            return false;
        }

        error = null;
        if (TclTkExtras.RegisterAll(_interpreter, ref error) != ReturnCode.Ok)
        {
            Report("Extras registration failed: " + error);
            return false;
        }

        _bridge = hosted
            ? TkTclBridge.RegisterHosted(_interpreter, tree)
            : TkTclBridge.Register(_interpreter, tree);
        if (fileDialogs != null) { _bridge.FileDialogs = fileDialogs; }
        _bridge.BackgroundError += message => Report("bgerror: " + message);

        Action<Action<Interpreter>> dispatch;
        if (hosted) { dispatch = action => _bridge.Post(action); }
        else { dispatch = action => action(_interpreter); }

        dispatch(interp =>
        {
            //Diagnostic reporting command reachable from Tcl.
            var diagnostic = new DiagnosticReportCommand(Report);
            long token = 0;
            Result addError = null;
            interp.AddCommand(diagnostic, null, ref token, ref addError);

            //The real-quit command; bootstrap.tcl shadows exit onto it so
            //DRAKON's File > Quit ends the app. The exit action is injected so
            //non-hosted callers (tests) pass a safe no-op.
            var quit = new QuitCommand(onQuit);
            long quitToken = 0;
            Result quitError = null;
            interp.AddCommand(quit, null, ref quitToken, ref quitError);
        });

        string bootstrap = Path.Combine(assetsDirectory, "bootstrap.tcl");
        string editor = Path.Combine(assetsDirectory, "drakon", "drakon_editor.tcl");

        dispatch(interp =>
        {
            Result result = null;
            if (interp.EvaluateScript("source {" + bootstrap + "}", ref result) != ReturnCode.Ok)
            {
                Report("bootstrap.tcl failed: " + result);
                return;
            }

            result = null;
            if (interp.EvaluateScript("source {" + editor + "}", ref result) != ReturnCode.Ok)
            {
                Report("drakon_editor.tcl failed: " + result);
                return;
            }

            Report("DRAKON Editor is up.");
        });

        return true;
    }

    private void RunHostedDiagnostics()
    {
        // A diagnostic: when DRAKONBRIX_PROBE is set, open a diagram's first icon
        // and its edit-text dialog from Tcl (no mouse) and report the resulting
        // geometry chain, so live-only layout issues surface in the log.
        if (Environment.GetEnvironmentVariable("DRAKONBRIX_PROBE") == "1")
        {
            _bridge.Post(pollInterp =>
            {
                Result probe = null;
                pollInterp.EvaluateScript(
                    "after 1500 {\n" +
                    "  catch { mwc::change_text 4 } cerr\n" +
                    "  __brixreport \"open=$cerr\"\n" +
                    "  after 500 {\n" +
                    "    if {[winfo exists .twindow.root.entry.text]} {\n" +
                    "      __brixreport \"text=[winfo geometry .twindow.root.entry.text]" +
                    " frame=[winfo geometry .twindow.root.entry]" +
                    " root=[winfo geometry .twindow.root]" +
                    " top=[winfo geometry .twindow]\"\n" +
                    "    } else { __brixreport {dialog not open} }\n" +
                    "  }\n" +
                    "}", ref probe);
            });
        }

        // A generic diagnostic: when DRAKONBRIX_EVAL is set, its content is
        // evaluated as a Tcl script once the editor is up, and the completion
        // code/result are reported — lets any DRAKON flow be driven and observed
        // from the log without mouse input.
        string evalScript = Environment.GetEnvironmentVariable("DRAKONBRIX_EVAL");
        if (!String.IsNullOrEmpty(evalScript))
        {
            _bridge.Post(evalInterp =>
            {
                Result evalResult = null;
                ReturnCode evalCode = evalInterp.EvaluateScript(evalScript, ref evalResult);
                Report("eval(" + evalCode + "): " + evalResult);
            });
        }
    }

    /// <summary>
    /// Test-only: evaluate a Tcl script on the DIRECT-booted interpreter and
    /// return its string result. Throws on a Tcl error.
    /// </summary>
    /// <param name="script">The Tcl script to evaluate.</param>
    /// <returns>The script's string result.</returns>
    internal string EvaluateScriptForTest(string script)
    {
        Result result = null;
        ReturnCode code = _interpreter.EvaluateScript(script, ref result);
        if (code != ReturnCode.Ok)
        {
            throw new InvalidOperationException(
                "Tcl error: " + (result != null ? result.ToString() : "(null)"));
        }
        return result != null ? result.ToString() : String.Empty;
    }

    private void Report(string message)
    {
        Console.WriteLine("DRAKONBRIX: " + message);
        Action<string> handler = Diagnostic;
        if (handler != null) { handler(message); }
    }

    /// <summary>Stops the Tcl thread and disposes the interpreter.</summary>
    public void Dispose()
    {
        if (_bridge != null) { _bridge.Dispose(); }
        if (_interpreter != null) { _interpreter.Dispose(); }
    }
}
