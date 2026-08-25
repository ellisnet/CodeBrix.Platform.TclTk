================================================================================
EXTRAS-README: CodeBrix.Platform.TclTk
Samples, tools and other content in this repository that is not part of a NuGet package
================================================================================

samples/DRAKON.Brix — the reference consumer application
=========================================================
Path: samples/DRAKON.Brix (own solution: samples/DRAKON.Brix/DRAKON.Brix.slnx)

What it is: the ACTUAL DRAKON Editor (by Stepan Mitkin and contributors,
public domain) — its Tcl/Tk source vendored UNMODIFIED under
src/DRAKON.Brix.Core/Assets/drakon — booted on the managed interpreter,
the Extras shims (sqlite3 for .drn files, pdf4tcl for PDF export) and the
TkCanvas Tcl command bridge inside one TkHostView, as a CodeBrix.Platform
application with the six desktop heads (LinuxX11, LinuxWayland,
LinuxFrameBuffer, MacOS, Win32Skia, WinWpfSkia). It is the end-to-end
proof that a large real-world Tcl/Tk program runs on the family.

Layout:
    DRAKON.Brix.slnx                 the app solution; also lists the three
                                     src/ packages by project reference
    src/DRAKON.Brix.UI/              shared XAML project (App.xaml, the page
                                     hosting the TkHostView)
    src/DRAKON.Brix.Core/            core project; Assets/bootstrap.tcl and
                                     Assets/drakon/** (copied to output)
    src/DRAKON.Brix.<Head>/          one head project per platform
    src/libs/DRAKON.Brix.TclBridge/  DrakonRuntime.cs (the boot sequence),
                                     RuntimeHost.cs (the UI-facing owner),
                                     Commands/ (QuitCommand,
                                     DiagnosticReportCommand — app-specific
                                     Tcl commands)
    tests/libs/DRAKON.Brix.TclBridge.Tests/   DrakonRuntimeTests,
                                     DrnFileOpenTests, ProfileOpenTests —
                                     boot DRAKON headlessly and open .drn
                                     files
    examples/                        DRAKON Editor's own example diagrams
                                     (.drn) per target language
    docs/                            DRAKON Editor's documentation (DRAKON.pdf,
                                     per-language generator docs, the .drn
                                     file-format description)
    art/                             icon sources

How the app-side C# works (all of it is DrakonRuntime.Boot):
    Interpreter.Create(ref result)            (default BooleanResultMode)
    interpreter.CacheParsedScripts = true;    DRAKON re-runs the same proc
                                              bodies thousands of times per
                                              file open
    interpreter.ProductionMode = true;        DRAKON never cancels scripts
    TkBootstrap.Register(interpreter, ref error)
    TclTkExtras.RegisterAll(interpreter, ref error)
    TkTclBridge.RegisterHosted(interpreter, tree)   (hosted mode: the
                                              interpreter on its own Tcl
                                              thread, Tk commands marshal
                                              to the UI thread)
    source Assets/bootstrap.tcl, then Assets/drakon/drakon_editor.tcl
Hosted mode (DrakonRuntime.Start(TkHostView)) is what the application
uses; direct mode (DrakonRuntime.StartDirect(assetsDirectory)) runs the
same sequence inline against a headless TkWindow.CreateRoot() for tests.

How to run:
    cd samples/DRAKON.Brix
    dotnet run --project src/DRAKON.Brix.LinuxX11      (or another head)
    dotnet test DRAKON.Brix.slnx                        (headless boot tests)
The heads reference the src/ projects directly, so the sample always
exercises the working tree, not the published packages.

Asset change-note convention ("for DRAKON.Brix"):
  * Assets/drakon/** is vendored VERBATIM (see its PROVENANCE.txt). The
    DRAKON logic Tcl runs unmodified; all adaptation lives in
    Assets/bootstrap.tcl and in the TkCanvas Tcl command bridge.
  * bootstrap.tcl is a NEW file (stock DRAKON has none). Every block in it
    is tagged
        # Added for DRAKON.Brix - because <reason>
    with a "Stock DRAKON / Tcl:" reference comment showing the mechanism
    it stands in for (e.g. empty "package provide snit"/"msgcat" stubs
    because their only consumers are now managed shims).
  * If a genuinely vendored original file ever has to be edited, the
    change is tagged "Removed for DRAKON.Brix" / "Added for DRAKON.Brix"
    and the original lines are COMMENTED OUT, never deleted.
  * The invariant marker text "for DRAKON.Brix" is searchable; grep it to
    find every port-specific change (search src/ and Assets/, not bin/).

samples/TkCanvas_Testing — XAML-declared Tk UI smoke app
=========================================================
Path: samples/TkCanvas_Testing (own solution: DRAKON.Brix.slnx — the app
was scaffolded from the same template and keeps the DRAKON.Brix project
names)

What it is: a small CodeBrix.Platform application whose whole UI is
declared in MainPage.xaml with the CodeBrix.Platform.TkCanvas.Xaml
elements (TkHostView + TkMenubar/TkFrame/TkButton/TkLabel/TkEntry/
TkScrollbar/TkText/...). MainViewModel exposes an ITkWidgetBridge
(Func<string> GetEntryText, Action<string> AppendOutputLine) that the
page wires in DataContextChanged — the bridge-interface pattern
recommended in the TkCanvas AGENT-README for reading/writing widget
state without two-way binding. It demonstrates the XAML path of TkCanvas
without any Tcl script.

How to run:
    cd samples/TkCanvas_Testing
    dotnet run --project src/DRAKON.Brix.LinuxX11      (or another head)

tools/layout-oracle — DEV-ONLY oracle fixture capture
=====================================================
Path: tools/layout-oracle (see its README.txt for the full scenario rules)

What it is: the Tcl scripts that run REAL Tk (wish 8.6.16, Debian) to
capture the behavior fixtures the TkCanvas tests replay headlessly:
    capture_layout.tcl        builds one pack/grid scenario in wish and
                              dumps geometry (one line per window: PATH x y
                              width height reqwidth reqheight ismapped)
    capture_canvas.tcl        runs one canvas scenario and dumps each query
                              result (coords/bbox/find)
    capture_bind.tcl          runs one bind scenario (event dispatch order)
    capture_theming.tcl       captures tk_setPalette / tk_bisque derivation
    random_pack_scenarios.tcl, random_grid_scenarios.tcl,
    random_canvas_scenarios.tcl   deterministic (seeded) scenario generators
    generate_fixtures.sh      regenerates every *.expected from its
                              *.scenario under tests/CodeBrix.Platform
                              .TkCanvas.Tests/Assets/{LayoutOracle,BindOracle}
                              by running wish; echoes the Tk patchlevel

How to run (only when scenarios change or a newer Tk becomes the oracle):
    tools/layout-oracle/generate_fixtures.sh
Requires a host wish (Tk) and a display (X11); windows are created
off-screen and unmanaged, so nothing visibly flashes. The xUnit tests
NEVER run this — they replay the committed fixtures.

Scenario-authoring rules (from the tool README):
  * Windows before use; parents before children.
  * Never packpropagate/gridpropagate on "." with no rootsize — a real Tk
    toplevel then keeps its 200x200 default, which the headless engine
    does not model. Use interior fixed-size frames, or give the root an
    explicit rootsize.
  * Canvas scenarios must NOT contain text items (font-dependent geometry
    would make fixtures machine-specific).
  * The scenario line formats are kept in sync with the C# parsers in
    tests/CodeBrix.Platform.TkCanvas.Tests/Oracle/*.cs.

Optional test data
==================
  * tests/CodeBrix.Platform.TclTk.Extras.Tests/Assets: sample.drn (a real
    DRAKON file for sqlite3 interchange tests) and fonts/ for pdf4tcl
    TrueType tests.
  * tests/CodeBrix.Platform.TkCanvas.Tests/Assets: LayoutOracle,
    CanvasOracle, BindOracle, ThemingOracle (*.scenario + *.expected pairs)
    and Images (GIF/PNG decode fixtures). All vendored; no test needs Tk
    installed.
  * The interpreter tests need no external data.
================================================================================
