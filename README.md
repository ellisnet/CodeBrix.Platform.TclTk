# CodeBrix.Platform.TclTk

A fully managed, cross-platform implementation of the Tcl scripting language — and of the classic Tk
widget toolkit — for .NET. CodeBrix.Platform.TclTk embeds a complete Tcl interpreter — variables,
expressions, commands, procedures, namespaces, and a rich two-way .NET object-interop bridge —
directly into your application, with no native dependencies.
CodeBrix.Platform.TclTk is provided as three .NET 10 libraries and their associated NuGet packages:
`CodeBrix.Platform.TclTk.BsdLicenseForever` (the interpreter),
`CodeBrix.Platform.TclTk.Extras.BsdLicenseForever` (interpreter-side `sqlite3` and `pdf4tcl` command
extensions) and `CodeBrix.Platform.TkCanvas.BsdLicenseForever` (the Tk widget toolkit, drawn on
SkiaSharp and hosted in a CodeBrix.Platform application).

CodeBrix.Platform.TclTk supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

The interpreter - the package to start with:

```
dotnet add package CodeBrix.Platform.TclTk.BsdLicenseForever
```

The interpreter-side `sqlite3` and `pdf4tcl` command extensions:

```
dotnet add package CodeBrix.Platform.TclTk.Extras.BsdLicenseForever
```

The Tk widget toolkit and its CodeBrix.Platform host control:

```
dotnet add package CodeBrix.Platform.TkCanvas.BsdLicenseForever
```

**Which one do I reference?** Reference `CodeBrix.Platform.TclTk.BsdLicenseForever` on its own when you
only need to run Tcl scripts from .NET. Add `CodeBrix.Platform.TclTk.Extras.BsdLicenseForever` when your
scripts expect the `sqlite3` or `pdf4tcl` commands. Reference
`CodeBrix.Platform.TkCanvas.BsdLicenseForever` when your scripts - or your C# code - build a user
interface: it brings the interpreter package in with it, so there is no need to reference the
interpreter separately. `.Extras` and `.TkCanvas` are independent of each other; reference both if you
want both.

Note that the NuGet package IDs and the namespaces are different - the license suffix is part of the
package ID only, and there is no package named plain `CodeBrix.Platform.TclTk`:

* NuGet package ID: `CodeBrix.Platform.TclTk.BsdLicenseForever` - assembly and root namespace
  `CodeBrix.Platform.TclTk`, with the public API in the `_Components.Public` namespace - i.e.
  `using CodeBrix.Platform.TclTk._Components.Public;`
* NuGet package ID: `CodeBrix.Platform.TclTk.Extras.BsdLicenseForever` - assembly and primary namespace
  `CodeBrix.Platform.TclTk.Extras` - i.e. `using CodeBrix.Platform.TclTk.Extras;`
* NuGet package ID: `CodeBrix.Platform.TkCanvas.BsdLicenseForever` - assembly and primary namespace
  `CodeBrix.Platform.TkCanvas` - i.e. `using CodeBrix.Platform.TkCanvas;`

XML documentation (IntelliSense) ships alongside the `.Extras` and `.TkCanvas` assemblies.

Each package pulls its dependencies in automatically; no version pinning is needed in the consuming
project:

* `CodeBrix.Platform.TclTk.BsdLicenseForever` has a single dependency, `System.Security.Cryptography.Pkcs`
  - a Microsoft first-party package used for signature verification.
* `CodeBrix.Platform.TclTk.Extras.BsdLicenseForever` adds
  [CodeBrix.Sqlite](https://www.nuget.org/packages/CodeBrix.Sqlite.ApacheLicenseForever) (the SQLite
  engine) and
  [CodeBrix.PdfDocuments](https://www.nuget.org/packages/CodeBrix.PdfDocuments.MitLicenseForever) (the
  PDF writer and its font embedding).
