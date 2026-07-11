using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Images;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.10b coverage: the <c>-image</c> option on its DRAKON-verified consumers
/// — label and button (image replaces text; -width/-height become pixel
/// sizes), the canvas <c>image</c> item (anchored geometry + painting),
/// treeview items, and menu entries (drawn left of the label, widening the
/// entry). Unresolvable names always fall back gracefully.
/// </summary>
public class ImageWidgetsTests
{
    private static string AssetPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Images", name);
    }

    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(300, 200);
        return root;
    }

    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    private static PhotoImage LoadBackGif(TkWindow root, string name)
    {
        return root.Tree.Images.CreatePhoto(name, Opts("-file", AssetPath("back.gif")));
    }

    private static SKColor PaintAndSample(IWidget widget, int x, int y)
    {
        TkWindow window = widget.Window;
        using (var bitmap = new SKBitmap(window.Width, window.Height))
        using (var canvas = new SKCanvas(bitmap))
        {
            widget.Paint(canvas);
            canvas.Flush();
            return bitmap.GetPixel(x, y);
        }
    }

    // ------------------------------------------------------------------
    // Label / button
    // ------------------------------------------------------------------

    [Fact]
    public void Label_with_image_requests_image_size_plus_padding_and_inset()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        var label = new LabelWidget(root.CreateChild("l"));

        label.Configure(Opts("-image", "icon"));

        // 16x16 image + 2*padx(1) + 2*inset(bd 1) = 20.
        root.Tree.Root.FindDescendant(".l").RequestedWidth.Should().Be(20);
        root.Tree.Root.FindDescendant(".l").RequestedHeight.Should().Be(20);
    }

    [Fact]
    public void Label_image_width_height_are_pixel_sizes_not_characters()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        var label = new LabelWidget(root.CreateChild("l"));

        label.Configure(Opts("-image", "icon", "-width", "50", "-height", "30"));

        root.Tree.Root.FindDescendant(".l").RequestedWidth.Should().Be(54);
        root.Tree.Root.FindDescendant(".l").RequestedHeight.Should().Be(34);
    }

    [Fact]
    public void Label_paints_the_image_pixels()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        TkWindow window = root.CreateChild("l");
        var label = new LabelWidget(window);
        label.Configure(Opts("-image", "icon"));
        CodeBrix.Platform.TkCanvas.Layout.PackLayout.Configure(
                window, new CodeBrix.Platform.TkCanvas.Layout.PackOptions());
        root.Tree.Scheduler.UpdateIdleTasks();

        // Center of the label = center of the 16x16 gif; back.gif (8,8) is
        // black per the wish fixture.
        SKColor pixel = PaintAndSample(label, 10, 10);
        pixel.Red.Should().Be(0);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    [Fact]
    public void Label_with_unresolvable_image_falls_back_to_text()
    {
        TkWindow root = Root();
        var label = new LabelWidget(root.CreateChild("l"));

        Action act = () => label.Configure(Opts("-image", "nosuch", "-text", "hello"));

        act.Should().NotThrow();
        root.Tree.Root.FindDescendant(".l").RequestedWidth.Should().BeGreaterThan(20);
    }

    [Fact]
    public void Button_with_image_requests_image_size_plus_padding_and_inset()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        var button = new ButtonWidget(root.CreateChild("b"));

        button.Configure(Opts("-image", "icon"));

        // 16 + 2*padx(4) + 2*(bd 2 + highlight 1) = 30 wide;
        // 16 + 2*pady(2) + 6 = 26 high.
        root.Tree.Root.FindDescendant(".b").RequestedWidth.Should().Be(30);
        root.Tree.Root.FindDescendant(".b").RequestedHeight.Should().Be(26);
    }

    // ------------------------------------------------------------------
    // Canvas image item
    // ------------------------------------------------------------------

    [Fact]
    public void Canvas_image_item_bounds_follow_the_anchored_photo()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        TkWindow canvasWindow = root.CreateChild("c");
        var canvas = new CanvasWidget(canvasWindow);

        canvas.Execute(new[] { "create", "image", "100", "50", "-image", "icon" });

        canvas.Execute(new[] { "bbox", "1" }).Should().Be("92 42 108 58");

        canvas.Execute(new[] { "itemconfigure", "1", "-anchor", "nw" });
        canvas.Execute(new[] { "bbox", "1" }).Should().Be("100 50 116 66");
    }

    [Fact]
    public void Canvas_image_item_without_image_collapses_to_its_point()
    {
        TkWindow root = Root();
        TkWindow canvasWindow = root.CreateChild("c");
        var canvas = new CanvasWidget(canvasWindow);

        Action act = () => canvas.Execute(new[] { "create", "image", "10", "20", "-image", "nosuch" });

        act.Should().NotThrow();
        // Tk's bbox SKIPS zero-area items (tkCanvas.c: x1 >= x2), so a
        // sizeless image item contributes nothing.
        canvas.Execute(new[] { "bbox", "1" }).Should().Be("");
    }

    [Fact]
    public void Canvas_image_item_paints_the_photo()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        TkWindow canvasWindow = root.CreateChild("c");
        var canvas = new CanvasWidget(canvasWindow);
        canvas.Configure(Opts("-width", "60", "-height", "60"));
        CodeBrix.Platform.TkCanvas.Layout.PackLayout.Configure(
                canvasWindow, new CodeBrix.Platform.TkCanvas.Layout.PackOptions());
        root.Tree.Scheduler.UpdateIdleTasks();
        canvas.Execute(new[] { "create", "image", "30", "30", "-image", "icon" });

        // back.gif (8,8) is black; the item is centred at (30,30), so photo
        // pixel (8,8) lands at canvas (30,30).
        SKColor pixel = PaintAndSample(canvas, 30, 30);
        pixel.Red.Should().Be(0);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Treeview item image + menu entry image
    // ------------------------------------------------------------------

    [Fact]
    public void Treeview_item_image_paints_without_error()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        TkWindow treeWindow = root.CreateChild("t");
        var tree = new TreeviewWidget(treeWindow);
        string id = tree.Insert("", -1, "diagram");
        tree.Item(id).Image = "icon";
        CodeBrix.Platform.TkCanvas.Layout.PackLayout.Configure(
                treeWindow, new CodeBrix.Platform.TkCanvas.Layout.PackOptions());
        root.Tree.Scheduler.UpdateIdleTasks();

        Action act = () => PaintAndSample(tree, 5, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void Menu_entry_image_widens_the_menubar_entry()
    {
        TkWindow root = Root();
        LoadBackGif(root, "icon");
        TkWindow plainWindow = root.CreateChild("m1");
        var plain = new MenuWidget(plainWindow);
        plain.Configure(Opts("-type", "menubar"));
        plain.AddCommand("File");

        TkWindow iconWindow = root.CreateChild("m2");
        var withIcon = new MenuWidget(iconWindow);
        withIcon.Configure(Opts("-type", "menubar"));
        withIcon.AddCommand("File");
        withIcon.Entries[0].Image = "icon";
        withIcon.Entries[0].Compound = "left";

        int plainWidth = plain.EntryRect(0).Width;
        int iconWidth = withIcon.EntryRect(0).Width;
        iconWidth.Should().Be(plainWidth + 16 + 4);
    }
}
