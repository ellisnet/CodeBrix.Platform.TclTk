using System;

using CodeBrix.Platform.TkCanvas.Fonts;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Tests for the font seam (R2): descriptor parsing, named-font lifecycle,
/// and — the point of the seam — that <c>font measure</c>-style results come
/// from the very same <see cref="SKFont"/> the painter draws with, so the
/// two can never disagree.
/// </summary>
public class FontManagerTests
{
    [Fact]
    public void Standard_tk_fonts_are_predefined()
    {
        //Arrange
        var fonts = new FontManager();

        //Act / Assert
        fonts.GetNamed("TkDefaultFont").Should().NotBeNull();
        fonts.GetNamed("TkTextFont").Should().NotBeNull();
        fonts.GetNamed("TkFixedFont").Should().NotBeNull();
        fonts.GetNamed("TkHeadingFont").Bold.Should().BeTrue();
    }

    [Fact]
    public void Parse_list_form_with_braced_family()
    {
        //Arrange
        var fonts = new FontManager();

        //Act
        TkFont font = fonts.Parse("{DejaVu Sans} 12 bold italic");

        //Assert
        font.Family.Should().Be("DejaVu Sans");
        font.Size.Should().Be(12);
        font.Bold.Should().BeTrue();
        font.Italic.Should().BeTrue();
    }

    [Fact]
    public void Parse_option_form()
    {
        //Arrange
        var fonts = new FontManager();

        //Act
        TkFont font = fonts.Parse("-family Courier -size -14 -weight bold -underline 1");

        //Assert
        font.Family.Should().Be("Courier");
        font.Size.Should().Be(-14);
        font.Bold.Should().BeTrue();
        font.Underline.Should().BeTrue();
    }

    [Fact]
    public void Parse_named_font_returns_the_shared_instance()
    {
        //Arrange
        var fonts = new FontManager();
        TkFont created = fonts.CreateNamed("appFont", fonts.Parse("Courier 11"));

        //Act
        TkFont resolved = fonts.Parse("appFont");

        //Assert
        resolved.Should().BeSameAs(created);
    }

    [Fact]
    public void Parse_x_core_font_name_falls_back_to_the_default_font()
    {
        //Arrange
        var fonts = new FontManager();

        //Act
        TkFont font = fonts.Parse("-adobe-helvetica-medium-r-normal--12-120-75-75-p-67-iso8859-1");

        //Assert (accept-and-no-op: never throw on legacy X font names)
        font.Should().BeSameAs(fonts.GetNamed("TkDefaultFont"));
    }

    [Fact]
    public void Named_font_reconfiguration_affects_later_measurement()
    {
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.CreateNamed("mutable", fonts.Parse("{DejaVu Sans} 10"));
        int before = fonts.Measure(font, "The quick brown fox");

        //Act (font configure: same shared instance, bigger size)
        font.Size = 20;
        int after = fonts.Measure(font, "The quick brown fox");

        //Assert
        (after > before).Should().BeTrue();
    }

    [Fact]
    public void CreateNamed_rejects_duplicates_and_DeleteNamed_removes()
    {
        //Arrange
        var fonts = new FontManager();
        fonts.CreateNamed("dup");

        //Act / Assert
        ((Action)(() => fonts.CreateNamed("dup"))).Should().Throw<InvalidOperationException>();
        fonts.DeleteNamed("dup");
        fonts.GetNamed("dup").Should().BeNull();
        ((Action)(() => fonts.DeleteNamed("dup"))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Measure_agrees_with_the_painters_skfont_by_construction()
    {
        //Arrange (R2: the seam's measurement IS the painter's measurement)
        var fonts = new FontManager();
        TkFont font = fonts.Parse("{DejaVu Sans} 12");
        const string text = "Diagram icon label 42";

        //Act
        int seam = fonts.Measure(font, text);
        int painter;
        using (SKFont skFont = fonts.GetSkFont(font))
        {
            painter = (int)Math.Ceiling(skFont.MeasureText(text));
        }

        //Assert
        seam.Should().Be(painter);
    }

    [Fact]
    public void Measure_of_empty_text_is_zero_and_longer_text_is_wider()
    {
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.Parse("{DejaVu Sans} 12");

        //Act / Assert
        fonts.Measure(font, "").Should().Be(0);
        (fonts.Measure(font, "wide wide wide") > fonts.Measure(font, "w")).Should().BeTrue();
    }

    [Fact]
    public void Negative_size_means_pixels_positive_means_points()
    {
        //Arrange
        var fonts = new FontManager();

        //Act / Assert (12pt at 96dpi = 16px; -12 = exactly 12px)
        fonts.PixelSize(fonts.Parse("{DejaVu Sans} 12")).Should().Be(16f);
        fonts.PixelSize(fonts.Parse("{DejaVu Sans} -12")).Should().Be(12f);
    }

    [Fact]
    public void Metrics_linespace_is_ascent_plus_descent()
    {
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.Parse("{DejaVu Sans} 12");

        //Act
        FontMetrics metrics = fonts.Metrics(font);

        //Assert
        (metrics.Ascent > 0).Should().BeTrue();
        (metrics.Descent > 0).Should().BeTrue();
        metrics.LineSpace.Should().Be(metrics.Ascent + metrics.Descent);
    }

    [Fact]
    public void Metrics_detects_fixed_pitch_for_monospace()
    {
        //Arrange
        var fonts = new FontManager();

        //Act
        FontMetrics mono = fonts.Metrics(fonts.Parse("{DejaVu Sans Mono} 12"));

        //Assert
        mono.IsFixed.Should().BeTrue();
    }

    [Fact]
    public void Unknown_family_falls_back_without_throwing()
    {
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.Parse("{NoSuchFamily XYZ} 12");

        //Act
        int width = fonts.Measure(font, "abc");

        //Assert
        (width > 0).Should().BeTrue();
    }
}
