using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Dialogs;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless coverage for B.7b: the Skia menu system (posting, active-entry
/// tracking, cascades, invoke, outside-click dismiss, the menubar, and the
/// <c>&lt;&lt;MenuSelect&gt;&gt;</c> virtual event) and the overlay message
/// dialogs (button sets, modal grab, result callback, teardown).
/// </summary>
public class B7bMenusDialogsTests
{
    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(500, 400);
        return root;
    }

    private static void Point(MenuWidget menu, int entry, out int x, out int y)
    {
        SkiaSharp.SKRectI r = menu.EntryRect(entry);
        x = menu.Window.X + r.Left + 4;
        y = menu.Window.Y + (r.Top + r.Bottom) / 2;
    }

    [Fact]
    public void Popup_menu_tracks_active_entry_and_invokes_command_on_release()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;
        MenuWidget menu = menus.CreateMenu("m");
        int opened = 0;
        menu.AddCommand("Open", () => opened++);
        menu.AddSeparator();
        menu.AddCommand("Quit", () => { });

        menus.Popup(menu, 40, 40);
        TkLayoutUpdate(root);
        menus.IsPosted.Should().BeTrue();

        int x, y;
        Point(menu, 0, out x, out y);
        root.Tree.PointerEvent(TkEventType.Motion, x, y);
        menu.ActiveIndex.Should().Be(0);

        root.Tree.PointerEvent(TkEventType.ButtonPress, x, y, 1);
        root.Tree.PointerEvent(TkEventType.ButtonRelease, x, y, 1);

        opened.Should().Be(1);
        menus.IsPosted.Should().BeFalse();
    }

    [Fact]
    public void Separator_is_not_selectable_as_an_active_entry()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;
        MenuWidget menu = menus.CreateMenu("m");
        menu.AddCommand("A", () => { });
        menu.AddSeparator();
        menu.AddCommand("B", () => { });
        menus.Popup(menu, 30, 30);
        TkLayoutUpdate(root);

        int x, y;
        Point(menu, 1, out x, out y); // the separator
        root.Tree.PointerEvent(TkEventType.Motion, x, y);
        menu.ActiveIndex.Should().Be(-1);
    }

    [Fact]
    public void Hovering_a_cascade_opens_its_submenu()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;
        MenuWidget menu = menus.CreateMenu("m");
        MenuWidget sub = menus.CreateMenu("sub");
        sub.AddCommand("Deep", () => { });
        menu.AddCommand("Top", () => { });
        menu.AddCascade("More", sub);

        menus.Popup(menu, 40, 40);
        TkLayoutUpdate(root);

        int x, y;
        Point(menu, 1, out x, out y); // the cascade
        root.Tree.PointerEvent(TkEventType.Motion, x, y);

        menus.Posted.Count.Should().Be(2);
        menus.Posted[1].Should().BeSameAs(sub);
    }

    [Fact]
    public void Press_outside_all_menus_dismisses_them()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;
        MenuWidget menu = menus.CreateMenu("m");
        menu.AddCommand("X", () => { });
        menus.Popup(menu, 40, 40);
        TkLayoutUpdate(root);

        root.Tree.PointerEvent(TkEventType.ButtonPress, 400, 380, 1);
        menus.IsPosted.Should().BeFalse();
    }

    [Fact]
    public void Menu_select_virtual_event_fires_when_active_entry_changes()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;
        MenuWidget menu = menus.CreateMenu("m");
        menu.AddCommand("One", () => { });
        menu.AddCommand("Two", () => { });
        int selects = 0;
        root.Tree.Bindings.Bind(menu.Window.PathName, "<<MenuSelect>>",
                e => { selects++; return DispatchResult.Continue; });

        menus.Popup(menu, 40, 40);
        TkLayoutUpdate(root);
        int x, y;
        Point(menu, 0, out x, out y);
        root.Tree.PointerEvent(TkEventType.Motion, x, y);
        Point(menu, 1, out x, out y);
        root.Tree.PointerEvent(TkEventType.Motion, x, y);

        selects.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Menubar_press_opens_the_entry_submenu()
    {
        TkWindow root = Root();
        MenuManager menus = root.Tree.Menus;

        // A menubar lives in the root as a normal (non-overlay) widget.
        TkWindow barWin = root.CreateChild("mb");
        var bar = new MenuWidget(barWin);
        bar.Configure(new Dictionary<string, string> { { "-type", "menubar" } });
        MenuWidget fileMenu = menus.CreateMenu("file");
        fileMenu.AddCommand("New", () => { });
        bar.AddCascade("File", fileMenu);
        PackLayout.Configure(barWin, new PackOptions { Side = Side.Top, Fill = Fill.X });
        menus.SetMenubar(bar);
        TkLayoutUpdate(root);

        SkiaSharp.SKRectI r = bar.EntryRect(0);
        int x = barWin.X + r.Left + 4;
        int y = barWin.Y + (r.Top + r.Bottom) / 2;
        root.Tree.PointerEvent(TkEventType.ButtonPress, x, y, 1);

        menus.IsPosted.Should().BeTrue();
        menus.Posted[0].Should().BeSameAs(fileMenu);
    }

    [Fact]
    public void MessageBox_is_modal_and_returns_the_clicked_button()
    {
        TkWindow root = Root();
        var options = new MessageDialogOptions
        {
            Type = "okcancel",
            Message = "Proceed?",
            Title = "Confirm",
            Icon = "question",
        };
        string result = null;
        TkWindow dialog = MessageDialog.Show(root.Tree, options, r => result = r);

        // Modal: the grab is on the dialog.
        root.Tree.GrabWindow.Should().BeSameAs(dialog);

        // The button row holds two buttons (ok, cancel); invoke "cancel".
        ButtonWidget cancel = FindButton(dialog, "Cancel");
        cancel.Should().NotBeNull();
        cancel.Invoke();

        result.Should().Be("cancel");
        root.Tree.GrabWindow.Should().BeNull(); // released
        dialog.IsDestroyed.Should().BeTrue();
    }

    [Fact]
    public void MessageBox_yesnocancel_has_three_buttons()
    {
        TkWindow root = Root();
        var options = new MessageDialogOptions { Type = "yesnocancel", Message = "Save?" };
        TkWindow dialog = MessageDialog.Show(root.Tree, options, r => { });

        int count = 0;
        foreach (TkWindow child in FindButtonsRow(dialog).Children)
        {
            if (child.Widget is ButtonWidget) { count++; }
        }
        count.Should().Be(3);
    }

    private static void TkLayoutUpdate(TkWindow root)
    {
        TkLayout.Update(root);
    }

    private static TkWindow FindButtonsRow(TkWindow dialog)
    {
        foreach (TkWindow child in dialog.Children)
        {
            if (child.Name == "buttons") { return child; }
        }
        return null;
    }

    private static ButtonWidget FindButton(TkWindow dialog, string text)
    {
        foreach (TkWindow child in FindButtonsRow(dialog).Children)
        {
            var b = child.Widget as ButtonWidget;
            if (b != null && b.Options.Get("-text") == text) { return b; }
        }
        return null;
    }
}
