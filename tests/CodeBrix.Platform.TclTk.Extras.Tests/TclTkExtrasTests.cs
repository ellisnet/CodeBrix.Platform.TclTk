using System;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the <see cref="TclTkExtras"/> registration entry points: the commands
/// appear, the packages are provided (so DRAKON's <c>package require</c> lines
/// succeed), and the pdf4tcl array variables are published.
/// </summary>
public class TclTkExtrasTests
{
    [Fact]
    public void RegisterAll_registers_sqlite3_and_pdf4tcl_commands()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            string sqlite3 = ExtrasTestHelpers.Eval(interpreter, "info commands sqlite3");
            string pdfNew = ExtrasTestHelpers.Eval(interpreter, "info commands pdf4tcl::new");

            //Assert - with Tcl 8.4+ namespaces enabled, "info commands" reports the
            // fully-qualified name of a namespaced command (matches stock Tcl 8.6);
            // the global "sqlite3" command stays unqualified.
            sqlite3.Should().Be("sqlite3");
            pdfNew.Should().Be("::pdf4tcl::new");
        }
    }

    [Fact]
    public void RegisterAll_provides_the_sqlite3_package()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            ExtrasTestHelpers.Eval(interpreter, "package require sqlite3")
                .Should().Be("3.45.0");
        }
    }

    [Fact]
    public void RegisterAll_provides_the_pdf4tcl_package()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            ExtrasTestHelpers.Eval(interpreter, "package require pdf4tcl")
                .Should().Be("0.7");
        }
    }

    [Fact]
    public void RegisterPdf4Tcl_publishes_the_paper_sizes_array()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            // DRAKON reads $pdf4tcl::paper_sizes($format) directly (export_pdf.tcl).
            ExtrasTestHelpers.Eval(interpreter, "set pdf4tcl::paper_sizes(a4)")
                .Should().Be("595 842");
        }
    }

    [Fact]
    public void RegisterPdf4Tcl_publishes_the_units_array()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            // DRAKON reads $pdf4tcl::units(mm) directly (export_pdf.tcl).
            string mm = ExtrasTestHelpers.Eval(interpreter, "set pdf4tcl::units(mm)");

            double value = double.Parse(
                mm, System.Globalization.CultureInfo.InvariantCulture);
            value.Should().BeApproximately(72.0 / 25.4, 1e-12);
        }
    }

    [Fact]
    public void RegisterSqlite3_rejects_a_null_interpreter()
    {
        //Arrange
        Result error = null;

        //Act / Assert
        Assert.Throws<ArgumentNullException>(
            () => TclTkExtras.RegisterSqlite3(null, ref error));
    }

    [Fact]
    public void RegisterPdf4Tcl_rejects_a_null_interpreter()
    {
        //Arrange
        Result error = null;

        //Act / Assert
        Assert.Throws<ArgumentNullException>(
            () => TclTkExtras.RegisterPdf4Tcl(null, ref error));
    }
}
