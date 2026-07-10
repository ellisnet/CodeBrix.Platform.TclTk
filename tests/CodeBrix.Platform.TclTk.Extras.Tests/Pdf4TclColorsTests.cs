using CodeBrix.PdfDocuments.Drawing;
using CodeBrix.Platform.TclTk.Extras.Pdf;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for pdf4tcl's color rule: <c>#RRGGBB</c> hex (DRAKON's only format) and
/// three-component 0..1 lists.
/// </summary>
public class Pdf4TclColorsTests
{
    [Fact]
    public void TryParse_accepts_hex_colors()
    {
        //Arrange / Act
        bool ok = Pdf4TclColors.TryParse("#ff8000", out XColor color);

        //Assert
        ok.Should().BeTrue();
        color.R.Should().Be((byte)255);
        color.G.Should().Be((byte)128);
        color.B.Should().Be((byte)0);
    }

    [Fact]
    public void TryParse_accepts_black_and_white_hex()
    {
        Pdf4TclColors.TryParse("#000000", out XColor black).Should().BeTrue();
        Pdf4TclColors.TryParse("#ffffff", out XColor white).Should().BeTrue();

        black.R.Should().Be((byte)0);
        white.R.Should().Be((byte)255);
    }

    [Fact]
    public void TryParse_accepts_a_three_component_list()
    {
        //Arrange / Act
        bool ok = Pdf4TclColors.TryParse("1.0 0.5 0.0", out XColor color);

        //Assert
        ok.Should().BeTrue();
        color.R.Should().Be((byte)255);
        color.G.Should().Be((byte)128);
        color.B.Should().Be((byte)0);
    }

    [Fact]
    public void TryParse_rejects_out_of_range_components()
        => Pdf4TclColors.TryParse("1.5 0 0", out XColor _).Should().BeFalse();

    [Fact]
    public void TryParse_rejects_tk_color_names()
        => Pdf4TclColors.TryParse("red", out XColor _).Should().BeFalse();

    [Fact]
    public void TryParse_rejects_malformed_hex()
    {
        Pdf4TclColors.TryParse("#12345", out XColor _).Should().BeFalse();
        Pdf4TclColors.TryParse("#gggggg", out XColor _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_empty_input()
        => Pdf4TclColors.TryParse("", out XColor _).Should().BeFalse();
}
