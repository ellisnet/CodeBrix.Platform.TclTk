using System;
using System.IO;
using System.Threading.Tasks;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras;
using CodeBrix.Platform.TkCanvas;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Tcl;

using DRAKON.Brix.Drakon.Commands;

namespace DRAKON.Brix.Drakon;

/// <summary>
/// Boots the unmodified DRAKON Editor Tcl on the managed interpreter and
/// the TkCanvas toolkit: creates the interpreter, registers the Extras
/// shims (sqlite3, pdf4tcl), the Tk bootstrap, and the Tcl command bridge
/// in hosted (dedicated-Tcl-thread) mode, then sources the vendored
/// bootstrap.tcl glue followed by drakon_editor.tcl itself.
/// </summary>
public sealed class DrakonRuntime : IDisposable
{
    private Interpreter _interpreter;
    private TkTclBridge _bridge;
    private bool _started;

    /// <summary>Raised with diagnostic text (startup failures, bgerror).</summary>
    public event Action<string> Diagnostic;

    /// <summary>
    /// Starts DRAKON inside the given host view. Call once, from the UI
    /// thread, after the host has loaded (its tree and dispatcher exist).
    /// </summary>
    /// <param name="host">The loaded Tk host view.</param>
    public void Start(TkHostView host)
    {
        if (host == null) { throw new ArgumentNullException(nameof(host)); }
        if (_started) { return; }
        _started = true;

        var tree = host.Tree;

        Task.Run(() =>
        {
            try
            {
                Result createResult = null;
                _interpreter = Interpreter.Create(ref createResult);
                if (_interpreter == null)
                {
                    Report("interpreter creation failed: " + createResult);
                    return;
                }

                Result error = null;
                if (TkBootstrap.Register(_interpreter, ref error) != ReturnCode.Ok)
                {
                    Report("TkBootstrap failed: " + error);
                    return;
                }

                error = null;
                if (TclTkExtras.RegisterAll(_interpreter, ref error) != ReturnCode.Ok)
                {
                    Report("Extras registration failed: " + error);
                    return;
                }

                _bridge = TkTclBridge.RegisterHosted(_interpreter, tree);
                _bridge.FileDialogs = new TkHostFileDialogs();
                _bridge.BackgroundError += message => Report("bgerror: " + message);

                //Diagnostic reporting command reachable from Tcl.
                _bridge.Post(reportInterp =>
                {
                    var command = new DiagnosticReportCommand(Report);
                    long token = 0;
                    Result addError = null;
                    reportInterp.AddCommand(command, null, ref token, ref addError);

                    //The real-quit command; bootstrap.tcl shadows exit onto it
                    //so DRAKON's File > Quit terminates the hosted process. The
                    //exit action is injected so non-hosted callers (e.g. tests)
                    //can supply a safe no-op instead of tearing down the process.
                    var quit = new QuitCommand(code => Environment.Exit(code));
                    long quitToken = 0;
                    Result quitError = null;
                    reportInterp.AddCommand(quit, null, ref quitToken, ref quitError);
                });

                string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
                string bootstrap = Path.Combine(assets, "bootstrap.tcl");
                string editor = Path.Combine(assets, "drakon", "drakon_editor.tcl");

                _bridge.Post(interpreter =>
                {
                    Result result = null;
                    if (interpreter.EvaluateScript(
                        "source {" + bootstrap + "}", ref result) != ReturnCode.Ok)
                    {
                        Report("bootstrap.tcl failed: " + result);
                        return;
                    }

                    result = null;
                    if (interpreter.EvaluateScript(
                        "source {" + editor + "}", ref result) != ReturnCode.Ok)
                    {
                        Report("drakon_editor.tcl failed: " + result);
                        return;
                    }

                    Report("DRAKON Editor is up.");

                    // A diagnostic: when DRAKONBRIX_PROBE is set, open a
                    // diagram's first icon and its edit-text dialog from Tcl
                    // (no mouse) and report the resulting geometry chain, so
                    // live-only layout issues surface in the log.
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

                    // A generic diagnostic: when DRAKONBRIX_EVAL is set, its
                    // content is evaluated as a Tcl script once the editor is
                    // up, and the completion code/result are reported — lets
                    // any DRAKON flow be driven and observed from the log
                    // without mouse input.
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
                });
            }
            catch (Exception ex)
            {
                Report("startup exception: " + ex);
            }
        });
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
