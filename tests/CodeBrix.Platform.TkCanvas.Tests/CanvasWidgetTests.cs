using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless tests for the canvas widget machinery the wish oracle cannot
/// capture: current-item picking with Enter/Leave and the implicit
/// button-hold grab, item-binding dispatch order, focus-item key routing,
/// scroll notifications, and the accept-and-no-op deferral surface.
/// </summary>
public class CanvasWidgetTests
{
    private static CanvasWidget CreateCanvas(out TkWindow root, int width = 200, int height = 100)
    {
        root = TkWindow.CreateRoot();
        TkWindow window = root.CreateChild("c");
        var canvas = new CanvasWidget(window);
        canvas.Configure(new Dictionary<string, string>
        {
            { "-width", width.ToString() },
            { "-height", height.ToString() },
            { "-highlightthickness", "0" },
            { "-borderwidth", "0" },
        });
        PackLayout.Configure(window, new PackOptions());
        TkLayout.Update(root);
        return canvas;
    }

    [Fact]
    public void Pointer_motion_picks_topmost_item_and_sets_current_tag()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int below = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 },
                new Dictionary<string, string> { { "-fill", "red" } });
        int above = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 },
                new Dictionary<string, string> { { "-fill", "blue" } });

        //Act
        root.Tree.PointerEvent(TkEventType.Motion, 30, 30);

        //Assert
        canvas.CurrentItem.Id.Should().Be(above);
        canvas.GetTags(above.ToString()).Should().Contain("current");
        canvas.GetTags(below.ToString()).Should().NotContain("current");
    }

    [Fact]
    public void Pointer_move_between_items_fires_item_leave_then_enter()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Create("rectangle", new double[] { 10, 10, 40, 40 },
                new Dictionary<string, string> { { "-fill", "red" }, { "-tags", "left" } });
        canvas.Create("rectangle", new double[] { 60, 10, 90, 40 },
                new Dictionary<string, string> { { "-fill", "red" }, { "-tags", "right" } });
        var log = new List<string>();
        canvas.BindItem("left", "<Enter>", e => { log.Add("enter-left"); return DispatchResult.Continue; });
        canvas.BindItem("left", "<Leave>", e => { log.Add("leave-left"); return DispatchResult.Continue; });
        canvas.BindItem("right", "<Enter>", e => { log.Add("enter-right"); return DispatchResult.Continue; });

        //Act
        root.Tree.PointerEvent(TkEventType.Motion, 20, 20);
        root.Tree.PointerEvent(TkEventType.Motion, 70, 20);

        //Assert
        log.Should().Equal(new[] { "enter-left", "leave-left", "enter-right" });
    }

    [Fact]
    public void Item_bindings_fire_all_then_tags_then_id_and_break_stops()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 },
                new Dictionary<string, string> { { "-fill", "red" }, { "-tags", "a b" } });
        var log = new List<string>();
        canvas.BindItem("all", "<ButtonPress-1>", e => { log.Add("all"); return DispatchResult.Continue; });
        canvas.BindItem("a", "<ButtonPress-1>", e => { log.Add("a"); return DispatchResult.Continue; });
        canvas.BindItem("b", "<ButtonPress-1>", e => { log.Add("b"); return DispatchResult.Continue; });
        canvas.BindItem(id.ToString(), "<ButtonPress-1>", e => { log.Add("id"); return DispatchResult.Continue; });

        //Act
        root.Tree.PointerEvent(TkEventType.ButtonPress, 30, 30, 1);

        //Assert
        log.Should().Equal(new[] { "all", "a", "b", "id" });

        //Arrange (break in a tag binding stops later tags and the id)
        log.Clear();
        canvas.BindItem("a", "<ButtonPress-1>", e => { log.Add("a"); return DispatchResult.Break; });
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 30, 30, 1);

        //Act
        root.Tree.PointerEvent(TkEventType.ButtonPress, 30, 30, 1);

        //Assert
        log.Should().Equal(new[] { "all", "a" });
    }

    [Fact]
    public void Current_item_is_frozen_while_a_button_is_held()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int left = canvas.Create("rectangle", new double[] { 10, 10, 40, 40 },
                new Dictionary<string, string> { { "-fill", "red" } });
        int right = canvas.Create("rectangle", new double[] { 60, 10, 90, 40 },
                new Dictionary<string, string> { { "-fill", "red" } });

        //Act (press on the left item, drag over the right one)
        root.Tree.PointerEvent(TkEventType.ButtonPress, 20, 20, 1);
        root.Tree.PointerEvent(TkEventType.Motion, 70, 20, 0, EventModifiers.Button1);

        //Assert (Tk defers the repick while the button is down)
        canvas.CurrentItem.Id.Should().Be(left);

        //Act (release: the repick happens)
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 70, 20, 1);

        //Assert
        canvas.CurrentItem.Id.Should().Be(right);
    }

    [Fact]
    public void Close_enough_halo_picks_nearby_thin_line()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Configure(new Dictionary<string, string> { { "-closeenough", "3" } });
        int line = canvas.Create("line", new double[] { 10, 50, 190, 50 });

        //Act (3 pixels above the line: inside the halo)
        root.Tree.PointerEvent(TkEventType.Motion, 100, 47);

        //Assert
        canvas.CurrentItem.Id.Should().Be(line);

        //Act (7 pixels away: outside the halo)
        root.Tree.PointerEvent(TkEventType.Motion, 100, 43);

        //Assert
        canvas.CurrentItem.Should().BeNull();
    }

    [Fact]
    public void Disabled_items_are_never_picked()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Create("rectangle", new double[] { 10, 10, 60, 60 },
                new Dictionary<string, string> { { "-fill", "red" }, { "-state", "disabled" } });

        //Act
        root.Tree.PointerEvent(TkEventType.Motion, 30, 30);

        //Assert
        canvas.CurrentItem.Should().BeNull();
    }

    [Fact]
    public void Key_events_route_to_the_focus_item()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 });
        var log = new List<string>();
        canvas.BindItem(id.ToString(), "<KeyPress-Return>", e => { log.Add("return"); return DispatchResult.Continue; });
        canvas.SetFocusItem(id.ToString());
        canvas.Window.Focusable = true;
        root.Tree.SetFocus(canvas.Window);

        //Act
        root.Tree.KeyEvent(TkEventType.KeyPress, "Return");

        //Assert
        log.Should().Equal(new[] { "return" });
    }

    [Fact]
    public void Scroll_notifications_fire_when_the_origin_moves()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Configure(new Dictionary<string, string> { { "-scrollregion", "0 0 1000 500" } });
        var fractions = new List<double>();
        canvas.XScrollChanged += (first, last) => { fractions.Add(first); fractions.Add(last); };

        //Act
        canvas.XViewMoveTo(0.5);

        //Assert
        fractions.Count.Should().Be(2);
        fractions[0].Should().BeGreaterThan(0.4);
        fractions[1].Should().BeLessThan(0.8);
    }

    [Fact]
    public void Deferred_subcommands_accept_and_return_empty()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        canvas.Create("text", new double[] { 10, 10 },
                new Dictionary<string, string> { { "-text", "hello" } });

        //Act + Assert (none of these may throw — the §3.20 deferral rule)
        canvas.Execute(new[] { "postscript" }).Should().Be("");
        canvas.Execute(new[] { "icursor", "1", "3" }).Should().Be("");
        canvas.Execute(new[] { "insert", "1", "3", "abc" }).Should().Be("");
        canvas.Execute(new[] { "dchars", "1", "0", "2" }).Should().Be("");
        canvas.Execute(new[] { "select", "from", "1", "0" }).Should().Be("");
        canvas.Execute(new[] { "index", "1", "insert" }).Should().Be("");
    }

    [Fact]
    public void Unknown_item_options_are_stored_and_read_back()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 });

        //Act (an option this build does not interpret yet)
        canvas.Execute(new[] { "itemconfigure", id.ToString(), "-stipple", "gray25" });

        //Assert (cget reads back what configure wrote)
        canvas.Execute(new[] { "itemcget", id.ToString(), "-stipple" }).Should().Be("gray25");
    }

    [Fact]
    public void Delete_of_current_item_clears_current()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);
        int id = canvas.Create("rectangle", new double[] { 10, 10, 60, 60 },
                new Dictionary<string, string> { { "-fill", "red" } });
        root.Tree.PointerEvent(TkEventType.Motion, 30, 30);
        canvas.CurrentItem.Id.Should().Be(id);

        //Act
        canvas.Delete(id.ToString());

        //Assert
        canvas.CurrentItem.Should().BeNull();
        canvas.FindAll().Count.Should().Be(0);
    }

    [Fact]
    public void Registered_item_type_is_creatable_by_prefix()
    {
        //Arrange
        TkWindow root;
        CanvasWidget canvas = CreateCanvas(out root);

        //Act
        int id = canvas.Create("rect", new double[] { 1, 2, 3, 4 });

        //Assert
        canvas.Execute(new[] { "type", id.ToString() }).Should().Be("rectangle");
    }
}
