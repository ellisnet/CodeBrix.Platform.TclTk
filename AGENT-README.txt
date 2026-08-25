================================================================================
AGENT-README: CodeBrix.Platform.TclTk
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.TclTk.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.TclTk is a fully managed, cross-platform Tcl interpreter for
.NET 10 or later. It has no native dependencies: the core Tcl script library
is embedded in the assembly, so there are no files to deploy. You create one
or more independent Interpreter instances in-process, evaluate scripts and
expressions, read and write Tcl variables, register your own Tcl commands
written in C#, expose .NET objects to scripts (and call back into .NET), run
scripts on dedicated script threads, cancel running scripts, host the
interpreter behind your own IHost, and create restricted ("safe") child
interpreters for untrusted script text.

Provenance: the engine is a port of the Eagle project by Joe Mistachkin and
the Eagle Development Team (https://github.com/mistachkin/eagle). Every
upstream "Eagle.*" namespace maps 1:1 to "CodeBrix.Platform.TclTk.*"
(Eagle._Components.Public -> CodeBrix.Platform.TclTk._Components.Public,
Eagle._Commands -> CodeBrix.Platform.TclTk._Commands, and so on). Do NOT use
the upstream namespaces, and do NOT introduce the word "Eagle" into any
namespace, type, method, or identifier you write; it appears only in
license text and in the BooleanResultMode.EagleCompat enum member. Script
files use the ".tcltk" extension for the embedded library; your own scripts
can be plain ".tcl" text.

Naming rule: "TclTk" refers to THIS managed engine. Names beginning "Tcl..."
(TclEncoding, ITclManager / ITclEntityManager, LoadTcl, EvaluateTclScript,
the [tcl] script command) refer instead to the OPTIONAL native Tcl/Tk shared
library the engine can bridge to at run time — a different thing entirely.
Keep the two apart in anything you write; see WHAT THIS PACKAGE DOES NOT DO
for what that bridge does and does not give you.

Sibling packages built from the same repository (each has its own file):
  * CodeBrix.Platform.TclTk.Extras.BsdLicenseForever — interpreter-side
    Tcl command shims: a tclsqlite-compatible "sqlite3" command and the
    pdf4tcl command surface. See src/CodeBrix.Platform.TclTk.Extras/
    AGENT-README.txt.
  * CodeBrix.Platform.TkCanvas.BsdLicenseForever — the classic Tk widget
    toolkit re-implemented on SkiaSharp with a CodeBrix.Platform host
    control and a Tcl command bridge ("button", "pack", "canvas", ...) so
    unmodified Tcl/Tk programs run on this interpreter. See
    src/CodeBrix.Platform.TkCanvas/AGENT-README.txt.
The three packages publish together; reference only what you need. The
interpreter alone is GUI-free.

INSTALLATION
============
    dotnet add package CodeBrix.Platform.TclTk.BsdLicenseForever

  * PackageId: CodeBrix.Platform.TclTk.BsdLicenseForever. The license suffix
    is part of the PackageId only; the assembly and root namespace are
    CodeBrix.Platform.TclTk.
  * Target framework: .NET 10 or later (net10.0). No multi-targeting.
  * NuGet dependency (transitive): System.Security.Cryptography.Pkcs
    (Microsoft first-party; used by the engine's PKCS#7 signature checks).
  * License: TCL AND BSD-2-Clause (Tcl/Tk License for the ported engine,
    BSD-2-Clause for the modifications). PackageRequireLicenseAcceptance is
    on.
  * Requirements: none beyond the runtime. Works on Windows, Linux and
    macOS; no native libraries, no display, no console needed.
  * Nullable reference types are OFF in this library: its public signatures
    carry no "?" annotations and it returns null on failure paths (e.g.
    Interpreter.Create).

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.TclTk._Components.Public;   // Interpreter, Result,
                                                        // ReturnCode, all flag
                                                        // enums, CommandData,
                                                        // Engine, ScriptThread,
                                                        // InterpreterSettings
    using CodeBrix.Platform.TclTk._Interfaces.Public;   // ICommand, IExecute,
                                                        // IClientData, IHost,
                                                        // IPlugin, IScriptThread
    using CodeBrix.Platform.TclTk._Containers.Public;   // ArgumentList,
                                                        // StringList, TraceList
    using CodeBrix.Platform.TclTk._Commands;            // Default (command base)
    using CodeBrix.Platform.TclTk._Components.Public.Delegates;
                                                        // ExecuteCallback,
                                                        // EventCallback,
                                                        // TraceCallback,
                                                        // AsynchronousCallback
Other folders you may need, each with its own namespace and its own class
named "Default" (the base implementation of that extension kind):
    CodeBrix.Platform.TclTk._Hosts       Console, Null, Diagnostic, Fake,
                                         Wrapper; abstract Default/Engine/
                                         File/Profile/Shell/Core
    CodeBrix.Platform.TclTk._Plugins     Default (plugin base)
    CodeBrix.Platform.TclTk._Traces      Default (variable-trace base)
    CodeBrix.Platform.TclTk._Objects     Default (IObject wrapper)
    CodeBrix.Platform.TclTk._Packages    Default (IPackage base)
    CodeBrix.Platform.TclTk._Policies    Default (IPolicy base)
PITFALL: because every one of those namespaces has a type called Default,
import only the one you derive from, or alias it:
    using TclCommandBase = CodeBrix.Platform.TclTk._Commands.Default;

CORE API REFERENCE
==================

1. CREATING AND DISPOSING AN INTERPRETER
----------------------------------------
Interpreter is IDisposable. Always dispose it (using). After Dispose, every
member throws (the default creation flags include ThrowOnDisposed;
InterpreterDisposedException derives from the BCL disposed exception) and
interpreter.Disposed is true.

    static Interpreter Create(ref Result result,
        BooleanResultMode compatMode = BooleanResultMode.EagleCompat)
        The everyday overload: default flags (CreateFlags.Default =
        ShellUse | Initialize, i.e. the full command set, namespaces on,
        the embedded script library loaded). Returns null on failure with
        the reason in 'result'. Because the default flags include
        ThrowOnError, a failure INSIDE script-library initialization can
        surface as an exception instead of null — handle both.

    static Interpreter Create(IEnumerable<string> args, ref Result result)
        Same, plus the script-level argument list (::argv).

    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        ref Result result)
        The overload to reach for when you need non-default flags (safe
        interpreters, no host, lean command sets). Pass
        HostCreateFlags.Default when you do not care about the host.

    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        TraceList traces, ref Result result)
        As above, with variable traces installed at creation (section 12).

    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        string libraryPath, ref Result result)
    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        string text, string libraryPath, ref Result result)
    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        string text, string libraryPath, StringList autoPathList,
        ref Result result)
        'text' is a script evaluated right after initialization;
        'libraryPath' overrides the script-library location (normally you
        leave it null so the embedded library is used); 'autoPathList'
        seeds ::auto_path for "package require" lookups.

    static Interpreter Create(IInterpreterSettings interpreterSettings,
        bool strict, ref Result result)
        Create from a settings object (section 1b). 'strict' makes unknown
        settings an error.

    static Interpreter Create(ulong? token, ref Result result)
    static Interpreter Create(ulong? token, IEnumerable<string> args,
        ref Result result)
    static Interpreter Create(ulong? token,
        IInterpreterSettings interpreterSettings, bool strict,
        ref Result result)
        'token' variants exist for the upstream licensing plugin; pass null.

    static Interpreter Create(IEnumerable<string> args,
        CreateFlags createFlags, HostCreateFlags hostCreateFlags,
        InitializeFlags initializeFlags, ScriptFlags scriptFlags,
        InterpreterFlags interpreterFlags, object applicationObject,
        object policyObject, object resolverObject, object userObject,
        PolicyList policies, TraceList traces, string text,
        string libraryPath, StringList autoPathList, ref Result result)
        The kitchen-sink overload (there is also one taking PluginFlags and
        a ulong? token). Only needed when installing policies (section 10)
        at creation time.

Static lookups: Interpreter.GetActive() (the interpreter evaluating on the
current thread, or null), Interpreter.GetAny(), Interpreter.GetFirst().

