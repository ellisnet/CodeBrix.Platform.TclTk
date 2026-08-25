================================================================================
MAINTAINER-README: CodeBrix.Platform.TclTk
Notes for people and agents MAINTAINING this repository — not for package consumers
================================================================================

PURPOSE AND SCOPE
=================
This repository produces a three-package family for running Tcl — and
classic Tk user interfaces — inside .NET / CodeBrix.Platform applications.
The packages publish together, at ONE shared version, in one event.

  PackageId                                          Project                              Consumer doc
  -------------------------------------------------  -----------------------------------  ---------------------------------------------------
  CodeBrix.Platform.TclTk.BsdLicenseForever          src/CodeBrix.Platform.TclTk          AGENT-README.txt (repo root)
  CodeBrix.Platform.TclTk.Extras.BsdLicenseForever   src/CodeBrix.Platform.TclTk.Extras   src/CodeBrix.Platform.TclTk.Extras/AGENT-README.txt
  CodeBrix.Platform.TkCanvas.BsdLicenseForever       src/CodeBrix.Platform.TkCanvas       src/CodeBrix.Platform.TkCanvas/AGENT-README.txt

  * .TclTk    — the managed Tcl interpreter (ported engine; TCL AND
                BSD-2-Clause).
  * .Extras   — original CodeBrix code: tclsqlite-compatible "sqlite3"
                over CodeBrix.Sqlite and the pdf4tcl command surface over
                CodeBrix.PdfDocuments (BSD-2-Clause).
  * .TkCanvas — original CodeBrix code: the Tk widget toolkit on SkiaSharp,
                the CodeBrix.Platform host control, and the Tcl command
                bridge (BSD-2-Clause).

README-INDEX.txt maps every README file in the repository.

