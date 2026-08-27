using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Treeview painting: ttk hard-clips every cell to its column (no ellipsis),
/// so a long tree-column name never overruns the first value column.
/// </summary>
public class TreeviewWidgetTests
{
    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    private static SKBitmap Paint(IWidget widget)
    {
        TkWindow window = widget.Window;
        var bitmap = new SKBitmap(window.Width, window.Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            widget.Paint(canvas);
            canvas.Flush();
        }
        return bitmap;
    }

    private static int CountInkPixels(SKBitmap bitmap, SKColor background, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (bitmap.GetPixel(x, y) != background) { count++; }
            }
        }
        return count;
    }

    [Fact]
    public void Long_tree_column_text_is_clipped_at_the_first_value_column()
    {
        //Arrange — 300px wide, one 100px value column, so the tree column is
        //  the remaining ~200px; the item text is far wider than that.
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(300, 120);
        TkWindow w = root.CreateChild("tv");
        var tree = new TreeviewWidget(w);
        tree.Configure(Opts("-columns", "c1", "-show", "tree"));
        tree.Insert("", -1, new string('M', 120));
        PackLayout.Configure(w, new PackOptions { Fill = Fill.Both, Expand = true });
        TkLayout.Update(root);

        //Act
        using SKBitmap bitmap = Paint(tree);

        //Assert — the value-column band (right 100px, minus a margin for the
        //  inset/border) carries no ink at all; the tree column does.
        int width = w.Width;
        SKColor background = bitmap.GetPixel(width - 50, w.Height - 4);
        int valueInk = CountInkPixels(bitmap, background, width - 100 + 6, 6, width - 6, w.Height - 6);
        int treeInk = CountInkPixels(bitmap, background, 6, 6, width - 100 - 6, w.Height - 6);
        valueInk.Should().Be(0);
        treeInk.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Long_value_text_is_clipped_to_its_own_column()
    {
        //Arrange — two value columns; the first value overruns into the second
        //  unless clipped. The second value is empty, so its cell must be blank.
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(400, 120);
        TkWindow w = root.CreateChild("tv");
        var tree = new TreeviewWidget(w);
        tree.Configure(Opts("-columns", "c1 c2", "-show", "tree"));
        string id = tree.Insert("", -1, "x");
        tree.Item(id).Values.Add(new string('W', 80));
        tree.Item(id).Values.Add("");
        PackLayout.Configure(w, new PackOptions { Fill = Fill.Both, Expand = true });
        TkLayout.Update(root);

        //Act
        using SKBitmap bitmap = Paint(tree);

        //Assert — the last (empty) column band is ink-free.
        int width = w.Width;
        SKColor background = bitmap.GetPixel(width - 50, w.Height - 4);
        int lastColumnInk = CountInkPixels(bitmap, background, width - 100 + 6, 6, width - 6, w.Height - 6);
        int firstColumnInk = CountInkPixels(bitmap, background, width - 200 + 6, 6, width - 100 - 6, w.Height - 6);
        lastColumnInk.Should().Be(0);
        firstColumnInk.Should().BeGreaterThan(0);
    }
}
