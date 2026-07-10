using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Unit tests for the event nervous system ABOVE the binding table: pointer
/// hit-testing and routing, Enter/Leave crossing chains, the implicit mouse
/// grab during a button hold, explicit grab confinement, keyboard focus
/// routing and traversal, and event-system teardown on window destruction.
/// (Bind-tag order, specificity, and break semantics are covered by the
/// wish-verified BindOracle fixtures.)
/// </summary>
public class WindowTreeTests
{
    /// <summary>A 200x200 root with two 100x100 children side by side (.left at 0,0 and .right at 100,0), and a 40x40 .right.inner at its (10,10).</summary>
    private static TkWindow BuildTree(out TkWindow left, out TkWindow right, out TkWindow inner)
    {
        TkWindow root = TkWindow.CreateRoot();
        left = root.CreateChild("left");
        right = root.CreateChild("right");
        inner = right.CreateChild("inner");

        left.SetRequestedSize(100, 200);
        right.SetRequestedSize(100, 200);
        inner.SetRequestedSize(40, 40);

        // .right hosts packed content; keep its own 100x200 request instead
        // of shrinking to the content (a fixed-size panel).
        PackLayout.SetPropagate(right, false);

        PackLayout.Configure(left, new PackOptions { Side = Side.Left });
        PackLayout.Configure(right, new PackOptions { Side = Side.Left });
        var innerOptions = new PackOptions { Side = Side.Top, Anchor = Anchor.NW };
        innerOptions.PadLeft = 10;
        innerOptions.PadTop = 10;
        PackLayout.Configure(inner, innerOptions);

        TkLayout.Update(root);
        return root;
    }

    private static List<string> Log(WindowTree tree, string tag, string pattern)
    {
        var log = new List<string>();
        tree.Bindings.Bind(tag, pattern, e =>
        {
            log.Add(e.Window.PathName + " " + e.Type + " " + e.X + "," + e.Y);
            return DispatchResult.Continue;
        });
        return log;
    }

