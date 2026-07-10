using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the pdf4tcl factory commands: object creation with DRAKON's exact
/// option set, <c>%AUTO%</c> naming, font loading/creation, and error paths.
/// </summary>
public class Pdf4TclFactoryCommandsTests
{
    [Fact]
    public void new_creates_an_object_command_with_the_given_name()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act - DRAKON's exact call shape (export_pdf.tcl line 378).
            string name = ExtrasTestHelpers.Eval(interpreter,
                "pdf4tcl::new mypdf -paper a4 -margin 20 -landscape 0");

            //Assert
            name.Should().Be("mypdf");
            ExtrasTestHelpers.Eval(interpreter, "info commands mypdf").Should().Be("mypdf");
            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");
        }
    }

    [Fact]
    public void new_generates_a_name_for_auto()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            string name = ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new %AUTO%");

            //Assert
            name.Should().StartWith("pdf4tcl");
            ExtrasTestHelpers.Eval(interpreter, "info commands " + name).Should().Be(name);
            ExtrasTestHelpers.Eval(interpreter, name + " destroy");
        }
    }

    [Fact]
    public void new_rejects_an_unknown_paper_size()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "pdf4tcl::new mypdf -paper nosuchpaper");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("papersize nosuchpaper is unknown");
        }
    }

    [Fact]
    public void new_rejects_an_unknown_option()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "pdf4tcl::new mypdf -bogus 1");

            code.Should().Be(ReturnCode.Error);
            result.Should().Be("unknown option -bogus");
        }
    }

    [Fact]
    public void new_accepts_a_custom_width_height_paper_list()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act - DRAKON also passes "{w h}" lists via p.get_paper_size.
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper {300 400}");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "mypdf startPage");
            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");
        }
    }

    [Fact]
    public void loadBaseTrueTypeFont_loads_a_real_ttf_file()
    {
        string ttf = ExtrasTestHelpers.RequireMonospaceTtf();

        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange / Act / Assert - DRAKON's exact two-step font setup.
            ExtrasTestHelpers.Eval(interpreter,
                "pdf4tcl::loadBaseTrueTypeFont BaseFontA {" + ttf + "}");
            ExtrasTestHelpers.Eval(interpreter,
                "pdf4tcl::createFont BaseFontA FontA cp1252");
        }
    }

    [Fact]
    public void loadBaseTrueTypeFont_fails_for_a_missing_file()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            (ReturnCode code, string _) = ExtrasTestHelpers.TryEval(interpreter,
                "pdf4tcl::loadBaseTrueTypeFont BaseFontB {/nonexistent/font.ttf}");

            code.Should().Be(ReturnCode.Error);
        }
    }

    [Fact]
    public void createFont_fails_for_an_unloaded_base_font()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter,
                "pdf4tcl::createFont NeverLoaded SomeFont cp1252");

            code.Should().Be(ReturnCode.Error);
            result.Should().Be("base font NeverLoaded doesn't exist");
        }
    }
}
