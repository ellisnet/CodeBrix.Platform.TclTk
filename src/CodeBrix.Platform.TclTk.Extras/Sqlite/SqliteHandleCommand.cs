using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using CodeBrix.Sqlite;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Platform.TclTk.Extras.Sqlite;

/// <summary>
/// The per-database Tcl handle command created by <c>sqlite3 NAME PATH</c> — the
/// tclsqlite-compatible object command on which scripts invoke
/// <c>$db eval</c>, <c>$db onecolumn</c>, <c>$db changes</c>, and <c>$db close</c>.
/// </summary>
internal sealed class SqliteHandleCommand : Default
{
    private readonly SqliteDatabase _database;
    private bool _closed;

    public SqliteHandleCommand(ICommandData commandData, SqliteDatabase database)
        : base(commandData)
    {
        if (database == null) { throw new ArgumentNullException(nameof(database)); }
        _database = database;
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (interpreter == null) { result = "invalid interpreter"; return ReturnCode.Error; }
        if (arguments == null || arguments.Count < 2)
        {
            result = string.Format(
                "wrong # args: should be \"{0} SUBCOMMAND ...\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        string subCommand = arguments[1];

        if (_closed && subCommand != "close")
        {
            result = string.Format("invalid command name \"{0}\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        switch (subCommand)
        {
            case "eval": return EvalSubCommand(interpreter, arguments, ref result);
            case "onecolumn": return OneColumnSubCommand(interpreter, arguments, ref result);
            case "changes": return ChangesSubCommand(arguments, ref result);
            case "close": return CloseSubCommand(interpreter, arguments, ref result);
            default:
                result = string.Format(
                    "bad option \"{0}\": must be changes, close, eval, or onecolumn", subCommand);
                return ReturnCode.Error;
        }
    }

    private static string GetCommandName(ArgumentList arguments)
        => (arguments != null && arguments.Count > 0) ? (string)arguments[0] : "db";

    private ReturnCode EvalSubCommand(
        Interpreter interpreter, ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 3 && arguments.Count != 4)
        {
            result = string.Format(
                "wrong # args: should be \"{0} eval SQL ?SCRIPT?\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        string sql = arguments[2];
        string body = (arguments.Count == 4) ? (string)arguments[3] : null;

        try
        {
            using (SqliteCommand command = CreateBoundCommand(interpreter, sql))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (body == null)
                {
                    // No script: return a flat list of every column of every row.
                    var list = new StringList();
                    do
                    {
                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                list.Add(SqliteTclValues.ToTclString(reader.GetValue(i)));
                            }
                        }
                    }
                    while (reader.NextResult());

                    result = list;
                    return ReturnCode.Ok;
                }

                // Script mode: bind each row's columns into the caller's scope as
                // scalar variables and evaluate the body once per row. Commands do
                // not push a call frame, so the current frame IS the caller's frame.
                do
                {
                    while (reader.Read())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            Result error = null;
                            string columnName = reader.GetName(i);
                            string columnValue = SqliteTclValues.ToTclString(reader.GetValue(i));

                            if (interpreter.SetVariableValue(
                                    columnName, columnValue, ref error) != ReturnCode.Ok)
                            {
                                result = error;
                                return ReturnCode.Error;
                            }
                        }

                        Result bodyResult = null;
                        ReturnCode bodyCode = interpreter.EvaluateScript(body, ref bodyResult);

                        if (bodyCode == ReturnCode.Break)
                        {
                            result = string.Empty;
                            return ReturnCode.Ok;
                        }
                        if (bodyCode != ReturnCode.Ok && bodyCode != ReturnCode.Continue)
                        {
                            // Error and Return propagate to the caller, as in tclsqlite.
                            result = bodyResult;
                            return bodyCode;
                        }
                    }
                }
                while (reader.NextResult());
            }

            result = string.Empty;
            return ReturnCode.Ok;
        }
        catch (Exception ex)
        {
            result = GetSqliteErrorMessage(ex);
            return ReturnCode.Error;
        }
    }

