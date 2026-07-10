using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Headless coverage for the B.6 structural widgets. Text-bearing sizes are
/// font-stack dependent (not wish byte-comparable), so these tests assert
/// formula-consistency (size grows with content, honours width/height, keeps
/// the border inset) and the widgets' behaviour models (button press/invoke,
/// entry text model, scrollbar set/command, panedwindow sash split).
/// </summary>
public class B6WidgetsTests
{
    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(400, 300);
        return root;
    }

    private static void Layout(TkWindow root)
    {
        TkLayout.Update(root);
    }

    private static TkWindow Child(TkWindow root, string name)
    {
        return root.CreateChild(name);
    }

    private static Dictionary<string, string> Opts(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) { d[pairs[i]] = pairs[i + 1]; }
        return d;
    }

    [Fact]
    public void Frame_honours_explicit_width_height_and_sets_internal_border()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "f");
        var frame = new FrameWidget(w);
        frame.Configure(Opts("-width", "120", "-height", "80", "-borderwidth", "3", "-relief", "ridge"));

        w.RequestedWidth.Should().Be(120);
        w.RequestedHeight.Should().Be(80);
        w.InternalBorderLeft.Should().Be(3);
    }

    [Fact]
    public void Label_natural_size_grows_with_text_and_padding()
    {
        TkWindow root = Root();
        var shortLbl = new LabelWidget(Child(root, "a"));
        shortLbl.Configure(Opts("-text", "hi", "-padx", "2", "-pady", "2"));
        var longLbl = new LabelWidget(Child(root, "b"));
        longLbl.Configure(Opts("-text", "a much longer label", "-padx", "2", "-pady", "2"));

        longLbl.Window.RequestedWidth.Should().BeGreaterThan(shortLbl.Window.RequestedWidth);
        // Two padding units + at least the border inset are included on each axis.
        shortLbl.Window.RequestedHeight.Should().BeGreaterThan(2 * 2);
    }

    [Fact]
    public void Button_press_then_release_over_it_invokes_command()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "btn");
        var button = new ButtonWidget(w);
        button.Configure(Opts("-text", "Go"));
        int invoked = 0;
        button.Invoked += () => invoked++;

        WindowTree tree = root.Tree;
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.Enter });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonPress, Button = 1 });
        button.IsPressed.Should().BeTrue();
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonRelease, Button = 1 });

        button.IsPressed.Should().BeFalse();
        invoked.Should().Be(1);
    }

    [Fact]
    public void Button_release_after_leaving_does_not_invoke()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "btn");
        var button = new ButtonWidget(w);
        button.Configure(Opts("-text", "Go"));
        int invoked = 0;
        button.Invoked += () => invoked++;

        WindowTree tree = root.Tree;
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.Enter });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonPress, Button = 1 });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.Leave });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonRelease, Button = 1 });

        invoked.Should().Be(0);
    }

    [Fact]
    public void Disabled_button_never_invokes()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "btn");
        var button = new ButtonWidget(w);
        button.Configure(Opts("-text", "Go", "-state", "disabled"));
        int invoked = 0;
        button.Invoked += () => invoked++;

        WindowTree tree = root.Tree;
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.Enter });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonPress, Button = 1 });
        tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonRelease, Button = 1 });

        button.Invoke(); // direct call also refuses
        invoked.Should().Be(0);
    }

    [Fact]
    public void Entry_text_model_insert_delete_and_selection()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "e");
        var entry = new EntryWidget(w);
        entry.Configure(Opts("-width", "20"));
        PackLayout.Configure(w, new PackOptions());
        Layout(root);

        entry.Insert(0, "Hello world");
        entry.Text.Should().Be("Hello world");
        entry.Delete(5, 11); // remove " world"
        entry.Text.Should().Be("Hello");
        entry.SelectRange(1, 4);
        entry.SelectedText.Should().Be("ell");
        entry.SetCursor(2);
        entry.Cursor.Should().Be(2);
    }

    [Fact]
    public void Entry_show_option_masks_index_hit_testing_without_changing_text()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "e");
        var entry = new EntryWidget(w);
        entry.Configure(Opts("-width", "20", "-show", "*"));
        PackLayout.Configure(w, new PackOptions());
        Layout(root);
        entry.Insert(0, "secret");

        entry.Text.Should().Be("secret"); // model unchanged by -show
        entry.IndexAt(0).Should().Be(0);
    }

    [Fact]
    public void Separator_requested_size_follows_orient()
    {
        TkWindow root = Root();
        var h = new SeparatorWidget(Child(root, "h"));
        h.Configure(Opts("-orient", "horizontal"));
        var v = new SeparatorWidget(Child(root, "v"));
        v.Configure(Opts("-orient", "vertical"));

        h.Window.RequestedHeight.Should().Be(2);
        v.Window.RequestedWidth.Should().Be(2);
    }

    [Fact]
    public void Scrollbar_set_clamps_and_reports_fractions()
    {
        TkWindow root = Root();
        var sb = new ScrollbarWidget(Child(root, "sb"));
        sb.Configure(Opts("-orient", "vertical"));

        sb.Set(-0.5, 1.5);
        sb.First.Should().Be(0.0);
        sb.Last.Should().Be(1.0);

        sb.Set(0.25, 0.75);
        sb.First.Should().Be(0.25);
        sb.Last.Should().Be(0.75);
    }

    [Fact]
    public void Scrollbar_arrow_press_fires_scroll_command()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "sb");
        var sb = new ScrollbarWidget(w);
        sb.Configure(Opts("-orient", "vertical"));
        PackLayout.Configure(w, new PackOptions { Fill = Fill.Y, Expand = true });
        Layout(root);
        sb.Set(0.3, 0.6);

        string[] words = null;
        sb.Command += w2 => words = w2;

        // Press near the top → first arrow → "scroll -1 units".
        root.Tree.DispatchEvent(w, new TkEvent { Type = TkEventType.ButtonPress, Button = 1, X = 3, Y = 2 });

        words.Should().NotBeNull();
        words[0].Should().Be("scroll");
        words[1].Should().Be("-1");
        words[2].Should().Be("units");
    }

    [Fact]
    public void Labelframe_reserves_label_height_in_top_internal_border()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "lf");
        var lf = new LabelframeWidget(w);
        lf.Configure(Opts("-text", "Group", "-borderwidth", "2"));

        // Top internal border exceeds the plain inset by the caption height.
        w.InternalBorderTop.Should().BeGreaterThan(w.InternalBorderLeft);
    }

    [Fact]
    public void Panedwindow_lays_out_panes_with_sashes_and_moves_the_split()
    {
        TkWindow root = Root();
        TkWindow w = Child(root, "pw");
        var pw = new PanedWindowWidget(w);
        pw.Configure(Opts("-orient", "horizontal", "-width", "300", "-height", "100"));

        TkWindow p1 = w.CreateChild("p1");
        var f1 = new FrameWidget(p1);
        f1.Configure(Opts("-width", "100", "-height", "80"));
        TkWindow p2 = w.CreateChild("p2");
        var f2 = new FrameWidget(p2);
        f2.Configure(Opts("-width", "100", "-height", "80"));
        pw.Add(p1);
        pw.Add(p2);

        PackLayout.Configure(w, new PackOptions());
        Layout(root);

        p1.IsDisplayed.Should().BeTrue();
        p2.IsDisplayed.Should().BeTrue();
        // Pane 2 starts to the right of pane 1 plus the sash.
        p2.X.Should().BeGreaterThan(p1.X + p1.Width);

        int before = p1.Width;
        pw.MoveSash(0, 40); // grow the first pane
        Layout(root);
        p1.Width.Should().BeGreaterThan(before);
    }
}
