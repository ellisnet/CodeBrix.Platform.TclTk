================================================================================
AGENT-README: CodeBrix.Platform.TclTk
A Comprehensive Guide for AI Coding Agents
================================================================================

OVERVIEW
--------
CodeBrix.Platform.TclTk is a fully managed, cross-platform implementation of
the Tcl scripting language for .NET 10.0+. It embeds a complete Tcl interpreter
into your application: variables, expressions, the core Tcl command set,
procedures, namespaces, a built-in (assembly-embedded) Tcl script library, and
a two-way .NET object-interop bridge. It has no native dependencies and runs on
Windows, Linux, and macOS.

CodeBrix.Platform.TclTk is a port of the Eagle project by Joe Mistachkin and the
Eagle Development Team (https://github.com/mistachkin/eagle). All namespaces,
type names, and members use "CodeBrix.Platform.TclTk" / "TclTk" instead of the
upstream "Eagle" naming. The word "Eagle" appears ONLY in copyright/license text
and in per-file "//was previously:" provenance comments. Do NOT introduce the
"Eagle" name into any namespace, type, method, or identifier.

Note the deliberate distinction preserved from upstream: "TclTk" refers to THIS
managed engine, while "Tcl" (e.g. TclBridge, TclApi, TclWrapper) refers to the
optional NATIVE Tcl/Tk library this engine can bridge to. Keep them distinct.

CodeBrix.Platform.TclTk is the interpreter member of a three-package family
built from this one repository: CodeBrix.Platform.TclTk (this interpreter),
CodeBrix.Platform.TclTk.Extras (interpreter-side command extensions, in this
repository — see the EXTRAS section below), and the planned
CodeBrix.Platform.TkCanvas (a Tk-widget-toolkit-on-Skia). Each ships as its OWN
NuGet package; the family publishes together at one shared version.


INSTALLATION
------------
NuGet Package: CodeBrix.Platform.TclTk.BsdLicenseForever
Dependencies:  System.Security.Cryptography.Pkcs (Microsoft first-party; used for
               signature verification; matches the upstream Eagle package's dependency)

    dotnet add package CodeBrix.Platform.TclTk.BsdLicenseForever

The NuGet PackageId carries the ".BsdLicenseForever" suffix; the code namespace
is "CodeBrix.Platform.TclTk" (without the suffix). Target framework: .NET 10.0
or higher.


KEY NAMESPACES
--------------
The public API lives under CodeBrix.Platform.TclTk._Components.Public:

    using CodeBrix.Platform.TclTk._Components.Public;

Notable sub-namespaces (rebranded from the upstream Eagle._* hierarchy):

    CodeBrix.Platform.TclTk._Components.Public    Public API surface (Interpreter, ...)
    CodeBrix.Platform.TclTk._Components.Private    Implementation detail (internal)
    CodeBrix.Platform.TclTk._Interfaces.Public     Public interfaces
    CodeBrix.Platform.TclTk._Containers.Public      Public collection types
    CodeBrix.Platform.TclTk._Commands               Built-in Tcl commands
    CodeBrix.Platform.TclTk._Functions              Built-in expression functions
    CodeBrix.Platform.TclTk._Operators              Built-in expression operators
    CodeBrix.Platform.TclTk._Hosts                  Interpreter host abstractions


CORE API REFERENCE
------------------
Interpreter (CodeBrix.Platform.TclTk._Components.Public.Interpreter)
  The main entry point. Create one with the static factory, use it, dispose it.
  Interpreter is IDisposable; always wrap it in a using block.

    static Interpreter Interpreter.Create(ref Result result)
        Create and initialize an interpreter (loads the embedded script library).
        Returns null on failure, with details in 'result'.

    ReturnCode EvaluateScript(string text, ref Result result)
        Evaluate a Tcl script; the script result (or error) is placed in 'result'.

    ReturnCode EvaluateExpression(string text, ref Result result)
        Evaluate a single Tcl expression.

    ReturnCode SetVariableValue(string name, string value, ref Result error)
    ReturnCode GetVariableValue(string name, ref Result value, ref Result error)
        Get/set Tcl variables from managed code.

ReturnCode (enum) — Ok, Error, Return, Break, Continue (Tcl-compatible codes).
Result — the boxed result/error value; converts to string.

Minimal example:

    using CodeBrix.Platform.TclTk._Components.Public;

    Result result = null;

    using (Interpreter interpreter = Interpreter.Create(ref result))
    {
        ReturnCode code = interpreter.EvaluateScript("expr {6 * 7}", ref result);

        if (code == ReturnCode.Ok)
            System.Console.WriteLine(result); // 42
    }


CODING CONVENTIONS (CodeBrix family)
------------------------------------
  * Target framework net10.0 only; no multi-targeting.
  * Nullable reference types are OFF (no "#nullable enable", no "?" on reference
    types, no null-forgiveness "!").
  * No global usings; no ImplicitUsings. Usings are explicit and at the top.
  * No project-wide warning suppression EXCEPT the documented CS1591 case below.
  * Tests use xUnit v3 + SilverAssertions (fluent "x.Should()..." assertions).

DOCUMENTED, SANCTIONED DEVIATIONS (large fidelity port)
  These follow the CodeBrix precedent for very large ports (CodeBrix.AssemblyTools,
  CodeBrix.Platform.OpenGL):

  * <GenerateDocumentationFile>false</> + <NoWarn>1591</NoWarn>. The ported engine
    exposes thousands of public members with no upstream XML doc comments;
    documenting every one is out of scope for v1. This is the ONLY warning
    suppression in the library.
  * The ported engine source retains upstream BLOCK-SCOPED namespaces
    ("namespace X { ... }") to minimize churn/risk across ~1000 files. New
    CodeBrix-authored files (tests, helpers) use file-scoped namespaces.
  * Every ported .cs file carries a "//was previously: Eagle.<original>;"
    provenance comment on its namespace line.


ARCHITECTURE
------------
The engine mirrors the upstream Library/ layout, rebranded to
CodeBrix.Platform.TclTk. The public Interpreter delegates parsing/evaluation to
internal engine components (Components.Private), which dispatch to built-in
Commands, Functions, and Operators, backed by Containers (collections),
Interfaces, and Hosts. A Tcl core script library is embedded in the assembly as
compiled resources and loaded at interpreter creation.

Target is a single cross-platform net10.0 ("core") build: Windows-only,
desktop-GUI, .NET-Framework-only, native-interop, and enterprise code-signing
features from upstream are compiled out.


PORT-ADDED TCL 8.5/8.6 FEATURES (not present in upstream Eagle)
---------------------------------------------------------------
Four core-language features that upstream Eagle lacks (its lineage targets
Tcl 8.4-era semantics) were implemented by this port, validated line-by-line
against real tclsh 8.6 as the behavioral oracle:

  * "binary" command - format / scan (the full field-specifier
    mini-language: a A b B h H c s S t i I n w W m f r R d q Q x X @, counts,
    "*", and the "u" flag) plus encode / decode for base64, hex, and
    uuencode with -maxlen / -wrapchar / -strict. Implementation:
    Commands/Binary.cs + Components/Private/BinaryOps.cs. Known deliberate
    divergences: integers wider than 64 bits wrap instead of erroring (the
    engine has no bignum; consistent with engine-wide parsing), and inputs
    that PANIC or SEGFAULT stock tclsh 8.6.16 (X0-then-write, counts over
    int.MaxValue) are handled gracefully.

  * "tailcall" command - full Tcl 8.6 semantics: recorded on the procedure
    frame, fires only on normal return (a catch sees the return code but the
    tailcall still fires; error/break discard it), target resolved in the
    current namespace but executed at the caller's level, and chained
    tailcalls (self/mutual recursion) do not grow the stack (trampoline
    hand-off). Implementation: Commands/Tailcall.cs +
    Components/Private/TailcallOps.cs + small hooks in the four
    procedure/lambda Execute methods and Commands/Apply.cs.

  * "trace" command - variable traces (read / write / unset, element and
    whole-array, legacy trace variable / vdelete / vinfo forms), command
    traces (rename / delete via the [rename] command), and execution traces
    (enter / leave / enterstep / leavestep). Implementation:
    Commands/Trace.cs + Components/Private/ScriptTraceOps.cs, riding the
    engine's own ITrace machinery for variables and a zero-cost-when-unused
    hook in Engine.Execute for execution traces. Known deliberate
    divergences: the variable "array" operation is accepted but never fires
    (no engine hook exists for it); command delete traces fire only for
    deletions performed via [rename]; a FAILING write trace on a
    never-defined variable rolls the variable back instead of leaving the
    half-written value.

  * "{*}" argument expansion (Tcl 8.5) - a word beginning with {*} followed
    by non-whitespace is split, after substitution, into separate command
    words; a lone {*} stays the literal "*". Implementation: a prefix check
    in Parser.ParseCommand (TokenFlags.Expand) plus expansion at the
    argument-building loop in Engine. This unblocks DRAKON's export_pdf.tcl
    and mwindow.tcl idioms.

The development oracle scripts comparing all of this against real tclsh
live with the validation harness (see ~/ClaudeHome/tcltk-validation-harness
on the dev machine); the distilled cases are permanent xUnit tests in
tests/CodeBrix.Platform.TclTk.Tests (BinaryFormatTests, BinaryScanTests,
BinaryEncodeDecodeTests, TailcallCommandTests, TraceCommandTests,
ArgumentExpansionTests).


THE EXTRAS LIBRARY (CodeBrix.Platform.TclTk.Extras)
---------------------------------------------------
src/CodeBrix.Platform.TclTk.Extras builds the second NuGet package of this
repository: CodeBrix.Platform.TclTk.Extras.BsdLicenseForever (root namespace
CodeBrix.Platform.TclTk.Extras). It contains interpreter-side Tcl command
shims — NOT Tk/GUI code — that map classic Tcl package surfaces onto CodeBrix
libraries, so existing Tcl programs (DRAKON Editor being the driving consumer)
run unmodified:

  * "sqlite3 NAME PATH" (Sqlite/ folder) - tclsqlite-compatible database
    command over CodeBrix.Sqlite. Handle verbs: eval (flat-list and per-row
    script modes), onecolumn, changes, close. Binding rules replicate
    tclsqlite exactly: :name/@name/$name host parameters resolve from the
    CALLER's Tcl frame (commands push no call frame); an UNSET variable binds
    SQL NULL; a set-but-empty variable binds '' TEXT; SQL NULL reads back as
    "". String-repped Tcl values bind as TEXT (never sniffed into numbers), so
    numeric-looking text like "007" survives byte-for-byte. The open path is
    PRAGMA-neutral (no WAL, no foreign-key enforcement, rollback journal) so
    written files are interchangeable with stock-Tcl-written SQLite files
    (e.g. DRAKON .drn files). The SQL text always passes through verbatim.

  * "pdf4tcl::new / ::loadBaseTrueTypeFont / ::createFont" plus the pdf4tcl
    0.7 object surface (Pdf/ folder) - startPage, setFont, setFillColor,
    setStrokeColor, setLineStyle, getStringWidth, text, line, rectangle,
    polygon, write, destroy - over CodeBrix.PdfDocuments. The
    pdf4tcl::paper_sizes and pdf4tcl::units Tcl array variables are published
    too. Coordinates replicate pdf4tcl's Trans/TransR model: user coordinates
    are margin-box-relative; -orient 1 (default) means top-left origin
    y-down. Text -x/-y is the BASELINE origin. -filled 1 fills AND strokes
    unless -stroke 0. Deliberately ignored (accepted, never thrown): text
    -angle/-xangle/-yangle, page -rotate, -compress.

Entry point (the only public type):

    using CodeBrix.Platform.TclTk.Extras;

    Result error = null;
    TclTkExtras.RegisterAll(interpreter, ref error);      // or
    TclTkExtras.RegisterSqlite3(interpreter, ref error);  // or
    TclTkExtras.RegisterPdf4Tcl(interpreter, ref error);

Registration also runs "package provide sqlite3 3.45.0" / "package provide
pdf4tcl 0.7", so Tcl "package require" lines succeed.

Unlike the ported interpreter, the Extras project follows ALL standard
CodeBrix family conventions with no exceptions: XML doc comments on, no
warning suppression, file-scoped namespaces, InternalsVisibleTo its Tests
project.

TESTING
-------
Tests live in tests/CodeBrix.Platform.TclTk.Tests (the interpreter) and
tests/CodeBrix.Platform.TclTk.Extras.Tests (the Extras shims), both
xUnit v3 + SilverAssertions. Run them with:

    dotnet test CodeBrix.Platform.TclTk.slnx

A smoke test creates an Interpreter and evaluates a script, verifying the
embedded script library loads and type resolution works after the rebrand.
The Extras tests cover the full shim surface (sqlite3 verbs, binding/NULL
rules, pdf4tcl drawing/measure/write) and include integration tests that
open real .drn files from a stock DRAKON Editor checkout at
~/GitHome/drakon_editor (those tests skip when the checkout is absent) and
font tests that use a system TrueType font (skipped when none is found).
