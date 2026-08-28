using System;
using System.IO;

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
        // R2: the seam's measurement IS the painter's measurement. The painter
        // (FontManager.DrawTextOnGrid) walks a pen across Tk's whole-pixel grid,
        // one GridAdvance per glyph, so the seam agrees with it exactly when
        // Measure(prefix) lands on the pen position of the glyph after that
        // prefix. Recomputed here from the RAW Skia advances rather than from
        // the grid helpers, so this is a real check and not a tautology.
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.Parse("{DejaVu Sans} 12");
        const string text = "Diagram icon label 42";

        //Act / Assert
        using (SKFont skFont = fonts.GetSkFont(font))
        {
            ushort[] glyphs = skFont.GetGlyphs(text);
            glyphs.Length.Should().Be(text.Length); // Latin: one glyph per char
            float[] advances = skFont.GetGlyphWidths(glyphs);

            double pen = 0;
            for (int i = 0; i < text.Length; i++)
            {
                fonts.Measure(font, text.Substring(0, i)).Should().Be((int)pen);
                pen += Math.Ceiling(advances[i]);
            }

            fonts.Measure(font, text).Should().Be((int)pen);

            // The grid never reports a width NARROWER than the ink it must hold
            // (this is why GridAdvance rounds up rather than to nearest).
            (fonts.Measure(font, text) >= (int)Math.Ceiling(skFont.MeasureText(text)))
                    .Should().BeTrue();
        }
    }

    [Fact]
    public void Monospace_comes_from_the_packaged_font_and_never_from_the_system()
    {
        // POLICY: the toolkit ships its own fonts and must not depend on the host
        // having any particular one installed. If this ever reads back a system
        // face (Consolas on Windows, DejaVu/Noto via fontconfig on Linux) then the
        // font files did not reach the output directory and geometry has quietly
        // become machine-dependent again.
        //Arrange
        var fonts = new FontManager();

        //Act / Assert — monospace, sans-serif and serif all come out of the packages
        using (SKFont skFont = fonts.GetSkFont(fonts.GetNamed("TkFixedFont")))
        {
            skFont.Typeface.FamilyName.Should().Be("Noto Sans Mono");
        }

        using (SKFont skFont = fonts.GetSkFont(fonts.GetNamed("TkDefaultFont")))
        {
            skFont.Typeface.FamilyName.Should().Be("Roboto");

            // Not the face's own axis default: Merriweather's wght defaults to 300,
            // so a face left unpinned would render body text in Light.
            skFont.Typeface.FontWeight.Should().Be(400);
        }

        using (SKFont skFont = fonts.GetSkFont(fonts.Parse("{times} 10")))
        {
            skFont.Typeface.FamilyName.Should().Contain("Merriweather");
            skFont.Typeface.FontWeight.Should().Be(400);
        }

        // Bold comes off the variable font's weight axis, not a second file — and
        // must NOT disturb the column grid.
        TkFont bold = fonts.CreateNamed("boldfixed", fonts.GetNamed("TkFixedFont"));
        bold.Bold = true;
        using (SKFont boldFont = fonts.GetSkFont(bold))
        {
            boldFont.Typeface.FamilyName.Should().Be("Noto Sans Mono");
            boldFont.Typeface.FontWeight.Should().Be(700);
        }

        fonts.Measure(bold, "0").Should().Be(fonts.Measure(fonts.GetNamed("TkFixedFont"), "0"));
    }

    [Theory]
    // The scripts TkCanvas supports: European, including Armenian and Georgian.
    [InlineData("latin", "Abc")]
    [InlineData("latin-extended", "āșż")]
    [InlineData("cyrillic", "Джф")]
    [InlineData("greek", "αβγ")]
    [InlineData("polytonic-greek", "ᾳῆῷ")]
    [InlineData("armenian", "ԱԲգ")]
    [InlineData("georgian", "აბგ")]
    public void European_scripts_render_through_the_fallback_chain(string script, string text)
    {
        // Roboto/Merriweather/Noto Sans Mono cover Latin, Cyrillic and modern
        // Greek; the packaged companions add Armenian, Georgian and polytonic
        // Greek. A codepoint no face carries would come back as glyph 0 and
        // paint tofu, so asserting "no notdef" IS the coverage test.
        //Arrange
        var fonts = new FontManager();

        //Act / Assert — every generic must render every European script
        foreach (string named in new[] { "TkDefaultFont", "TkFixedFont" })
        {
            AssertNoTofu(fonts, fonts.GetNamed(named), text, script + " via " + named);
        }
        AssertNoTofu(fonts, fonts.Parse("{times} 10"), text, script + " via serif");
    }

    private static void AssertNoTofu(FontManager fonts, TkFont font, string text, string because)
    {
        // A codepoint is legible when SOME face in the chain carries it; Measure
        // proves it also occupies real width.
        fonts.Measure(font, text).Should().BeGreaterThan(0, because);

        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            fonts.HasGlyph(font, rune.Value).Should()
                    .BeTrue("no packaged face covers U+" + rune.Value.ToString("X4") + " (" + because + ")");
        }
    }

    [Fact]
    public void Polytonic_greek_falls_back_within_the_sans_family_not_to_a_serif()
    {
        // Roboto has the complete MODERN Greek alphabet but only 1 of 233
        // polytonic (Greek Extended) codepoints, so Ancient Greek needs a
        // fallback. It must land on Noto Sans — a sans face — and not on the
        // Noto Serif this chain used before Noto Sans was packaged.
        //Arrange
        var fonts = new FontManager();
        TkFont sans = fonts.GetNamed("TkDefaultFont");

        //Act / Assert — modern Greek never leaves the primary
        using (SKFont primary = fonts.GetSkFont(sans))
        {
            foreach (char c in "αβγδεζηθικλμνξοπρςστυφχψω")
            {
                primary.ContainsGlyph(c).Should().BeTrue("Roboto covers modern Greek itself");
            }

            // ...while polytonic does, and is covered by the chain.
            foreach (char c in "ᾳῆῷἀἐἠὠῥ")
            {
                primary.ContainsGlyph(c).Should().BeFalse("Roboto has no Greek Extended");
                fonts.HasGlyph(sans, c).Should().BeTrue("the sans chain must cover polytonic Greek");
            }
        }
    }

    [Fact]
    public void Unsupported_scripts_are_tofu_and_never_throw()
    {
        // Hebrew, Arabic and CJK are deliberately out of scope. They must degrade
        // to a tofu box — measured, painted, no exception — because an
        // unsupported script is a rendering limitation, not a broken install.
        //Arrange
        var fonts = new FontManager();
        TkFont font = fonts.GetNamed("TkDefaultFont");

        //Act / Assert
        foreach (string text in new[] { "שלו", "مرح", "你好" })
        {
            Exception thrown = Record.Exception(() => fonts.Measure(font, text));
            thrown.Should().BeNull();
            fonts.Measure(font, text).Should().BeGreaterThan(0); // tofu still occupies width
            fonts.HasGlyph(font, char.ConvertToUtf32(text, 0)).Should().BeFalse();
        }
    }

    [Fact]
    public void A_family_we_do_not_ship_is_served_by_a_packaged_face_not_the_host()
    {
        // Without the opt-in, naming a real INSTALLED family must still not reach
        // the host — otherwise a Tcl script saying "{Segoe UI 12}" would silently
        // make that machine's layout different from every other machine's.
        //Arrange
        var fonts = new FontManager();

        //Act / Assert
        using (SKFont skFont = fonts.GetSkFont(fonts.Parse("{Segoe UI} 12")))
        {
            skFont.Typeface.FamilyName.Should().Be("Roboto");
        }
    }

    [Fact]
    public void Opting_in_to_system_fonts_lets_a_host_family_through()
    {
        //Arrange
        var fonts = new FontManager { AllowSystemFontFallback = true };

        //Act (a family no font package ships)
        using (SKFont skFont = fonts.GetSkFont(fonts.Parse("{NoSuchFamily XYZ} 12")))
        {
            //Assert — resolved by the host, so NOT one of our packaged faces
            skFont.Typeface.FamilyName.Should().NotBe("Roboto");
        }
    }

    [Fact]
    public void A_missing_packaged_font_throws_instead_of_silently_using_a_host_font()
    {
        // The whole point of shipping fonts is that geometry cannot drift between
        // machines. A deployment that lost its .ttf files must fail loudly rather
        // than quietly render in whatever the box happens to have installed.
        //Arrange (an empty directory stands in for a deployment that lost its fonts)
        string empty = Path.Combine(Path.GetTempPath(), "tkcanvas-no-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var fonts = new FontManager { PackagedFontBaseDirectory = empty };

            //Act / Assert
            Exception thrown = Record.Exception(() => fonts.GetSkFont(fonts.GetNamed("TkDefaultFont")));
            thrown.Should().BeOfType<InvalidOperationException>();

            // The message has to be actionable: what is missing, and both ways out.
            thrown.Message.Should().Contain("Roboto.ttf");
            thrown.Message.Should().Contain("AddFontDirectory");
            thrown.Message.Should().Contain("AllowSystemFontFallback");

            //Assert — and the opt-in genuinely rescues that same broken deployment
            var opted = new FontManager
            {
                PackagedFontBaseDirectory = empty,
                AllowSystemFontFallback = true,
            };
            using (SKFont skFont = opted.GetSkFont(opted.GetNamed("TkDefaultFont")))
            {
                skFont.Typeface.Should().NotBeNull();
            }
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void Measure_is_additive_so_a_fixed_font_lays_out_on_an_exact_column_grid()
    {
        // The property every widget leans on: a text widget's "-width N" is
        // N * Measure("0"), a caret is placed by measuring the prefix, and a
        // display line's width is the sum of its runs. Skia only gives this for
        // free on FreeType (Linux); DirectWrite and CoreText hand back
        // fractional advances, which is what put xview/see/index @x,y a column
        // off on Windows and macOS before the seam moved onto whole pixels.
        //Arrange
        var fonts = new FontManager();
        TkFont fixedFont = fonts.GetNamed("TkFixedFont");
        fonts.Metrics(fixedFont).IsFixed.Should().BeTrue();

        //Act
        int cell = fonts.Measure(fixedFont, "0");

        //Assert (a run of N cells is exactly N cells wide, on every platform)
        fonts.Measure(fixedFont, new string('0', 10)).Should().Be(10 * cell);
        fonts.Measure(fixedFont, new string('0', 40)).Should().Be(40 * cell);

        //Assert (and measurement is additive for proportional fonts too)
        TkFont proportional = fonts.GetNamed("TkDefaultFont");
        int whole = fonts.Measure(proportional, "world wide");
        int parts = fonts.Measure(proportional, "world") + fonts.Measure(proportional, " wide");
        whole.Should().Be(parts);
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

        // TkFixedFont is the toolkit's standard fixed-pitch font (family
        // "monospace"). FontManager resolves it to a genuinely monospace face on
        // every OS — including Windows/macOS, whose Skia backends do NOT resolve
        // the bare CSS generic "monospace" to a fixed-pitch font and would
        // otherwise fall back to the proportional system UI font. So this must be
        // fixed-pitch regardless of platform.
        FontMetrics mono = fonts.Metrics(fonts.GetNamed("TkFixedFont"));

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
