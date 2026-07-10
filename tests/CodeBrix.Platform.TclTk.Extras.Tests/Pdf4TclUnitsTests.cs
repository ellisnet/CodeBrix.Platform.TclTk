using System;

using CodeBrix.Platform.TclTk.Extras.Pdf;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for pdf4tcl's measurement rules: <c>getPoints</c> unit-suffix parsing and
/// paper-size resolution.
/// </summary>
public class Pdf4TclUnitsTests
{
    [Fact]
    public void GetPoints_multiplies_a_bare_number_by_the_document_unit()
        => Pdf4TclUnits.GetPoints("10", 2.0).Should().Be(20.0);

    [Fact]
    public void GetPoints_converts_a_millimeter_suffix()
        => Pdf4TclUnits.GetPoints("25.4mm", 1.0).Should().BeApproximately(72.0, 1e-9);

    [Fact]
    public void GetPoints_converts_an_inch_suffix()
        => Pdf4TclUnits.GetPoints("2i", 1.0).Should().Be(144.0);

    [Fact]
    public void GetPoints_converts_a_point_suffix_ignoring_the_document_unit()
        => Pdf4TclUnits.GetPoints("15p", 3.0).Should().Be(15.0);

    [Fact]
    public void GetPoints_allows_whitespace_between_number_and_unit()
        => Pdf4TclUnits.GetPoints(" 1 cm ", 1.0).Should().BeApproximately(72.0 / 2.54, 1e-9);

    [Fact]
    public void GetPoints_rejects_an_unknown_unit()
        => Assert.Throws<FormatException>(() => Pdf4TclUnits.GetPoints("10furlongs", 1.0));

    [Fact]
    public void GetPoints_rejects_a_non_number()
        => Assert.Throws<FormatException>(() => Pdf4TclUnits.GetPoints("abc", 1.0));

    [Fact]
    public void TryGetPaperSize_resolves_a4_in_points()
    {
        //Arrange / Act
        bool ok = Pdf4TclUnits.TryGetPaperSize("a4", 1.0, out double width, out double height);

        //Assert
        ok.Should().BeTrue();
        width.Should().Be(595.0);
        height.Should().Be(842.0);
    }

    [Fact]
    public void TryGetPaperSize_is_case_insensitive_for_names()
    {
        bool ok = Pdf4TclUnits.TryGetPaperSize("Letter", 1.0, out double width, out double height);

        ok.Should().BeTrue();
        width.Should().Be(612.0);
        height.Should().Be(792.0);
    }

    [Fact]
    public void TryGetPaperSize_accepts_a_two_element_width_height_list()
    {
        bool ok = Pdf4TclUnits.TryGetPaperSize("100 200", 1.0, out double width, out double height);

        ok.Should().BeTrue();
        width.Should().Be(100.0);
        height.Should().Be(200.0);
    }

    [Fact]
    public void TryGetPaperSize_scales_list_values_by_the_document_unit()
    {
        bool ok = Pdf4TclUnits.TryGetPaperSize("100 200", 2.0, out double width, out double height);

        ok.Should().BeTrue();
        width.Should().Be(200.0);
        height.Should().Be(400.0);
    }

    [Fact]
    public void TryGetPaperSize_rejects_garbage()
    {
        Pdf4TclUnits.TryGetPaperSize("not-a-paper", 1.0, out double _, out double _)
            .Should().BeFalse();
        Pdf4TclUnits.TryGetPaperSize("1 2 3", 1.0, out double _, out double _)
            .Should().BeFalse();
    }
}
