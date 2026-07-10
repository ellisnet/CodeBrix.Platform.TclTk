using CodeBrix.Platform.TkCanvas.Canvas;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Tests for the Tcl-compatible string plumbing: the double formatting the
/// canvas oracle depends on byte-for-byte, list splitting/joining, and
/// screen-distance parsing.
/// </summary>
public class TclStringTests
{
    [Theory]
    [InlineData(10.0, "10.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(-3.0, "-3.0")]
    [InlineData(10.5, "10.5")]
    [InlineData(0.001, "0.001")]
    [InlineData(0.201, "0.201")]
    [InlineData(1e22, "1e+22")]
    [InlineData(1e-07, "1e-07")]
    [InlineData(0.3333333333333333, "0.3333333333333333")]
    public void FormatDouble_matches_tcl_printdouble(double value, string expected) =>
        TclString.FormatDouble(value).Should().Be(expected);

    [Fact]
    public void SplitList_handles_braces_and_quotes()
    {
        //Act
        var words = TclString.SplitList("a {b c} \"d e\" {} f");

        //Assert
        words.Should().Equal(new[] { "a", "b c", "d e", "", "f" });
    }

    [Fact]
    public void JoinList_braces_elements_with_whitespace()
    {
        //Act
        string joined = TclString.JoinList(new[] { "a", "b c", "" });

        //Assert
        joined.Should().Be("a {b c} {}");
    }

    [Theory]
    [InlineData("15", 15.0)]
    [InlineData("-4.5", -4.5)]
    [InlineData("1i", 96.0)]
    [InlineData("72p", 96.0)]
    public void TryParseCoord_handles_units(string text, double expected)
    {
        //Act
        double value;
        bool ok = TclString.TryParseCoord(text, out value);

        //Assert
        ok.Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void TryParsePixels_rounds_to_nearest()
    {
        //Act
        int value;
        bool ok = TclString.TryParsePixels("2.6", out value);

        //Assert
        ok.Should().BeTrue();
        value.Should().Be(3);
    }
}
