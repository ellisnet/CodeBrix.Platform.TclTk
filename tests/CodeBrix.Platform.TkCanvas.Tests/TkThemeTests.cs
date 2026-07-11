using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The TkTheme model (B.12a): the classic default is pinned byte-for-byte to
/// the pre-theming literals (the regression guard), the palette derivation
/// matches Tk's math (the oracle fixtures cover the shades; these tests
/// cover the field mapping), the registry resolves names and aliases, and a
/// theme switch actually recolors painted pixels.
/// </summary>
public class TkThemeTests
{
    [Fact]
    public void Classic_theme_pins_the_pre_theming_defaults()
    {
        //Arrange + Act
        TkTheme theme = TkTheme.CreateClassic();

        //Assert — these exact values were the hardcoded literals before B.12a;
        // the classic theme MUST reproduce them so the default look is unchanged.
        theme.Background.Should().Be("#d9d9d9");
        theme.Foreground.Should().Be("black");
        theme.ActiveBackground.Should().Be("#d9d9d9");
        theme.DisabledForeground.Should().Be("#a3a3a3");
        theme.SelectBackground.Should().Be("#c3c3c3");
        theme.SelectColor.Should().Be("white");
        theme.IndicatorForeground.Should().Be("#1a1a1a");
        theme.TroughColor.Should().Be("#b3b3b3");
        theme.FieldBackground.Should().Be("white");
        theme.FieldForeground.Should().Be("black");
        theme.ListSelectBackground.Should().Be("#4a6984");
        theme.ListSelectForeground.Should().Be("white");
        theme.HeadingBackground.Should().Be("#e0e0e0");
        theme.MenuBackground.Should().Be("#d9d9d9");
        theme.MenuActiveBackground.Should().Be("#4a6984");
        theme.MenuActiveForeground.Should().Be("white");
        theme.StageBackground.Should().Be("#d9d9d9");
        theme.TitleBarBackground.Should().Be("#4a6984");
        theme.TitleBarForeground.Should().Be("white");
        theme.ButtonBackground.Should().Be("#d9d9d9");
        theme.ScrollbarBackground.Should().Be("#d9d9d9");
        theme.CanvasBackground.Should().Be("#d9d9d9");
        theme.DialogInfoAccent.Should().Be("#204a87");
        theme.DialogWarningAccent.Should().Be("#c08000");
        theme.DialogErrorAccent.Should().Be("#c00000");
    }

    [Fact]
    public void Tree_theme_defaults_to_classic_and_never_null()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();