1a. CreateFlags essentials (enum CreateFlags : ulong, [Flags])
    Default            = ShellUse | Initialize  (what Create(ref Result) uses)
    ShellUse           = CommonUse | DebuggerUse | ... | ThrowOnError
    CommonUse          = ThrowOnDisposed | UseNamespaces | NoDefaultBinder |
                         (test/monitor plugin exclusions)
    EmbeddedUse        = CommonUse | Initialize | ThrowOnError
    SafeEmbeddedUse    = EmbeddedUse | SafeAndHideUnsafe
    SingleUse          = EmbeddedUse & ~ThrowOnError
    SafeSingleUse      = SingleUse | SafeAndHideUnsafe
    LeanAndMeanUse     = CommonUse minus optional services (no home dir
                         probing, no config, no native utility, ...)
    FastSingleUse      = LeanAndMeanUse | Initialize | NoPluginPreview
    FastSafeSingleUse  = SafeLeanAndMeanUse | Initialize | NoPluginPreview
    Individual bits you will meet: Initialize (load the script library —
    without it "proc"/"namespace" work but library procs like "lassign
    helpers" are absent), UseNamespaces (Tcl namespaces; ON in every *Use
    set), Safe, HideUnsafe, SafeAndHideUnsafe, Standard, ThrowOnError,
    ThrowOnDisposed, NoLibrary, NoShellLibrary, NoPlugins, NoCommands,
    NoVariables, NoObjects, NoObjectPlugin, NoFunctions, NoOperators,
    NoRandom, NoCorePolicies, NoCoreTraces, Debug, Verbose, MeasureTime.
    The "No..." bits are for embedders that want a smaller interpreter;
    combining them with Safe is what the Safe*Use sets do for you.

    HostCreateFlags essentials: Default = ShellUse; EmbeddedUse =
    CommonUse | NoTitle | NoIcon | NoCancel | ForEmbeddedUse;
    SafeEmbeddedUse; NoConsole / NoDiagnostic / NoNull / NoFake choose
    which shipped host types may be created; ResourceManager is in every
    set. Hosts are described in section 9.

1b. InterpreterSettings (sealed class; implements IInterpreterSettings)
    static IInterpreterSettings Create()
    static IInterpreterSettings CreateDefault()
    static IInterpreterSettings CreateDefault(IRuleSet ruleSet,
        IEnumerable<string> args)
    static IInterpreterSettings CreateSafe(IRuleSet ruleSet,
        IEnumerable<string> args)
    static IInterpreterSettings CreateFrom(string fileName,
        CultureInfo cultureInfo, bool merge, bool expand, ref Result error)
    static ReturnCode LoadFrom(...) / SaveTo(...)   (settings persistence)
    IInterpreterSettingsData (the data half of the interface) exposes the
    complete creation recipe as read/write properties: Args, Culture,
    CreateFlags, HostCreateFlags, InitializeFlags, ScriptFlags,
    InterpreterFlags, InterpreterTestFlags, PluginFlags, Host (an IHost to
    use instead of creating one), Profile, Policies (PolicyList), Traces
    (TraceList), Text, LibraryPath, AutoPathList (StringList), RuleSet, and
    the four opaque objects ApplicationObject / PolicyObject /
    ResolverObject / UserObject. IInterpreterSettings adds MakeSafe(),
    MakeStandard(), DisableInitialize(), UseDefaultsForFlags(),
    UseFlagsFromInterpreter(Interpreter), UseObjectsFromInterpreter(
    Interpreter), ResetEverything(). Several of those are commented
    "DO NOT USE: TESTS ONLY" in the source; prefer setting the flag
    properties directly.

    Example — a safe, host-less interpreter from settings:

        IInterpreterSettings settings = InterpreterSettings.CreateDefault();
        settings.CreateFlags = CreateFlags.SafeEmbeddedUse;
        settings.HostCreateFlags = HostCreateFlags.SafeEmbeddedUse;
        Result result = null;
        using Interpreter safe = Interpreter.Create(settings, false, ref result);
        if (safe == null) throw new InvalidOperationException(result);
        Console.WriteLine(safe.IsSafe());   // True

1c. Interpreter properties you will use
    bool ProductionMode { get; set; }        section 13
    bool CacheParsedScripts { get; set; }    section 13
    BooleanResultMode BooleanResultMode { get; }   read-only, section 14
    bool Disposed { get; }
    long Id { get; }          string Name { get; }
    IHost Host { get; }       IInteractiveHost InteractiveHost { get; }
    IEventManager EventManager { get; }
    int ErrorLine { get; }    (line of the last error, when known)
    bool Exit { get; set; }   (set by [exit]; the script asked to quit)
    bool Interactive { get; set; }
    bool IsSafe()             bool IsStandard()

2. EVALUATING SCRIPTS AND EXPRESSIONS
-------------------------------------
All evaluation entry points return a ReturnCode and hand the value (or the
error message) back through 'ref Result'. Never throw for script errors:
check the code.

    ReturnCode EvaluateScript(string text, ref Result result)
    ReturnCode EvaluateScript(string text, ref Result result,
        ref int errorLine)
    ReturnCode EvaluateScript(string text, EngineFlags engineFlags,
        ref Result result, ref int errorLine)
    ReturnCode EvaluateScript(string fileName, string text,
        ref Result result)                 // 'fileName' labels errorInfo
    ReturnCode EvaluateScript(string fileName, string text,
        ref Result result, ref int errorLine)
    ReturnCode EvaluateScript(IScript script, ref Result result)
    ReturnCode EvaluateScript(IScript script, ref Result result,
        ref int errorLine)
        IScript is created with Script.Create(string text) or
        Script.Create(string text, IClientData clientData).

    ReturnCode EvaluateExpression(string text, ref Result result)
        Evaluates an [expr] expression without the surrounding command:
        EvaluateExpression("6 * 7") -> "42".

    ReturnCode EvaluateFile(string fileName, ref Result result)
    ReturnCode EvaluateFile(string fileName, ref Result result,
        ref int errorLine)
    ReturnCode EvaluateFile(Encoding encoding, string fileName,
        ref Result result)
    ReturnCode EvaluateFile(Encoding encoding, string fileName,
        ref Result result, ref int errorLine)
    ReturnCode EvaluateFile(Encoding encoding, string fileName,
        EngineFlags engineFlags, ref Result result, ref int errorLine)
        Equivalent to [source]; errorInfo records the file name.

    ReturnCode EvaluateStream(string name, TextReader textReader,
        ref Result result)
        Evaluate script text from any TextReader (embedded resources,
        network streams) under a display 'name'.

    ReturnCode SubstituteString(string text, ref Result result)
    ReturnCode SubstituteString(string text,
        SubstitutionFlags substitutionFlags, ref Result result)
        [subst] semantics from C#: $var, [cmd] and backslash substitution.

    ReturnCode Invoke(string name, IClientData clientData,
        ArgumentList arguments, ref Result result)
        Invoke one command directly (no parsing): name resolution goes
        through the interpreter's resolvers, so procs, aliases and your
        registered commands all work. arguments[0] must be the command name.

    Asynchronous ("fire and forget" or callback) forms:
    ReturnCode EvaluateScript(string text, AsynchronousCallback callback,
        IClientData clientData, ref Result error)
    ReturnCode EvaluateFile(string fileName, AsynchronousCallback callback,
        IClientData clientData, ref Result error)
    ReturnCode SubstituteString(string text, AsynchronousCallback callback,
        IClientData clientData, ref Result error)
        delegate void AsynchronousCallback(IAsynchronousContext context);
        IAsynchronousContext exposes Text, ReturnCode, Result, ErrorLine,
        ThreadId, EngineFlags and ClientData. 'callback' may be null. The
        script runs on a pool thread; prefer ScriptThread (section 7) when
        you need ordering or a long-lived worker.

Static engine entry points (same semantics, usable when you hold only an
Interpreter reference and want the flag-explicit forms):
    static ReturnCode Engine.EvaluateScript(Interpreter interpreter,
        string text, ref Result result)
    static ReturnCode Engine.EvaluateScript(Interpreter interpreter,
        string text, ref Result result, ref int errorLine)
    static ReturnCode Engine.EvaluateScript(Interpreter interpreter,
        IScript script, ref Result result)
    static ReturnCode Engine.EvaluateFile(...)   / Engine.EvaluateStream(...)
    static ReturnCode Engine.EvaluateExpression(...)
    static ReturnCode Engine.EvaluateScriptWithScopeFrame(...)
