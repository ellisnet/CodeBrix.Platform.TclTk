using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless coverage for the B.5b canvas completion pass: the full nine-type
/// item registry, and the behaviour of the anchor-positioned items
/// (<c>bitmap</c>/<c>image</c>/<c>window</c>) and tag expressions that the
/// wish oracle does not (or cannot, without resources) cover.
/// </summary>
public class CanvasItemsB5bTests
{
    private static CanvasWidget CreateCanvas(out TkWindow root)
    {
        root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("c");
        var canvas = new CanvasWidget(window);
        canvas.Configure(new Dictionary<string, string>
        {
            { "-width", "300" },
            { "-height", "200" },
            { "-highlightthickness", "0" },
            { "-borderwidth", "0" },
        });
        PackLayout.Configure(window, new PackOptions());
        TkLayout.Update(root);
        return canvas;
    }

    [Theory]
    [InlineData("line", "10 10 20 20")]
    [InlineData("rectangle", "10 10 20 20")]
    [InlineData("polygon", "10 10 20 10 15 20")]
    [InlineData("oval", "10 10 20 20")]
    [InlineData("arc", "10 10 20 20")]
    [InlineData("bitmap", "10 10")]
    [InlineData("image", "10 10")]
    [InlineData("window", "10 10")]
    public void All_item_types_create_and_report_their_type(string type, string coords)
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        var create = new List<string> { "create", type };
        create.AddRange(coords.Split(' '));

        //Act
        string id = canvas.Execute(create);

        //Assert
        canvas.Execute(new List<string> { "type", id }).Should().Be(type);
    }

    [Fact]
    public void Type_prefix_resolves_unambiguously()
    {
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Execute(new List<string> { "create", "rect", "10", "10", "20", "20" })
                .Should().Be("1");
        canvas.Execute(new List<string> { "type", "1" }).Should().Be("rectangle");
    }

    [Fact]
    public void Window_item_bbox_uses_width_height_and_anchor()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);

        //Act — nw anchor puts the top-left at the point.
        canvas.Execute(new List<string>
        {
            "create", "window", "100", "50",
            "-width", "40", "-height", "20", "-anchor", "nw",
        });

        //Assert
        canvas.Execute(new List<string> { "bbox", "1" }).Should().Be("100 50 140 70");
    }

    [Fact]
    public void Window_item_center_anchor_is_default()
    {
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Execute(new List<string>
        {
            "create", "window", "100", "50", "-width", "40", "-height", "20",
        });
        // center anchor: 100-40/2 .. , 50-20/2 ..
        canvas.Execute(new List<string> { "bbox", "1" }).Should().Be("80 40 120 60");
    }

    [Fact]
    public void Bitmap_and_image_items_collapse_to_point_without_resource()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);

        //Act
        canvas.Execute(new List<string> { "create", "bitmap", "30", "40", "-anchor", "nw" });
        canvas.Execute(new List<string> { "create", "image", "70", "80" });

        //Assert — no backing resource → zero-area box, which Tk's bbox skips
        // (returns empty), while the item keeps its coordinates and type.
        canvas.Execute(new List<string> { "bbox", "1" }).Should().Be("");
        canvas.Execute(new List<string> { "bbox", "2" }).Should().Be("");
        canvas.Execute(new List<string> { "coords", "1" }).Should().Be("30.0 40.0");
        canvas.Execute(new List<string> { "type", "2" }).Should().Be("image");
    }

    [Fact]
    public void Deferred_item_paint_and_unknown_options_never_throw()
    {
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        // Unknown-but-valid options are accepted and stored.
        canvas.Execute(new List<string>
        {
            "create", "window", "10", "10", "-window", ".x", "-madeup", "value",
        });
        canvas.Execute(new List<string> { "itemcget", "1", "-madeup" }).Should().Be("value");
    }

    [Fact]
    public void Oval_find_closest_prefers_topmost_within_halo()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Execute(new List<string> { "create", "oval", "10", "10", "60", "60", "-fill", "red" });
        canvas.Execute(new List<string> { "create", "oval", "10", "10", "60", "60", "-fill", "blue" });

        //Act — a point inside both filled ovals → the topmost (id 2).
        string hit = canvas.Execute(new List<string> { "find", "closest", "35", "35" });

        //Assert
        hit.Should().Be("2");
    }

    [Fact]
    public void Arc_styles_are_parsed_and_reported()
    {
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Execute(new List<string> { "create", "arc", "0", "0", "50", "50", "-style", "chord" });
        canvas.Execute(new List<string> { "create", "arc", "0", "0", "50", "50", "-style", "arc" });
        canvas.Execute(new List<string> { "create", "arc", "0", "0", "50", "50" });

        canvas.Execute(new List<string> { "itemcget", "1", "-style" }).Should().Be("chord");
        canvas.Execute(new List<string> { "itemcget", "2", "-style" }).Should().Be("arc");
        canvas.Execute(new List<string> { "itemcget", "3", "-style" }).Should().Be("pieslice");
    }

    [Fact]
    public void Tag_expression_not_and_xor_select_expected_items()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Execute(new List<string> { "create", "rectangle", "0", "0", "10", "10", "-tags", "a b" });
        canvas.Execute(new List<string> { "create", "rectangle", "0", "0", "10", "10", "-tags", "b" });
        canvas.Execute(new List<string> { "create", "rectangle", "0", "0", "10", "10", "-tags", "a" });

        //Act + Assert
        canvas.Execute(new List<string> { "find", "withtag", "a&&b" }).Should().Be("1");
        canvas.Execute(new List<string> { "find", "withtag", "!a" }).Should().Be("2");
        canvas.Execute(new List<string> { "find", "withtag", "a^b" }).Should().Be("2 3");
        canvas.Execute(new List<string> { "find", "withtag", "(a||b)" }).Should().Be("1 2 3");
    }
}
