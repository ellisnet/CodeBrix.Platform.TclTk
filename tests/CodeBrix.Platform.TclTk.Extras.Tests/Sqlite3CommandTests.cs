using System;
using System.Data;
using System.IO;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using CodeBrix.Platform.TclTk.Extras.Sqlite;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the <c>sqlite3 NAME PATH</c> command itself: database open/create
/// behavior, handle-command lifecycle, and error paths.
/// </summary>
public class Sqlite3CommandTests
{
    [Fact]
    public void Execute_opens_a_memory_database_and_registers_the_handle_command()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 mdb :memory:");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "info commands mdb").Should().Be("mdb");
            ExtrasTestHelpers.Eval(interpreter, "mdb onecolumn {select 42}").Should().Be("42");
            ExtrasTestHelpers.Eval(interpreter, "mdb close");
        }
    }

    [Fact]
    public void Execute_creates_a_missing_database_file()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string path = Path.Combine(folder, "created.db");

                //Act
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + path + "}");
                ExtrasTestHelpers.Eval(interpreter, "db eval {create table t (a integer)}");
                ExtrasTestHelpers.Eval(interpreter, "db close");

                //Assert
                File.Exists(path).Should().BeTrue();
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void Execute_fails_for_an_unopenable_path()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "sqlite3 db {/nonexistent-folder-xyz/sub/file.db}");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Contain("unable to open database");
        }
    }

    [Fact]
    public void Execute_reports_wrong_number_of_arguments()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter, "sqlite3 db");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Contain("wrong # args");
        }
    }

    [Fact]
    public void close_removes_the_handle_command()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 gdb :memory:");

            //Act
            ExtrasTestHelpers.Eval(interpreter, "gdb close");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "info commands gdb").Should().Be("");
        }
    }

    [Fact]
    public void close_releases_the_database_file()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string path = Path.Combine(folder, "released.db");
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + path + "}");
                ExtrasTestHelpers.Eval(interpreter, "db eval {create table t (a integer)}");

                //Act
                ExtrasTestHelpers.Eval(interpreter, "db close");

                //Assert - the file is deletable, so no pooled connection holds it open.
                File.Delete(path);
                File.Exists(path).Should().BeFalse();
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void close_allows_reopening_the_same_handle_name()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange - the DRAKON pattern: "catch { udb close }" then reopen (model.tcl).
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 udb :memory:");
            ExtrasTestHelpers.Eval(interpreter, "udb eval {create table t (a integer)}");

            //Act
            ExtrasTestHelpers.Eval(interpreter, "catch { udb close }");
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 udb :memory:");

            //Assert - the new handle is a fresh empty database.
            ExtrasTestHelpers.Eval(
                    interpreter,
                    "udb onecolumn {select count(*) from sqlite_master}")
                .Should().Be("0");
            ExtrasTestHelpers.Eval(interpreter, "udb close");
        }
    }

    [Fact]
    public void handle_rejects_an_unknown_subcommand()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db :memory:");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter, "db bogus");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("bad option \"bogus\": must be changes, close, eval, or onecolumn");
            ExtrasTestHelpers.Eval(interpreter, "db close");
        }
    }

    [Fact]
    public void multiple_databases_can_be_open_at_once()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange - DRAKON keeps several handles live (db, gdb, mb, settings, ...).
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db1 :memory:");
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db2 :memory:");

            //Act
            ExtrasTestHelpers.Eval(interpreter, "db1 eval {create table one (a integer)}");
            ExtrasTestHelpers.Eval(interpreter, "db2 eval {create table two (b integer)}");

            //Assert - each handle sees only its own schema.
            ExtrasTestHelpers.Eval(
                    interpreter, "db1 onecolumn {select name from sqlite_master}")
                .Should().Be("one");
            ExtrasTestHelpers.Eval(
                    interpreter, "db2 onecolumn {select name from sqlite_master}")
                .Should().Be("two");

            ExtrasTestHelpers.Eval(interpreter, "db1 close");
            ExtrasTestHelpers.Eval(interpreter, "db2 close");
        }
    }

    // ------------------------------------------- release without an explicit close

    private static SqliteHandleCommand GetHandle(Interpreter interpreter, string name)
    {
        long token = 0;
        ICommand command = null;
        Result error = null;

        //Without the Wrapper flag the lookup returns the handle itself, not the
        //engine's wrapper around it.
        interpreter.GetCommand(
                name, LookupFlags.Default & ~LookupFlags.Wrapper, ref token, ref command, ref error)
            .Should().Be(ReturnCode.Ok,
                "the handle command should exist; error: " + (error != null ? error.ToString() : "<null>"));

        return (SqliteHandleCommand)command;
    }

    [Fact]
    public void disposing_the_interpreter_closes_a_handle_that_was_never_closed()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        Interpreter interpreter = null;
        try
        {
            //Arrange - the DRAKON pattern: the open .drn stays open for the whole
            // session, and nothing runs "db close" before the interpreter goes away.
            string path = Path.Combine(folder, "left-open.db");
            interpreter = ExtrasTestHelpers.CreateInterpreter();
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + path + "}");
            ExtrasTestHelpers.Eval(interpreter, "db eval {create table t (a integer)}");
            SqliteHandleCommand handle = GetHandle(interpreter, "db");
            handle.Database.State.Should().Be(ConnectionState.Open);

            //Act
            interpreter.Dispose();

            //Assert
            handle.Database.State.Should().Be(ConnectionState.Closed);
        }
        finally
        {
            interpreter?.Dispose();
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void disposing_the_interpreter_lets_windows_delete_a_file_that_was_never_closed()
    {
        //Only Windows refuses to delete a file that is still open; Linux and macOS
        //delete it anyway, so on those systems this check would prove nothing.
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "Only Windows refuses to delete a file that is still open.");

        string folder = ExtrasTestHelpers.CreateTempFolder();
        Interpreter interpreter = null;
        try
        {
            //Arrange
            string path = Path.Combine(folder, "left-open.db");
            interpreter = ExtrasTestHelpers.CreateInterpreter();
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + path + "}");
            ExtrasTestHelpers.Eval(interpreter, "db eval {create table t (a integer)}");

            //Act
            interpreter.Dispose();

            //Assert - File.Delete throws IOException on Windows while the file is open.
            File.Delete(path);
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            interpreter?.Dispose();
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void deleting_the_handle_command_closes_the_database()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db :memory:");
            SqliteHandleCommand handle = GetHandle(interpreter, "db");

            //Act - tclsqlite closes the database when its command is deleted.
            ExtrasTestHelpers.Eval(interpreter, "rename db {}");

            //Assert
            handle.Database.State.Should().Be(ConnectionState.Closed);
            ExtrasTestHelpers.Eval(interpreter, "info commands db").Should().Be("");
        }
    }

    [Fact]
    public void renaming_the_handle_command_keeps_the_database_open()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "sqlite3 db :memory:");
            ExtrasTestHelpers.Eval(interpreter, "db eval {create table t (a integer)}");

            //Act
            ExtrasTestHelpers.Eval(interpreter, "rename db db2");

            //Assert - the same database answers under the new name.
            ExtrasTestHelpers.Eval(interpreter, "db2 onecolumn {select count(*) from sqlite_master}")
                .Should().Be("1");
            ExtrasTestHelpers.Eval(interpreter, "db2 close");
        }
    }
}