REPOSITORY LAYOUT
=================
    CodeBrix.Platform.TclTk.slnx     the solution: three src projects, three
                                     test projects under /Tests/, and the
                                     "Solution Items" folder (AGENT-README.txt,
                                     README.md, LICENSE, LICENSE-MODIFICATIONS.txt,
                                     THIRD-PARTY-NOTICES.txt, icon-codebrix-128.png)
    src/CodeBrix.Platform.TclTk/     the interpreter. Ported layout is kept
                                     verbatim: Components/{Public,Private},
                                     Interfaces/{Public,Private}, Containers/
                                     {Public,Private}, Commands, SubCommands,
                                     Functions, Operators, Hosts, Plugins,
                                     Policies, Packages, Procedures, Lambdas,
                                     Objects, ObjectTypes, Resolvers, Traces,
                                     Wrappers, Tests (the engine's own [test]
                                     plugin), Encodings, Comparers, Attributes,
                                     Diagnostics, Generated, lib/ (the embedded
                                     script library: lib/TclTk1.0/*.tcltk and
                                     lib/Test1.0), Resources/ (.resx carrying
                                     the library, loader.tcltk, syntax.tsv,
                                     DefaultTrustValues.txt, ...)
    src/CodeBrix.Platform.TclTk.Extras/   TclTkExtras.cs (the only public
                                     type), Sqlite/ and Pdf/ command classes
    src/CodeBrix.Platform.TkCanvas/  Canvas/, Events/, Hosting/, Layout/,
                                     Widgets/, Windowing/, Xaml/, Tcl/ (the
                                     command bridge), Theming, Fonts, Images
    tests/CodeBrix.Platform.TclTk.Tests/         interpreter tests
    tests/CodeBrix.Platform.TclTk.Extras.Tests/  sqlite3 / pdf4tcl / .drn
                                     compatibility tests; Assets/ holds
                                     sample.drn and test fonts
    tests/CodeBrix.Platform.TkCanvas.Tests/      toolkit tests; Assets/
                                     {LayoutOracle,CanvasOracle,BindOracle,
                                     ThemingOracle,Images} hold the vendored
                                     oracle fixtures; Oracle/ holds the
                                     scenario parsers
    samples/DRAKON.Brix/             reference consumer application (see
                                     EXTRAS-README.txt)
    samples/TkCanvas_Testing/        XAML-declared Tk UI smoke app (see
                                     EXTRAS-README.txt)
    tools/layout-oracle/             dev-only fixture capture scripts (see
                                     EXTRAS-README.txt)
    Every packable project sits under the case-sensitive src/ folder; tests
    under tests/; samples under samples/. The root carries
    icon-codebrix-128.png, which all three packages embed as PackageIcon.

BUILDING
========
    dotnet build CodeBrix.Platform.TclTk.slnx

  * Target framework net10.0 only, family-wide; no multi-targeting.
  * The interpreter project (src/CodeBrix.Platform.TclTk) has several
    load-bearing settings you must not "clean up":
      - <AssemblyTitle>TclTk</AssemblyTitle> MUST stay the single token
        "TclTk": the engine derives its base package name — hence the
        embedded script-library directory "lib/TclTk<major>.<minor>" and
        the ".tcltk" script extension — from the assembly title at
        runtime. A dotted title breaks interpreter startup (init.tcltk
        not found).
      - GenerateAssemblyInfo=true and a non-zero AssemblyVersion are
        required: the library directory name also derives from the
        assembly version at runtime. The ported Properties/AssemblyInfo.cs
        is excluded from compilation (it carried a wildcard version).
      - EnableDefaultEmbeddedResourceItems=false; every embedded resource
        is declared explicitly with a LogicalName (messages.resx,
        library.resx, packages.resx, loader.tcltk, syntax.tsv,
        DefaultTrustValues.txt, WellKnownAssemblyFilePluginNames.tsv).
        library.resx / packages.resx reference lib/TclTk1.0/*.tcltk via
        ResXFileRef links.
      - DefineConstants: the cross-platform "core" symbol set (TCLTK,
        USE_NAMESPACES, UNIX, SHELL, CONSOLE, THREADING, DEBUGGER*,
        NOTIFY*, *_CACHE, NATIVE (runtime-guarded Unix support), TCL,
        TCL_KITS, TCL_THREADED, TCL_THREADS, TCL_UNICODE — so the native
        Tcl bridge (TclApi/TclBridge/TclThread, ITclManager and the [tcl]
        command) IS compiled in; it stays dormant unless a script or a
        caller asks it to load a native Tcl library, and no test covers
        it, ...). USE_NAMESPACES is REQUIRED: it ORs
        CreateFlags.UseNamespaces into CreateFlags.CommonUse, and without
        it "namespace eval foo { proc bar ... }" silently defines ::bar.
      - AllowUnsafeBlocks=true.
      - Compile Remove: NativeConsole.cs, KeyOps.cs, FormOps.cs,
        StatusFormOps.cs, Interfaces/Public/KeyEventManager.cs (Windows
        console / WinForms only), WinTrustMono.cs (needs Mono.Security),
        Properties/AssemblyInfo.cs.
      - GenerateDocumentationFile=false and a long NoWarn list (1591 plus
        the SYSLIB*/CS0612/CS0618/CS0672 obsolete-API families, CA1416,
        CS0162/CS0164/CS0649/CS8073/CS9191/CA2022/CA2259, CS8981) — the
        sanctioned situational exception for this large fidelity port
        (precedent: CodeBrix.AssemblyTools, CodeBrix.Platform.OpenGL).
        These are the ONLY suppressions in the family; .Extras and
        .TkCanvas ship XML docs and have no suppressions.
  * .Extras references CodeBrix.Sqlite.ApacheLicenseForever and
    CodeBrix.PdfDocuments.MitLicenseForever. .TkCanvas references
    SkiaSharp, CodeBrix.Imaging.ApacheLicenseForever (core, NOT .Drawing —
    TkCanvas must remain the single owner of the SkiaSharp stack),
    CodeBrix.Platform.ApacheLicenseForever and
    CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever. Keep the
    SkiaSharp pin in step with the CodeBrix.Platform Skia runtime heads so
    consuming apps never see a SkiaSharp diamond conflict.
  * The host-integration layer (TkHostView, dispatcher/clipboard bridges,
    the hidden-input-element IME sink) lives INSIDE .TkCanvas by decision;
    there is no separate host package.
  * Diagnostic builds: -p:TclTkExtraDefines=PERFORMANCE_DIAGNOSIS adds
    extra symbols to .TclTk and .TkCanvas. It must be empty for shipped
    builds.