* `CodeBrix.Platform.TkCanvas.BsdLicenseForever` adds `SkiaSharp` (all drawing),
  `CodeBrix.Imaging.ApacheLicenseForever` (image decoding and encoding for photo images),
  `CodeBrix.Platform.ApacheLicenseForever` and `CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever`
  (the UI stack the host control paints on), and the three font packages it renders with -
  `CodeBrix.Platform.Fonts.RobotoMono.OflLicenseForever`,
  `CodeBrix.Platform.Fonts.Roboto.OflLicenseForever` and
  `CodeBrix.Platform.Fonts.Merriweather.OflLicenseForever`.

Do not add a second `SkiaSharp` package reference alongside `.TkCanvas`: it already pins the SkiaSharp
line that the CodeBrix.Platform Skia runtime heads use, and a second reference can produce a version
conflict.

To put a Tk user interface on screen, a `.TkCanvas` application also needs exactly one CodeBrix.Platform
platform head package (for example the Linux X11 head). Headless and offscreen use - rendering into any
`SKCanvas` and driving input synthetically - needs no head.

## CodeBrix.Platform.TclTk supports:

* Creating one or more independent, embeddable Tcl interpreters in-process
* Evaluating Tcl scripts and expressions from C#, and reading their results
* Getting and setting Tcl variables (scalars and arrays) from managed code
* The full core Tcl command set, procedures (`proc`), and `namespace`s
* A built-in script library, embedded in the assembly (no files to deploy)
* A two-way object-interop bridge for calling .NET from Tcl and vice-versa
* Custom commands, script threads, cancellation, hosts and safe interpreters
* Cross-platform operation on Windows, Linux, and macOS with zero native dependencies

## CodeBrix.Platform.TclTk.Extras supports:

Interpreter-side Tcl command extensions that let existing Tcl programs which expect the classic
`sqlite3` and `pdf4tcl` packages run unmodified on the managed interpreter.