Engine also owns the cancellation statics (section 6).

Script-level errorInfo / errorCode are the Tcl globals ::errorInfo and
::errorCode; read them with GetVariableValue or "set ::errorInfo" after a
failed evaluation (the tests do exactly this).

3. THE RESULT AND RETURNCODE MODEL
----------------------------------
enum ReturnCode: Ok = 0, Error = 1, Return = 2, Break = 3, Continue = 4
(the five standard Tcl codes, numerically identical to tcl.h), plus
WhatIf, Exception, Invalid and the Convert* codes you will not normally
see. Treat anything other than Ok as failure unless you are implementing
control flow.

Result (sealed class) is the boxed script value or error message.
  * Implicit conversion TO Result from: string, int, long, bool, double,
    decimal, byte, byte[], char, DateTime, TimeSpan, Guid, Uri, Version,
    Exception, Enum, BigInteger, StringBuilder, StringList,
    StringPairList, StringDictionary, ObjectDictionary, ResultList,
    Argument, Interpreter. So "result = 42;" and "result = "message";"
    both compile inside a command.
  * Implicit conversion FROM Result to string; ToString() gives the same.
    A null Result is legal (it means "no result yet") — always null-check
    before ToString().
  * Properties: object Value, string String, int Length, ReturnCode
    ReturnCode, ReturnCode PreviousReturnCode, int ErrorLine, string
    ErrorCode, string ErrorInfo, Exception Exception, ResultFlags Flags,
    IClientData ClientData.
  * Helpers: static Result Copy(...), static Result Combine(...),
    Reset(...), Clear(), Save(...)/Restore(...), and the string-like
    members StartsWith/EndsWith/Contains/IndexOf/Substring/Trim/Replace/
    Compare.
Pattern: declare "Result result = null;" once, pass it by ref, reuse it.

4. VARIABLES
------------
    ReturnCode SetVariableValue(string name, string value, ref Result error)
    ReturnCode SetVariableValue(VariableFlags flags, string name,
        string value, TraceList traces, ref Result error)
    ReturnCode GetVariableValue(string name, ref Result value,
        ref Result error)
    ReturnCode GetVariableValue(VariableFlags flags, string name,
        ref Result value, ref Result error)
    ReturnCode UnsetVariable(string name, ref Result error)
    ReturnCode UnsetVariable(VariableFlags flags, string name,
        ref Result error)
    ReturnCode DoesVariableExist(VariableFlags flags, string name)
    ReturnCode DoesVariableExist(VariableFlags flags, string name,
        ref Result error)
    ReturnCode AddVariable(VariableFlags flags, string name,
        TraceList traces, bool errorOnExist, ref Result error)
    ReturnCode WaitVariable(EventWaitFlags eventWaitFlags,
        VariableFlags variableFlags, string name, long microseconds,
        long? threadId, int limit, EventWaitHandle @event, ...)
        [vwait] from C#.
Names follow Tcl resolution: "x" is resolved in the current frame (global
when called from outside a proc), "::ns::x" is fully qualified. Useful
VariableFlags: None, GlobalOnly (force the global namespace), NoCreate,
ReadOnly, Array, NoTrace (skip traces for this access), AppendValue.
GetVariableValue on an unknown name returns ReturnCode.Error with the
message in 'error'. A value set from C# is an ordinary Tcl string: "incr
counter 5" on a variable set to "10" yields 15. For array elements and
whole arrays the dependable route from C# is script text ("set a(k) v",
"array get a", "array set a {...}") through EvaluateScript.

5. IMPLEMENTING YOUR OWN TCL COMMANDS (THE EXTENSION MODEL)
-----------------------------------------------------------
The command contract is the IExecute interface; a full command also
carries identity/flags/state through ICommand:

    public interface IExecute
    {
        ReturnCode Execute(Interpreter interpreter, IClientData clientData,
            ArgumentList arguments, ref Result result);
    }
    public interface ICommand : ICommandData, IState, IDynamicExecuteCallback,
        IExecute, IEnsemble, IPolicyEnsemble, ISyntax, IUsageData { }
    // ICommandData : IIdentifier (Name, Group, Description, Kind, Id,
    //   ClientData), ICommandBaseData (TypeName, Type, CommandFlags
    //   CommandFlags), IHavePlugin, IWrapperData; plus CommandFlags Flags.
    // IState: bool Initialized; Initialize(...); Terminate(...).

Do not implement ICommand by hand — derive from the shipped base class
CodeBrix.Platform.TclTk._Commands.Default, which implements every member
with sensible defaults and leaves you one virtual to override:

    public class Default : ICommand, ...
    {
        public Default(ICommandData commandData);
        public virtual ReturnCode Execute(Interpreter interpreter,
            IClientData clientData, ArgumentList arguments,
            ref Result result);            // base returns ReturnCode.Ok
        public virtual string Name { get; set; }
        public virtual CommandFlags Flags { get; set; }
        public virtual string Syntax { get; set; }
        ...
    }

The identity object is CommandData:

    public CommandData(string name, string group, string description,
        IClientData clientData, string typeName, CommandFlags flags,
        IPlugin plugin, long token);
    public CommandData(ICommandData commandData);

ArgumentList (sealed; derives from List<Argument>) is the word list of the
invocation INCLUDING the command name at index 0, exactly as Tcl_ObjCmdProc
sees it. Argument converts implicitly to string (and string to Argument),
so "string x = arguments[1];" works; arguments.Count is argc. Helpers:
static ArgumentList GetRange(...), static StringList GetRangeAsStringList(
...), ToString() (a proper Tcl list), ToRawString().

IClientData is an opaque object holder you attach at registration and get
back on every call: new ClientData(object data); ClientData.Empty; the
static ClientData.Pack/TryGet helpers wrap several values.

Registration and removal on Interpreter:

    ReturnCode AddCommand(ICommand command, IClientData clientData,
        ref long token, ref Result result)
    ReturnCode RemoveCommand(long token, IClientData clientData,
        ref Result result)
    ReturnCode RemoveCommand(string name, IClientData clientData,
        ref Result result)
    ReturnCode RenameCommand(string oldName, string newName, bool delete,
        ref Result result)
    ReturnCode AddIExecute(string name, IExecute execute,
        IClientData clientData, ref long token, ref Result result)
        Register a bare IExecute under a name (no CommandData needed;
        ideal for a lambda-style class). RemoveIExecute(token|name, ...)
        removes it.
    ReturnCode ProvidePackage(string name, Version version,
        ref Result result)
        Make "package require <name>" succeed after you registered the
        commands that package promises.
    ReturnCode AddFunction(IFunction function, IClientData clientData,
        ref long token, ref Result result)
        Register an [expr] math function (derive from
        CodeBrix.Platform.TclTk._Functions.Default).

CommandFlags worth setting on your CommandData: None (fine for most),
Safe (allowed inside safe interpreters), Unsafe (hidden/removed when an
interpreter is made safe), Hidden, ReadOnly, NoRename, NoRemove,
Ensemble/SubCommand (for ensembles), Standard/NonStandard. A command
registered with CommandFlags.None is treated as NOT safe.

