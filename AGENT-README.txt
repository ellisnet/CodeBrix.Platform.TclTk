================================================================================
AGENT-README: CodeBrix.Platform.TclTk / .TclTk.Extras / .TkCanvas
A Comprehensive Guide for AI Coding Agents — CONSUMING these libraries
================================================================================

OVERVIEW
--------
This repository produces a three-package family for running Tcl — and classic
Tk user interfaces — inside .NET 10 / CodeBrix.Platform applications:

  * CodeBrix.Platform.TclTk        A fully managed, cross-platform Tcl
                                   interpreter (no native dependencies).
  * CodeBrix.Platform.TclTk.Extras Interpreter-side Tcl command shims:
                                   tclsqlite-compatible "sqlite3" (over
                                   CodeBrix.Sqlite) and pdf4tcl (over
                                   CodeBrix.PdfDocuments).
  * CodeBrix.Platform.TkCanvas     A retained-mode reimplementation of the
                                   classic Tk widget toolkit, drawn entirely
                                   onto a SkiaSharp surface, with a ready-made
                                   CodeBrix.Platform host control.

The packages publish together at one shared version. Reference only what you
need: the interpreter alone is GUI-free; Extras adds the database/PDF command
surfaces; TkCanvas adds the widget toolkit (and references the interpreter and
the CodeBrix.Platform UI stack itself).

