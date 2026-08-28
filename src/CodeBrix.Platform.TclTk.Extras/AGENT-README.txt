================================================================================
AGENT-README: CodeBrix.Platform.TclTk.Extras
A Guide for AI Coding Agents — CONSUMING the
CodeBrix.Platform.TclTk.Extras.BsdLicenseForever NuGet package
================================================================================

OVERVIEW
========
Interpreter-side Tcl command extensions for the CodeBrix.Platform.TclTk
managed Tcl interpreter. The package adds two command surfaces that existing
Tcl programs expect to find as packages:

  * a tclsqlite-compatible "sqlite3" database command, backed by
    CodeBrix.Sqlite (Microsoft.Data.Sqlite underneath); and
  * a pdf4tcl-compatible PDF drawing command set (pdf4tcl::new and the
    per-document object command), backed by CodeBrix.PdfDocuments.

Each registration also runs the matching "package provide", so Tcl code that
starts with "package require sqlite3" or "package require pdf4tcl" runs
unmodified. Nothing is native: the interpreter, the SQLite driver and the PDF
writer are all managed code. Target: .NET 10 or later.

Provenance: this assembly is original CodeBrix code (it is NOT a port of
tclsqlite or pdf4tcl); it replicates their Tcl-visible behavior over CodeBrix
libraries. The pdf4tcl surface implemented is the pdf4tcl 0.7 drawing subset a
real application exercises. The only public type is TclTkExtras; every command
implementation is internal.

INSTALLATION
============
    dotnet add package CodeBrix.Platform.TclTk.Extras.BsdLicenseForever

PackageId: CodeBrix.Platform.TclTk.Extras.BsdLicenseForever
License:   BSD-2-Clause (the interpreter dependency is BSD-2-Clause too,
           and discloses its Eagle-derived, Tcl/Tk-licensed half in its
           own THIRD-PARTY-NOTICES.txt)

NuGet dependencies you get transitively:
  * CodeBrix.Platform.TclTk.BsdLicenseForever   (the interpreter)
  * CodeBrix.Sqlite.ApacheLicenseForever         (bundles the SQLite engine)
  * CodeBrix.PdfDocuments.MitLicenseForever      (PDF writer + font embedding)

Requirements: none beyond .NET 10. No native Tcl, SQLite or PDF tooling is
needed on the machine. PDF text needs a TrueType (.ttf) font file you supply
(see pdf4tcl::loadBaseTrueTypeFont).

The PackageId carries a license suffix; the code namespace does not.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.TclTk._Components.Public;  // Interpreter, Result,
                                                       // ReturnCode
    using CodeBrix.Platform.TclTk.Extras;              // TclTkExtras

The registered commands are Tcl commands: "sqlite3", the per-database handle
command it creates, "pdf4tcl::new", "pdf4tcl::loadBaseTrueTypeFont",
"pdf4tcl::createFont", the per-document object command pdf4tcl::new creates,
and the Tcl array variables ::pdf4tcl::paper_sizes and ::pdf4tcl::units.

