using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.9 coverage: the checkbutton/radiobutton toggles (internal and shared-
/// variable state, mutual exclusion, commands) and the toolkit-wide
/// accept-and-no-op generality discipline (unknown options are stored and
/// read back; deferred canvas subcommands return empty instead of throwing).
/// </summary>
public class B9TogglesSweepTests
{
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

    [Fact]
    public void Checkbutton_toggles_internal_state_and_fires_command()
    {
        TkWindow root = Root();
        var cb = new CheckbuttonWidget(root.CreateChild("c"));
        cb.Configure(Opts("-text", "Auto-save"));
        int fires = 0;
        cb.Command = () => fires++;

        cb.IsSelected.Should().BeFalse();
        cb.Invoke();
        cb.IsSelected.Should().BeTrue();
        cb.Invoke();
        cb.IsSelected.Should().BeFalse();
        fires.Should().Be(2);
    }

    [Fact]
    public void Checkbutton_reflects_shared_variable_on_off_values()
    {
        TkWindow root = Root();
        var cb = new CheckbuttonWidget(root.CreateChild("c"));
        cb.Configure(Opts("-text", "On", "-onvalue", "yes", "-offvalue", "no"));
        var v = new ToggleVariable("no");
        cb.Variable = v;

        cb.IsSelected.Should().BeFalse();
        v.Set("yes");
        cb.IsSelected.Should().BeTrue();
        cb.Invoke();                 // toggles → sets variable to offvalue
        v.Value.Should().Be("no");
    }

    [Fact]
    public void Radiobutton_group_is_mutually_exclusive()
    {
        TkWindow root = Root();
        var group = new ToggleVariable("");
        var r1 = new RadiobuttonWidget(root.CreateChild("r1"));
        r1.Configure(Opts("-text", "C", "-value", "c"));
        r1.Variable = group;
        var r2 = new RadiobuttonWidget(root.CreateChild("r2"));
        r2.Configure(Opts("-text", "C++", "-value", "cpp"));
        r2.Variable = group;
        var r3 = new RadiobuttonWidget(root.CreateChild("r3"));
        r3.Configure(Opts("-text", "Python", "-value", "py"));
        r3.Variable = group;

        r2.Invoke();
        r1.IsSelected.Should().BeFalse();
        r2.IsSelected.Should().BeTrue();
        r3.IsSelected.Should().BeFalse();

        r3.Select();
        r2.IsSelected.Should().BeFalse();
        r3.IsSelected.Should().BeTrue();
        group.Value.Should().Be("py");
    }

    [Fact]
    public void Unknown_widget_options_are_accepted_and_read_back()
    {
        TkWindow root = Root();
        var frame = new FrameWidget(root.CreateChild("f"));
        // A deferred/unknown corner must accept-and-store, never throw.
        frame.Configure(Opts("-relief", "raised", "-madeupoption", "value123", "-anotherweird", "x"));
        frame.Options.Get("-madeupoption").Should().Be("value123");
        frame.Options.Get("-anotherweird").Should().Be("x");
    }

    [Fact]
    public void Deferred_canvas_subcommands_return_empty_not_throw()
    {
        TkWindow root = Root();
        var canvas = new CanvasWidget(root.CreateChild("cv"));
        canvas.Execute(new List<string> { "create", "rectangle", "0", "0", "10", "10" });

        // §3.20 deferred corners: postscript + in-canvas text editing.
        canvas.Execute(new List<string> { "postscript", "-file", "out.ps" }).Should().Be("");
        canvas.Execute(new List<string> { "icursor", "1", "0" }).Should().Be("");
        canvas.Execute(new List<string> { "select", "from", "1", "0" }).Should().Be("");
    }
}
