using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Text widget engine tests. Every index/mark/tag expectation below was
/// probed against REAL Tk 8.6.16 (<c>wish</c>) before being asserted here —
/// the same oracle discipline as the interpreter work; the probe scripts
/// live with the session summary. Rendering-adjacent behavior (layout,
/// wrapping, view) is asserted against the widget's own font seam.
/// </summary>
public class TextWidgetTests
{
    private static TextWidget CreateText(out TkWindow root)
    {
        root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("t");
        var text = new TextWidget(window);
        PackLayout.Configure(window, new PackOptions());
        TkLayout.Update(root);
        return text;
    }

    private static TextWidget CreateFilled(out TkWindow root)
    {
        TextWidget text = CreateText(out root);
        text.Insert("1.0", "hello\nworld wide\nweb");
        return text;
    }

    [Fact]
    public void Empty_widget_end_is_2_0()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateText(out root);

        //Assert (wish: index end => 2.0)
        text.Index("end").Should().Be("2.0");
    }

    [Fact]
    public void Insert_and_get_round_trip_with_final_newline()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);

        //Assert (wish: end => 4.0; get includes the final newline)
        text.Index("end").Should().Be("4.0");
        text.Get("1.0", "end").Should().Be("hello\nworld wide\nweb\n");
        text.Get("2.0", "2.5").Should().Be("world");
        text.Get("1.4", "2.1").Should().Be("o\nw");
    }

    [Theory]
    [InlineData("1.99", "1.5")]
    [InlineData("99.0", "4.0")]
    [InlineData("1.3 + 5 chars", "2.2")]
    [InlineData("1.5 + 3 chars", "2.2")]
    [InlineData("2.2 - 4 chars", "1.4")]
    [InlineData("2.7 linestart", "2.0")]
    [InlineData("2.7 lineend", "2.10")]
    [InlineData("2.8 wordstart", "2.6")]
    [InlineData("2.8 wordend", "2.10")]
    [InlineData("2.5 wordstart", "2.5")]
    [InlineData("1.4 + 2 lines", "3.3")]
    [InlineData("end - 1 chars", "3.3")]
    [InlineData("2.end", "2.10")]
    public void Index_expressions_match_real_tk(string expression, string expected)
    {
        //Arrange (wish-probed values, buffer "hello\nworld wide\nweb")
        TkWindow root;
        TextWidget text = CreateFilled(out root);

        //Assert
        text.Index(expression).Should().Be(expected);
    }

    [Fact]
    public void Mark_gravity_controls_movement_on_insert_at_the_mark()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.MarkSet("m1", "2.3");
        text.MarkGravity("m1", "left");
        text.MarkSet("m2", "2.3");

        //Act
        text.Insert("2.3", "XX");

        //Assert (wish: left stays at 2.3, right moves to 2.5)
        text.Index("m1").Should().Be("2.3");
        text.Index("m2").Should().Be("2.5");
    }

    [Fact]
    public void Tag_ranges_stretch_inside_but_not_at_boundaries()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.TagAdd("big", "2.2", "2.8");

        //Act + Assert (wish-probed boundary rules)
        text.Insert("2.4", "YY");
        text.TagRanges("big").Should().Equal(new[] { "2.2", "2.10" });

        text.Insert("2.2", "Z");
        text.TagRanges("big").Should().Equal(new[] { "2.3", "2.11" });

        //Arrange (insert exactly AT the end boundary: range must not grow)
        text.TagRemove("big", "1.0", "end");
        text.TagAdd("big", "2.2", "2.8");
        text.Insert("2.8", "QQ");
        text.TagRanges("big").Should().Equal(new[] { "2.2", "2.8" });
    }

    [Fact]
    public void Delete_shrinks_tags_and_collapses_marks()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.TagAdd("big", "2.2", "2.8");
        text.MarkSet("inner", "2.6");

        //Act
        text.Delete("2.3", "2.6");

        //Assert (wish: range 2.3 2.5; the mark collapses to the range start)
        text.TagRanges("big").Should().Equal(new[] { "2.2", "2.5" });
        text.Index("inner").Should().Be("2.3");
    }

    [Fact]
    public void Line_merge_delete_remaps_tags()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.TagAdd("big", "2.3", "2.8");

        //Act (delete across the newline merges lines 1 and 2)
        text.Delete("1.3", "2.2");

        //Assert (wish: end 3.0; ranges remap to the merged line)
        text.Index("end").Should().Be("3.0");
        text.Get("1.0", "end").Should().Be("helrld wide\nweb\n");
        text.TagRanges("big").Should().Equal(new[] { "1.4", "1.9" });
    }

    [Fact]
    public void Adjacent_tag_ranges_merge()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);

        //Act
        text.TagAdd("s2", "1.0", "1.2");
        text.TagAdd("s2", "1.2", "1.5");

        //Assert (wish: one merged range)
        text.TagRanges("s2").Should().Equal(new[] { "1.0", "1.5" });
    }

    [Fact]
    public void Tag_names_start_with_sel_and_follow_creation_order()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);

        //Act
        text.TagAdd("big", "1.0", "1.2");
        text.TagConfigure("other", new Dictionary<string, string> { { "-foreground", "red" } });

        //Assert (wish: sel first, then creation order)
        text.TagNames().Should().Equal(new[] { "sel", "big", "other" });

        //Act
        text.TagDelete("other");

        //Assert
        text.TagNames().Should().Equal(new[] { "sel", "big" });
    }

    [Fact]
    public void Insert_with_tags_applies_them_to_the_inserted_range()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);

        //Act
        text.Insert("1.0", "x", new[] { "big" });

        //Assert (wish: range 1.0 1.1)
        text.TagRanges("big").Should().Equal(new[] { "1.0", "1.1" });
    }

    [Fact]
    public void Multiline_insert_shifts_marks_and_tags_by_lines()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.TagAdd("big", "2.2", "2.8");
        text.MarkSet("m9", "3.2");

        //Act
        text.Insert("1.0", "A\nB\n");

        //Assert (wish-probed)
        text.Index("m9").Should().Be("5.2");
        text.TagRanges("big").Should().Equal(new[] { "4.2", "4.8" });
    }

    [Fact]
    public void See_and_yview_scroll_the_display()
    {
        //Arrange (30 lines in a 24-line-tall widget)
        TkWindow root;
        TextWidget text = CreateText(out root);
        for (int i = 1; i <= 30; i++)
        {
            text.Insert("end", "line " + i + "\n");
        }

        double first, last;
        text.YViewFractions(out first, out last);
        first.Should().Be(0.0);

        //Act (jump to the bottom)
        text.See("30.0");

        //Assert
        text.YViewFractions(out first, out last);
        (first > 0).Should().BeTrue();

        //Act
        text.YViewMoveTo(0);
        text.YViewFractions(out first, out last);

        //Assert
        first.Should().Be(0.0);

        //Act
        text.YViewScroll(3, false);
        text.YViewFractions(out first, out last);

        //Assert
        (first > 0).Should().BeTrue();
    }

    [Fact]
    public void Click_sets_caret_and_focus_and_drag_selects()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        int inset = 3; // borderwidth 1 + padx 1 + highlightthickness 0 = 2; +1 into the glyph
        var fonts = root.Tree.Fonts;
        var font = fonts.GetNamed("TkFixedFont");
        int lineHeight = fonts.Metrics(font).LineSpace;

        // Aim the drag at the MIDDLE of the space glyph that follows "world" on
        // line 2 ("world wide"), measured with the same FontManager the widget
        // hit-tests against. Deriving the x from 5 * Measure("0") assumes a strict
        // monospace grid; TkFixedFont resolves to different fonts across platforms
        // (Linux vs Windows), so that grid drifts and the drag lands a column off.
        // The gap-midpoint is unambiguous under any font's actual glyph advances.
        int worldRight = fonts.Measure(font, "world");
        int worldSpaceRight = fonts.Measure(font, "world ");
        int dragX = inset + (worldRight + worldSpaceRight) / 2;

        //Act (click at the start of line 2, drag to after "world")
        int y = inset + lineHeight + lineHeight / 2;
        root.Tree.PointerEvent(TkEventType.ButtonPress, inset, y, 1);

        //Assert
        text.Index("insert").Should().Be("2.0");
        root.Tree.FocusWindow.Should().Be(text.Window);

        //Act
        root.Tree.PointerEvent(TkEventType.Motion, dragX, y, 0,
                EventModifiers.Button1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, dragX, y, 1);

        //Assert
        text.TagRanges("sel").Should().Equal(new[] { "2.0", "2.5" });
        text.Get("sel.first", "sel.last").Should().Be("world");
    }

    [Fact]
    public void Typing_replaces_the_selection()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        text.GetTag("sel").ClearRanges();
        text.TagAdd("sel", "2.0", "2.5");
        text.MarkSet("insert", "2.5");

        //Act (what the input sink does on commit)
        text.InsertAtCaret("WORLD");

        //Assert
        text.Get("2.0", "2.5").Should().Be("WORLD");
        text.TagRanges("sel").Should().BeEmpty();
        text.Index("insert").Should().Be("2.5");
    }

    [Fact]
    public void Word_wrap_breaks_lines_at_spaces()
    {
        //Arrange (a widget 10 characters wide with word wrapping)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("t");
        var text = new TextWidget(window);
        text.Configure(new Dictionary<string, string>
        {
            { "-width", "10" },
            { "-height", "5" },
            { "-wrap", "word" },
        });
        PackLayout.Configure(window, new PackOptions());
        TkLayout.Update(root);

        //Act
        text.Insert("1.0", "aaa bbb ccc ddd eee");

        //Assert (positions on wrapped segments resolve through @x,y)
        var fonts = root.Tree.Fonts;
        var font = fonts.GetNamed("TkFixedFont");
        int lineHeight = fonts.Metrics(font).LineSpace;
        TextPosition second = text.PositionAtPoint(3, 2 + lineHeight + lineHeight / 2);
        (second.Char > 0).Should().BeTrue();
        second.Line.Should().Be(1);
    }

    [Fact]
    public void Input_sink_receives_caret_updates()
    {
        //Arrange
        TkWindow root;
        TextWidget text = CreateFilled(out root);
        var sink = new RecordingSink();
        text.InputSink = sink;

        //Act
        text.MarkSet("insert", "2.3");

        //Assert
        sink.CaretUpdates.Should().BeGreaterThan(0);
        (sink.LastY > 0).Should().BeTrue();
    }

    private sealed class RecordingSink : ITextInputSink
    {
        public int CaretUpdates;

        public int LastX;

        public int LastY;

        public void Attach(ITextInputTarget target)
        {
        }

        public void Detach()
        {
        }

        public void UpdateCaret(int x, int y, int height)
        {
            CaretUpdates++;
            LastX = x;
            LastY = y;
        }
    }
}