COMPILABLE EXAMPLE — a "greet ?name?" command (this is the exact shape the
Extras package's sqlite3 and pdf4tcl commands use):

    using System;
    using CodeBrix.Platform.TclTk._Commands;
    using CodeBrix.Platform.TclTk._Components.Public;
    using CodeBrix.Platform.TclTk._Containers.Public;
    using CodeBrix.Platform.TclTk._Interfaces.Public;

    internal sealed class GreetCommand : Default
    {
        public GreetCommand(ICommandData commandData)
            : base(commandData)
        {
        }

        public override ReturnCode Execute(
            Interpreter interpreter, IClientData clientData,
            ArgumentList arguments, ref Result result)
        {
            if (interpreter == null)
            {
                result = "invalid interpreter";
                return ReturnCode.Error;
            }
            if (arguments == null || arguments.Count < 1 || arguments.Count > 2)
            {
                result = "wrong # args: should be \"greet ?name?\"";
                return ReturnCode.Error;
            }

            string name = (arguments.Count == 2) ? arguments[1] : "world";
            result = "hello, " + name;      // string -> Result
            return ReturnCode.Ok;
        }
    }

    internal static class GreetRegistration
    {
        public static ReturnCode Register(Interpreter interpreter, ref Result error)
        {
            var command = new GreetCommand(
                new CommandData(
                    "greet", "demo", "Says hello.", null,
                    typeof(GreetCommand).FullName, CommandFlags.None, null, 0));

            long token = 0;
            ReturnCode code = interpreter.AddCommand(command, null, ref token, ref error);
            if (code != ReturnCode.Ok) { return code; }

            return interpreter.ProvidePackage("greet", new Version(1, 0), ref error);
        }
    }

    // Usage
    Result result = null;
    using (Interpreter interpreter = Interpreter.Create(ref result))
    {
        Result error = null;
        if (GreetRegistration.Register(interpreter, ref error) != ReturnCode.Ok)
            throw new InvalidOperationException(error);

        interpreter.EvaluateScript("package require greet; greet Tcl", ref result);
        Console.WriteLine(result);   // hello, Tcl
    }

Returning a Tcl list: build a StringList (new StringList(IEnumerable<string>)
or new StringList() + Add) and assign it to result — it renders as a
correctly quoted Tcl list. Returning an error: assign the message and
return ReturnCode.Error; the engine fills ::errorInfo. Nested evaluation
from inside Execute (calling back into script, e.g. a callback argument)
is simply interpreter.EvaluateScript(arguments[2], ref result) — the
engine is re-entrant.

Delegate-based alternative (no class): ExecuteCallback has the same
signature as Execute —
    delegate ReturnCode ExecuteCallback(Interpreter interpreter,
        IClientData clientData, ArgumentList arguments, ref Result result);
Wrap it in a tiny IExecute implementation and AddIExecute it, or set it as
the Callback property (IDynamicExecuteCallback) of a Default-derived
command whose Execute you leave alone.

6. CANCELLING AND INTERRUPTING SCRIPTS
--------------------------------------
From another thread, while EvaluateScript is running:

    ReturnCode CancelEvaluate(Result result, CancelFlags cancelFlags,
        ref Result error)
        'result' is the message the cancelled evaluation returns ("eval
        canceled" when null). Cancel the current evaluation on this
        interpreter.
    ReturnCode CancelAnyEvaluate(Result result, CancelFlags cancelFlags,
        ref Result error)
    ReturnCode CancelAnyEvaluate(object engineContext, Result result,
        CancelFlags cancelFlags, ref Result error)
        Cancel evaluation on any thread using this interpreter.
    ReturnCode ResetCancel(CancelFlags cancelFlags, ref Result error)
    ReturnCode ResetCancel(object engineContext, CancelFlags cancelFlags,
        ref Result error)
        Clear a pending cancel so the interpreter can evaluate again.
    static ReturnCode Engine.CancelEvaluate(...)
    static ReturnCode Engine.ResetCancel(...)
    static ReturnCode Engine.IsCanceled(...)
CancelFlags essentials: Default; Cancel (cooperative — the running script
sees an error at its next command boundary); Unwind (unwind the whole
evaluation, [catch] cannot swallow it); Notify; Global / Local /
ResetGlobal / ResetLocal / ResetAll; UseThreadInterrupt / UseThreadAbort /
WaitForThread for stuck native waits; NoComplain. Script-side equivalents
are [interp cancel ?-unwind? ?path? ?result?] and [interp resetcancel].
Cancellation is checked at each command's readiness check — so it does
NOT fire promptly while ProductionMode is on (section 13).

7. SCRIPT THREADS (ScriptThread / IScriptThread)
------------------------------------------------
A ScriptThread is a dedicated OS thread that owns its own interpreter and
serially processes work you send to it — the right tool for "run this
Tcl in the background and marshal results back", and the pattern the
TkCanvas bridge uses for hosted Tcl/Tk apps.

    static IScriptThread ScriptThread.Create(string varName,
        ref Result error)
    static IScriptThread ScriptThread.Create(string varName,
        int maxStackSize, ref Result error)
    static IScriptThread ScriptThread.Create(string varName,
        int maxStackSize, int timeout, ref Result error)
        'varName' is the Tcl variable (in the thread's interpreter) that
        is set when the thread has work/finishes; pass null if unused.
        Longer overloads add name, ThreadFlags, CreateFlags,
        HostCreateFlags, InitializeFlags, ScriptFlags, InterpreterFlags
        and args. Static Attach(...) overloads wrap an EXISTING
        interpreter instead of creating one.
    bool Start()                 bool Stop()   bool Stop(bool force)
    bool WaitForStart() / WaitForStart(int timeout)
    bool WaitForEnd()   / WaitForEnd(int timeout)
    bool WaitForEmpty() / WaitForEvent()
    ReturnCode Send(string text, ref Result result)
    ReturnCode Send(string text, bool useEngine, ref Result result)
    ReturnCode Send(string text, int timeout, bool useEngine, ref Result r)
        SYNCHRONOUS: evaluate 'text' on the script thread and wait for
        the result (indefinitely, or up to 'timeout' ms).
    bool Queue(EventCallback callback, IClientData clientData)
    bool Queue(DateTime dateTime, EventCallback callback,
        IClientData clientData)
        ASYNCHRONOUS: run a C# callback on the script thread —
        delegate ReturnCode EventCallback(Interpreter interpreter,
            IClientData clientData, ref Result result);
    ReturnCode AddObject(...)  (several overloads: expose a .NET object in
        the thread's interpreter; section 11)
    Properties: Interpreter Interpreter, Thread Thread, long Id, string
    Name, bool IsAlive, bool IsBusy, bool IsDisposed, ReturnCode
    ReturnCode, Result Result, int Timeout, bool IsBackground, IHost Host,
    CreateFlags CreateFlags, HostCreateFlags HostCreateFlags, ...
    ScriptThread is IDisposable (dispose stops the thread and disposes
    its interpreter).

    Example:
        Result error = null;
        using (IScriptThread worker = ScriptThread.Create(null, ref error))
        {
            if (worker == null) throw new InvalidOperationException(error);
            Result result = null;
            worker.Send("proc fib {n} { expr {$n < 2 ? $n : [fib [expr {$n-1}]] + [fib [expr {$n-2}]]} }; fib 20", ref result);
            Console.WriteLine(result);   // 6765
        }

Threading rules for the plain Interpreter: drive one interpreter from one
thread at a time (the engine entry points are re-entrant and lock
internally, but interleaving two evaluations on one interpreter from two
threads is not a supported pattern). Creating several interpreters
concurrently is also discouraged — the engine keeps process-global state,
which is why the repository's test suite runs strictly sequentially.

8. EVENTS, TIMERS AND THE EVENT LOOP
------------------------------------
The Tcl commands after, vwait and update are implemented. The queue behind
them is IEventManager (interpreter.EventManager). From C#:
    ReturnCode QueueScript(DateTime dateTime, string text, ref Result error)
    ReturnCode QueueScript(DateTime dateTime, string text,
        ref IEvent @event, ref Result error)
        Queue script text to run when the interpreter next services
        events (at or after 'dateTime'; use DateTime.UtcNow for "asap").
Queued scripts and [after] callbacks run when the script calls [vwait],
[update] or [after idle]-style pumping, or when a ScriptThread services
its queue — there is no hidden background pump on a plain Interpreter.
The TkCanvas package supplies a UI-thread dispatcher for "after"/"update"
when you host a Tk UI.

9. HOSTS (IHost / IInteractiveHost) AND THE SHELL
-------------------------------------------------
A host is the interpreter's console/IO surface: [puts] to stdout, prompts,
title, colors, "read line". IHost is the union of IDisplayHost,
IInteractiveHost, IFileSystemHost, IThreadHost, IProcessHost,
IStreamHost, IDebugHost (and more). The part you interact with:

    public interface IInteractiveHost : IIdentifier
    {
        string Title { get; set; }         bool RefreshTitle();
        bool IsInputRedirected();          bool IsOpen();
        bool Pause();                      bool Flush();
        int ReadLevels { get; }            int WriteLevels { get; }
        bool ReadLine(ref string value);
        bool Write(char value);            bool Write(string value);
        bool WriteLine();                  bool WriteLine(string value);
        bool WriteResultLine(ReturnCode code, Result result);
        bool WriteResultLine(ReturnCode code, Result result, int errorLine);
        ReturnCode Prompt(PromptType type, ref PromptFlags flags,
            ref Result error);
        ReturnCode BeginProcessing/EndProcessing(int levels,
            ref string text, ref Result error);
        ReturnCode DoneProcessing(int levels, ref Result error);
        HeaderFlags GetHeaderFlags(); DetailFlags GetDetailFlags();
        HostFlags GetHostFlags();
    }

Shipped hosts (namespace CodeBrix.Platform.TclTk._Hosts):
    Console     the real process console (what Create(ref Result) gives
                you when a console exists)
    Diagnostic  writes through System.Diagnostics tracing
    Null        discards everything (headless services, tests)
    Fake        a minimal in-memory host
    Wrapper     forwards to another IHost you supply
    abstract Default -> Engine -> File -> Profile -> Shell -> Core: the
    base chain to derive a custom host from (override the Write*/ReadLine
    members you care about; Core is the usual base).
All take an IHostData in the constructor:
    public HostData(string name, string group, string description,
        IClientData clientData, string typeName, Interpreter interpreter,
        ResourceManager resourceManager, string profile,
        HostCreateFlags hostCreateFlags);
Access: interpreter.Host (IHost) / interpreter.InteractiveHost. To supply
your own host at creation, set IInterpreterSettingsData.Host, or use the
NewHostCallback delegate (delegate IHost NewHostCallback(IHostData
hostData)) where an API accepts one. A Tcl [puts] with no channel goes to
the host's Write; redirecting script output into your application
therefore means either a custom host or a Tcl channel of your own.

REPL / shell entry points (static on Interpreter):
    static ExitCode ShellMain(IEnumerable<string> args)
    static ExitCode ShellMain(IInterpreterSettings interpreterSettings,
        IEnumerable<string> args)
        A complete tclsh-style shell: create, run the args/script, drop
        into the interactive loop on the console host. Use it to make
        your own "tclsh" executable in three lines.
    static ReturnCode InteractiveLoop(Interpreter interpreter,
        IEnumerable<string> args, ref Result result)
        Run the interactive read-eval-print loop on an interpreter you
        already created (needs a host that can ReadLine).
InterpreterHelper (sealed) bundles "create + optional interactive loop +
dispose": static InterpreterHelper Create(...), ReturnCode
InteractiveLoop(...), bool RemoveInterpreter(), Dispose().

10. PLUGINS, PACKAGES, POLICIES AND SAFE INTERPRETERS
-----------------------------------------------------
Plugins group commands/functions/policies/traces into one loadable unit
(the script side is [load] / [unload] and "package require" via
pkgIndex). Contract:
    public interface IPlugin : IPluginData, IState, IExecuteRequest
    {   void PostInitialize(Interpreter interpreter, IClientData clientData);
        ReturnCode GetFramework(Guid? id, FrameworkFlags flags,
            ref Result result);
        Stream GetStream(Interpreter, string name, CultureInfo,
            ref Result error);
        string GetString(Interpreter, string name, CultureInfo,
            ref Result error);
        Uri GetUri(...); ... }
    IPluginData: PluginFlags Flags, Version Version, Uri Uri, Assembly
        Assembly, AssemblyName AssemblyName, string FileName,
        CommandDataList Commands, PolicyDataList Policies, LongList
        CommandTokens/FunctionTokens/PolicyTokens/TraceTokens,
        ResourceManager ResourceManager, ObjectDictionary AuxiliaryData.
    Base class: CodeBrix.Platform.TclTk._Plugins.Default(IPluginData) —
        override Initialize(Interpreter, IClientData, ref Result) to
        AddCommand your commands and Terminate(...) to remove them.
    ReturnCode AddPlugin(IPlugin plugin, IClientData clientData,
        ref long token, ref Result result)
    ReturnCode RemovePlugin(long token | string name,
        IClientData clientData, ref Result result)
For an in-process embedding you usually do NOT need a plugin: AddCommand
+ ProvidePackage (section 5) is enough. Plugins matter when scripts must
be able to [load] your assembly by name.

Packages ([package] command model):
    public interface IPackage : IPackageData, IState
    {   ReturnCode Select(PackagePreference preference,
            ref Version version, ref Result error);
        ReturnCode Load(Interpreter interpreter, Version version,
            ref Result result); }
    IPackageData: string IndexFileName, string ProvideFileName,
        PackageFlags Flags, Version Loaded, VersionStringDictionary
        IfNeeded, string WasNeeded.
    ReturnCode AddPackage(IPackage package, IClientData clientData,
        ref long token, ref Result result)
    ReturnCode ProvidePackage(string name, Version version,
        ref Result result)          (the one you actually need)

Policies decide, per command execution, whether a command may run — the
mechanism behind safe interpreters and custom sandboxes:
    public interface IPolicy : IPolicyData, IDynamicExecuteCallback,
        IExecute, ISetup { }
    IPolicyData: string MethodName, BindingFlags BindingFlags,
        MethodFlags MethodFlags, PolicyFlags PolicyFlags.
    ReturnCode AddPolicy(ExecuteCallback callback, IPlugin plugin,
        IClientData clientData, ref long token, ref Result result)
    ReturnCode AddPolicy(IPolicy policy, IClientData clientData,
        ref long token, ref Result result)
    ReturnCode RemovePolicy(long token | string name, IClientData
        clientData, ref Result result)
    enum PolicyDecision: None, Undecided, Denied, Approved, Continue,
        Pending, Stop, Success, Unknown, Failure.
    Policies receive a PolicyContext through the callback's clientData;
    the base class CodeBrix.Platform.TclTk._Policies.Default exists for
    class-based policies. The pre-built policies installed by
    CreateFlags (unless NoCorePolicies) are what enforce "safe".

Safe interpreters — three routes:
  1. Create with CreateFlags.SafeEmbeddedUse (or SafeSingleUse /
     FastSafeSingleUse) and HostCreateFlags.SafeEmbeddedUse: only
     commands flagged CommandFlags.Safe exist, unsafe ones are hidden.
  2. Convert an existing interpreter:
         bool IsSafe()
         ReturnCode MakeSafe(MakeFlags makeFlags, bool safe,
             ref Result error)
             MakeFlags: SafeAll, SafeLibrary, SafeShell, IncludeCommands,
             IncludeProcedures, IncludeFunctions, IncludeOperators,
             IncludeVariables, IncludeLibrary, Default, ...
         bool IsStandard() / ReturnCode MakeStandard(...)
  3. A child interpreter:
         ReturnCode CreateChildInterpreter(string path,
             IClientData clientData,
             IInterpreterSettings interpreterSettings, bool isolated,
             bool security, ref Result result)
         or, in script, [interp create -safe child] then
         [interp eval child {...}] / [interp alias ...] (the full [interp]
         ensemble is implemented, including cancel/resetcancel/issafe).
     Commands you register yourself are absent from a safe interpreter
     unless their CommandFlags include Safe; alias a trusted parent
     command into the child instead of flagging arbitrary commands Safe.

11. BRIDGING .NET OBJECTS INTO SCRIPTS ([object] AND AddObject)
---------------------------------------------------------------
Two directions exist.

From C#: hand an existing object to the interpreter under a name.
    ReturnCode AddObject(string name, Type type, ObjectFlags objectFlags,
        IClientData clientData, int referenceCount, string interpName,
        ArgumentList executeArguments, object value,
        ref long token, ref Result result)
        'interpName' (the native-Tcl interpreter to expose the object in)
        and 'executeArguments' (debugger context) are compiled in by this
        build's symbol set; pass null for both in normal use.
    ReturnCode AddObject(IObject @object, IClientData clientData,
        ref long token, ref Result result)
    ReturnCode RemoveObject(long token | string name,
        IClientData clientData, ref bool dispose, ref Result result)
    (also RemoveObject(..., bool synchronous, ref bool dispose, ...))
    IObject : IObjectData, IValue, IValueData, IMaybeDisposed — AddReference()
    / RemoveReference() reference counting. Build one with
    new CodeBrix.Platform.TclTk._Objects.Default(IObjectData objectData,
    object value, IClientData valueData) and
    public ObjectData(string name, string group, string description,
        IClientData clientData, bool disposed, bool disposing, Type type,
        IAlias alias, ObjectFlags objectFlags, int referenceCount,
        int temporaryReferenceCount, string interpName,
        ArgumentList executeArguments, long token) — the same two extra
    parameters as AddObject above; pass null for both.
    ObjectFlags essentials: None, Default, Locked, Safe, NoDispose,
    AutoDispose / NoAutoDispose, AllowExisting, ForceNew, ForceDelete,
    NoComplain, AddReference, SharedObject, NullObject.
    The interpreter then knows the object by 'name'; scripts reach it
    through the [object] command below. Objects with a non-zero reference
    count are disposed when the last reference goes (or when the
    interpreter is disposed) unless NoDispose is set.

From script: the [object] command ensemble (present in the default command
set; it is what CreateFlags.NoObjects / NoObjectPlugin remove). Verified
subcommands: create, invoke, invokeall, invokeraw, load, dispose, members,
import, declare, get, alias, exists, isnull, isoftype, isdisposed,
interfaces, list, search, assemblies, addreference, removereference,
resolve, strongname, hash, foreach, lmap, fromvar, flags.
    object load System.Text.RegularExpressions   ;# by assembly name
    set sb [object create System.Text.StringBuilder]
    object invoke $sb Append "hello"
    object invoke $sb Length                     ;# property read
    object invoke System.Math Max 3 7            ;# static call -> 7
    object dispose $sb
Type resolution uses the assemblies already loaded plus [object load];
[object import System.IO] shortens type names. Method arguments are
converted from their Tcl strings by the engine's binder; ambiguous
overloads are resolved by argument count and convertibility — pass
"-argumenttypes" style options (see [object invoke] usage text in the
interpreter: "object invoke" with no arguments prints its syntax) when it
picks the wrong one. Boolean results follow BooleanResultMode (section
14), so a .NET bool comes back "True"/"False" by default.

12. VARIABLE TRACES AND BREAKPOINT CALLBACKS
--------------------------------------------
Script side: the full [trace] command — trace add/remove/info for
variable (read/write/unset/array), command (rename/delete) and execution
traces (see the divergence notes in section 15).

C# side: a trace is an ITrace whose Execute fires on variable access.
    delegate ReturnCode TraceCallback(BreakpointType breakpointType,
        Interpreter interpreter, ITraceInfo traceInfo, ref Result result);
    public interface ITrace : ITraceData, IDynamicExecuteTrace,
        IExecuteTrace, ISetup { }
    ITraceData: string MethodName, BindingFlags BindingFlags, MethodFlags
        MethodFlags, TraceFlags TraceFlags (+ IIdentifier, IHavePlugin).
    Base class CodeBrix.Platform.TclTk._Traces.Default(ITraceData traceData)
        with
        public virtual ReturnCode Execute(BreakpointType breakpointType,
            Interpreter interpreter, ITraceInfo traceInfo,
            ref Result result);
    public TraceData(string name, string group, string description,
        IClientData clientData, string typeName, Type type,
        string methodName, BindingFlags bindingFlags,
        MethodFlags methodFlags, TraceFlags traceFlags, IPlugin plugin,
        long token);
    ITraceInfo: ITrace Trace, BreakpointType BreakpointType, ICallFrame
        Frame, IVariable Variable, string Name, string Index (array
        element), VariableFlags Flags, object OldValue, object NewValue,
        ElementDictionary OldValues/NewValues, bool Cancel, bool
        PostProcess, ReturnCode ReturnCode.
    BreakpointType values a variable trace sees: BeforeVariableGet,
        BeforeVariableSet, BeforeVariableUnset, BeforeVariableAdd,
        BeforeVariableExist, BeforeVariableCount, BeforeVariableReset,
        BeforeVariableArrayNames / ArrayValues / ArrayGet (plus the masks
        BeforeVariable, BeforeVariableScalar, BeforeVariableArray).
Attach traces when creating or setting a variable:
    AddVariable(VariableFlags.None, "watched", new TraceList { trace },
        false, ref error);
    SetVariableValue(VariableFlags.None, "watched", "1",
        new TraceList { trace }, ref error);
or interpreter-wide at creation through the TraceList parameter of
Interpreter.Create(...) / IInterpreterSettingsData.Traces (those fire for
every variable and are how a debugger watches an interpreter). Returning
ReturnCode.Error from Execute aborts the access with your message;
setting traceInfo.NewValue on a BeforeVariableSet rewrites the value.
ICallFrame (the frame handed to traces and available to commands via
traceInfo.Frame) exposes FrameId, Level, Arguments, ProcedureArguments,
VariableDictionary Variables, Previous/Next, and CallFrameFlags Flags.

13. PERFORMANCE SWITCHES: ProductionMode AND CacheParsedScripts
---------------------------------------------------------------
    bool ProductionMode { get; set; }     default false
        Sets EngineFlags.FastMask: skips the per-command readiness check,
        debugger breakpoint checks, change notifications, callback-queue
        processing, argument caching, usage counters and previous-result
        tracking. Results are byte-identical; measured ~1.8x faster on
        script-heavy batch work. Trade-off: cancellation (section 6) is
        no longer prompt. Setting it back to false clears ALL FastMask
        bits, including any you set individually via EngineFlags.
    bool CacheParsedScripts { get; set; } default false
        Caches the tokenized form of any script text evaluated more than
        once (proc bodies, loop/if/catch/switch bodies, bracketed
        substitutions) from its SECOND evaluation on; entries live for
        the interpreter's lifetime. Results, error messages, error line
        numbers and cancellation are unchanged. Can be toggled any time;
        turning it off stops lookups but keeps existing entries.
Both are independent; the DRAKON.Brix sample turns both on for its
diagram re-rendering workload.

14. LANGUAGE LEVEL AND BooleanResultMode
----------------------------------------
The engine implements the Tcl language including namespaces, upvar/uplevel,
arrays, glob/file/open channels, regexp/regsub, dict, the Tcl 8.5/8.6
features "binary" (format/scan + base64/hex/uuencode encode/decode),
"tailcall" (with stack elimination), "trace" (variable, command rename/
delete and execution traces), [interp], [after]/[vwait]/[update], and the
"{*}" argument expansion. Behavior is validated against real tclsh as the
oracle; the DRAKON Editor code generator runs byte-identical to native
tclsh on it.

*** BooleanResultMode — MAKE BOOLEAN RESULTS MATCH real tclsh ***
By DEFAULT this engine renders a boolean RESULT as the .NET-style string
"True"/"False", where real tclsh renders the canonical "1"/"0". This
applies both to a boolean-valued [expr] AND to the boolean-returning
commands:

    expr {1 && 1}               -> "True"   (tclsh: "1")
    expr {1 < 0}                -> "False"  (tclsh: "0")
    string equal a a            -> "True"   (tclsh: "1")
    info complete {set x}       -> "True"   (tclsh: "1")
    interp exists {} / issafe   -> "True"/"False"  (tclsh: "1"/"0")
    dict exists {a 1} a         -> "True"   (tclsh: "1")
    package vsatisfies 8.6 8.5  -> "True"   (tclsh: "1")
    eof $chan / fblocked $chan  -> "True"/"False"  (tclsh: "1"/"0")

This is inherited upstream behavior. Boolean CONTEXTS are unaffected
either way — [if], [while], &&/|| short-circuit and the ?: ternary
coerce the value to an actual boolean, so conditionals always behave
correctly. The divergence only BITES when code treats a boolean result
as a LITERAL STRING, i.e. in DEFAULT (EagleCompat) mode:

    pattern                       tclsh      EagleCompat (ours)  consequence
    ----------------------------  ---------  ------------------  ----------------------------
    set x [expr {2>1}]                                           x is "True", not "1" ...
      switch -- $x {1 {..} 0 {..}} matches 1  falls to default   ... wrong branch dispatched
      string length $x            1          4                   ... off by 3
      "count=$x" (output/store)   count=1    count=True          ... wrong text to file/SQLite/gen
    string equal $a $b            1/0        True/False          ... string compare / switch fails

Risk categories: [switch] on a computed flag, string-identity comparisons,
and anywhere a boolean result is EMITTED or STORED (a data file, generated
source code, a UI string).

THE FIX — choose the mode ONCE, at Interpreter.Create() (it cannot be
changed afterward, so nothing can flip it mid-run and desync your scripts):

    Result r = null;
    using Interpreter interp = Interpreter.Create(
        ref r, BooleanResultMode.TclshCompat);
    // now EVERY boolean result above renders "1"/"0", byte-for-byte tclsh

  * BooleanResultMode.EagleCompat (DEFAULT) — historical "True"/"False"
    rendering; what Create(ref r) uses when you omit the argument.
  * BooleanResultMode.TclshCompat — canonical "1"/"0" for boolean [expr]
    results AND boolean-returning commands (string equal, info complete,
    info default, interp exists, interp issafe, dict exists, package
    vsatisfies, eof, fblocked, and the string starts/ends helpers).
  The Interpreter.BooleanResultMode property has a getter but no setter.
  Only the Create(ref Result, BooleanResultMode) overload takes the mode;
  every other Create overload yields EagleCompat.

15. KNOWN LANGUAGE DIVERGENCES (all deliberate or documented)
-------------------------------------------------------------
  * "binary format" integers wider than 64 bits wrap instead of erroring.
  * trace's variable "array" operation is accepted but never fires;
    command delete traces fire only for deletions via [rename].
  * "string is dict" (not in tclsh either), "file owned", and the
    "chan eof"/"chan blocked" aliases are unimplemented.
  * Boolean rendering: section 14.
Repository policy: divergences from stock Tcl are closed with OPT-IN
switches (like BooleanResultMode) so existing consumers never change
behavior silently.

COMPLETE EXAMPLES
=================

A. Evaluate, read the result, handle errors

    using System;
    using CodeBrix.Platform.TclTk._Components.Public;

    Result result = null;
    using (Interpreter interpreter = Interpreter.Create(ref result))
    {
        if (interpreter == null)
            throw new InvalidOperationException("create failed: " + result);

        int errorLine = 0;
        ReturnCode code = interpreter.EvaluateScript(
            "proc square {x} { expr {$x * $x} }\nsquare 12", ref result, ref errorLine);

        if (code == ReturnCode.Ok)
            Console.WriteLine(result);                 // 144
        else
            Console.WriteLine("error at line {0}: {1}", errorLine, result);

        code = interpreter.EvaluateScript("error {custom failure}", ref result);
        // code == ReturnCode.Error, result == "custom failure"
        Result info = null, error = null;
        interpreter.GetVariableValue("::errorInfo", ref info, ref error);
        Console.WriteLine(info);                       // stack trace text
    }

B. Variables both ways

    Result error = null, value = null;
    interpreter.SetVariableValue("name", "world", ref error);
    interpreter.EvaluateScript("set greeting \"hello, $name\"", ref error);
    interpreter.GetVariableValue("greeting", ref value, ref error);
    Console.WriteLine(value);                          // hello, world
    interpreter.SetVariableValue("counter", "10", ref error);
    interpreter.EvaluateScript("incr counter 5", ref value);   // 15
    interpreter.UnsetVariable("counter", ref error);
    bool exists = interpreter.DoesVariableExist(VariableFlags.None, "counter")
        == ReturnCode.Ok;                               // false

C. Custom command — see section 5 (GreetCommand); the Extras package's
   sqlite3 and pdf4tcl commands are the same pattern in production.

D. Cancel a runaway script from another thread

    var worker = new System.Threading.Thread(() =>
    {
        Result r = null;
        ReturnCode c = interpreter.EvaluateScript("while {1} {}", ref r);
        Console.WriteLine("{0}: {1}", c, r);   // Error: eval canceled
    });
    worker.Start();
    System.Threading.Thread.Sleep(200);
    Result cancelError = null;
    interpreter.CancelEvaluate(null, CancelFlags.Default, ref cancelError);
    worker.Join();
    interpreter.ResetCancel(CancelFlags.Default, ref cancelError); // reusable

E. Background script thread — section 7.

F. Safe sandbox for untrusted text

    Result r = null;
    using Interpreter sandbox = Interpreter.Create(
        null, CreateFlags.SafeEmbeddedUse, HostCreateFlags.SafeEmbeddedUse, ref r);
    ReturnCode c = sandbox.EvaluateScript("file delete /etc/passwd", ref r);
    // c == ReturnCode.Error: the unsafe [file] subcommands are hidden

G. Your own tclsh

    using CodeBrix.Platform.TclTk._Components.Public;
    return (int)Interpreter.ShellMain(args);

MINIMUM VIABLE PROJECT
======================
A console application that embeds the interpreter and runs a script file
given on the command line (or a demo script when none is given).

    TclRunner.csproj:

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>disable</ImplicitUsings>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.TclTk.BsdLicenseForever"
                          Version="*" />
      </ItemGroup>
    </Project>

    Program.cs:

    using System;
    using CodeBrix.Platform.TclTk._Components.Public;

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Result result = null;
            using (Interpreter interpreter = Interpreter.Create(
                       ref result, BooleanResultMode.TclshCompat))
            {
                if (interpreter == null)
                {
                    Console.Error.WriteLine("cannot create interpreter: " + result);
                    return 2;
                }

                interpreter.CacheParsedScripts = true;

                ReturnCode code;
                int errorLine = 0;

                if (args.Length > 0)
                    code = interpreter.EvaluateFile(args[0], ref result, ref errorLine);
                else
                    code = interpreter.EvaluateScript(
                        "set total 0\n" +
                        "foreach n {1 2 3 4 5} { incr total $n }\n" +
                        "puts \"total = $total\"\n" +
                        "expr {$total > 10}", ref result, ref errorLine);

                if (code != ReturnCode.Ok)
                {
                    Console.Error.WriteLine("error (line {0}): {1}", errorLine, result);
                    return 1;
                }

                Console.WriteLine("result: " + result);   // result: 1
                return 0;
            }
        }
    }

Replace Version="*" with the current package version from nuget.org.
[puts] writes through the interpreter's Console host, so script output
appears on the process console with no extra wiring.

PERFORMANCE TIPS
================
  * The managed interpreter is substantially slower than native tclsh
    (order of magnitude, workload-dependent). Budget accordingly.
  * Turn on CacheParsedScripts for anything that re-runs proc bodies or
    loops; it is free of semantic side effects.
  * Turn on ProductionMode for batch/generation work that never needs
    cancellation; ~1.8x on top. Leave it off for interactive or
    cancellable interpreters.
  * Create interpreters rarely and reuse them: creation loads and
    evaluates the embedded script library. CreateFlags.FastSingleUse /
    LeanAndMeanUse trim creation cost when you need many short-lived
    interpreters.
  * Prefer one EvaluateScript call with a multi-line script over many
    tiny calls; each call has fixed entry overhead.
  * Keep [object invoke] out of hot loops; every call binds by reflection.
  * Do not create interpreters concurrently on several threads.

COMMON PITFALLS TO AVOID
========================
  * Treating a boolean result as text ("True" vs "1") — pass
    BooleanResultMode.TclshCompat to Create when scripts store, print,
    switch on, or string-compare boolean results (section 14).
  * Forgetting that Result can be null: "result.ToString()" on a
    never-assigned Result throws. Use "result == null ? "" : result".
  * Checking the Result instead of the ReturnCode: an error message is a
    perfectly valid Result; the ReturnCode is the only status signal.
  * Using the interpreter after Dispose: every member throws
    (ThrowOnDisposed is in the default flags). Check interpreter.Disposed
    if lifetime is shared.
  * Expecting Create to always return null on failure: with the default
    flags a script-library initialization failure throws (ThrowOnError).
    Catch exceptions around Create as well as testing for null.
  * Importing two "_Xxx" namespaces that both define Default — CS0104
    ambiguity. Import only the base you derive from, or alias it.
  * arguments[0] is the command name; the first real argument is
    arguments[1]. Off-by-one here is the most common custom-command bug.
  * A command registered with CommandFlags.None disappears in a safe
    interpreter; that is by design, not a registration failure.
  * ProductionMode on + expecting [interp cancel] / CancelEvaluate to be
    prompt — it is not; cancellation waits for a readiness check that
    FastMask skips.
  * Turning ProductionMode off clears every FastMask EngineFlags bit,
    including ones you set individually.
  * Driving one interpreter from two threads at once, or creating
    interpreters in parallel — serialize both.
  * Expecting [after]/[vwait] callbacks to fire without anything pumping
    events: on a plain Interpreter they run during [vwait]/[update] or
    on a ScriptThread that services its queue.
  * Reading array elements with GetVariableValue("a(k)", ...) — use
    script text ("set a(k)", "array get a") from C# instead.
  * Writing "Eagle" into identifiers or using the upstream namespaces —
    they do not exist in this package.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * No Tk, no GUI, no canvas: that is CodeBrix.Platform.TkCanvas (see the
    catalogue in OVERVIEW). The interpreter alone has no "button",
    "pack", "wm", "tk_messageBox" commands.
  * No sqlite3 / pdf4tcl commands: CodeBrix.Platform.TclTk.Extras.
  * It does not BUNDLE a native Tcl/Tk library, and the managed engine
    never needs one. The upstream native bridge IS compiled in —
    Interpreter implements ITclManager / ITclEntityManager (HasTcl,
    LoadTcl, UnloadTcl, GetTclPatchLevel, CreateTclInterpreter,
    EvaluateTclScript, ...) and the [tcl] script command exists — but
    each of those needs a real Tcl shared library already installed on
    the machine, and [tcl] carries CommandFlags.NativeCode | Unsafe, so
    it is hidden in safe interpreters. Nothing in this repository's test
    suite exercises that path; treat it as unverified and stay on the
    managed engine, which needs nothing native.
  * No tcllib/tklib: "package require snit/msgcat/..." fail unless you
    supply the packages (the DRAKON.Brix sample shows how to stub them).
  * No Windows-only console/WinForms/WinTrust features of the upstream
    engine: native console, key/form helpers and Authenticode-via-WinTrust
    are excluded from the build (PKCS#7 checks remain, via the Pkcs
    dependency).
  * No XML documentation file ships with this package (the ported engine
    has no upstream doc comments); IntelliSense shows signatures only.
    The sibling packages do ship XML docs.
  * Unimplemented Tcl corners: "string is dict", "file owned", "chan eof"
    / "chan blocked" aliases; the trace "array" op never fires (section
    15).
  * It does not sandbox by default: a default interpreter can read and
    write files, open sockets and [exec] processes. Use a safe
    interpreter for untrusted script text (section 10).

WORKING EXAMPLES ON GITHUB
==========================
Repository: https://github.com/ellisnet/CodeBrix.Platform.TclTk

Interpreter test suite (xUnit v3 + SilverAssertions; every expected value
probed against real tclsh):
  https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TclTk.Tests
    TclTkTest.cs                     the shared helpers (create, Eval,
                                     TryEval, EvalOnce) — copy this shape
    InterpreterSmokeTests.cs         create + evaluate
    InterpreterLifecycleAndErrorTests.cs  dispose semantics, isolation
                                     between interpreters, error codes
    InterpreterVariableTests.cs      Set/GetVariableValue, incr on a
                                     managed value, namespace resolution
    InterpreterEvaluationTests.cs    language behavior
    BooleanResultModeTests.cs        EagleCompat vs TclshCompat
    ProductionModeTests.cs / CacheParsedScriptsTests.cs  the two switches
    TraceCommandTests.cs             variable/command/execution traces
    TailcallCommandTests.cs, ArgumentExpansionTests.cs,
    BinaryFormatTests.cs, BinaryScanTests.cs, BinaryEncodeDecodeTests.cs,
    VariableLinkLifetimeTests.cs     Tcl 8.5/8.6 feature coverage

Custom commands in production (derive from _Commands.Default, CommandData,
AddCommand, ProvidePackage):
  https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/src/CodeBrix.Platform.TclTk.Extras
    TclTkExtras.cs, Sqlite/Sqlite3Command.cs, Pdf/Pdf4TclFactoryCommands.cs

A complete application booting a large unmodified Tcl program on this
interpreter (CacheParsedScripts + ProductionMode, Extras, Tk bridge on a
dedicated Tcl thread):
  https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/samples/DRAKON.Brix
    src/libs/DRAKON.Brix.TclBridge/DrakonRuntime.cs   the boot sequence
    src/libs/DRAKON.Brix.TclBridge/Commands/          app-specific commands
    src/DRAKON.Brix.Core/Assets/bootstrap.tcl         stubbing tcllib
                                                      packages in script

QUICK REFERENCE CARD
====================
    Package      CodeBrix.Platform.TclTk.BsdLicenseForever   (.NET 10+)
    Usings       CodeBrix.Platform.TclTk._Components.Public
                 CodeBrix.Platform.TclTk._Interfaces.Public
                 CodeBrix.Platform.TclTk._Containers.Public
                 CodeBrix.Platform.TclTk._Commands   (Default base class)

    Create       Interpreter.Create(ref Result r)
                 Interpreter.Create(ref r, BooleanResultMode.TclshCompat)
                 Interpreter.Create(args, CreateFlags, HostCreateFlags, ref r)
                 Interpreter.Create(IInterpreterSettings, bool strict, ref r)
                 -> null (or throws) on failure; always "using"
    Flags        CreateFlags.Default | EmbeddedUse | SafeEmbeddedUse |
                 FastSingleUse ; HostCreateFlags.Default | EmbeddedUse
    Evaluate     EvaluateScript(text, ref r [, ref errorLine])
                 EvaluateExpression(text, ref r)
                 EvaluateFile(fileName, ref r)   EvaluateStream(name, reader, ref r)
                 SubstituteString(text, ref r)   Invoke(name, cd, ArgumentList, ref r)
                 EvaluateScript(text, AsynchronousCallback, cd, ref err)
    Status       ReturnCode.Ok/Error/Return/Break/Continue ; Result <-> string
                 ::errorInfo / ::errorCode via GetVariableValue
    Variables    SetVariableValue(name, value, ref err)
                 GetVariableValue(name, ref value, ref err)
                 UnsetVariable(name, ref err)  DoesVariableExist(flags, name)
                 AddVariable(flags, name, TraceList, errorOnExist, ref err)
    Commands     class X : _Commands.Default { ctor(ICommandData);
                   override Execute(Interpreter, IClientData, ArgumentList, ref Result) }
                 new CommandData(name, group, desc, cd, typeName, CommandFlags, plugin, 0)
                 AddCommand(cmd, cd, ref token, ref r)  RemoveCommand(token|name, cd, ref r)
                 AddIExecute(name, IExecute, cd, ref token, ref r)
                 ProvidePackage(name, Version, ref r)   AddFunction(...)
    Cancel       CancelEvaluate(Result msg, CancelFlags, ref err)
                 CancelAnyEvaluate(...)  ResetCancel(CancelFlags, ref err)
                 Engine.IsCanceled / ResetCancel / CancelEvaluate (static)
    Threads      ScriptThread.Create(varName, ref err) -> IScriptThread
                 Start()  Send(text, ref r)  Queue(EventCallback, cd)
                 WaitForEnd()  Stop()  Dispose()
    Events       QueueScript(DateTime, text, ref err) ; EventManager
                 after / vwait / update in script
    Hosts        interpreter.Host (IHost) / InteractiveHost
                 _Hosts.Console | Null | Diagnostic | Fake | Wrapper
                 Interpreter.ShellMain(args)  Interpreter.InteractiveLoop(...)
    Sandbox      CreateFlags.SafeEmbeddedUse ; IsSafe() ; MakeSafe(MakeFlags, bool, ref err)
                 CreateChildInterpreter(path, cd, settings, isolated, security, ref r)
                 [interp create -safe]
    Objects      AddObject(name, Type, ObjectFlags, cd, refCount, null, null,
                   value, ref token, ref r)     (the two nulls: interpName,
                                                 executeArguments)
                 [object create|invoke|load|dispose|members|import ...]
    Traces       _Traces.Default.Execute(BreakpointType, Interpreter, ITraceInfo, ref r)
                 TraceCallback ; TraceList on AddVariable/SetVariableValue/Create
    Tuning       CacheParsedScripts = true ; ProductionMode = true (no prompt cancel)
    Plugins      IPlugin / _Plugins.Default ; AddPlugin(plugin, cd, ref token, ref r)
    Policies     AddPolicy(ExecuteCallback|IPolicy, ...) ; PolicyDecision
    Booleans     default "True"/"False" ; TclshCompat -> "1"/"0"
    Siblings     .TclTk.Extras (sqlite3, pdf4tcl) ; .TkCanvas (Tk toolkit)
================================================================================
