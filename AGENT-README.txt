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

CodeBrix.Platform.TclTk is the Tcl half of the eventual .TclTk library; a
companion CodeBrix.Platform.TkCanvas project (the Tk/canvas half) will later be
added and shipped in the same NuGet package.


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


TESTING
-------
Tests live in tests/CodeBrix.Platform.TclTk.Tests (xUnit v3 + SilverAssertions).
Run them with:

    dotnet test CodeBrix.Platform.TclTk.slnx

A smoke test creates an Interpreter and evaluates a script, verifying the
embedded script library loads and type resolution works after the rebrand.
