# CodeBrix.Platform.TclTk

A fully managed, cross-platform implementation of the Tcl scripting language for .NET.
CodeBrix.Platform.TclTk embeds a complete Tcl interpreter — variables, expressions, commands,
procedures, namespaces, and a rich two-way .NET object-interop bridge — directly into your
application, with no native dependencies.
CodeBrix.Platform.TclTk has a single NuGet dependency (`System.Security.Cryptography.Pkcs`, a Microsoft first-party package used for signature verification — the same dependency the upstream Eagle package declares), and is provided as a .NET 10 library and associated `CodeBrix.Platform.TclTk.BsdLicenseForever` NuGet package.

CodeBrix.Platform.TclTk supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## CodeBrix.Platform.TclTk supports:

* Creating one or more independent, embeddable Tcl interpreters in-process
* Evaluating Tcl scripts and expressions from C#, and reading their results
* Getting and setting Tcl variables (scalars and arrays) from managed code
* The full core Tcl command set, procedures (`proc`), and `namespace`s
* A built-in script library, embedded in the assembly (no files to deploy)
* A two-way object-interop bridge for calling .NET from Tcl and vice-versa
* Cross-platform operation on Windows, Linux, and macOS with zero native dependencies

## Sample Code

### Evaluating a Tcl script and reading the result

```csharp
using CodeBrix.Platform.TclTk._Components.Public;

Result result = null;

using (Interpreter interpreter = Interpreter.Create(ref result))
{
    ReturnCode code = interpreter.EvaluateScript("expr {6 * 7}", ref result);

    if (code == ReturnCode.Ok)
        System.Console.WriteLine(result); // 42
    else
        System.Console.WriteLine("error: {0}", result);
}
```

### Getting and setting variables

```csharp
using CodeBrix.Platform.TclTk._Components.Public;

Result result = null;
Result value = null;

using (Interpreter interpreter = Interpreter.Create(ref result))
{
    interpreter.SetVariableValue("name", "world", ref result);
    interpreter.EvaluateScript("set greeting \"hello, $name\"", ref result);

    interpreter.GetVariableValue("greeting", ref value, ref result);
    System.Console.WriteLine(value); // hello, world
}
```

## CodeBrix.Platform.TclTk.Extras

This repository also builds the companion `CodeBrix.Platform.TclTk.Extras.BsdLicenseForever`
NuGet package: interpreter-side Tcl command extensions that let existing Tcl programs which
expect the classic `sqlite3` and `pdf4tcl` packages run unmodified on the managed interpreter.

* `sqlite3 NAME PATH` — a tclsqlite-compatible database command (handle verbs `eval`,
  `onecolumn`, `changes`, `close`; caller-scope `:name` parameter binding; unset-variable →
  SQL NULL; NULL → empty-string read-back), backed by
  [CodeBrix.Sqlite](https://www.nuget.org/packages/CodeBrix.Sqlite.ApacheLicenseForever)
  on a PRAGMA-neutral plaintext path, so SQLite files it writes are interchangeable with
  files written by stock Tcl applications
* `pdf4tcl::new` / `pdf4tcl::loadBaseTrueTypeFont` / `pdf4tcl::createFont` and the pdf4tcl 0.7
  drawing surface (`startPage`, `setFont`, `setFillColor`, `setStrokeColor`, `setLineStyle`,
  `getStringWidth`, `text`, `line`, `rectangle`, `polygon`, `write`, `destroy`), backed by
  [CodeBrix.PdfDocuments](https://www.nuget.org/packages/CodeBrix.PdfDocuments.MitLicenseForever)

```csharp
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras;

Result result = null;

using (Interpreter interpreter = Interpreter.Create(ref result))
{
    Result error = null;
    TclTkExtras.RegisterAll(interpreter, ref error);

    interpreter.EvaluateScript(
        "sqlite3 db :memory:\n" +
        "db eval {create table t (a integer, b text)}\n" +
        "set b hello\n" +
        "db eval {insert into t values (1, :b)}\n" +
        "db onecolumn {select b from t}", ref result);

    System.Console.WriteLine(result); // hello
}
```

## Provenance

CodeBrix.Platform.TclTk is a port of the [Eagle](https://github.com/mistachkin/eagle) project
by Joe Mistachkin and the Eagle Development Team. See `THIRD-PARTY-NOTICES.txt` for full
attribution and the upstream license terms.

## License

The project is licensed under the Tcl/Tk License (for the ported upstream code) and the
BSD 2-Clause License (for modifications). See `LICENSE`, `LICENSE-MODIFICATIONS.txt`, and:
https://en.wikipedia.org/wiki/BSD_licenses
