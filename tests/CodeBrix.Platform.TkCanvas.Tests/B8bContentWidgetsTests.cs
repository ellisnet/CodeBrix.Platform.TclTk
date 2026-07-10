using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless coverage for the B.8b content widgets: the listbox, treeview, and
/// combobox data models, their virtual events, and the scrollbar interplay.
/// </summary>
public class B8bContentWidgetsTests
{
    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(500, 400);
        return root;
    }

    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    // ---- Listbox ----

    [Fact]
    public void Listbox_insert_get_delete_and_size()
    {
        TkWindow root = Root();
        var lb = new ListboxWidget(root.CreateChild("lb"));
        lb.Insert(-1, "alpha", "beta", "gamma");
        lb.Size.Should().Be(3);
        lb.Get(1).Should().Be("beta");
        lb.Delete(0);
        lb.Get(0).Should().Be("beta");
        lb.Size.Should().Be(2);
    }

    [Fact]
    public void Listbox_selection_fires_virtual_event()
    {
        TkWindow root = Root();
        TkWindow w = root.CreateChild("lb");
        var lb = new ListboxWidget(w);
        lb.Insert(-1, "a", "b", "c");
        int selects = 0;
        root.Tree.Bindings.Bind(w.PathName, "<<ListboxSelect>>",
                e => { selects++; return DispatchResult.Continue; });

        lb.SelectionSet(2);
        lb.CurSelection().Should().Equal(new[] { 2 });
        lb.SelectionIncludes(2).Should().BeTrue();
        selects.Should().Be(1);
    }

    [Fact]
    public void Listbox_click_selects_nearest_row()
    {
        TkWindow root = Root();
        TkWindow w = root.CreateChild("lb");
        var lb = new ListboxWidget(w);
        lb.Configure(Opts("-height", "5", "-width", "10"));
        lb.Insert(-1, "r0", "r1", "r2", "r3");
        PackLayout.Configure(w, new PackOptions());
        TkLayout.Update(root);

        int y = w.Y + 2 + 1 * (root.Tree.Fonts.Metrics(root.Tree.Fonts.GetNamed("TkDefaultFont")).LineSpace + 2);
        root.Tree.PointerEvent(TkEventType.ButtonPress, w.X + 5, y, 1);

        lb.CurSelection().Count.Should().Be(1);
    }

    [Fact]
    public void Listbox_scrollbar_interplay_tracks_both_ways()
    {
        TkWindow root = Root();
        TkWindow lbw = root.CreateChild("lb");
        var lb = new ListboxWidget(lbw);
        lb.Configure(Opts("-height", "4", "-width", "10"));
        for (int i = 0; i < 20; i++) { lb.Insert(-1, "item" + i); }
        TkWindow sbw = root.CreateChild("sb");
        var sb = new ScrollbarWidget(sbw);
        sb.Configure(Opts("-orient", "vertical"));

        // Wire them like Tk: -yscrollcommand → sb.set; sb -command → lb yview.
        lb.YScrollChanged += (first, last) => sb.Set(first, last);
        sb.Command += words =>
        {
            if (words[0] == "moveto") { lb.YViewMoveTo(double.Parse(words[1], System.Globalization.CultureInfo.InvariantCulture)); }
            else { lb.YViewScroll(int.Parse(words[1]), words[2].StartsWith("p")); }
        };
        PackLayout.Configure(lbw, new PackOptions());
        TkLayout.Update(root);

        lb.See(19); // scroll to the bottom → scrollbar should advance
        sb.Last.Should().BeApproximately(1.0, 0.001);
        sb.First.Should().BeGreaterThan(0.0);
    }

    // ---- Treeview ----

    [Fact]
    public void Treeview_hierarchy_open_close_controls_visibility()
    {
        TkWindow root = Root();
        var tv = new TreeviewWidget(root.CreateChild("tv"));
        string parent = tv.Insert("", -1, "Parent");
        string child = tv.Insert(parent, -1, "Child");
        tv.Insert("", -1, "Sibling");

        // Collapsed by default → child hidden.
        tv.VisibleItems().Should().NotContain(child);
        tv.SetOpen(parent, true);
        tv.VisibleItems().Should().Contain(child);
        tv.ChildrenOf(parent).Should().Equal(new[] { child });
    }

    [Fact]
    public void Treeview_selection_fires_virtual_event_and_reports_ids()
    {
        TkWindow root = Root();
        TkWindow w = root.CreateChild("tv");
        var tv = new TreeviewWidget(w);
        string a = tv.Insert("", -1, "A");
        tv.Insert("", -1, "B");
        int selects = 0;
        root.Tree.Bindings.Bind(w.PathName, "<<TreeviewSelect>>",
                e => { selects++; return DispatchResult.Continue; });

        tv.SelectionSet(a);
        tv.Selection.Should().Equal(new[] { a });
        selects.Should().Be(1);
    }

    [Fact]
    public void Treeview_columns_and_values_round_trip()
    {
        TkWindow root = Root();
        var tv = new TreeviewWidget(root.CreateChild("tv"));
        tv.SetColumns("size", "kind");
        tv.SetHeading("#0", "Name");
        tv.SetHeading("size", "Size");
        string id = tv.Insert("", -1, "file.txt", new[] { "1.2K", "text" });
        tv.Item(id).Values.Should().Equal(new[] { "1.2K", "text" });
        tv.Columns.Should().Equal(new[] { "size", "kind" });
    }

    // ---- Combobox ----

    [Fact]
    public void Combobox_dropdown_select_sets_value_and_fires_event()
    {
        TkWindow root = Root();
        TkWindow w = root.CreateChild("cb");
        var cb = new ComboboxWidget(w);
        cb.Configure(Opts("-width", "14"));
        cb.SetValues("Red", "Green", "Blue");
        PackLayout.Configure(w, new PackOptions());
        TkLayout.Update(root);

        int events = 0;
        cb.Selected += () => events++;

        cb.OpenDropDown();
        cb.IsDropDownOpen.Should().BeTrue();
        root.Tree.GrabWindow.Should().NotBeNull();

        // Release over the second row of the drop-down.
        TkWindow dd = root.Tree.WindowManager.Overlays[root.Tree.WindowManager.Overlays.Count - 1].Window;
        int rowH = root.Tree.Fonts.Metrics(root.Tree.Fonts.GetNamed("TkTextFont")).LineSpace + 2;
        int y = dd.Y + 2 + 1 * rowH + rowH / 2;
        root.Tree.PointerEvent(TkEventType.ButtonRelease, dd.X + 5, y, 1);

        cb.Value.Should().Be("Green");
        cb.IsDropDownOpen.Should().BeFalse();
        root.Tree.GrabWindow.Should().BeNull();
        events.Should().Be(1);
    }

    [Fact]
    public void Combobox_press_outside_dropdown_closes_it_without_selecting()
    {
        TkWindow root = Root();
        TkWindow w = root.CreateChild("cb");
        var cb = new ComboboxWidget(w);
        cb.Configure(Opts("-width", "14"));
        cb.SetValues("One", "Two");
        PackLayout.Configure(w, new PackOptions());
        TkLayout.Update(root);

        cb.OpenDropDown();
        root.Tree.PointerEvent(TkEventType.ButtonPress, 480, 380, 1); // far outside
        cb.IsDropDownOpen.Should().BeFalse();
        cb.Value.Should().Be("");
    }
}
