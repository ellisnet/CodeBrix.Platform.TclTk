using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Canvas text-item tests. Text geometry flows through the toolkit's font
/// seam (the plan's R2), so it is deliberately NOT wish-oracled — real Tk
/// resolves a different font stack. These tests assert the layout contracts
/// instead: measurement-consistent bboxes, anchor arithmetic, wrapping, and
/// hit-testing against the item's own layout.
/// </summary>
public class TextItemTests
{
    private static CanvasWidget CreateCanvas(out TkWindow root)
    {
        root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("c");
        var canvas = new CanvasWidget(window);
        PackLayout.Configure(window, new PackOptions());
        TkLayout.Update(root);
        return canvas;
    }

    [Fact]
    public void Bbox_matches_font_measurement_for_single_line()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("text", new double[] { 100, 50 }, new Dictionary<string, string>
        {
            { "-text", "hello" },
            { "-anchor", "nw" },
        });

        //Act
        SkiaSharp.SKRectI? box = canvas.BBox(id.ToString());

        //Assert
        var fonts = root.Tree.Fonts;
        int width = fonts.Measure(fonts.Parse(""), "hello");
        int height = fonts.Metrics(fonts.Parse("")).LineSpace;
        box.HasValue.Should().BeTrue();
        box.Value.Left.Should().Be(100);
        box.Value.Top.Should().Be(50);
        box.Value.Right.Should().Be(100 + width);
        box.Value.Bottom.Should().Be(50 + height);
    }

    [Fact]
    public void Center_anchor_centers_the_layout_on_the_point()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int nw = canvas.Create("text", new double[] { 0, 0 }, new Dictionary<string, string>
        {
            { "-text", "hello" },
            { "-anchor", "nw" },
        });
        int centered = canvas.Create("text", new double[] { 0, 0 }, new Dictionary<string, string>
        {
            { "-text", "hello" },
        });

        //Act
        SkiaSharp.SKRectI nwBox = canvas.BBox(nw.ToString()).Value;
        SkiaSharp.SKRectI centerBox = canvas.BBox(centered.ToString()).Value;

        //Assert (the centered box straddles the anchor point)
        centerBox.Left.Should().BeLessThan(0);
        centerBox.Top.Should().BeLessThan(0);
        (centerBox.Right - centerBox.Left).Should().Be(nwBox.Right - nwBox.Left);
        (centerBox.Bottom - centerBox.Top).Should().Be(nwBox.Bottom - nwBox.Top);
    }

    [Fact]
    public void Newlines_produce_multiple_lines()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("text", new double[] { 0, 0 }, new Dictionary<string, string>
        {
            { "-text", "one\ntwo\nthree" },
            { "-anchor", "nw" },
        });

        //Act
        TextItem item = FindText(canvas, id);

        //Assert
        item.Lines.Should().Equal(new[] { "one", "two", "three" });
        var fonts = root.Tree.Fonts;
        int lineHeight = fonts.Metrics(fonts.Parse("")).LineSpace;
        SkiaSharp.SKRectI box = canvas.BBox(id.ToString()).Value;
        (box.Bottom - box.Top).Should().Be(3 * lineHeight);
    }

    [Fact]
    public void Wrap_width_breaks_at_spaces()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        var fonts = root.Tree.Fonts;
        int wordWidth = fonts.Measure(fonts.Parse(""), "word word");

        //Act (a wrap width that fits two words per line)
        int id = canvas.Create("text", new double[] { 0, 0 }, new Dictionary<string, string>
        {
            { "-text", "word word word word" },
            { "-anchor", "nw" },
            { "-width", wordWidth.ToString() },
        });
        TextItem item = FindText(canvas, id);

        //Assert
        item.Lines.Count.Should().Be(2);
        item.Lines[0].Should().Be("word word");
        item.Lines[1].Should().Be("word word");
    }

    [Fact]
    public void Hit_testing_uses_the_layout_box()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("text", new double[] { 100, 50 }, new Dictionary<string, string>
        {
            { "-text", "hello world" },
            { "-anchor", "nw" },
        });

        //Act + Assert (inside the layout box)
        canvas.FindClosest(105, 55).Id.Should().Be(id);
        TextItem item = FindText(canvas, id);
        item.DistanceTo(105, 55).Should().Be(0.0);
        item.DistanceTo(100, 40).Should().BeGreaterThan(5.0);
    }

    [Fact]
    public void Coords_require_exactly_one_point() =>
        Assert.Throws<System.ArgumentException>(() =>
        {
            TkWindow root;
            CanvasWidget canvas = CreateCanvas(out root);
            canvas.Create("text", new double[] { 1, 2, 3 });
        });

    private static TextItem FindText(CanvasWidget canvas, int id)
    {
        foreach (ICanvasItem item in canvas.FindWithTag(id.ToString()))
        {
            return (TextItem)item;
        }
        return null;
    }
}