* `sqlite3 NAME PATH` - a tclsqlite-compatible database command (handle verbs `eval`, `onecolumn`,
  `changes`, `close`; caller-scope `:name` parameter binding; unset-variable → SQL NULL; NULL →
  empty-string read-back), backed by
  [CodeBrix.Sqlite](https://www.nuget.org/packages/CodeBrix.Sqlite.ApacheLicenseForever)
  on a PRAGMA-neutral plaintext path, so SQLite files it writes are interchangeable with
  files written by stock Tcl applications
* `pdf4tcl::new` / `pdf4tcl::loadBaseTrueTypeFont` / `pdf4tcl::createFont` and the pdf4tcl-compatible
  drawing surface (`startPage`, `setFont`, `setFillColor`, `setStrokeColor`, `setLineStyle`,
  `getStringWidth`, `text`, `line`, `rectangle`, `polygon`, `write`, `destroy`), backed by
  [CodeBrix.PdfDocuments](https://www.nuget.org/packages/CodeBrix.PdfDocuments.MitLicenseForever)
* Registration of either command set individually, or of both at once

## CodeBrix.Platform.TkCanvas supports:

A retained-mode implementation of the classic Tk widget toolkit, drawn entirely onto a SkiaSharp
surface, for CodeBrix.Platform applications and for headless use.

* The Tk geometry managers (`pack`, `grid`) and the Tk window tree
* The classic widget set - frame, labelframe, label, button, entry, text, listbox, treeview, combobox,
  checkbutton, radiobutton, panedwindow, scrollbar, separator and menus - as typed C# classes
* The canvas widget with its scene-graph item model (arc, bitmap, image, line, oval, polygon,
  rectangle, text, window) and the full search and geometry surface: find, bbox, coords, tags, scroll,
  scan and item bindings
* The Tk event / `bind` / focus / grab system, `after` and `update` scheduling, photo images, fonts,
  clipboard, overlay toplevels with a mini window manager, message dialogs, colour theming, the option
  database and `ttk::style`
* `TkHostView` - a ready-made CodeBrix.Platform control hosting a whole Tk tree - plus XAML declaration
  elements for every widget
* A Tcl command bridge that registers the classic Tk command surface on a CodeBrix.Platform.TclTk
  interpreter, so an unmodified Tcl/Tk program presents its user interface through this toolkit
* Identical rendering and measurement on every platform: the toolkit draws with the font packages it
  brings with it and never with an operating-system font
* Headless operation - build a window tree, lay it out and render it to an image with no display

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

### Registering the `sqlite3` and `pdf4tcl` commands

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

### Building a Tk user interface in code

```csharp
using System;
using System.Collections.Generic;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

TkWindow root = TkWindow.CreateRoot();   // or: TkHostView host = new(); TkWindow root = host.Root;
root.SetForcedSize(320, 200);

TkWindow buttonWindow = root.CreateChild("hello");
var button = new ButtonWidget(buttonWindow);
button.Configure(new Dictionary<string, string> { { "-text", "Hello" } });
button.Invoked += () => Console.WriteLine("clicked");
PackLayout.Configure(buttonWindow, new PackOptions { Side = Side.Top });

TkWindow listWindow = root.CreateChild("list");
var listbox = new ListboxWidget(listWindow);
listbox.Insert(0, "alpha", "beta", "gamma");
PackLayout.Configure(listWindow, new PackOptions { Side = Side.Left, Fill = Fill.Y });

root.Tree.Scheduler.UpdateIdleTasks();   // geometry is final after this
Console.WriteLine(buttonWindow.Width);
```

### Declaring the same user interface in XAML

```xml
<Page x:Class="MyApp.Views.MainPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:tkhost="using:CodeBrix.Platform.TkCanvas.Hosting"
      xmlns:tk="using:CodeBrix.Platform.TkCanvas.Xaml">

  <tkhost:TkHostView x:Name="TkHost" Theme="DarkNew">
    <tk:TkFrame Side="Top" Fill="X">
      <tk:TkLabel Side="Left" Text="Name:" PadX="4" />
      <tk:TkEntry x:Name="NameEntry" Side="Left" Fill="X" Expand="True" />
    </tk:TkFrame>
    <tk:TkText x:Name="Output" Fill="Both" Expand="True"
               WidthChars="60" HeightLines="12" />
  </tkhost:TkHostView>
</Page>
```

## Documentation

Each NuGet package includes an `AGENT-README.txt`, a complete API reference and usage guide written for
AI coding agents - point your agent at the file inside the package it is writing code against:

* `CodeBrix.Platform.TclTk.BsdLicenseForever` - the interpreter: creating and evaluating, variables,
  custom commands, script threads, cancellation, hosts, safe interpreters, .NET object bridging and
  traces.
  ([AGENT-README.txt](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/AGENT-README.txt))
* `CodeBrix.Platform.TclTk.Extras.BsdLicenseForever` - the `sqlite3` and `pdf4tcl` command surfaces,
  verb by verb, with their exact error texts.
  ([AGENT-README.txt](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/src/CodeBrix.Platform.TclTk.Extras/AGENT-README.txt))
* `CodeBrix.Platform.TkCanvas.BsdLicenseForever` - the widget, canvas, layout, event and font APIs, the
  XAML elements, the Tcl command bridge, and hosted versus headless operation.
  ([AGENT-README.txt](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/src/CodeBrix.Platform.TkCanvas/AGENT-README.txt))

Additional sample code and usage examples are available in the test projects:

* https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TclTk.Tests
* https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TclTk.Extras.Tests
* https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TkCanvas.Tests

## License

CodeBrix.Platform.TclTk, CodeBrix.Platform.TclTk.Extras and CodeBrix.Platform.TkCanvas are licensed
under the BSD 2-Clause License - see the
[LICENSE-MODIFICATIONS.txt](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/LICENSE-MODIFICATIONS.txt) file.

For licensing and provenance information about the open source code included in
these packages - including the further permissive license reproduced in the
[LICENSE](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/LICENSE) file - see
[THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Platform.TclTk/blob/main/THIRD-PARTY-NOTICES.txt).