TESTING
=======
    dotnet test CodeBrix.Platform.TclTk.slnx

  * xUnit v3 + SilverAssertions ("x.Should()...") in all three test
    projects; xunit.runner.json in each sets parallelizeTestCollections
    = false, parallelizeAssembly = false, maxParallelThreads = 1. The
    ported engine keeps heavy process-global state, so tests MUST run
    strictly sequentially — concurrent interpreter creation races/crashes.
  * The interpreter suite's shared helpers are in TclTkTest.cs
    (CreateInterpreter, Eval, TryEval, EvalOnce, EvalOnceError). Setting
    the environment variable TCLTK_TEST_BOOLEAN_MODE=TclshCompat runs the
    ENTIRE suite in TclshCompat mode — a diagnostic pass to surface tests
    that fail for reasons other than asserting the old "True"/"False"
    rendering.
  * Oracle discipline: every expected value in the interpreter and
    toolkit tests was probed on real tclsh/wish 8.6.16 (Debian). The
    behavior fixtures are VENDORED under the test projects' Assets/
    folders; tests never invoke the real tools. Geometry (pack/grid),
    canvas coords/bbox/find, bind dispatch, theming derivation and GIF
    pixel decoding compare byte-for-byte against those fixtures.
  * Regenerating oracle fixtures (only when scenarios change or a newer
    Tk becomes the reference) uses tools/layout-oracle/ — see
    EXTRAS-README.txt. Canvas scenarios must not contain text items
    (font-dependent geometry would make fixtures machine-specific).
  * Interpreter test files map to feature areas: InterpreterSmokeTests,
    InterpreterLifecycleAndErrorTests, InterpreterVariableTests,
    InterpreterEvaluationTests, BooleanResultModeTests,
    ProductionModeTests, CacheParsedScriptsTests, TraceCommandTests,
    TailcallCommandTests, ArgumentExpansionTests, BinaryFormatTests,
    BinaryScanTests, BinaryEncodeDecodeTests, VariableLinkLifetimeTests.
    Extras: Sqlite3CommandTests, SqliteHandleCommandTests,
    SqlParameterScannerTests, Pdf4Tcl*Tests, DrakonDrnCompatibilityTests,
    TclTkExtrasTests. TkCanvas: *OracleTests (pack/canvas/bind/theming),
    per-area widget/layout/event/font/image/menu/dialog/scheduler/style
    tests, TkTclBridgeTests, TkBootstrapTests.
  * Approximate size (grep of [Fact]/[Theory] attributes): interpreter
    ~170 test methods (+~300 inline cases), Extras ~100, TkCanvas ~310
    (+~40 inline cases).
  * The DRAKON.Brix sample has its own test project
    (samples/DRAKON.Brix/tests/libs/DRAKON.Brix.TclBridge.Tests:
    DrakonRuntimeTests, DrnFileOpenTests, ProfileOpenTests) that boots the
    real DRAKON Editor Tcl headlessly (DrakonRuntime.StartDirect) — it is
    part of samples/DRAKON.Brix/DRAKON.Brix.slnx, not the root slnx.

