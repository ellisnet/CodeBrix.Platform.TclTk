using System;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using CodeBrix.Sqlite;

namespace CodeBrix.Platform.TclTk.Extras.Sqlite;

/// <summary>
/// The tclsqlite-compatible <c>sqlite3 NAME PATH</c> Tcl command: opens (or creates)
/// the SQLite database at PATH — including <c>:memory:</c> databases — and registers
/// a handle command NAME on which scripts invoke <c>eval</c> / <c>onecolumn</c> /
/// <c>changes</c> / <c>close</c>.
/// </summary>
internal sealed class Sqlite3Command : Default
{
    public Sqlite3Command(ICommandData commandData)
        : base(commandData)
    {
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (interpreter == null) { result = "invalid interpreter"; return ReturnCode.Error; }
        if (arguments == null || arguments.Count != 3)
        {
            result = "wrong # args: should be \"sqlite3 HANDLE FILENAME\"";
            return ReturnCode.Error;
        }

        string handleName = arguments[1];
        string path = arguments[2];

        // PRAGMA-neutral open: WAL and foreign-key enforcement stay OFF so the file
        // keeps the default rollback journal and no fingerprinting sidecars — required
        // for .drn interchange with stock (tclsqlite-based) DRAKON Editor.
        var options = new SqliteDatabaseOptions
        {
            UseWriteAheadLogging = false,
            EnforceForeignKeys = false,
            CreateIfMissing = true
        };

        SqliteDatabase database;
        try
        {
            database = new SqliteDatabase(path, null, options);
            database.SafeOpen();

            // The bundled e_sqlite3 engine is compiled with foreign-key enforcement
            // ON by default, unlike the stock SQLite that tclsqlite runs on. Switch
            // it off explicitly (a per-connection runtime setting, not stored in the
            // file) so schema code that relies on classic FK-off behavior works.
            database.ExecuteNonQuery("PRAGMA foreign_keys=OFF;");
        }
        catch (Exception ex)
        {
            result = string.Format("unable to open database \"{0}\": {1}", path, ex.Message);
            return ReturnCode.Error;
        }

        var handle = new SqliteHandleCommand(
            new CommandData(
                handleName, "sqlite3", null, null,
                typeof(SqliteHandleCommand).FullName, CommandFlags.None, null, 0),
            database);

        long token = 0;
        Result addError = null;

        if (interpreter.AddCommand(handle, null, ref token, ref addError) != ReturnCode.Ok)
        {
            database.Dispose();
            result = addError;
            return ReturnCode.Error;
        }

        handle.Token = token;

        result = string.Empty;
        return ReturnCode.Ok;
    }
}
