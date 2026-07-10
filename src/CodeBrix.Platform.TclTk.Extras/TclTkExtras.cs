using System;
using System.Globalization;
using System.Text;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using CodeBrix.Platform.TclTk.Extras.Pdf;
using CodeBrix.Platform.TclTk.Extras.Sqlite;

namespace CodeBrix.Platform.TclTk.Extras;

/// <summary>
/// Registers the interpreter-side Tcl command extensions of this library on a
/// CodeBrix.Platform.TclTk <see cref="Interpreter"/>: the tclsqlite-compatible
/// <c>sqlite3</c> database command (backed by CodeBrix.Sqlite) and the
/// pdf4tcl-compatible PDF drawing commands (backed by CodeBrix.PdfDocuments).
/// Each registration also runs the matching <c>package provide</c>, so Tcl code
/// that starts with <c>package require sqlite3</c> / <c>package require pdf4tcl</c>
/// runs unmodified.
/// </summary>
public static class TclTkExtras
{
    /// <summary>
    /// The version reported for <c>package provide sqlite3</c>: the SQLite major
    /// generation bundled by CodeBrix.Sqlite (via Microsoft.Data.Sqlite/e_sqlite3).
    /// </summary>
    private static readonly Version Sqlite3PackageVersion = new Version(3, 45, 0);

    /// <summary>The pdf4tcl surface this library implements is pdf4tcl 0.7.</summary>
    private static readonly Version Pdf4TclPackageVersion = new Version(0, 7);

    /// <summary>
    /// Registers both the <c>sqlite3</c> and the <c>pdf4tcl</c> command sets on
    /// <paramref name="interpreter"/>.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <param name="error">The failure detail when the result is not <see cref="ReturnCode.Ok"/>.</param>
    /// <returns><see cref="ReturnCode.Ok"/> on success.</returns>
    public static ReturnCode RegisterAll(Interpreter interpreter, ref Result error)
    {
        ReturnCode code = RegisterSqlite3(interpreter, ref error);
        if (code != ReturnCode.Ok) { return code; }
        return RegisterPdf4Tcl(interpreter, ref error);
    }

    /// <summary>
    /// Registers the tclsqlite-compatible <c>sqlite3 NAME PATH</c> command and
    /// provides the <c>sqlite3</c> package. Databases opened through it use the
    /// PRAGMA-neutral plaintext path required for interchange of SQLite files
    /// (such as DRAKON <c>.drn</c> files) with stock Tcl applications.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <param name="error">The failure detail when the result is not <see cref="ReturnCode.Ok"/>.</param>
    /// <returns><see cref="ReturnCode.Ok"/> on success.</returns>
    public static ReturnCode RegisterSqlite3(Interpreter interpreter, ref Result error)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }

        var command = new Sqlite3Command(
            new CommandData(
                "sqlite3", "sqlite3", null, null,
                typeof(Sqlite3Command).FullName, CommandFlags.None, null, 0));

        long token = 0;
        ReturnCode code = interpreter.AddCommand(command, null, ref token, ref error);
        if (code != ReturnCode.Ok) { return code; }

        return interpreter.ProvidePackage("sqlite3", Sqlite3PackageVersion, ref error);
    }

    /// <summary>
    /// Registers the pdf4tcl-compatible commands (<c>pdf4tcl::new</c>,
    /// <c>pdf4tcl::loadBaseTrueTypeFont</c>, <c>pdf4tcl::createFont</c>), publishes
    /// the <c>pdf4tcl::paper_sizes</c> and <c>pdf4tcl::units</c> array variables,
    /// and provides the <c>pdf4tcl</c> package.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <param name="error">The failure detail when the result is not <see cref="ReturnCode.Ok"/>.</param>
    /// <returns><see cref="ReturnCode.Ok"/> on success.</returns>
    public static ReturnCode RegisterPdf4Tcl(Interpreter interpreter, ref Result error)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }

        // The namespace must exist before commands can be added under it, and the
        // pdf4tcl::paper_sizes / pdf4tcl::units arrays are plain Tcl variables that
        // consumers read directly (DRAKON reads both).
        Result setupResult = null;
        ReturnCode code = interpreter.EvaluateScript(BuildPdf4TclSetupScript(), ref setupResult);
        if (code != ReturnCode.Ok)
        {
            error = setupResult;
            return code;
        }

        code = AddPdf4TclCommand(
            interpreter, "pdf4tcl::new",
            data => new Pdf4TclNewCommand(data), ref error);
        if (code != ReturnCode.Ok) { return code; }

        code = AddPdf4TclCommand(
            interpreter, "pdf4tcl::loadBaseTrueTypeFont",
            data => new Pdf4TclLoadBaseTrueTypeFontCommand(data), ref error);
        if (code != ReturnCode.Ok) { return code; }

        code = AddPdf4TclCommand(
            interpreter, "pdf4tcl::createFont",
            data => new Pdf4TclCreateFontCommand(data), ref error);
        if (code != ReturnCode.Ok) { return code; }

        return interpreter.ProvidePackage("pdf4tcl", Pdf4TclPackageVersion, ref error);
    }

    private static ReturnCode AddPdf4TclCommand(
        Interpreter interpreter, string name,
        Func<CommandData, ICommand> factory, ref Result error)
    {
        var data = new CommandData(
            name, "pdf4tcl", null, null, null, CommandFlags.None, null, 0);

        long token = 0;
        return interpreter.AddCommand(factory(data), null, ref token, ref error);
    }

    private static string BuildPdf4TclSetupScript()
    {
        var script = new StringBuilder();
        script.AppendLine("namespace eval ::pdf4tcl {}");

        script.Append("array set ::pdf4tcl::units {");
        foreach (var pair in Pdf4TclUnits.Units)
        {
            script.Append(pair.Key).Append(' ')
                .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(' ');
        }
        script.AppendLine("}");

        script.Append("array set ::pdf4tcl::paper_sizes {");
        foreach (var pair in Pdf4TclUnits.PaperSizes)
        {
            script.Append(pair.Key).Append(" {")
                .Append(pair.Value.Width.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(pair.Value.Height.ToString(CultureInfo.InvariantCulture)).Append("} ");
        }
        script.AppendLine("}");

        return script.ToString();
    }
}