PACKAGING AND PUBLISHING
========================
  * GeneratePackageOnBuild=true in all three packable projects; every
    build produces a fresh .nupkg.
  * Version scheme (all three projects): date-stamped, auto-incrementing
    1.<x>.<y>.<z> derived from UTC now — x = whole years since
    _VersionBaseYear (2026), y = day of year (1-based), z = minute of day
    (0..1439). Strictly increasing over time; NOT SemVer (major pinned to
    1, minor encodes the year). Two builds in the same UTC minute produce
    the same version — never publish two packages from within one minute.
    To re-baseline the minor number change _VersionBaseYear.
  * FAMILY SHIP RULE: .TclTk, .Extras and .TkCanvas publish together at
    ONE shared version in ONE event. Because the minute field can drift
    across projects built in different UTC minutes, the pack/publish
    driver must stamp all three with the same version. Spot-check the
    packed DLL versions before pushing.
  * What ships in each nupkg: the assembly, icon-codebrix-128.png,
    README.md (PackageReadmeFile), THIRD-PARTY-NOTICES.txt, and the
    package's AGENT-README:
      - .TclTk packs the repo-root AGENT-README.txt.
      - .Extras packs src/CodeBrix.Platform.TclTk.Extras/AGENT-README.txt.
      - .TkCanvas packs src/CodeBrix.Platform.TkCanvas/AGENT-README.txt.
    Keep each AGENT-README about exactly its own package; the root file
    carries only a catalogue line for the other two.
  * License metadata: .TclTk declares "TCL AND BSD-2-Clause" and PREPENDS
    the upstream attribution to the family copyright line (the upstream
    license requires the original notice to travel with derivatives);
    .Extras and .TkCanvas are entirely original code and declare plain
    BSD-2-Clause with the family copyright only.
    PackageRequireLicenseAcceptance is on for all three.
  * PackageProjectUrl / RepositoryUrl:
    https://github.com/ellisnet/CodeBrix.Platform.TclTk