NAMING RULES (apply to ALL code you write against or inside these libraries)
  * CodeBrix.Platform.TclTk is a port of the Eagle project by Joe Mistachkin
    and the Eagle Development Team (https://github.com/mistachkin/eagle). The
    word "Eagle" appears ONLY in copyright/license text and per-file
    "//was previously:" provenance comments. Do NOT introduce "Eagle" into any
    namespace, type, method, or identifier.
  * "TclTk" refers to THIS managed engine; "Tcl" (TclBridge, TclApi, ...)
    refers to the optional NATIVE Tcl/Tk library the engine can bridge to.
    Keep them distinct.
  * The UI platform is CodeBrix.Platform. Do not name its upstreams in code,
    comments, or docs.

INSTALLATION
------------
    dotnet add package CodeBrix.Platform.TclTk.BsdLicenseForever
    dotnet add package CodeBrix.Platform.TclTk.Extras.BsdLicenseForever
    dotnet add package CodeBrix.Platform.TkCanvas.BsdLicenseForever

PackageIds carry license suffixes; code namespaces do not (they are
CodeBrix.Platform.TclTk, CodeBrix.Platform.TclTk.Extras, and
CodeBrix.Platform.TkCanvas). Target framework: net10.0 or higher.

Dependencies you get transitively:
  * .TclTk   -> System.Security.Cryptography.Pkcs
  * .Extras  -> .TclTk, CodeBrix.Sqlite, CodeBrix.PdfDocuments
  * .TkCanvas-> .TclTk, SkiaSharp, CodeBrix.Imaging (core),
                CodeBrix.Platform + its SkiaSharp XAML canvas (for the
                built-in host control; headless/offscreen use works without
                ever touching the UI stack at runtime)

================================================================================
1. THE INTERPRETER (CodeBrix.Platform.TclTk)
================================================================================

KEY NAMESPACES
--------------
The public API lives under CodeBrix.Platform.TclTk._Components.Public:

    using CodeBrix.Platform.TclTk._Components.Public;

Notable sub-namespaces:

    CodeBrix.Platform.TclTk._Components.Public   Public API (Interpreter, ...)
    CodeBrix.Platform.TclTk._Interfaces.Public    Public interfaces
    CodeBrix.Platform.TclTk._Containers.Public    Public collection types
    CodeBrix.Platform.TclTk._Commands             Built-in Tcl commands

CORE API
--------
Interpreter (IDisposable — always wrap in using):

    static Interpreter Interpreter.Create(ref Result result)
        Create and initialize an interpreter (loads the embedded script
        library). Returns null on failure, with details in 'result'.

    ReturnCode EvaluateScript(string text, ref Result result)
    ReturnCode EvaluateExpression(string text, ref Result result)
    ReturnCode SetVariableValue(string name, string value, ref Result error)
    ReturnCode GetVariableValue(string name, ref Result value, ref Result error)
    ReturnCode ProvidePackage(string name, Version version, ref Result result)
    bool ProductionMode { get; set; }   // see PERFORMANCE below

Extension surface (for registering your own Tcl commands): AddCommand,
AddIExecute, AddObject, DoesVariableExist, CreateChildInterpreter, plus the
public ICommand/IExecute interfaces.

ReturnCode (enum): Ok, Error, Return, Break, Continue (Tcl-compatible codes).
Result: the boxed result/error value; converts to string.

Minimal example:

    using CodeBrix.Platform.TclTk._Components.Public;

    Result result = null;
    using (Interpreter interpreter = Interpreter.Create(ref result))
    {
        ReturnCode code = interpreter.EvaluateScript("expr {6 * 7}", ref result);
        if (code == ReturnCode.Ok)
            System.Console.WriteLine(result); // 42
    }

LANGUAGE LEVEL
--------------
The engine implements the Tcl language including namespaces, upvar/uplevel,
arrays, glob/file/open, regexp, and the Tcl 8.5/8.6 features "binary"
(format/scan + base64/hex/uuencode encode/decode), "tailcall" (with stack
elimination), "trace" (variable, command rename/delete, and execution
traces), and "{*}" argument expansion. Behavior is validated against real
tclsh 8.6 as the oracle; DRAKON Editor's code generator runs byte-identical
to native tclsh on it.

Known divergences a consumer may notice (all deliberate or documented):
  * "binary format" integers wider than 64 bits wrap instead of erroring.
  * trace's variable "array" operation is accepted but never fires; command
    delete traces fire only for deletions via [rename].
  * BOOLEAN RESULT RENDERING — see the dedicated section below; a one-line
    opt-in (BooleanResultMode.TclshCompat) makes it match real tclsh.

*** BooleanResultMode — MAKE BOOLEAN RESULTS MATCH real tclsh ***
--------------------------------------------------------------------------
By DEFAULT this engine renders a boolean RESULT as the .NET-style string
"True"/"False", where real tclsh renders the canonical "1"/"0". This applies
both to a boolean-valued [expr] AND to the boolean-returning commands:

    expr {1 && 1}               -> "True"   (tclsh: "1")
    expr {1 < 0}                -> "False"  (tclsh: "0")
    string equal a a            -> "True"   (tclsh: "1")
    info complete {set x}       -> "True"   (tclsh: "1")
    interp exists {} / issafe   -> "True"/"False"  (tclsh: "1"/"0")
    dict exists {a 1} a         -> "True"   (tclsh: "1")
    package vsatisfies 8.6 8.5  -> "True"   (tclsh: "1")
    eof $chan / fblocked $chan  -> "True"/"False"  (tclsh: "1"/"0")

This is inherited Eagle behavior. Boolean CONTEXTS are unaffected either way
-- [if], [while], &&/|| short-circuit, and the ?: ternary coerce the value to
an actual boolean, so conditionals always behave correctly. The divergence
only BITES when code treats a boolean result as a LITERAL STRING (verified
live against tclsh 8.6.16), i.e. in DEFAULT (EagleCompat) mode:

    pattern                       tclsh      EagleCompat (ours)  consequence
    ----------------------------  ---------  ------------------  ----------------------------
    set x [expr {2>1}]                                           x is "True", not "1" ...
      switch -- $x {1 {..} 0 {..}} matches 1  falls to default   ... wrong branch dispatched
      string length $x            1          4                   ... off by 3
      "count=$x" (output/store)   count=1    count=True          ... wrong text to file/SQLite/gen
    string equal $a $b            1/0        True/False          ... string compare / switch fails

Risk categories: [switch] on a computed flag, string-identity comparisons,
and anywhere a boolean result is EMITTED or STORED (a .drn, generated source
code, a UI string).

THE FIX -- choose the mode ONCE, at Interpreter.Create() (it cannot be
changed afterward, so nothing can flip it mid-run and desync your scripts):

    using CodeBrix.Platform.TclTk._Components.Public;

    Result r = null;
    using Interpreter interp = Interpreter.Create(
        ref r, BooleanResultMode.TclshCompat);
    // now EVERY boolean result above renders "1"/"0", byte-for-byte tclsh:
    //   expr {1 && 1} -> "1";  string equal a a -> "1";  eof $c -> "0"; ...

  * BooleanResultMode.EagleCompat (DEFAULT) -- historical "True"/"False"
    rendering; nothing changes for existing consumers. This is what
    Create(ref r) uses when you omit the argument.
  * BooleanResultMode.TclshCompat -- canonical "1"/"0" for boolean [expr]
    results AND boolean-returning commands (string equal, info complete,
    info default, interp exists, interp issafe, dict exists,
    package vsatisfies, eof, fblocked, and the string starts/ends helpers).
    Boolean CONTEXTS are unchanged either way, so if/while/&&/||/ternary
    behave identically in both modes.

  The mode is passed to Create() and is then READ-ONLY -- the
  Interpreter.BooleanResultMode property has a getter but no setter, so it is
  fixed for the interpreter's lifetime by design.

  Not covered (these are NOT boolean-rendering issues, just feature gaps):
  "string is dict" (no such class in tclsh 8.6), "file owned", and the
  "chan eof"/"chan blocked" aliases are unimplemented, unrelated to this mode.

PERFORMANCE
-----------
The managed interpreter is substantially slower than native tclsh (order of
magnitude, workload-dependent). For batch workloads set:

    interpreter.ProductionMode = true;   // ~1.8x faster, byte-identical

Caveat: ProductionMode disables the per-command ready checks, so
[interp cancel]-style cancellation is not prompt while enabled. Use it for
batch/generation work, not for interpreters that must stay interruptible.

================================================================================
2. THE EXTRAS (CodeBrix.Platform.TclTk.Extras)
================================================================================

Entry point (the only public type):

    using CodeBrix.Platform.TclTk.Extras;

    Result error = null;
    TclTkExtras.RegisterAll(interpreter, ref error);      // or
    TclTkExtras.RegisterSqlite3(interpreter, ref error);  // or
    TclTkExtras.RegisterPdf4Tcl(interpreter, ref error);

Registration also runs "package provide sqlite3 3.45.0" / "package provide
pdf4tcl 0.7", so Tcl "package require" lines succeed.

"sqlite3 NAME PATH" — tclsqlite-compatible database command over
CodeBrix.Sqlite. Handle verbs: eval (flat-list and per-row script modes),
onecolumn, changes, close. Binding rules replicate tclsqlite exactly:
:name/@name/$name host parameters resolve from the CALLER's Tcl frame; an
UNSET variable binds SQL NULL; a set-but-empty variable binds '' TEXT; SQL
NULL reads back as "". String-repped Tcl values bind as TEXT (never sniffed
into numbers), so numeric-looking text like "007" survives byte-for-byte.
The open path is PRAGMA-neutral (no WAL, no foreign-key enforcement,
rollback journal), so written files are interchangeable with stock-Tcl
SQLite files (e.g. DRAKON .drn files). SQL text passes through verbatim.

"pdf4tcl::new / ::loadBaseTrueTypeFont / ::createFont" plus the pdf4tcl 0.7
object surface (startPage, setFont, setFillColor, setStrokeColor,
setLineStyle, getStringWidth, text, line, rectangle, polygon, write,
destroy) — over CodeBrix.PdfDocuments. The pdf4tcl::paper_sizes and
pdf4tcl::units Tcl array variables are published too. Coordinates replicate
pdf4tcl's model: user coordinates are margin-box-relative; -orient 1
(default) means top-left origin y-down; text -x/-y is the BASELINE origin.
Deliberately ignored (accepted, never thrown): text -angle/-xangle/-yangle,
page -rotate, -compress.

================================================================================
3. THE TOOLKIT (CodeBrix.Platform.TkCanvas)
================================================================================

WHAT IT IS
----------
The classic Tk widget toolkit re-implemented as original CodeBrix code and
drawn entirely onto one Skia surface: the pack and grid geometry managers,
the Tk bind/focus/grab event system, the full widget set (frame, labelframe,
label, button, entry, text, listbox, treeview, combobox, checkbutton,
radiobutton, panedwindow, scrollbar, separator, menus), the canvas widget
with all nine item types and its scene-graph search/geometry surface, photo
images, overlay toplevels with a mini window-manager (wm/grab), clipboard,
fonts, and color theming (the classic Tk look by default, seventeen named
schemes, tk_setPalette, the option database, and the ttk::style surface).
Geometry and behavior are validated against real Tk 8.6.16
(wish) — layout results, canvas coords/bbox/find, and GIF pixel decoding are
byte-identical on the vendored oracle fixtures.

THE HOST CONTROL
----------------
Both UI-building paths below run inside a TkHostView
(CodeBrix.Platform.TkCanvas.Hosting) — one control that owns everything:
rendering (TkRenderer over an SKXamlCanvas), pointer routing with
double-click and wheel support, ALL keyboard routing through the
hidden-input-element IME sink (entry/text typing included), the OS
clipboard bridge, the dispatcher bridge for "after" timers and the
synchronous "update" flush, and the host-resize pipeline (root geometry
follows the control; pack/grid re-run; overlays re-clamp).

TWO WAYS TO BUILD THE UI (both fully supported; they mix freely)
-----------------------------------------------------------------

PATH 1 — DECLARE THE UI IN XAML (preferred for CodeBrix.Platform apps).
UI creation/definition/declaration belongs in the XAML; the code-behind
stays thin. The CodeBrix.Platform.TkCanvas.Xaml namespace has a declaration
element for every widget (TkFrame, TkLabelframe, TkLabel, TkButton,
TkEntry, TkText, TkCanvasView, TkListbox, TkTreeview, TkCombobox,
TkCheckbutton, TkRadiobutton, TkPanedwindow, TkScrollbar, TkSeparator,
TkMenubar/TkMenu/TkMenuItem/TkMenuSeparator) plus TkPhoto for named photo
images. Nest them inside the TkHostView; the host materializes the real
widget tree when it loads. Layout is declared per element (pack by
default: Side/Fill/Expand/PackAnchor/PadX/PadY; set GridRow/GridColumn/
Sticky to use grid instead). Common Tk options are typed properties
(Text, Image, Relief, BorderWidth, TkBackground, ...); EVERY other option
is reachable through the Options catch-all (a Tcl-style pair list, e.g.
Options="-highlightthickness 0"). Command properties bind to view-model
commands (e.g. a CodeBrix.Platform.Simple SimpleCommand):

    <Page ...
          xmlns:tkhost="using:CodeBrix.Platform.TkCanvas.Hosting"
          xmlns:tk="using:CodeBrix.Platform.TkCanvas.Xaml">
      <Page.DataContext><vm:MainViewModel /></Page.DataContext>

      <tkhost:TkHostView x:Name="TkHost" Theme="DarkNew">
        <tk:TkPhoto Name="backIcon" File="Assets/tk-back.gif" />

        <tk:TkMenubar Side="Top" Fill="X">
          <tk:TkMenu Label="File" Underline="0">
            <tk:TkMenuItem Label="New" Accelerator="Ctrl+N"
                           Command="{Binding NewCommand}" />
            <tk:TkMenuSeparator />
            <tk:TkMenuItem Label="About" Command="{Binding AboutCommand}" />
          </tk:TkMenu>
        </tk:TkMenubar>

        <tk:TkFrame Side="Top" Fill="X" Relief="raised" BorderWidth="1">
          <tk:TkButton Side="Left" Image="backIcon" Relief="flat"
                       BorderWidth="0" Command="{Binding BackCommand}" />
          <tk:TkButton Side="Left" Text="Greet"
                       Command="{Binding GreetCommand}" />
        </tk:TkFrame>

        <tk:TkFrame Side="Top" Fill="X">
          <tk:TkLabel Side="Left" Text="Name:" PadX="4" />
          <tk:TkEntry x:Name="NameEntry" Side="Left" Fill="X" Expand="True" />
        </tk:TkFrame>

        <tk:TkScrollbar Side="Right" Fill="Y" Orient="vertical" For="Output" />
        <tk:TkText x:Name="Output" Fill="Both" Expand="True" />
      </tkhost:TkHostView>
    </Page>

Scope note: this is DECLARATION-time setup plus one-shot command wiring —
there is deliberately no live two-way binding between widget state and
view-model properties. When the view model needs widget state (read the
entry, append to the text widget, draw canvas items), use the bridge-
interface pattern the CodeBrix.Samples apps use: the view model exposes
Func/Action properties behind a small interface, and the page wires them
in DataContextChanged — the whole code-behind stays this size:

    public MainPage()
    {
        DataContextChanged += (s, e) =>
        {
            if (DataContext is ITkWidgetBridge bridge)
            {
                bridge.GetEntryText = () => NameEntry.EntryWidget?.Text ?? "";
                bridge.AppendOutputLine = line =>
                {
                    Output.TextWidget?.Insert("end - 1 chars", line + "\n");
                    TkHost.RequestUpdate();
                };
            }
        };
        InitializeComponent();
    }

Every element exposes its materialized widget (NameEntry.EntryWidget,
Output.TextWidget, myCanvas.CanvasWidget, ...) — null until the host
loads. Setting a declared property AFTER load reconfigures the live
widget. The samples/DRAKON.Brix app in this repository is the complete
working reference for this path.

PATH 2 — BUILD THE UI IN CODE (for generated/dynamic UIs and non-XAML
hosts; this is also what a Tcl command bridge drives). Create windows and
widgets directly under the host's root:

    using CodeBrix.Platform.TkCanvas.Canvas;    // CanvasWidget
    using CodeBrix.Platform.TkCanvas.Hosting;   // TkHostView
    using CodeBrix.Platform.TkCanvas.Layout;    // PackLayout, GridLayout
    using CodeBrix.Platform.TkCanvas.Widgets;   // the widget classes
    using CodeBrix.Platform.TkCanvas.Windowing; // TkWindow

    var host = new TkHostView();
    containerGrid.Children.Add(host);

    TkWindow buttonWindow = host.Root.CreateChild("hello");
    var button = new ButtonWidget(buttonWindow);
    button.Configure(new Dictionary<string, string> { { "-text", "Hello" } });
    button.Invoked += () => Console.WriteLine("clicked");
    PackLayout.Configure(buttonWindow, new PackOptions { Side = Side.Top });

    TkWindow canvasWindow = host.Root.CreateChild("c");
    var canvas = new CanvasWidget(canvasWindow);
    canvas.Configure(new Dictionary<string, string>
    {
        { "-width", "300" }, { "-height", "200" }, { "-background", "white" },
    });
    PackLayout.Configure(canvasWindow, new PackOptions
    {
        Fill = Fill.Both, Expand = true,
    });
    canvas.Execute(new[] { "create", "rectangle", "20", "20", "120", "80",
                           "-fill", "red", "-tags", "box" });

BUILDING UIs — the shared concepts
-----------------------------------
  * Windows: TkWindow.CreateRoot() / window.CreateChild(name). Every widget
    class takes its TkWindow in the constructor and owns it.
  * Layout: PackLayout.Configure(window, new PackOptions {...}) and
    GridLayout.Configure(window, new GridOptions {...}) implement Tk's pack
    (cavity model) and grid (constraint solver), including forget/propagate
    and the pack/grid per-container exclusivity rule.
  * Options: widget.Configure(dictionary) with Tk option names ("-text",
    "-background", "-relief", ...). Unknown-but-valid options are accepted
    and stored (never thrown) — the toolkit-wide deferral discipline.
  * Events: window.Tree gives the WindowTree — Bindings (Tk bind patterns,
    bindtags precedence instance -> class -> root -> all), SetFocus,
    GrabWindow, PointerEvent/KeyEvent/VirtualEvent for synthetic input.
  * The canvas widget: new CanvasWidget(window), then either the typed API
    (Create, BindItem, ...) or the string layer
    canvas.Execute(new[] { "create", "rectangle", "10", "10", "60", "40",
    "-fill", "red" }) which takes Tcl argument shapes verbatim and returns
    what Tk returns (this is what a Tcl bridge should call).
  * Photo images: window.Tree.Images (the "image" command model) —
    images.Execute(new[] { "create", "photo", "icon", "-file", "x.gif" }),
    then "-image icon" on labels/buttons/canvas items/menu entries/treeview
    items. GIF decode, PNG/GIF write, base64 -data, blank/put/copy/
    transparency, and the "-format window -data .path" widget snapshot are
    implemented (decode/encode via CodeBrix.Imaging).
  * Clipboard: window.Tree.Clipboard implements Tk's "clipboard" command
    (clear/append/get). TkHostView bridges it to the OS clipboard; headless
    it stays in-process.
  * Menus/dialogs: MenuWidget + tree.Menus (popups, cascades, menubar,
    tk_popup) and MessageDialog (tk_messageBox/tk_dialog) as Skia overlay
    toplevels; tree.WindowManager is the wm surface (title, geometry,
    withdraw/deiconify, transient, overrideredirect, grab modality).
  * Fonts: tree.Fonts is the single measurement seam ("font measure"/
    "font metrics" and the painter share one SKFont by construction).
  * update/after: tree.Scheduler — After(ms, action), Update(),
    UpdateIdleTasks(). Geometry reads after UpdateIdleTasks() are final
    (Tk's synchronous "update" semantics).
  * Theming: tree.Theme is the whole tree's color scheme (a TkTheme); the
    default IS the classic battleship-gray Tk look, byte-identical to the
    pre-theming rendering. Named schemes come from TkThemeRegistry:
    "Classic" (aliases "Default"/"clam"/"alt"), "Bisque", and fifteen
    built-ins (DarkNew, LightNew, DarkPlus, LightPlus, DarkModern,
    LightModern, Monokai, DimmedMonokai, SolarizedDark, SolarizedLight,
    Abyss, QuietLight, Red, TomorrowNightBlue, KimbieDark). In XAML set
    TkHostView Theme="DarkNew" (case-insensitive; unknown names are
    ignored, so the classic look stays) or assign host.ThemePalette in
    code; switching repaints the running UI with no per-widget
    reconfiguration, and explicitly configured widget colors are never
    touched (explicit always wins). A CUSTOM theme is just a plain
    mutable TkTheme — every default the toolkit paints is a string
    property holding a Tk color spec (Background, Foreground,
    FieldBackground, SelectBackground, MenuActiveBackground,
    TitleBarBackground, TroughColor, dialog accents, ...; unset ones keep
    their classic values) — assign it to ThemePalette/tree.Theme or
    TkThemeRegistry.Register(name, factory) it to make it available by
    name in XAML. tree.SetPalette(args) is tk_setPalette (derive a whole
    scheme from one base color, Tk's exact derivation math,
    wish-verified); TkTheme.CreateBisque() is tk_bisque.
  * Option database: tree.OptionDatabase implements "option add/get/clear"
    (X-resource patterns, four priorities, wish-verified matching: highest
    priority wins, ties go to the most recently added — no specificity).
    Entries apply when a widget is CREATED, for options not explicitly
    configured; later additions never restyle existing widgets (Tk's rule).
    XAML: TkHostView OptionsDatabase="*Button.background green" (one
    "pattern value ?priority?" per line).
  * ttk::style: tree.Styles implements configure/map/lookup and theme
    names/use/create (per-theme isolated tables; the standard names
    default/clam/alt/classic all exist). Widgets resolve colors explicit >
    option-database (creation-time) > style map (state-matched) > style
    configure > theme default; the per-widget -style option redirects
    resolution (class styles are TButton, TLabel, ...). The ttk
    element/layout engine is deferred (accept-and-no-op). tree.Styles
    .Execute(words) takes "ttk::style ..." argument shapes verbatim.

RUNNING Tcl/Tk PROGRAMS — THE Tcl COMMAND BRIDGE
-------------------------------------------------
CodeBrix.Platform.TkCanvas.Tcl.TkTclBridge wires the classic Tk command
surface onto these widget classes so an UNMODIFIED Tcl/Tk application runs
on the toolkit. Bootstrap, then register the bridge:

    using CodeBrix.Platform.TkCanvas;       // TkBootstrap
    using CodeBrix.Platform.TkCanvas.Tcl;   // TkTclBridge

    Result error = null;
    TkBootstrap.Register(interpreter, ref error);   // ::tcl_version/::tk_version
                                                    // /::tk_patchLevel + Tk/Img
    // Headless / same-thread:
    TkTclBridge bridge = TkTclBridge.Register(interpreter, host.Tree);
    // OR hosted (interpreter on its own Tcl thread; Tk commands marshal to
    // the UI thread, so modal dialogs can block the script safely):
    TkTclBridge bridge = TkTclBridge.RegisterHosted(interpreter, host.Tree);
    bridge.FileDialogs = new TkHostFileDialogs();   // native file/folder pickers
    bridge.Post(interp => interp.EvaluateScript("source app.tcl", ref r));

TkBootstrap.Register alone (without the bridge) just satisfies the package
requires and version gates; the bridge is what makes "button", "pack",
"grid", "bind", "canvas", "menu", "wm", "image", "font", "option",
"ttk::style", "tk_messageBox", "after"/"update", etc. real Tcl commands.

Registered command surface (all drive the widget classes above):
  * Widget creation: frame labelframe label button entry text listbox
    checkbutton radiobutton scrollbar panedwindow canvas combobox treeview
    separator menu toplevel, and the ttk:: forms — each returns its path
    and registers a ".path subcommand" instance command.
  * Geometry: pack (+ forget/propagate/info/slaves) and grid (+ forget/
    row|columnconfigure/size/slaves/info), over the oracle-verified engines.
  * Events: bind (Tk %-substitution; X11 <Button-4/5> mirror to MouseWheel),
    bindtags; -command / -textvariable / -variable wired through the
    interpreter's variable traces (a check/radio group over one variable
    shares one ToggleVariable).
  * Windowing: wm (title/geometry/withdraw/deiconify/transient/
    overrideredirect/resizable over the overlay window-manager), winfo,
    destroy, focus, grab, raise/lower. "." configure -menu builds the root
    menubar (a bar packed above existing content, sharing the menu's entries
    so entryconfigure stays live). "tkwait visibility" flushes and returns.
  * Resources: image (photo names become instance commands), font (measure/
    metrics through the R2 seam), clipboard, option, ttk::style, and the
    tk_setPalette / tk_bisque / tk_classic / tk_<theme> appliers.
  * Dialogs: tk_messageBox / tk_dialog as Skia overlays (block the Tcl
    thread until answered); tk_popup; tk_getOpenFile / tk_getSaveFile /
    tk_chooseDirectory via the ITkFileDialogProvider host seam (the only
    native escape). A ModalAutoResponder answers them headless/scripted.

samples/DRAKON.Brix is the reference consumer: the ACTUAL DRAKON Editor
1.33 Tcl (vendored under Assets/drakon, run unmodified) booted on the
interpreter + Extras shims + this bridge inside one TkHostView. The whole
app-side C# is DrakonRuntime (create interpreter, register Extras +
TkBootstrap + TkTclBridge.RegisterHosted, source bootstrap.tcl then
drakon_editor.tcl) plus a ~15-line code-behind; there is no view model.

HEADLESS / CUSTOM HOSTS
-----------------------
Everything except Hosting/ runs without a UI: render into any SKCanvas via
TkRenderer.Render(root, canvas) (or RenderWindow for one subtree), drive
input with tree.PointerEvent/KeyEvent, pump with scheduler.UpdateIdleTasks().
A custom host supplies ITkDispatcher (UI-thread posting + timers; assign to
tree.Scheduler.Host), and optionally ITkClipboard (tree.Clipboard.Host) and
ITextInputSink (tree.InputSink) — TkHostDispatcher / TkHostClipboard /
TkHostTextInputSink in Hosting/ are the CodeBrix.Platform implementations
and the reference for writing new ones.

FIDELITY NOTES / KNOWN EDGES
----------------------------
  * Color names follow Tk 8.6 (the "Web colors" list): green=#008000,
    gray/grey=#808080, maroon=#800000, purple=#800080.
  * Text-bearing widgets' natural sizes are font-stack dependent (formula-
    consistent with Tk, not pixel-identical to a given X server's fonts).
  * GIF writes are lossless up to 255 distinct colors; a full 256-color
    image gets one color remapped (the encoder reserves a transparency
    slot). PNG writes round-trip exactly.
  * Deferred corners accept-and-no-op rather than throw: canvas postscript,
    in-canvas text-item editing, the window canvas item's embedded paint,
    and the screen-reader/accessibility bridge (a fully Skia-drawn UI has no
    native control tree).
  * IME: committed text and composition flow through the hidden-input
    element; full pre-edit display depends on the platform head's
    composition events.

CODING CONVENTIONS (CodeBrix family)
------------------------------------
  * Target framework net10.0 only; no multi-targeting.
  * Nullable reference types are OFF in .TclTk/.Extras/.TkCanvas code (no
    "#nullable enable", no "?" on reference types, no null-forgiveness "!").
  * No global usings; usings are explicit and at the top.
  * Tests use xUnit v3 + SilverAssertions ("x.Should()..." assertions).
  * Sanctioned deviations in the PORTED interpreter only (documented, do not
    imitate in new code): XML docs off + NoWarn 1591, upstream block-scoped
    namespaces, per-file "//was previously:" provenance comments. New code —
    including everything in .Extras and .TkCanvas — has XML docs ON, no
    suppressions, and file-scoped namespaces.

TESTING
-------
    dotnet test CodeBrix.Platform.TclTk.slnx

Tests live in tests/ (interpreter, Extras, TkCanvas), all xUnit v3 +
SilverAssertions, and run strictly sequentially (interpreters keep heavy
process-global state). Behavior fixtures captured from real tclsh/wish
8.6.16 are VENDORED under the test projects' Assets/ — tests never invoke
the real tools. Maintainers: the capture tooling lives in
tools/layout-oracle/ (dev-only; requires tk + a display), and the project
history/plan lives outside this file.
================================================================================