CORE API REFERENCE
==================
TclTkExtras (static class, the whole C# surface)
-------------------------------------------------
    static ReturnCode RegisterAll(Interpreter interpreter, ref Result error)
        Registers sqlite3 first, then pdf4tcl. Stops at the first failure and
        returns its code; 'error' then holds the failure text.

    static ReturnCode RegisterSqlite3(Interpreter interpreter, ref Result error)
        Adds the "sqlite3" command and runs "package provide sqlite3".

    static ReturnCode RegisterPdf4Tcl(Interpreter interpreter, ref Result error)
        Evaluates a setup script ("namespace eval ::pdf4tcl {}" and the two
        array variables), adds pdf4tcl::new / ::loadBaseTrueTypeFont /
        ::createFont, and runs "package provide pdf4tcl".

All three throw ArgumentNullException for a null interpreter and otherwise
report failure through the return code (never by exception). ReturnCode.Ok
means success; anything else means the interpreter is only partially
extended — treat it as fatal for that interpreter (see COMPLETE EXAMPLES).

Registration is per interpreter instance: call it again for every
Interpreter you create (child interpreters included). Registering twice on
the same interpreter fails with "can't add sqlite3: command already exists",
so register once.

THE sqlite3 COMMAND (Tcl level)
===============================
    sqlite3 HANDLE FILENAME

Exactly two arguments. Opens (or creates) the SQLite database at FILENAME —
":memory:" is accepted — and registers HANDLE as a new Tcl command. The open
is PRAGMA-neutral: write-ahead logging stays OFF (default rollback journal),
foreign-key enforcement is switched OFF at open ("PRAGMA foreign_keys=OFF"),
and no sidecar files are created, so files written here are interchangeable
with files written by stock tclsqlite (for example DRAKON Editor .drn files).
A path that cannot be opened returns a Tcl error:
'unable to open database "PATH": ...'.

Handle verbs (the complete set)
-------------------------------
    HANDLE eval SQL
        Runs SQL (one or more statements) and returns a flat Tcl list of
        every column of every row, in order.

    HANDLE eval SQL SCRIPT
        Runs SQL; for each row, sets one scalar Tcl variable per column
        (named after the column) in the CALLER's frame and evaluates SCRIPT.
        "break" inside SCRIPT stops the loop (eval returns ""); "continue"
        skips to the next row; an error or "return" propagates out of eval.
        Returns "".

    HANDLE onecolumn SQL
        Returns the first column of the first row, or "" when there is none.

    HANDLE changes
        Returns the number of rows changed by the most recent statement
        (SQLite's changes()).

    HANDLE close
        Disposes the connection, clears the connection pool (so the file is
        released immediately, like tclsqlite) and DELETES the HANDLE command.
        Any other verb after close is 'invalid command name "HANDLE"'.

Any other verb fails with:
    bad option "X": must be changes, close, eval, or onecolumn

Host-parameter binding rules (replicate tclsqlite)
--------------------------------------------------
The SQL text is passed to SQLite verbatim — never rewritten. Parameters of
the forms :name, @name and $name are found by a scanner that skips string
literals, quoted/bracketed identifiers and comments (a colon inside
'a text literal' is never a parameter). Each parameter resolves from the Tcl
variable of that name in the caller's frame:

  * variable UNSET (or unreadable)    -> binds SQL NULL
  * variable set to ""                -> binds '' (TEXT), not NULL
  * a Tcl value whose internal representation is a boxed integer/real/bool
    (the result of [expr], [incr], ...) -> binds INTEGER / REAL (bool -> 1/0)
  * ANY string-represented value      -> binds TEXT, even when it looks
    numeric: "007" stays "007", "1.10" stays "1.10". Values are never
    sniffed into numbers.
  * a byte[] value                    -> binds BLOB

Values read back: SQL NULL -> "" (tclsqlite's default -nullvalue); INTEGER
-> decimal text; REAL -> Tcl-style rendering (lower-case exponent, integral
reals render with ".0", e.g. "3.0"); BLOB -> its bytes decoded as UTF-8.

SQLite error messages come back bare ("no such table: foo"), not in the
Microsoft.Data.Sqlite decorated form.

sqlite3 verbs and forms that are NOT provided
---------------------------------------------
Verified against the command implementation; each of these is a Tcl error:
  * Any open option — "sqlite3 db file -readonly 1", -create, -key, -uri,
    -vfs, -nomutex, -fullmutex: the command takes exactly HANDLE FILENAME.
  * The array-variable form of eval: "db eval SQL ARRAYNAME SCRIPT". Only
    "eval SQL" and "eval SQL SCRIPT" (scalar variables per column) exist.
  * transaction, exists, function, last_insert_rowid, total_changes,
    nullvalue, busy, timeout, errorcode, collate, collation_needed, copy,
    backup, restore, authorizer, progress, trace, profile, version, complete,
    enable_load_extension, incrblob, interrupt, cache, status, update_hook,
    rollback_hook, commit_hook, wal_hook, unlock_notify.
  Write "begin"/"commit"/"rollback" as SQL through eval instead of the
  transaction verb; use "select last_insert_rowid()" through onecolumn.

THE pdf4tcl COMMANDS (Tcl level)
================================
Factory / font commands (the complete set)
------------------------------------------
    pdf4tcl::new NAME ?option value ...?
        Creates a document and registers NAME as its object command; NAME
        may be %AUTO% (a generated name is returned). Returns the object
        command name. Options:
          -paper NAME|{W H}   default a4; a paper_sizes name (case-
                              insensitive) or a two-element width/height list
                              (unit suffixes allowed). Unknown -> error
                              "papersize X is unknown".
          -landscape BOOL     default 0 (swaps width and height)
          -margin SPEC        default 0; one, two (x y) or four (left right
                              top bottom) values, unit suffixes allowed
          -orient BOOL        default 1: origin TOP-left, y grows DOWN.
                              0: origin BOTTOM-left, y grows UP.
          -unit NAME          default p (points); mm, m, cm, c, i, p
          -file PATH          default output for "write"
          -rotate, -compress  accepted and IGNORED
        Boolean values accept 1/true/yes/on and 0/false/no/off.

    pdf4tcl::loadBaseTrueTypeFont BASEFONTNAME FILENAME ?validate?
        Reads a .ttf file into a process-wide base-font store.

    pdf4tcl::createFont BASEFONTNAME FONTNAME ?ENCODING?
        Makes FONTNAME usable by setFont. ENCODING is accepted for
        compatibility and otherwise unused (fonts embed with Unicode
        encoding and are subset automatically). Error when the base font
        was never loaded: "base font X doesn't exist".

    ::pdf4tcl::units          Tcl array: mm m cm c i p -> points per unit
    ::pdf4tcl::paper_sizes    Tcl array: a0 a1 a2 a3 a4 a5 a6 11x17 ledger
                              legal letter -> {width height} in points

Object command verbs (the complete set)
---------------------------------------
    OBJ startPage ?PAPER? | ?-paper P -landscape B -margin M -orient B?
        Starts a new page (the previous page is finished). A single
        positional argument is the paper name. -rotate is accepted and
        ignored. Drawing before any startPage starts a default page
        implicitly. After "write": "PDF document already finished".
    OBJ setFillColor COLOR | R G B
    OBJ setStrokeColor COLOR | R G B
        COLOR is "#RRGGBB" or a list of three 0..1 components (also
        accepted as three loose arguments). Tk color NAMES are not accepted
        ("Unknown color: red"), exactly as in Tk-less pdf4tcl.
    OBJ setFont SIZE ?FONTNAME?
        FONTNAME must have been created with pdf4tcl::createFont; SIZE may
        carry a unit suffix. Without any font yet: "No font family set".
    OBJ setLineStyle WIDTH ?DASH ...?
        Line width and an optional dash pattern (values in points).
    OBJ getStringWidth TEXT
        Width of TEXT in the current font, in DOCUMENT UNITS (-unit). Works
        before any page is started. Needs a font set ("No font set").
    OBJ text TEXT ?-x X? ?-y Y? ?-align left|right|center?
             ?-background|-bg|-fill COLOR|BOOL? ?-angle A? ?-xangle A?
             ?-yangle A?
        Draws TEXT with (X, Y) as the BASELINE origin in user coordinates;
        -align right/center shifts the start by the string width. A color
        for -background fills the ascender-to-descender box behind the
        text; a bare boolean is accepted and ignored. -angle/-xangle/-yangle
        are accepted and IGNORED (no rotated text). Without -x/-y the text
        continues where the previous text ended. Returns the drawn width in
        points.
    OBJ line X1 Y1 X2 Y2
    OBJ rectangle X Y W H ?-filled BOOL? ?-stroke BOOL?
        With -orient 1, (X, Y) is the top-left corner; with -orient 0 the
        bottom-left corner. Negative W/H are normalized.
    OBJ polygon X Y X Y ... ?-filled BOOL? ?-stroke BOOL?
        Option pairs may be interleaved anywhere among the coordinate pairs;
        at least two points.
    OBJ write ?-file FILENAME?
        Saves the document. FILENAME is REQUIRED unless -file was given to
        pdf4tcl::new ("no output file specified: use \"write -file
        FILENAME\""); there is no streaming to stdout. A document with no
        page gets one empty page. The document is finished afterwards.
    OBJ destroy
        Releases the document and deletes the object command.

Fill/stroke rule (pdf4tcl's): the default strokes only; "-filled 1" fills AND
strokes; "-filled 1 -stroke 0" fills only. Fill uses the fill color, strokes
use the stroke color, line width and dash pattern in effect.

Coordinate model: user coordinates are relative to the MARGIN box and scaled
by -unit. With -orient 1 (default) they map onto the PDF page as a plain
margin offset (top-left origin, y down); with -orient 0 the y axis is
flipped (bottom-left origin, y up).

Any other verb fails with:
    bad option "X": must be destroy, getStringWidth, line, polygon,
    rectangle, setFillColor, setFont, setLineStyle, setStrokeColor,
    startPage, text, or write

pdf4tcl verbs and commands that are NOT provided
------------------------------------------------
Verified against the command implementations; each is a Tcl error:
  * Object verbs outside the twelve above — for example endPage, finish,
    get, getDrawableArea, getStringHeight, setBgColor, setTextPosition,
    moveTextPosition, newLine, drawTextBox, drawTextAt, circle, oval, arc,
    curve, closePath, addImage / putImage / addJpeg / putJpeg / rawImage /
    putRawImage, bookmarkAdd, canvas, metadata, cget, configure.
  * Namespace commands other than the three registered: pdf4tcl::getPoints,
    pdf4tcl::getPaperSize, pdf4tcl::loadBaseType1Font, pdf4tcl::catalog
    etc. Read the ::pdf4tcl::units / ::pdf4tcl::paper_sizes arrays instead.
  * Rotated/skewed text, page rotation and compression control: the options
    exist but do nothing (stream compression is handled by the PDF writer).
  * Tk color names in colors; fonts other than TrueType files you load.

COMPLETE EXAMPLES
=================
1. Create an interpreter, register, open a database, query it
--------------------------------------------------------------
    using System;
    using CodeBrix.Platform.TclTk._Components.Public;
    using CodeBrix.Platform.TclTk.Extras;

    internal static class Program
    {
        private static int Main()
        {
            Result created = null;
            using (Interpreter interpreter = Interpreter.Create(ref created))
            {
                if (interpreter == null)
                {
                    Console.Error.WriteLine("interpreter: " + created);
                    return 1;
                }

                Result error = null;
                ReturnCode code = TclTkExtras.RegisterAll(interpreter, ref error);
                if (code != ReturnCode.Ok)
                {
                    Console.Error.WriteLine("extras: " + code + " " + error);
                    return 1;
                }

                const string script = @"
                    package require sqlite3
                    sqlite3 db :memory:
                    db eval {create table people (id integer primary key,
                                                  name text, code text)}
                    set name Ada
                    set codeText 007
                    db eval {insert into people (name, code)
                             values (:name, :codeText)}
                    set rows {}
                    db eval {select id, name, code from people} {
                        lappend rows ""$id $name $code""
                    }
                    set count [db onecolumn {select count(*) from people}]
                    db close
                    return ""$count: $rows""
                ";

                Result result = null;
                code = interpreter.EvaluateScript(script, ref result);
                if (code != ReturnCode.Ok)
                {
                    Console.Error.WriteLine("tcl error: " + result);
                    return 1;
                }
                Console.WriteLine(result);   // 1: {1 Ada 007}
                return 0;
            }
        }
    }

Note the C# verbatim string doubles the quotes ("" -> "). The script mode of
eval sets $id/$name/$code in the caller's frame, and "007" round-trips as
TEXT because $codeText is a string-represented value.

2. Write a PDF (fonts loaded from a .ttf you ship)
--------------------------------------------------
    // after the same Create + RegisterAll as above:
    string ttf = System.IO.Path.Combine(AppContext.BaseDirectory,
                                        "Assets", "LiberationMono-Regular.ttf");
    string output = System.IO.Path.Combine(AppContext.BaseDirectory, "out.pdf");

    Result result = null;
    ReturnCode code = interpreter.EvaluateScript(
        "package require pdf4tcl\n" +
        "pdf4tcl::loadBaseTrueTypeFont MonoBase {" + ttf + "}\n" +
        "pdf4tcl::createFont MonoBase Mono cp1252\n" +
        "pdf4tcl::new doc -paper a4 -margin 20 -unit mm -orient 1\n" +
        "doc startPage\n" +
        "doc setFont 4 Mono\n" +                  // 4 mm
        "doc setLineStyle 0.5\n" +
        "doc setStrokeColor #000000\n" +
        "doc setFillColor {0.9 0.9 0.9}\n" +
        "doc rectangle 10 10 80 30 -filled 1\n" +  // x y w h, mm
        "doc line 10 50 90 50\n" +
        "doc polygon 100 10 140 10 120 40 -filled 1\n" +
        "doc setFillColor #000000\n" +
        "doc text {Hello from Tcl} -x 15 -y 25\n" +   // baseline origin
        "set w [doc getStringWidth {Hello from Tcl}]\n" +
        "doc write -file {" + output + "}\n" +
        "doc destroy\n" +
        "return $w",
        ref result);

    if (code != ReturnCode.Ok) { throw new InvalidOperationException(result); }
    // result: the string width in mm (document units); out.pdf exists.

3. ReturnCode / Result handling after RegisterAll
-------------------------------------------------
    Result error = null;
    ReturnCode code = TclTkExtras.RegisterAll(interpreter, ref error);
    switch (code)
    {
        case ReturnCode.Ok:
            break;                       // both packages provided
        default:
            // 'error' is a Result; ToString() is the Tcl error text (for
            // example a duplicate command name when registering twice).
            // sqlite3 may already be registered when pdf4tcl fails, so
            // discard this interpreter rather than retrying on it.
            throw new InvalidOperationException(
                "Extras registration failed (" + code + "): " + error);
    }

    // Tcl-side failures of the commands themselves surface exactly like any
    // other Tcl error: EvaluateScript returns ReturnCode.Error and 'result'
    // holds the message ("no such table: t", "papersize b5 is unknown", ...).

4. Registering on an interpreter driven by TkTclBridge.RegisterHosted
----------------------------------------------------------------------
When the interpreter is also driving a TkCanvas UI through
CodeBrix.Platform.TkCanvas.Tcl.TkTclBridge.RegisterHosted, the interpreter
lives on the bridge's dedicated Tcl thread and must be touched only from
that thread. Two correct orders:

    // (a) register BEFORE handing the interpreter to the hosted bridge —
    //     on whichever thread created it (a background Task is fine):
    Result error = null;
    if (TclTkExtras.RegisterAll(interpreter, ref error) != ReturnCode.Ok) { ... }
    TkTclBridge bridge = TkTclBridge.RegisterHosted(interpreter, host.Tree);

    // (b) or AFTER, by posting the work onto the Tcl thread:
    bridge.Post(interp =>
    {
        Result e = null;
        if (TclTkExtras.RegisterAll(interp, ref e) != ReturnCode.Ok)
        {
            Console.Error.WriteLine("extras: " + e);
        }
    });

Never call RegisterAll (or EvaluateScript) on a hosted interpreter from the
UI thread. The reference consumer (DRAKON.Brix) uses order (a): create the
interpreter, TkBootstrap.Register, TclTkExtras.RegisterAll, then
TkTclBridge.RegisterHosted, then every script through bridge.Post.

THREADING
=========
  * The sqlite3 and pdf4tcl commands are plain interpreter commands: they run
    on whatever thread evaluates the script and never marshal anywhere. In a
    hosted TkCanvas app that is the Tcl worker thread, so database and PDF
    file I/O block only that thread, never the UI.
  * Keep each interpreter on one thread at a time. The hosted TkCanvas
    bridge's documented contract is that the interpreter is touched only
    from its Tcl thread (see example 4); follow the same rule in your own
    multi-threaded hosts.
  * The base-font store behind pdf4tcl::loadBaseTrueTypeFont /
    ::createFont is PROCESS-WIDE (the PDF font resolver is global). Fonts
    loaded in one interpreter are visible to all; re-creating an existing
    FONTNAME with different bytes keeps the FIRST registration. Use a fresh
    name per distinct font file.
  * getStringWidth measures through one shared, locked measuring context;
    it is safe to call from several interpreters on several threads.

MINIMUM VIABLE PROJECT
======================
A console app is enough — nothing here needs a UI.

    <!-- TclExtrasDemo.csproj -->
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.TclTk.Extras.BsdLicenseForever"
                          Version="..." />
      </ItemGroup>
      <ItemGroup>
        <!-- any TrueType font you are licensed to ship, for pdf4tcl text -->
        <None Include="Assets\LiberationMono-Regular.ttf"
              CopyToOutputDirectory="PreserveNewest" />
      </ItemGroup>
    </Project>

Program.cs is example 1 above. Referencing this package alone brings in the
interpreter, CodeBrix.Sqlite and CodeBrix.PdfDocuments; do not add a second
SQLite provider package.

PERFORMANCE TIPS
================
  * The managed interpreter is an order of magnitude slower than native
    tclsh; the SQLite and PDF work itself runs at library speed. For batch
    scripts set interpreter.ProductionMode = true (byte-identical results;
    script cancellation is no longer prompt) and interpreter.CacheParsedScripts
    = true when the same procedure bodies run repeatedly.
  * Each "db eval" creates and disposes one command; wrap bulk inserts in an
    explicit "begin ... commit" written as SQL (there is no transaction
    verb) — SQLite's per-statement auto-commit otherwise dominates.
  * "db eval SQL SCRIPT" sets every column variable for every row; select
    only the columns you use.
  * pdf4tcl objects hold the whole document in memory until write/destroy;
    always destroy documents you are done with.

COMMON PITFALLS TO AVOID
========================
  * Registering twice on one interpreter fails (duplicate command name).
    Register once per Interpreter instance, right after Create.
  * An UNSET variable binds NULL; a variable set to "" binds '' TEXT. If a
    nullable column must store NULL, "unset" the variable (or bind by
    leaving it unset) rather than setting it to "".
  * Numeric-looking TEXT is never converted: compare with SQL "= '007'",
    not "= 7", when the column has TEXT affinity.
  * Boolean results of [expr] bind as INTEGER 1/0 — but the interpreter
    RENDERS them as "True"/"False" by default (see the interpreter's
    AGENT-README on BooleanResultMode). Storing "$flag" in a TEXT column
    stores "True" unless the interpreter was created in TclshCompat mode.
  * "db close" deletes the handle command; a second close is an "invalid
    command name" error, not a no-op.
  * pdf4tcl text -x/-y is the BASELINE origin, not the top-left corner: a
    glyph drawn at -y 0 sits above the margin box.
  * pdf4tcl::new with -unit mm makes EVERY later number (coordinates, sizes,
    setFont size, rectangle extents) a millimetre value; getStringWidth
    returns mm, but "text" returns the width in points.
  * "write" needs a file: give -file to pdf4tcl::new or to write. There is no
    stdout output.
  * setFont before text: without a created font, text/getStringWidth fail
    with "No font set" / "No font family set".
  * Tk color names ("red") are rejected by setFillColor/setStrokeColor; use
    #RRGGBB or {r g b} with 0..1 components.
  * The -paper option is validated at pdf4tcl::new; a {W H} list is fine but
    an unknown name is a Tcl error, not a fallback to a4.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * It does not provide a C# database or PDF API — its whole C# surface is
    the three Register methods. Use CodeBrix.Sqlite / CodeBrix.PdfDocuments
    directly from C# when you need one.
  * It does not implement the full tclsqlite handle command (only eval,
    onecolumn, changes, close — see the NOT-provided list) nor any sqlite3
    open options.
  * It does not implement the full pdf4tcl object surface (twelve verbs; no
    images, curves, circles, text boxes, bookmarks, metadata, canvas dump —
    see the NOT-provided list), no rotated text, no Type1 fonts, no Tk color
    names.
  * It does not enable WAL or foreign-key enforcement, by design (file
    interchange with stock Tcl programs).
  * It does not wire Tk: for the Tk toolkit and the "wish"-style command
    bridge see the CodeBrix.Platform.TkCanvas package
    (src/CodeBrix.Platform.TkCanvas/AGENT-README.txt in the repository).

WORKING EXAMPLES ON GITHUB
==========================
  * Registration, sqlite3 handle semantics, binding rules, pdf4tcl verbs,
    units and colors, and a real .drn interchange round-trip:
    https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/tests/CodeBrix.Platform.TclTk.Extras.Tests
    (TclTkExtrasTests.cs, Sqlite3CommandTests.cs, SqliteHandleCommandTests.cs,
    SqlParameterScannerTests.cs, Pdf4TclFactoryCommandsTests.cs,
    Pdf4TclObjectCommandTests.cs, Pdf4TclUnitsTests.cs, Pdf4TclColorsTests.cs,
    DrakonDrnCompatibilityTests.cs; Support/ExtrasTestHelpers.cs shows the
    Create + RegisterAll pattern).
  * A complete application whose unmodified Tcl uses both command sets
    (DRAKON Editor: .drn files through sqlite3, PDF export through pdf4tcl),
    booted in hosted mode next to the TkCanvas bridge:
    https://github.com/ellisnet/CodeBrix.Platform.TclTk/tree/main/samples/DRAKON.Brix
    (src/libs/DRAKON.Brix.TclBridge/DrakonRuntime.cs is the boot sequence).

QUICK REFERENCE CARD
====================
    // C#
    Result r = null;
    using Interpreter interp = Interpreter.Create(ref r);          // null on failure
    Result e = null;
    ReturnCode code = TclTkExtras.RegisterAll(interp, ref e);        // Ok or fatal
    //   or RegisterSqlite3(interp, ref e) / RegisterPdf4Tcl(interp, ref e)
    code = interp.EvaluateScript(script, ref r);                     // r = result/error
    bridge.Post(i => { Result x = null; TclTkExtras.RegisterAll(i, ref x); });
                                                                     // hosted Tk apps

    # Tcl — sqlite3
    package require sqlite3
    sqlite3 db PATH            ;# or :memory:  (exactly two arguments)
    db eval SQL                ;# -> flat list of all columns of all rows
    db eval SQL { script }     ;# columns become $vars in the caller's frame
    db onecolumn SQL           ;# first column of first row, or ""
    db changes                 ;# rows changed by the last statement
    db close                   ;# releases the file, deletes the command
    ;# :name @name $name bind from Tcl vars: unset -> NULL, "" -> '' TEXT,
    ;# string -> TEXT verbatim, expr-number -> INTEGER/REAL

    # Tcl — pdf4tcl
    package require pdf4tcl
    pdf4tcl::loadBaseTrueTypeFont Base /path/font.ttf
    pdf4tcl::createFont Base MyFont cp1252
    pdf4tcl::new doc -paper a4 -margin 20 -unit mm -orient 1 -file out.pdf
    doc startPage ?-paper P -landscape B -margin M -orient B?
    doc setFont SIZE ?MyFont? ; doc setLineStyle W ?dash...?
    doc setFillColor #RRGGBB  ; doc setStrokeColor {r g b}   ;# 0..1
    doc text T -x X -y Y ?-align left|right|center? ?-bg COLOR?
    doc line X1 Y1 X2 Y2 ; doc rectangle X Y W H ?-filled 1? ?-stroke 0?
    doc polygon X Y X Y ... ?-filled 1?
    doc getStringWidth T      ;# in -unit units
    doc write ?-file F? ; doc destroy
    set pdf4tcl::paper_sizes(a4)  ;# "595 842"    set pdf4tcl::units(mm)