    [Fact]
    public void HitTest_finds_the_deepest_window_under_the_point()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);

        //Act / Assert
        root.Tree.HitTest(50, 50).Should().BeSameAs(left);
        root.Tree.HitTest(115, 15).Should().BeSameAs(inner);
        root.Tree.HitTest(105, 150).Should().BeSameAs(right);
        root.Tree.HitTest(300, 300).Should().BeNull();
    }

    [Fact]
    public void HitTest_prefers_later_siblings_on_overlap()
    {
        //Arrange (two unmanaged same-place children: later sibling is on top,
        //         like Tk's default stacking order)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        root.SetForcedSize(100, 100);
        a.X = 0; a.Y = 0; a.Width = 100; a.Height = 100; a.IsDisplayed = true;
        b.X = 0; b.Y = 0; b.Width = 100; b.Height = 100; b.IsDisplayed = true;
        TkLayout.Update(root);

        //Act / Assert
        root.Tree.HitTest(50, 50).Should().BeSameAs(b);
    }

    [Fact]
    public void PointerEvent_delivers_window_relative_coordinates()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        List<string> log = Log(root.Tree, ".right.inner", "<ButtonPress-1>");

        //Act
        root.Tree.PointerEvent(TkEventType.ButtonPress, 115, 15, 1);

        //Assert (root 115,15 -> right-relative 15,15 -> inner-relative 5,5)
        log.Should().Equal(".right.inner ButtonPress 5,5");
    }

    [Fact]
    public void PointerEvent_synthesizes_enter_and_leave_chains()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        foreach (string tag in new[] { ".left", ".right", ".right.inner" })
        {
            root.Tree.Bindings.Bind(tag, "<Enter>", e => { log.Add("enter " + e.Window.PathName); return DispatchResult.Continue; });
            root.Tree.Bindings.Bind(tag, "<Leave>", e => { log.Add("leave " + e.Window.PathName); return DispatchResult.Continue; });
        }

        //Act (into .left, then into .right.inner: leave .left, enter .right then .right.inner)
        root.Tree.PointerEvent(TkEventType.Motion, 50, 50);
        root.Tree.PointerEvent(TkEventType.Motion, 115, 15);

        //Assert
        log.Should().Equal("enter .left", "leave .left", "enter .right", "enter .right.inner");
    }

    [Fact]
    public void Implicit_grab_routes_motion_to_the_pressed_window_until_release()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        root.Tree.Bindings.Bind(".left", "<B1-Motion>", e => { log.Add("drag .left " + e.X + "," + e.Y); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind(".right", "<B1-Motion>", e => { log.Add("drag .right"); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind(".left", "<ButtonRelease-1>", e => { log.Add("release .left"); return DispatchResult.Continue; });

        //Act (press in .left, drag across .right, release there)
        root.Tree.PointerEvent(TkEventType.ButtonPress, 50, 50, 1);
        root.Tree.PointerEvent(TkEventType.Motion, 150, 50);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 150, 50, 1);

        //Assert (motion goes to .left with .left-relative coords, even at 150;
        //        release also goes to the grab window)
        log.Should().Equal("drag .left 150,50", "release .left");
    }

    [Fact]
    public void Explicit_grab_confines_pointer_events_to_the_grab_subtree()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        root.Tree.Bindings.Bind(".left", "<ButtonPress-1>", e => { log.Add("press .left"); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind(".right", "<ButtonPress-1>", e => { log.Add("press .right " + e.X + "," + e.Y); return DispatchResult.Continue; });
        root.Tree.GrabWindow = right;

        //Act (click inside .left: redirected to the grab window .right)
        root.Tree.PointerEvent(TkEventType.ButtonPress, 50, 50, 1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 50, 50, 1);

        //Assert
        log.Should().Equal("press .right -50,50");
    }

    [Fact]
    public void KeyEvent_goes_to_the_focus_window()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        root.Tree.Bindings.Bind(".left", "<KeyPress-Down>", e => { log.Add("down .left"); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind("all", "<KeyPress>", e => { log.Add("any " + e.Window.PathName + " " + e.KeySym); return DispatchResult.Continue; });

        //Act
        root.Tree.SetFocus(left);
        root.Tree.KeyEvent(TkEventType.KeyPress, "Down");
        root.Tree.SetFocus(right);
        root.Tree.KeyEvent(TkEventType.KeyPress, "x");

        //Assert (instance and "all" tags BOTH fire for the Down press)
        log.Should().Equal("down .left", "any .left Down", "any .right x");
    }

    [Fact]
    public void SetFocus_fires_focusout_then_focusin()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        root.Tree.Bindings.Bind("all", "<FocusIn>", e => { log.Add("in " + e.Window.PathName); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind("all", "<FocusOut>", e => { log.Add("out " + e.Window.PathName); return DispatchResult.Continue; });

        //Act
        root.Tree.SetFocus(left);
        root.Tree.SetFocus(right);

        //Assert
        log.Should().Equal("in .left", "out .left", "in .right");
    }

    [Fact]
    public void FocusNext_and_FocusPrev_cycle_focusable_displayed_windows()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        left.Focusable = true;
        right.Focusable = true;
        inner.Focusable = true;

        //Act / Assert (depth-first order: .left, .right, .right.inner, wrap)
        root.Tree.FocusNext(left).Should().BeSameAs(right);
        root.Tree.FocusNext(right).Should().BeSameAs(inner);
        root.Tree.FocusNext(inner).Should().BeSameAs(left);
        root.Tree.FocusPrev(left).Should().BeSameAs(inner);
    }

    [Fact]
    public void Destroy_fires_destroy_bindings_and_cleans_event_state()
    {
        //Arrange
        TkWindow left, right, inner;
        TkWindow root = BuildTree(out left, out right, out inner);
        var log = new List<string>();
        root.Tree.Bindings.Bind(".right", "<Destroy>", e => { log.Add("destroy " + e.Window.PathName); return DispatchResult.Continue; });
        root.Tree.Bindings.Bind("all", "<Destroy>", e => { log.Add("all-destroy " + e.Window.PathName); return DispatchResult.Continue; });
        root.Tree.SetFocus(right);

        //Act
        right.Destroy();

        //Assert (children first: .right.inner then .right; focus cleared)
        log.Should().Equal("all-destroy .right.inner", "destroy .right", "all-destroy .right");
        root.Tree.FocusWindow.Should().BeNull();
    }

    [Fact]
    public void Layout_fires_configure_events_for_resized_windows()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        a.SetRequestedSize(50, 40);
        PackLayout.Configure(a, new PackOptions { Fill = Fill.Both, Expand = true });
        var log = new List<string>();
        root.Tree.Bindings.Bind(".a", "<Configure>", e => { log.Add("conf .a " + e.Width + "x" + e.Height); return DispatchResult.Continue; });

        //Act
        TkLayout.Update(root);          // first layout: 50x40
        TkLayout.Update(root);          // unchanged: no event
        root.SetForcedSize(200, 100);   // resize
        TkLayout.Update(root);

        //Assert
        log.Should().Equal("conf .a 50x40", "conf .a 200x100");
    }
}
