using System.IO;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// The master plan's hard requirement: <c>.drn</c> files (ordinary SQLite databases)
/// written by stock DRAKON Editor and by this shim must be mutually readable and
/// logically equivalent. These tests run against real example files from the stock
/// DRAKON Editor checkout (skipped when it is absent) and verify the PRAGMA-neutral
/// fingerprint knobs on files the shim creates.
/// </summary>
public class DrakonDrnCompatibilityTests
{
    private static string CopyExampleDrn(string folder)
        => ExtrasTestHelpers.CopyDrakonSampleDrn(folder);

    [Fact]
    public void a_real_drn_file_opens_and_exposes_the_drakon_schema()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string drn = CopyExampleDrn(folder);
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + drn + "}");

                //Act
                string tables = ExtrasTestHelpers.Eval(interpreter,
                    "db eval {select name from sqlite_master where type = 'table' order by name}");
                string diagramCount = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select count(*) from diagrams}");

                //Assert - the classic DRAKON table set is present and readable.
                tables.Should().Contain("diagrams");
                tables.Should().Contain("items");
                tables.Should().Contain("info");
                int.Parse(diagramCount).Should().BeGreaterThan(0);

                ExtrasTestHelpers.Eval(interpreter, "db close");
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void a_read_only_session_leaves_the_drn_file_byte_identical()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string drn = CopyExampleDrn(folder);
                byte[] before = File.ReadAllBytes(drn);

                //Act - open, read broadly, close; the PRAGMA-neutral path must not
                // rewrite the header, switch journal modes, or leave sidecar files.
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + drn + "}");
                ExtrasTestHelpers.Eval(interpreter,
                    "db eval {select * from diagrams}");
                ExtrasTestHelpers.Eval(interpreter,
                    "db eval {select * from items}");
                ExtrasTestHelpers.Eval(interpreter, "db close");

                //Assert
                File.ReadAllBytes(drn).Should().Equal(before);
                File.Exists(drn + "-wal").Should().BeFalse();
                File.Exists(drn + "-shm").Should().BeFalse();
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void modifications_to_a_real_drn_survive_a_reopen_with_schema_intact()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string drn = CopyExampleDrn(folder);
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + drn + "}");
                string originalName = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select name from diagrams order by diagram_id limit 1}");

                //Act - a DRAKON-shaped read-modify-write cycle.
                ExtrasTestHelpers.Eval(interpreter, @"
                    set new_name {Renamed by shim}
                    db eval {update diagrams set name = :new_name
                             where diagram_id = (select min(diagram_id) from diagrams)}");
                ExtrasTestHelpers.Eval(interpreter, "db close");

                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db2 {" + drn + "}");
                string renamed = ExtrasTestHelpers.Eval(interpreter,
                    "db2 onecolumn {select name from diagrams order by diagram_id limit 1}");
                string userVersion = ExtrasTestHelpers.Eval(interpreter,
                    "db2 onecolumn {PRAGMA user_version}");
                string journalMode = ExtrasTestHelpers.Eval(interpreter,
                    "db2 onecolumn {PRAGMA journal_mode}");
                ExtrasTestHelpers.Eval(interpreter, "db2 close");

                //Assert
                originalName.Should().NotBe("Renamed by shim");
                renamed.Should().Be("Renamed by shim");
                // Fingerprint knobs stay stock: DRAKON versions its files via its own
                // info table (user_version stays 0) and uses the rollback journal.
                userVersion.Should().Be("0");
                journalMode.Should().Be("delete");
                File.Exists(drn + "-wal").Should().BeFalse();
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void a_database_created_by_the_shim_has_stock_fingerprint_knobs()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string path = Path.Combine(folder, "fresh.drn");

                //Act - create a new file the way DRAKON creates a new project.
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + path + "}");
                ExtrasTestHelpers.Eval(interpreter, @"
                    db eval {create table info (key text, value text)}
                    db eval {insert into info values ('type', 'drakon')}");

                string journalMode = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA journal_mode}");
                string userVersion = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA user_version}");
                string applicationId = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA application_id}");
                string autoVacuum = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA auto_vacuum}");
                string foreignKeys = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA foreign_keys}");
                string encoding = ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {PRAGMA encoding}");
                ExtrasTestHelpers.Eval(interpreter, "db close");

                //Assert - every knob from master-plan §6.
                journalMode.Should().Be("delete");
                userVersion.Should().Be("0");
                applicationId.Should().Be("0");
                autoVacuum.Should().Be("0");
                foreignKeys.Should().Be("0");
                encoding.Should().Be("UTF-8");

                // No AUTOINCREMENT was used, so no sqlite_sequence table appears.
                using (Interpreter check = ExtrasTestHelpers.CreateInterpreter())
                {
                    ExtrasTestHelpers.Eval(check, "sqlite3 db {" + path + "}");
                    ExtrasTestHelpers.Eval(check,
                            "db onecolumn {select count(*) from sqlite_master where name = 'sqlite_sequence'}")
                        .Should().Be("0");
                    ExtrasTestHelpers.Eval(check, "db close");
                }
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void every_example_drn_column_type_is_covered_by_the_binding_rules()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string drn = CopyExampleDrn(folder);
                ExtrasTestHelpers.Eval(interpreter, "sqlite3 db {" + drn + "}");

                //Act - collect the declared type of every column of every table.
                string types = ExtrasTestHelpers.Eval(interpreter, @"
                    set out {}
                    db eval {select name from sqlite_master where type = 'table'} {
                        set tbl $name
                        db eval ""PRAGMA table_info('$tbl')"" {
                            lappend out [string toupper $type]
                        }
                    }
                    lsort -unique $out");

                ExtrasTestHelpers.Eval(interpreter, "db close");

                //Assert - the master-plan §6 claim this shim's typing strategy rests
                // on: every DRAKON column carries a numeric or text affinity, never
                // untyped/BLOB. (Real files use DOUBLE alongside INTEGER/TEXT/REAL;
                // DOUBLE has REAL affinity, so the claim holds.)
                foreach (string type in types.Split(' '))
                {
                    (type == "INTEGER" || type == "TEXT" || type == "REAL" || type == "DOUBLE")
                        .Should().BeTrue("unexpected column type: " + type);
                }
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }
}