        //Act + Assert — no theme set means the classic theme IS in effect.
        root.Tree.Theme.Background.Should().Be("#d9d9d9");
        root.Tree.Theme = null;
        root.Tree.Theme.Background.Should().Be("#d9d9d9");
    }

    [Fact]
    public void From_palette_maps_derived_entries_onto_every_surface()
    {
        //Arrange + Act
        TkTheme theme = TkTheme.FromPalette(new[] { "blue" });

        //Assert — wish-verified derivation (007 fixture) mapped to fields.
        theme.Background.Should().Be("blue");
        theme.FieldBackground.Should().Be("blue");
        theme.Foreground.Should().Be("white");
        theme.ActiveBackground.Should().Be("#5555ff");
        theme.DisabledForeground.Should().Be("#3f3fff");
        theme.SelectBackground.Should().Be("#0000e6");
        theme.ListSelectBackground.Should().Be("#0000e6");
        theme.TroughColor.Should().Be("#0000e6");
        theme.MenuActiveBackground.Should().Be("#5555ff");
        theme.TitleBarBackground.Should().Be("#5555ff");
        theme.SelectColor.Should().Be("white");
    }

    [Fact]
    public void Bisque_theme_carries_the_legacy_palette()
    {
        //Arrange + Act
        TkTheme theme = TkTheme.CreateBisque();

        //Assert
        theme.Background.Should().Be("#ffe4c4");
        theme.Foreground.Should().Be("black");
        theme.ActiveBackground.Should().Be("#e6ceb1");
        theme.DisabledForeground.Should().Be("#b0b0b0");
        theme.SelectBackground.Should().Be("#e6ceb1");
        theme.TroughColor.Should().Be("#cdb79e");
    }

    [Fact]
    public void Registry_resolves_names_and_aliases()
    {
        //Arrange + Act + Assert — Default and the standard ttk names are
        // classic aliases; Bisque and the fifteen built-ins resolve too.
        TkThemeRegistry.TryCreate("Classic").Background.Should().Be("#d9d9d9");
        TkThemeRegistry.TryCreate("Default").Background.Should().Be("#d9d9d9");
        TkThemeRegistry.TryCreate("clam").Background.Should().Be("#d9d9d9");
        TkThemeRegistry.TryCreate("alt").Background.Should().Be("#d9d9d9");
        TkThemeRegistry.TryCreate("Bisque").Background.Should().Be("#ffe4c4");
        TkThemeRegistry.TryCreate("nosuchtheme").Should().BeNull();
        TkThemeRegistry.TryCreate("darkplus").Should().NotBeNull();
    }

    [Fact]
    public void Registry_carries_the_fifteen_built_in_schemes()
    {
        //Arrange
        var expected = new[]
        {
            "Abyss", "DarkModern", "DarkNew", "DarkPlus", "DimmedMonokai",
            "KimbieDark", "LightModern", "LightNew", "LightPlus", "Monokai",
            "QuietLight", "Red", "SolarizedDark", "SolarizedLight",
            "TomorrowNightBlue",
        };

        //Act
        IReadOnlyList<string> names = TkThemeRegistry.Names;

        //Assert — the fifteen built-ins plus Classic and Bisque.
        names.Should().Contain("Classic");
        names.Should().Contain("Bisque");
        foreach (string name in expected)
        {
            names.Should().Contain(name);
        }
    }

    [Fact]
    public void Every_built_in_theme_parses_every_field()
    {
        //Arrange + Act + Assert
        foreach (string name in TkThemeRegistry.Names)
        {
            TkTheme theme = TkThemeRegistry.TryCreate(name);
            foreach (System.Reflection.PropertyInfo property in typeof(TkTheme).GetProperties())
            {
                if (property.PropertyType != typeof(string) || property.Name == "Name") { continue; }
                string spec = (string)property.GetValue(theme);
                SKColor color;
                TkColor.TryParse(spec, out color)
                        .Should().BeTrue(name + "." + property.Name + " = \"" + spec + "\" must parse");
            }
        }
    }

    [Fact]
    public void Theme_switch_is_round_trippable_back_to_classic()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        root.Tree.Theme = TkThemeRegistry.TryCreate("DarkPlus");

        //Act — tk_classic applies the same registry entry the default uses.
        root.Tree.Theme = TkThemeRegistry.TryCreate("Classic");

        //Assert
        root.Tree.Theme.Background.Should().Be("#d9d9d9");
        root.Tree.Theme.FieldBackground.Should().Be("white");
    }

    [Fact]
    public void Set_palette_recolors_painted_pixels_and_classic_restores_them()
    {
        //Arrange — a packed frame rendered offscreen.
        TkWindow root = TkWindow.CreateRoot();
        TkWindow frameWindow = root.CreateChild("f");
        var frame = new FrameWidget(frameWindow);
        frame.Configure(new Dictionary<string, string> { { "-width", "50" }, { "-height", "50" } });
        PackLayout.Configure(frameWindow, new PackOptions());
        root.SetForcedSize(100, 100);
        TkLayout.Update(root);

        //Act + Assert — classic gray, then a palette recolor, then classic again.
        SamplePixel(root, 5, 5).Should().Be(new SKColor(0xD9, 0xD9, 0xD9));
        root.Tree.SetPalette(new[] { "#204060" });
        SamplePixel(root, 5, 5).Should().Be(new SKColor(0x20, 0x40, 0x60));
        root.Tree.Theme = TkTheme.CreateClassic();
        SamplePixel(root, 5, 5).Should().Be(new SKColor(0xD9, 0xD9, 0xD9));
    }

    private static SKColor SamplePixel(TkWindow root, int x, int y)
    {
        using (var surface = SKSurface.Create(new SKImageInfo(100, 100)))
        {
            TkRenderer.Render(root, surface.Canvas);
            using (SKImage image = surface.Snapshot())
            using (SKBitmap bitmap = SKBitmap.FromImage(image))
            {
                return bitmap.GetPixel(x, y);
            }
        }
    }
}
