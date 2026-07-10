using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless tests for the overlay layer and mini window-manager (§11): the
/// wm surface, stacking/raising, chrome pointer interception (drag, close),
/// modal grabs, host-resize clamping, transient lifetime, and withdrawal.
/// </summary>
public class WindowManagerTests
{
    private static TkWindow CreateRoot(int width = 400, int height = 300)
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(width, height);
        TkLayout.Update(root);
        return root;
    }

    private static TkWindow CreateDialog(TkWindow root, string name,
            int w = 100, int h = 60, int x = 50, int y = 50)
    {
        WindowManager wm = root.Tree.WindowManager;
        TkWindow dialog = wm.CreateToplevel(name);
        wm.SetGeometry(dialog, w, h, x, y);
        TkLayout.Update(root);
        return dialog;
    }

    [Fact]
    public void Toplevel_gets_wm_geometry_and_is_displayed()
    {
        //Arrange
        TkWindow root = CreateRoot();

        //Act
        TkWindow dialog = CreateDialog(root, "dlg", 120, 80, 30, 40);

        //Assert
        dialog.IsDisplayed.Should().BeTrue();
        dialog.X.Should().Be(30);
        dialog.Y.Should().Be(40);
        dialog.Width.Should().Be(120);
        dialog.Height.Should().Be(80);
        dialog.ClassName.Should().Be("Toplevel");
    }

    [Fact]
    public void Toplevel_without_geometry_takes_its_requested_size()
    {
        //Arrange
        TkWindow root = CreateRoot();
        WindowManager wm = root.Tree.WindowManager;
        TkWindow dialog = wm.CreateToplevel("dlg");
        dialog.SetRequestedSize(222, 111);

        //Act
        TkLayout.Update(root);

        //Assert
        dialog.Width.Should().Be(222);
        dialog.Height.Should().Be(111);
    }

    [Fact]
    public void Withdraw_hides_and_deiconify_shows_again()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg");
        WindowManager wm = root.Tree.WindowManager;

        //Act
        wm.Withdraw(dialog);
        TkLayout.Update(root);

        //Assert
        dialog.IsDisplayed.Should().BeFalse();
        root.Tree.HitTest(60, 60).Should().Be(root);

        //Act (the create-withdrawn → configure → deiconify dance; y leaves
        //room for the chrome title bar, else the clamp shifts the window)
        wm.SetGeometry(dialog, 100, 60, 10, 30);
        wm.Deiconify(dialog);
        TkLayout.Update(root);

        //Assert
        dialog.IsDisplayed.Should().BeTrue();
        dialog.X.Should().Be(10);
        dialog.Y.Should().Be(30);
        root.Tree.HitTest(20, 40).Should().Be(dialog);
    }

    [Fact]
    public void Overlays_stack_above_base_content_and_raise_reorders()
    {
        //Arrange (a base child covering the whole root, then two dialogs)
        TkWindow root = CreateRoot();
        TkWindow baseChild = root.CreateChild("base");
        baseChild.SetRequestedSize(400, 300);
        Layout.PackLayout.Configure(baseChild, new Layout.PackOptions
        {
            Fill = Layout.Fill.Both,
            Expand = true,
        });
        TkWindow first = CreateDialog(root, "one", 100, 60, 50, 50);
        TkWindow second = CreateDialog(root, "two", 100, 60, 80, 80);
        TkLayout.Update(root);

        //Assert (both dialogs overlap at 90,90: the later one wins)
        root.Tree.HitTest(90, 90).Should().Be(second);
        root.Tree.HitTest(60, 60).Should().Be(first);
        root.Tree.HitTest(300, 200).Should().Be(baseChild);

        //Act
        root.Tree.WindowManager.Raise(first);

        //Assert
        root.Tree.HitTest(90, 90).Should().Be(first);
    }

    [Fact]
    public void Title_bar_drag_moves_the_overlay()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 50, 50);
        OverlayState overlay = root.Tree.WindowManager.GetOverlay(dialog);
        SkiaSharp.SKRectI bar = overlay.TitleBarRect;
        int grabX = bar.Left + 10;
        int grabY = bar.Top + 5;

        //Act (press in the title bar, drag, release)
        root.Tree.PointerEvent(TkEventType.ButtonPress, grabX, grabY, 1);
        root.Tree.PointerEvent(TkEventType.Motion, grabX + 30, grabY + 20, 0, EventModifiers.Button1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, grabX + 30, grabY + 20, 1);

        //Assert
        dialog.X.Should().Be(80);
        dialog.Y.Should().Be(70);
    }

    [Fact]
    public void Chrome_clicks_do_not_reach_tk_bindings()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 50, 50);
        var log = new List<string>();
        root.Tree.Bindings.Bind("all", "<ButtonPress-1>", e =>
        {
            log.Add(e.Window.PathName);
            return DispatchResult.Continue;
        });
        OverlayState overlay = root.Tree.WindowManager.GetOverlay(dialog);
        SkiaSharp.SKRectI bar = overlay.TitleBarRect;

        //Act (title-bar press is a WM interaction; content press is Tk's)
        root.Tree.PointerEvent(TkEventType.ButtonPress, bar.Left + 10, bar.Top + 5, 1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, bar.Left + 10, bar.Top + 5, 1);
        root.Tree.PointerEvent(TkEventType.ButtonPress, 60, 60, 1);

        //Assert
        log.Should().Equal(new[] { ".dlg" });
    }

    [Fact]
    public void Close_box_requests_close_or_destroys()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 50, 50);
        OverlayState overlay = root.Tree.WindowManager.GetOverlay(dialog);
        SkiaSharp.SKRectI close = overlay.CloseBoxRect;

        //Act (no subscriber: the toolkit destroys the toplevel)
        root.Tree.PointerEvent(TkEventType.ButtonPress,
                (close.Left + close.Right) / 2, (close.Top + close.Bottom) / 2, 1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease,
                (close.Left + close.Right) / 2, (close.Top + close.Bottom) / 2, 1);

        //Assert
        dialog.IsDestroyed.Should().BeTrue();
        root.Tree.WindowManager.Overlays.Count.Should().Be(0);
    }

    [Fact]
    public void Grab_confines_events_to_the_modal_overlay()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 50, 50);
        WindowManager wm = root.Tree.WindowManager;
        var log = new List<string>();
        root.Tree.Bindings.Bind("all", "<ButtonPress-1>", e =>
        {
            log.Add(e.Window.PathName);
            return DispatchResult.Continue;
        });

        //Act
        wm.Grab(dialog);
        root.Tree.PointerEvent(TkEventType.ButtonPress, 300, 200, 1); // outside
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 300, 200, 1);
        root.Tree.PointerEvent(TkEventType.ButtonPress, 60, 60, 1);   // inside
        root.Tree.PointerEvent(TkEventType.ButtonRelease, 60, 60, 1);
        wm.ReleaseGrab();

        //Assert (the outside press was redirected to the grab window)
        log.Should().Equal(new[] { ".dlg", ".dlg" });
    }

    [Fact]
    public void Host_resize_clamps_overlays_back_into_bounds()
    {
        //Arrange
        TkWindow root = CreateRoot(400, 300);
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 280, 220);

        //Act (the host window shrinks)
        root.SetForcedSize(200, 150);
        TkLayout.Update(root);

        //Assert (the frame fits inside the new bounds)
        OverlayState overlay = root.Tree.WindowManager.GetOverlay(dialog);
        SkiaSharp.SKRectI frame = overlay.FrameRect;
        (frame.Right <= 200).Should().BeTrue();
        (frame.Bottom <= 150).Should().BeTrue();
        (frame.Left >= 0).Should().BeTrue();
        (frame.Top >= 0).Should().BeTrue();
    }

    [Fact]
    public void Transient_dies_with_its_master()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow master = CreateDialog(root, "master", 150, 100, 20, 20);
        TkWindow child = CreateDialog(root, "child", 80, 50, 60, 60);
        WindowManager wm = root.Tree.WindowManager;
        wm.SetTransient(child, master);

        //Act
        master.Destroy();

        //Assert
        child.IsDestroyed.Should().BeTrue();
        wm.Overlays.Count.Should().Be(0);
    }

    [Fact]
    public void Override_redirect_overlay_has_no_chrome_rects()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow tip = CreateDialog(root, "tip", 60, 20, 100, 100);
        WindowManager wm = root.Tree.WindowManager;

        //Act
        wm.SetOverrideRedirect(tip, true);

        //Assert
        OverlayState overlay = wm.GetOverlay(tip);
        overlay.TitleBarRect.Should().Be(SkiaSharp.SKRectI.Empty);
        overlay.CloseBoxRect.Should().Be(SkiaSharp.SKRectI.Empty);
    }

    [Fact]
    public void Root_title_propagates_to_the_host()
    {
        //Arrange
        TkWindow root = CreateRoot();
        WindowManager wm = root.Tree.WindowManager;
        string seen = null;
        wm.RootTitleChanged += title => seen = title;

        //Act
        wm.SetTitle(root, "DRAKON Editor");

        //Assert
        seen.Should().Be("DRAKON Editor");
        wm.RootTitle.Should().Be("DRAKON Editor");
    }

    [Fact]
    public void Interior_content_packs_inside_the_overlay()
    {
        //Arrange
        TkWindow root = CreateRoot();
        TkWindow dialog = CreateDialog(root, "dlg", 100, 60, 50, 50);
        TkWindow label = dialog.CreateChild("label");
        label.SetRequestedSize(40, 20);
        Layout.PackLayout.Configure(label, new Layout.PackOptions());

        //Act
        TkLayout.Update(root);

        //Assert (packed at the top, centered horizontally, inside the dialog)
        label.IsDisplayed.Should().BeTrue();
        label.X.Should().Be(30);
        label.Y.Should().Be(0);
        root.Tree.HitTest(50 + 40, 50 + 10).Should().Be(label);
    }
}