PROVENANCE AND VENDORED SOURCES
===============================
  * CodeBrix.Platform.TclTk is a port (source derivative) of the Eagle
    project by Joe Mistachkin and the Eagle Development Team
    (https://github.com/mistachkin/eagle, https://eagle.to/), Tcl/Tk
    License. The port was a rebrand-in-place: every "Eagle.*" namespace
    became "CodeBrix.Platform.TclTk.*" (Eagle._Components.Public ->
    CodeBrix.Platform.TclTk._Components.Public, etc.), the base
    preprocessor symbol EAGLE became TCLTK, the script extension became
    ".tcltk" and the library directory "lib/TclTk1.0". Each ported file
    keeps a "//was previously: <upstream namespace>" comment on its
    namespace line; that comment and the copyright/license text are the
    ONLY places the word "Eagle" may appear (plus the
    BooleanResultMode.EagleCompat enum member, which names the inherited
    behavior). Never introduce "Eagle" into a new identifier.
  * The preprocessor symbol set was derived from the upstream known-good
    netstandard constant set, pruned of features that cannot compile on
    cross-platform .NET (System.Web, BinaryFormatter, WinForms, the
    Windows console and WinTrust paths, remoting/CAS, Mono legacy hacks)
    and tuned against the compiler. TCL_WRAPPER is deliberately NOT
    defined: it only switches TclApi / TclBridge / TclThread from internal
    to public, and the family does not expose those types.
  * The Extras command surfaces reproduce the observable behavior of
    tclsqlite ("sqlite3") and pdf4tcl 0.7 as original code; no tclsqlite
    or pdf4tcl source is present in src/.
  * TkCanvas reproduces the observable behavior of Tk 8.6 as original
    code; real Tk (wish 8.6.16) is the behavior ORACLE, not a source.
    Colors follow the Tk 8.6 "Web colors" list.
  * THIRD-PARTY-NOTICES.txt is the one-stop record of every third-party
    work whose source or assets are present in the repository (ported
    engine, vendored test fixtures, sample assets). Referenced NuGet
    packages are NOT covered there — their notices ship with them.
  * samples/DRAKON.Brix vendors DRAKON Editor (public domain) unmodified
    under src/DRAKON.Brix.Core/Assets/drakon with its own PROVENANCE.txt
    (exceptions: the Liberation Mono font under the GPLv2 font-embedding
    exception, and pdf4tcl's pkgIndex.tcl). See EXTRAS-README.txt.
  * Fidelity policy (family-wide): the engine and toolkit track stock
    Tcl/Tk behavior. Divergences that consumers can observe are closed
    with OPT-IN switches (BooleanResultMode.TclshCompat is the model;
    CacheParsedScripts and ProductionMode are opt-in for the same reason)
    so that existing consumers never change behavior silently. Known,
    accepted divergences are listed in the consumer AGENT-READMEs and
    must stay listed there.

CODING CONVENTIONS
==================
  * Target framework net10.0 only; no multi-targeting.
  * Nullable reference types are OFF in .TclTk, .Extras and .TkCanvas (no
    "#nullable enable", no "?" on reference types, no null-forgiveness
    "!").
  * No global usings; usings are explicit and at the top of each file.
  * Sanctioned deviations in the PORTED interpreter only (documented; do
    not imitate in new code): XML docs off + NoWarn 1591 and the
    obsolete-API families, upstream block-scoped namespaces, per-file
    "//was previously:" provenance comments, POSIX-named interop types
    (dlopen, rlimit, timespec, utsname, ...) kept verbatim for fidelity.
  * New code — everything in .Extras and .TkCanvas, and anything new
    added to the interpreter outside the ported files — has XML doc
    comments ON, no suppressions, file-scoped namespaces, and the
    CodeBrix .cs top-of-file layout.
  * Public .TclTk API additions (e.g. BooleanResultMode,
    CacheParsedScripts, ProductionMode) carry full XML summaries even
    though the project does not emit a documentation file.
  * Tests: <Class>Tests.cs naming, snake_case method names describing the
    behavior, //Arrange //Act //Assert comments, xUnit v3 +
    SilverAssertions. Every expected value that mirrors Tcl/Tk behavior
    is captured from the real oracle and says so in the test's summary.
  * Custom Tcl commands derive from CodeBrix.Platform.TclTk._Commands
    .Default, take an ICommandData in the constructor, override Execute,
    and are registered with AddCommand + ProvidePackage (TclTkExtras.cs
    is the reference implementation).
  * Never name the UI platform's upstream project in code, comments or
    docs; the UI platform is CodeBrix.Platform.
  * Consumer-facing documentation lives in the AGENT-README files;
    maintainer facts (this file) and sample/tool notes (EXTRAS-README.txt)
    must not leak back into them.

NOTES
=====
  * The interpreter's embedded script library is lib/TclTk1.0/*.tcltk
    (init.tcltk is the entry point; safe.tcltk, object.tcltk, shell.tcltk,
    embed.tcltk, ... are the upstream library files renamed). Changing a
    library file means rebuilding library.resx's ResXFileRef links.
  * The optional native Tcl bridge IS part of this build: TCL and the
    TCL_* symbols are defined and no bridge source is excluded, so
    TclApi / TclBridge / TclThread (internal, because TCL_WRAPPER is off)
    and the public ITclManager / ITclEntityManager surface on Interpreter
    (HasTcl, LoadTcl, UnloadTcl, GetTclPatchLevel, CreateTclInterpreter,
    EvaluateTclScript, ...) plus the [tcl] script command all ship. It is
    dormant — it does nothing until something loads a native Tcl library —
    and NO test in this repository covers it. Treat it as unverified
    surface: do not advertise it to consumers as supported, and do not
    "clean it out" without a decision, because the symbol set is what
    determines several conditional signatures (AddObject and ObjectData
    take an extra 'interpName' parameter under NATIVE && TCL).
  * Anything under samples/*/bin and obj is build output; grep the
    source trees (src/, Assets/) when looking for the "for DRAKON.Brix"
    change-note markers described in EXTRAS-README.txt.
================================================================================
