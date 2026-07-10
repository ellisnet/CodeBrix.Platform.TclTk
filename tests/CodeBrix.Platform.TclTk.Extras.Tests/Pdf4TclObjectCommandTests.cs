using System.Globalization;
using System.IO;
using System.Text;

using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk.Extras.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the pdf4tcl object command's drawing surface, including a full
/// DRAKON-shaped export producing a real PDF file, text measurement semantics,
/// and lifecycle/error paths.
/// </summary>
public class Pdf4TclObjectCommandTests
{
    /// <summary>Registers the shim's two-step font setup with a real TTF and returns the interpreter.</summary>
    private static Interpreter CreateWithFont()
    {
        string ttf = ExtrasTestHelpers.RequireMonospaceTtf();
        Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter();
        ExtrasTestHelpers.Eval(interpreter,
            "pdf4tcl::loadBaseTrueTypeFont TestBase {" + ttf + "}");
        ExtrasTestHelpers.Eval(interpreter,
            "pdf4tcl::createFont TestBase TestMono cp1252");
        return interpreter;
    }

    [Fact]
    public void a_drakon_shaped_export_produces_a_valid_pdf_file()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = CreateWithFont())
            {
                //Arrange - mirror export_pdf.tcl's sequence: fonts, new, per-page
                // setFont, colors, line style, background rect, primitives, text.
                string output = Path.Combine(folder, "out.pdf");

                //Act
                ExtrasTestHelpers.Eval(interpreter, @"
                    pdf4tcl::new mypdf -paper a4 -margin 20 -landscape 0
                    mypdf startPage
                    mypdf setFont 12 TestMono
                    mypdf setLineStyle 0.5
                    mypdf setFillColor ""#c0c0c0""
                    mypdf setStrokeColor ""#c0c0c0""
                    mypdf rectangle 0 0 555 300 -filled 1
                    mypdf setLineStyle 0.75
                    mypdf setFillColor ""#ffffff""
                    mypdf setStrokeColor ""#000000""
                    mypdf rectangle 40 40 200 100 -filled 1
                    mypdf line 40 90 240 90
                    mypdf polygon 300 40 400 40 350 120 -filled 1
                    mypdf setFillColor ""#000000""
                    mypdf text {Diagram title} -x 50 -y 70");
                ExtrasTestHelpers.Eval(interpreter, "mypdf write -file {" + output + "}");
                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert
                File.Exists(output).Should().BeTrue();
                byte[] bytes = File.ReadAllBytes(output);
                bytes.Length.Should().BeGreaterThan(1000);
                Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void getStringWidth_scales_linearly_for_a_monospace_font()
    {
        using (Interpreter interpreter = CreateWithFont())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                pdf4tcl::new mypdf -paper a4
                mypdf setFont 10 TestMono");

            //Act
            double one = double.Parse(
                ExtrasTestHelpers.Eval(interpreter, "mypdf getStringWidth {m}"),
                CultureInfo.InvariantCulture);
            double four = double.Parse(
                ExtrasTestHelpers.Eval(interpreter, "mypdf getStringWidth {mmmm}"),
                CultureInfo.InvariantCulture);

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert - in a monospace face, four glyphs are exactly four advances.
            one.Should().BeGreaterThan(0.0);
            four.Should().BeApproximately(one * 4.0, 0.01);
        }
    }

    [Fact]
    public void getStringWidth_scales_with_the_font_size()
    {
        using (Interpreter interpreter = CreateWithFont())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act - DRAKON measures at several sizes (font_size vs the fixed 8).
            ExtrasTestHelpers.Eval(interpreter, "mypdf setFont 10 TestMono");
            double at10 = double.Parse(
                ExtrasTestHelpers.Eval(interpreter, "mypdf getStringWidth {Hello}"),
                CultureInfo.InvariantCulture);

            ExtrasTestHelpers.Eval(interpreter, "mypdf setFont 20 TestMono");
            double at20 = double.Parse(
                ExtrasTestHelpers.Eval(interpreter, "mypdf getStringWidth {Hello}"),
                CultureInfo.InvariantCulture);

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            at20.Should().BeApproximately(at10 * 2.0, 0.01);
        }
    }

    [Fact]
    public void text_returns_the_same_width_that_getStringWidth_reports()
    {
        using (Interpreter interpreter = CreateWithFont())
        {
            //Arrange - the measure/draw consistency requirement (master-plan §4.2):
            // DRAKON's line-wrapping math only holds if the two agree.
            ExtrasTestHelpers.Eval(interpreter, @"
                pdf4tcl::new mypdf -paper a4
                mypdf startPage
                mypdf setFont 12 TestMono");

            //Act
            string measured = ExtrasTestHelpers.Eval(
                interpreter, "mypdf getStringWidth {Sample text}");
            string drawn = ExtrasTestHelpers.Eval(
                interpreter, "mypdf text {Sample text} -x 10 -y 20");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            drawn.Should().Be(measured);
        }
    }

    [Fact]
    public void getStringWidth_works_before_any_page_is_started()
    {
        using (Interpreter interpreter = CreateWithFont())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, @"
                pdf4tcl::new mypdf -paper a4
                mypdf setFont 12 TestMono");

            //Act
            double width = double.Parse(
                ExtrasTestHelpers.Eval(interpreter, "mypdf getStringWidth {abc}"),
                CultureInfo.InvariantCulture);

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            width.Should().BeGreaterThan(0.0);
        }
    }

    [Fact]
    public void getStringWidth_fails_without_a_font()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "mypdf getStringWidth {abc}");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("No font set");
        }
    }

    [Fact]
    public void setFont_fails_for_a_font_that_was_never_created()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "mypdf setFont 12 NoSuchFont");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert - pdf4tcl's exact error text.
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("Font NoSuchFont doesn't exist");
        }
    }

    [Fact]
    public void multiple_pages_can_be_written()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = CreateWithFont())
            {
                //Arrange - DRAKON's multi-page export loops startPage per grid cell.
                string output = Path.Combine(folder, "pages.pdf");

                //Act
                ExtrasTestHelpers.Eval(interpreter, @"
                    pdf4tcl::new mypdf -paper a4 -margin 15
                    mypdf startPage
                    mypdf setFont 10 TestMono
                    mypdf text {page one} -x 10 -y 20
                    mypdf startPage
                    mypdf setFont 10 TestMono
                    mypdf text {page two} -x 10 -y 20");
                ExtrasTestHelpers.Eval(interpreter, "mypdf write -file {" + output + "}");
                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert - two page objects in the PDF.
                using (PdfDocument reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import))
                {
                    reopened.PageCount.Should().Be(2);
                }
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void startPage_accepts_landscape_and_margin_options()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = CreateWithFont())
            {
                //Arrange
                string output = Path.Combine(folder, "landscape.pdf");

                //Act - landscape a4 swaps width/height (842 x 595).
                ExtrasTestHelpers.Eval(interpreter, @"
                    pdf4tcl::new mypdf -paper a4 -landscape 1
                    mypdf startPage
                    mypdf setLineStyle 1
                    mypdf setStrokeColor ""#000000""
                    mypdf line 0 0 800 500");
                ExtrasTestHelpers.Eval(interpreter, "mypdf write -file {" + output + "}");
                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert - landscape a4 swaps width and height.
                using (PdfDocument reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import))
                {
                    ((double)reopened.Pages[0].Width).Should().BeApproximately(842.0, 0.5);
                    ((double)reopened.Pages[0].Height).Should().BeApproximately(595.0, 0.5);
                }
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void startPage_options_override_the_document_defaults_per_page()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange - document default a4 portrait; second page letter landscape.
                string output = Path.Combine(folder, "mixed.pdf");

                //Act
                ExtrasTestHelpers.Eval(interpreter, @"
                    pdf4tcl::new mypdf -paper a4
                    mypdf startPage
                    mypdf startPage -paper letter -landscape 1");
                ExtrasTestHelpers.Eval(interpreter, "mypdf write -file {" + output + "}");
                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert
                using (PdfDocument reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import))
                {
                    ((double)reopened.Pages[0].Width).Should().BeApproximately(595.0, 0.5);
                    ((double)reopened.Pages[1].Width).Should().BeApproximately(792.0, 0.5);
                    ((double)reopened.Pages[1].Height).Should().BeApproximately(612.0, 0.5);
                }
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void write_fails_without_a_file_destination()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter, "mypdf write");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Contain("no output file");
        }
    }

    [Fact]
    public void write_uses_the_file_given_to_new_as_the_default_destination()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string output = Path.Combine(folder, "default.pdf");
                ExtrasTestHelpers.Eval(interpreter,
                    "pdf4tcl::new mypdf -paper a4 -file {" + output + "}");
                ExtrasTestHelpers.Eval(interpreter, "mypdf startPage");

                //Act
                ExtrasTestHelpers.Eval(interpreter, "mypdf write");
                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert
                File.Exists(output).Should().BeTrue();
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void destroy_removes_the_object_command()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act - DRAKON calls "catch { mypdf destroy }" then destroy again at the
            // end of every export; the second call must not crash the script.
            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            ExtrasTestHelpers.Eval(interpreter, "info commands mypdf").Should().Be("");
            ExtrasTestHelpers.Eval(interpreter, "catch { mypdf destroy }").Should().Be("1");
        }
    }

    [Fact]
    public void drawing_after_write_fails_cleanly()
    {
        string folder = ExtrasTestHelpers.CreateTempFolder();
        try
        {
            using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
            {
                //Arrange
                string output = Path.Combine(folder, "done.pdf");
                ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");
                ExtrasTestHelpers.Eval(interpreter, "mypdf startPage");
                ExtrasTestHelpers.Eval(interpreter, "mypdf write -file {" + output + "}");

                //Act
                (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                    interpreter, "mypdf rectangle 0 0 10 10");

                ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

                //Assert
                code.Should().Be(ReturnCode.Error);
                result.Should().Be("PDF document already finished");
            }
        }
        finally
        {
            ExtrasTestHelpers.DeleteTempFolder(folder);
        }
    }

    [Fact]
    public void object_rejects_an_unknown_method_with_the_supported_list()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(interpreter, "mypdf bogus");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().StartWith("bad option \"bogus\"");
            result.Should().Contain("startPage");
        }
    }

    [Fact]
    public void setFillColor_rejects_an_unknown_color()
    {
        using (Interpreter interpreter = ExtrasTestHelpers.CreateInterpreter())
        {
            //Arrange
            ExtrasTestHelpers.Eval(interpreter, "pdf4tcl::new mypdf -paper a4");

            //Act
            (ReturnCode code, string result) = ExtrasTestHelpers.TryEval(
                interpreter, "mypdf setFillColor chartreuse");

            ExtrasTestHelpers.Eval(interpreter, "mypdf destroy");

            //Assert
            code.Should().Be(ReturnCode.Error);
            result.Should().Be("Unknown color: chartreuse");
        }
    }
}