    private ReturnCode OneColumnSubCommand(
        Interpreter interpreter, ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 3)
        {
            result = string.Format(
                "wrong # args: should be \"{0} onecolumn SQL\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        try
        {
            using (SqliteCommand command = CreateBoundCommand(interpreter, arguments[2]))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                do
                {
                    if (reader.Read())
                    {
                        result = SqliteTclValues.ToTclString(reader.GetValue(0));
                        return ReturnCode.Ok;
                    }
                }
                while (reader.NextResult());
            }

            result = string.Empty;
            return ReturnCode.Ok;
        }
        catch (Exception ex)
        {
            result = GetSqliteErrorMessage(ex);
            return ReturnCode.Error;
        }
    }

    private ReturnCode ChangesSubCommand(ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 2)
        {
            result = string.Format(
                "wrong # args: should be \"{0} changes\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        try
        {
            object changes = _database.ExecuteScalar("select changes();");
            result = Convert.ToInt64(changes, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
            return ReturnCode.Ok;
        }
        catch (Exception ex)
        {
            result = GetSqliteErrorMessage(ex);
            return ReturnCode.Error;
        }
    }

    private ReturnCode CloseSubCommand(
        Interpreter interpreter, ArgumentList arguments, ref Result result)
    {
        if (arguments.Count != 2)
        {
            result = string.Format(
                "wrong # args: should be \"{0} close\"", GetCommandName(arguments));
            return ReturnCode.Error;
        }

        if (!_closed)
        {
            _closed = true;

            SqliteConnection connection = _database.Connection;
            _database.Dispose();

            // Microsoft.Data.Sqlite pools connections; without clearing the pool the
            // database file stays locked/open after close, unlike tclsqlite.
            if (connection != null) { SqliteConnection.ClearPool(connection); }

            Result removeError = null;
            interpreter.RemoveCommand(Token, null, ref removeError);
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }

    /// <summary>
    /// Creates a command for <paramref name="sql"/> with every host parameter
    /// (<c>:name</c> / <c>@name</c> / <c>$name</c>) resolved from the CALLER's Tcl
    /// frame: an unset variable binds SQL NULL; a set variable (even an empty
    /// string) binds its value. The SQL text itself is passed through verbatim —
    /// never normalized or rewritten.
    /// </summary>
    private SqliteCommand CreateBoundCommand(Interpreter interpreter, string sql)
    {
        SqliteCommand command = _database.CreateCommand(sql);

        foreach (string token in SqlParameterScanner.FindParameters(sql))
        {
            string variableName = token.Substring(1);

            if (interpreter.DoesVariableExist(VariableFlags.None, variableName) == ReturnCode.Ok)
            {
                Result value = null;
                Result error = null;

                if (interpreter.GetVariableValue(variableName, ref value, ref error) == ReturnCode.Ok)
                {
                    command.Parameters.AddWithValue(token, SqliteTclValues.ToBindValue(value));
                    continue;
                }
            }

            // Unset (or unreadable) variable binds SQL NULL — the tclsqlite rule that
            // governs whether a nullable column stores typeof=null vs typeof=text.
            command.Parameters.AddWithValue(token, DBNull.Value);
        }

        return command;
    }

    /// <summary>
    /// Extracts the bare SQLite message text ("no such table: foo") from
    /// Microsoft.Data.Sqlite's decorated form ("SQLite Error 1: 'no such table: foo'.").
    /// </summary>
    private static string GetSqliteErrorMessage(Exception ex)
    {
        if (ex is SqliteException && ex.Message != null)
        {
            int first = ex.Message.IndexOf('\'');
            int last = ex.Message.LastIndexOf('\'');
            if (first >= 0 && last > first)
            {
                return ex.Message.Substring(first + 1, last - first - 1);
            }
        }
        return ex.Message;
    }
}
