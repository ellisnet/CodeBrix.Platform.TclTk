using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The ttk::style engine's widget-facing behavior (B.12c): styles resolve
/// into painted widget colors by class (TButton, ...), the per-widget
/// <c>-style</c> option redirects resolution, explicit options beat styles,
/// and the deferred element/layout subcommands accept-and-no-op. The lookup
/// semantics themselves are pinned by the ThemingOracle fixtures.
/// </summary>
public class TtkStyleEngineTests
{
    [Fact]
    public void Style_configure_recolors_a_button_by_class()
    {
        //Arrange
        TkWindow root = BuildButtonTree(out ButtonWidget _);

        //Act
        root.Tree.Styles.Configure("TButton", "-background", "#112233");

        //Assert
        SamplePixel(root, 10, 10).Should().Be(new SKColor(0x11, 0x22, 0x33));
    }

    [Fact]
    public void Explicit_option_beats_the_style()
    {
        //Arrange
        TkWindow root = BuildButtonTree(out ButtonWidget button);
        root.Tree.Styles.Configure("TButton", "-background", "#112233");

        //Act
        button.Configure(new Dictionary<string, string> { { "-background", "#445566" } });

        //Assert
        SamplePixel(root, 10, 10).Should().Be(new SKColor(0x44, 0x55, 0x66));
    }

    [Fact]
    public void The_style_option_redirects_resolution()
    {
        //Arrange
        TkWindow root = BuildButtonTree(out ButtonWidget button);
        root.Tree.Styles.Configure("Fancy.TButton", "-background", "#665544");

        //Act
        button.Configure(new Dictionary<string, string> { { "-style", "Fancy.TButton" } });

        //Assert
        SamplePixel(root, 10, 10).Should().Be(new SKColor(0x66, 0x55, 0x44));
    }

    [Fact]
    public void Root_style_reaches_every_class()
    {
        //Arrange
        TkWindow root = BuildButtonTree(out ButtonWidget _);

        //Act
        root.Tree.Styles.Configure(".", "-background", "#0a0b0c");

        //Assert
        SamplePixel(root, 10, 10).Should().Be(new SKColor(0x0A, 0x0B, 0x0C));
    }

    [Fact]
    public void Execute_covers_the_command_shapes()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        Theming.TtkStyleEngine styles = root.Tree.Styles;

        //Act + Assert
        styles.Execute(new[] { "configure", "TButton", "-background", "#123456" }).Should().Be("");
        styles.Execute(new[] { "configure", "TButton", "-background" }).Should().Be("#123456");
        styles.Execute(new[] { "map", "TButton", "-background", "active #654321" }).Should().Be("");
        styles.Execute(new[] { "map", "TButton", "-background" }).Should().Be("active #654321");
        styles.Execute(new[] { "lookup", "TButton", "-background", "active" }).Should().Be("#654321");
        styles.Execute(new[] { "theme", "use" }).Should().Be("default");
        styles.Execute(new[] { "theme", "names" }).Should().Contain("classic");
        styles.Execute(new[] { "element", "names" }).Should().Be("");
        styles.Execute(new[] { "layout", "TButton" }).Should().Be("");
    }

    private static TkWindow BuildButtonTree(out ButtonWidget button)
    {
        TkWindow root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("b");
        button = new ButtonWidget(window);
        button.Configure(new Dictionary<string, string>
        {
            { "-borderwidth", "0" }, { "-highlightthickness", "0" },
            { "-width", "5" }, { "-text", "x" },
        });
        PackLayout.Configure(window, new PackOptions { Fill = Fill.Both, Expand = true });
        root.SetForcedSize(60, 60);
        TkLayout.Update(root);
        return root;
    }

    private static SKColor SamplePixel(TkWindow root, int x, int y)
    {
        using (var surface = SKSurface.Create(new SKImageInfo(60, 60)))
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
