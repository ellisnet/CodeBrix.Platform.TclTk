using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the database handle command's verbs — <c>eval</c> (both modes),
/// <c>onecolumn</c>, <c>changes</c> — and the tclsqlite binding rules DRAKON
/// depends on: caller-frame <c>:name</c> resolution, unset-variable → SQL NULL,
/// NULL → empty-string read-back, and verbatim SQL passthrough.
/// </summary>
public class SqliteHandleCommandTests
{
    private static Interpreter CreateWithDb()
    {
        Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter();
        ExtrasTestHelpers.Eval(interpreter, "sqlite3 db :memory:");
        ExtrasTestHelpers.Eval(interpreter,
            "db eval {create table items (item_id integer primary key, text_field text, num_field real)}");
        return interpreter;
    }

    // ---------------------------------------------------------------- eval

    [Fact]
    public void eval_without_body_returns_a_flat_list_of_all_columns_and_rows()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items values (1, 'alpha', 1.5)}");
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items values (2, 'beta', 2.5)}");

            //Act
            string list = ExtrasTestHelpers.Eval(interpreter,
                "db eval {select item_id, text_field from items order by item_id}");

            //Assert
            list.Should().Be("1 alpha 2 beta");
        }
    }

    [Fact]
    public void eval_with_body_binds_columns_as_caller_scope_variables()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items values (1, 'alpha', 1.5)}");
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items values (2, 'beta', 2.5)}");

            //Act - the DRAKON idiom: accumulate row variables from the body.
            string collected = ExtrasTestHelpers.Eval(interpreter, @"
                set out {}
                db eval {select item_id, text_field from items order by item_id} {
                    lappend out ""$item_id=$text_field""
                }
                set out");

            //Assert
            collected.Should().Be("1=alpha 2=beta");
        }
    }

    [Fact]
    public void eval_with_body_supports_select_star_column_names()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items values (7, 'seven', 7.5)}");

            //Act - DRAKON uses "select * from primitives" with body vars (export_pdf.tcl).
            string collected = ExtrasTestHelpers.Eval(interpreter, @"
                set out {}
                db eval {select * from items} {
                    lappend out $item_id $text_field $num_field
                }
                set out");

            //Assert
            collected.Should().Be("7 seven 7.5");
        }
    }

    [Fact]
    public void eval_body_break_stops_the_iteration()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (1, 'a', 0)}
                db eval {insert into items values (2, 'b', 0)}
                db eval {insert into items values (3, 'c', 0)}");

            //Act
            string collected = ExtrasTestHelpers.Eval(interpreter, @"
                set out {}
                db eval {select item_id from items order by item_id} {
                    if { $item_id == 2 } { break }
                    lappend out $item_id
                }
                set out");

            //Assert
            collected.Should().Be("1");
        }
    }

    [Fact]
    public void eval_body_continue_skips_to_the_next_row()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (1, 'a', 0)}
                db eval {insert into items values (2, 'b', 0)}
                db eval {insert into items values (3, 'c', 0)}");

            //Act
            string collected = ExtrasTestHelpers.Eval(interpreter, @"
                set out {}
                db eval {select item_id from items order by item_id} {
                    if { $item_id == 2 } { continue }
                    lappend out $item_id
                }
                set out");

            //Assert
            collected.Should().Be("1 3");
        }
    }

    [Fact]
    public void eval_body_error_propagates_to_the_caller()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "db eval {insert into items values (1, 'a', 0)}");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter, @"
                db eval {select item_id from items} {
                    error ""boom from body""
                }");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("boom from body");
        }
    }

    [Fact]
    public void eval_body_return_propagates_out_of_the_enclosing_proc()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange - "return" inside the row body must return from the caller's
            // proc, exactly as in tclsqlite.
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (1, 'a', 0)}
                db eval {insert into items values (2, 'b', 0)}
                proc find_first {} {
                    db eval {select item_id from items order by item_id} {
                        return ""found:$item_id""
                    }
                    return notfound
                }");

            //Act / Assert
            ExtrasTestHelpers.Eval(interpreter, "find_first").Should().Be("found:1");
        }
    }

    [Fact]
    public void eval_binds_at_and_dollar_prefixed_parameters_too()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - tclsqlite's other two host-parameter prefixes; a
            // literal $name only reaches the SQL when Tcl did not substitute it,
            // hence the backslash escape.
            ExtrasTestHelpers.Eval(interpreter, @"
                set via_at at-value
                set via_dollar dollar-value
                db eval {insert into items (item_id, text_field) values (1, @via_at)}
                db eval ""insert into items (item_id, text_field) values (2, \$via_dollar)""");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 1}")
                .Should().Be("at-value");
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 2}")
                .Should().Be("dollar-value");
        }
    }

    [Fact]
    public void eval_round_trips_integers_beyond_32_bits()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act
            ExtrasTestHelpers.Eval(interpreter, @"
                set big 5000000001
                db eval {insert into items (item_id) values (:big)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "db onecolumn {select item_id from items}")
                .Should().Be("5000000001");
            ExtrasTestHelpers.Eval(interpreter, "db onecolumn {select typeof(item_id) from items}")
                .Should().Be("integer");
        }
    }

    [Fact]
    public void eval_runs_multiple_statements_in_one_script()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - DRAKON creates its whole schema in single eval calls.
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {
                    insert into items values (1, 'one', 1.0);
                    insert into items values (2, 'two', 2.0);
                }");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "db onecolumn {select count(*) from items}")
                .Should().Be("2");
        }
    }

    [Fact]
    public void eval_reports_sql_errors_with_the_bare_sqlite_message()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "db eval {select * from missing_table}");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("no such table: missing_table");
        }
    }

    // ---------------------------------------------------------- parameter binding

    [Fact]
    public void eval_binds_colon_parameters_from_the_callers_frame()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - DRAKON's dominant pattern (braced SQL, :name binds).
            ExtrasTestHelpers.Eval(interpreter, @"
                set item_id 5
                set text_field {hello world}
                db eval {insert into items (item_id, text_field) values (:item_id, :text_field)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = :item_id}")
                .Should().Be("hello world");
        }
    }

    [Fact]
    public void eval_binds_parameters_from_a_proc_local_frame()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange - the variable lives in a PROC frame, not globals; this is how
            // virtually every DRAKON accessor works (scripts/model.tcl et al).
            ExtrasTestHelpers.Eval(interpreter, @"
                proc insert_item { id text } {
                    db eval {insert into items (item_id, text_field) values (:id, :text)}
                }
                insert_item 9 nine");

            //Act / Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 9}")
                .Should().Be("nine");
        }
    }

    [Fact]
    public void eval_binds_an_unset_variable_as_sql_null()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - unset Tcl variable => SQL NULL (typeof = null).
            ExtrasTestHelpers.Eval(interpreter, @"
                catch { unset never_set }
                db eval {insert into items (item_id, text_field) values (1, :never_set)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select typeof(text_field) from items where item_id = 1}")
                .Should().Be("null");
        }
    }

    [Fact]
    public void eval_binds_an_empty_string_variable_as_empty_text_not_null()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - existing-but-empty variable => TEXT '' (typeof = text).
            ExtrasTestHelpers.Eval(interpreter, @"
                set empty_value {}
                db eval {insert into items (item_id, text_field) values (1, :empty_value)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select typeof(text_field) from items where item_id = 1}")
                .Should().Be("text");
        }
    }

    [Fact]
    public void eval_reads_back_sql_null_as_an_empty_string()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items (item_id) values (1)}");

            //Act - tclsqlite's default -nullvalue is the empty string.
            string value = ExtrasTestHelpers.Eval(interpreter,
                "db onecolumn {select text_field from items where item_id = 1}");
            string viaBody = ExtrasTestHelpers.Eval(interpreter, @"
                set out unchanged
                db eval {select text_field from items where item_id = 1} {
                    set out $text_field
                }
                set out");

            //Assert
            value.Should().Be("");
            viaBody.Should().Be("");
        }
    }

    [Fact]
    public void eval_preserves_numeric_looking_text_in_text_columns()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - "007" and "1.10" must stay literal text, exactly as in
            // tclsqlite (which binds string-repped values as TEXT). Sniffing the
            // string as a number would store "7" / "1.1" and corrupt the file.
            ExtrasTestHelpers.Eval(interpreter, @"
                set padded 007
                set version 1.10
                db eval {insert into items (item_id, text_field) values (1, :padded)}
                db eval {insert into items (item_id, text_field) values (2, :version)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 1}")
                .Should().Be("007");
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 2}")
                .Should().Be("1.10");
        }
    }

    [Fact]
    public void eval_lets_integer_affinity_coerce_numeric_text()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - every DRAKON column is typed, so affinity produces the
            // correct storage class no matter how the value was bound.
            ExtrasTestHelpers.Eval(interpreter, @"
                set id 42
                set num 1.5
                db eval {insert into items (item_id, num_field) values (:id, :num)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select typeof(item_id) from items}")
                .Should().Be("integer");
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select typeof(num_field) from items}")
                .Should().Be("real");
        }
    }

    [Fact]
    public void eval_does_not_treat_colons_inside_string_literals_as_parameters()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act - the ':w' inside the SQL literal must pass through.
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {insert into items (item_id, text_field) values (1, 'time 10:30')}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 1}")
                .Should().Be("time 10:30");
        }
    }

    [Fact]
    public void eval_binds_the_same_parameter_used_twice_once()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act
            ExtrasTestHelpers.Eval(interpreter, @"
                set v 3
                db eval {insert into items (item_id, num_field) values (:v, :v)}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db eval {select item_id, num_field from items}")
                .Should().Be("3 3.0");
        }
    }

    [Fact]
    public void eval_passes_pragma_statements_through_verbatim()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange / Act / Assert - faithful passthrough: PRAGMAs reach SQLite.
            ExtrasTestHelpers.Eval(interpreter, "db eval {PRAGMA journal_mode}")
                .Should().Be("memory"); // :memory: databases always report "memory"

            string pageSize = ExtrasTestHelpers.Eval(interpreter, "db eval {PRAGMA page_size}");
            pageSize.Should().Be("4096");
        }
    }

    // ---------------------------------------------------------------- onecolumn

    [Fact]
    public void onecolumn_returns_the_first_column_of_the_first_row()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (1, 'first', 0)}
                db eval {insert into items values (2, 'second', 0)}");

            //Act / Assert
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field, num_field from items order by item_id}")
                .Should().Be("first");
        }
    }

    [Fact]
    public void onecolumn_returns_an_empty_string_when_there_are_no_rows()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = 999}")
                .Should().Be("");
        }
    }

    [Fact]
    public void onecolumn_binds_parameters_like_eval()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (5, 'five', 0)}
                set wanted 5");

            //Act / Assert - DRAKON's second-most-common call shape (163 uses).
            ExtrasTestHelpers.Eval(interpreter,
                    "db onecolumn {select text_field from items where item_id = :wanted}")
                .Should().Be("five");
        }
    }

    // ---------------------------------------------------------------- changes

    [Fact]
    public void changes_reports_rows_affected_by_the_last_statement()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                db eval {insert into items values (1, 'a', 0)}
                db eval {insert into items values (2, 'b', 0)}
                db eval {insert into items values (3, 'c', 0)}");

            //Act
            ExtrasTestHelpers.Eval(interpreter, "db eval {update items set num_field = 9 where item_id > 1}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "db changes").Should().Be("2");
        }
    }

    [Fact]
    public void changes_is_zero_after_a_no_op_update()
    {
        using (Interpreter interpreter = CreateWithDb())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter,
                "db eval {update items set num_field = 1 where item_id = 12345}");

            //Act / Assert
            ExtrasTestHelpers.Eval(interpreter, "db changes").Should().Be("0");
        }
    }
}
