using CodeBrix.Platform.TkCanvas.Rendering;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Tests for the shared 3D relief primitive: Tk shadow-color derivation and
/// pixel-level checks that each relief puts light and dark on the correct
/// edges (rendered into an offscreen surface).
/// </summary>
public class ReliefPainterTests
{
    private static readonly SKColor Background = new SKColor(0xD9, 0xD9, 0xD9);

    [Fact]
    public void Dark_shadow_is_sixty_percent_of_background()
    {
        //Act
        SKColor dark = ReliefPainter.DarkShadow(Background);

        //Assert
        dark.Red.Should().Be((byte)(0xD9 * 6 / 10));
        dark.Green.Should().Be((byte)(0xD9 * 6 / 10));
        dark.Blue.Should().Be((byte)(0xD9 * 6 / 10));
    }

    [Fact]
    public void Light_shadow_is_brighter_than_background()
    {
        //Act
        SKColor light = ReliefPainter.LightShadow(Background);

        //Assert
        (light.Red > Background.Red).Should().BeTrue();
        (light.Red <= 255).Should().BeTrue();
    }

    [Theory]
    [InlineData("raised", Relief.Raised)]
    [InlineData("sunken", Relief.Sunken)]
    [InlineData("groove", Relief.Groove)]
    [InlineData("ridge", Relief.Ridge)]
    [InlineData("solid", Relief.Solid)]
    [InlineData("flat", Relief.Flat)]
    [InlineData("bogus", Relief.Flat)]
    public void Parse_maps_tk_relief_names(string name, Relief expected) =>
        ReliefPainter.Parse(name).Should().Be(expected);

    private static SKBitmap Draw(Relief relief)
    {
        var bitmap = new SKBitmap(40, 40);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Background);
            ReliefPainter.DrawBorder(canvas, new SKRect(0, 0, 40, 40), 4, relief, Background);
        }
        return bitmap;
    }

    [Fact]
    public void Raised_border_is_light_topleft_dark_bottomright()
    {
        //Act
        using (SKBitmap bitmap = Draw(Relief.Raised))
        {
            //Assert
            bitmap.GetPixel(20, 1).Should().Be(ReliefPainter.LightShadow(Background));
            bitmap.GetPixel(1, 20).Should().Be(ReliefPainter.LightShadow(Background));
            bitmap.GetPixel(20, 38).Should().Be(ReliefPainter.DarkShadow(Background));
            bitmap.GetPixel(38, 20).Should().Be(ReliefPainter.DarkShadow(Background));
            bitmap.GetPixel(20, 20).Should().Be(Background);
        }
    }

    [Fact]
    public void Sunken_border_is_dark_topleft_light_bottomright()
    {
        //Act
        using (SKBitmap bitmap = Draw(Relief.Sunken))
        {
            //Assert
            bitmap.GetPixel(20, 1).Should().Be(ReliefPainter.DarkShadow(Background));
            bitmap.GetPixel(20, 38).Should().Be(ReliefPainter.LightShadow(Background));
        }
    }

    [Fact]
    public void Ridge_border_flips_halfway()
    {
        //Act
        using (SKBitmap bitmap = Draw(Relief.Ridge))
        {
            //Assert (outer half light, inner half dark on the top edge)
            bitmap.GetPixel(20, 0).Should().Be(ReliefPainter.LightShadow(Background));
            bitmap.GetPixel(20, 3).Should().Be(ReliefPainter.DarkShadow(Background));
        }
    }

    [Fact]
    public void Flat_border_draws_nothing()
    {
        //Act
        using (SKBitmap bitmap = Draw(Relief.Flat))
        {
            //Assert
            bitmap.GetPixel(20, 1).Should().Be(Background);
        }
    }
}
