using System;

using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Unit tests for the pack manager's public configure/forget/info surface:
/// container validation, packing-order manipulation, propagation flags, and
/// window destruction — the semantics the geometry oracle cannot observe.
/// </summary>
public class PackLayoutTests
{
    [Fact]
    public void Configure_rejects_the_root_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();

        //Act
        Action act = () => PackLayout.Configure(root, null);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_rejects_a_container_that_is_not_parent_or_parents_descendant()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow deep = a.CreateChild("deep");
        TkWindow other = a.CreateChild("other");

        //Act (container "other" is a sibling of "deep", not its parent or below it: valid;
        //     container "deep" for a window under root but not under "a": invalid)
        TkWindow stranger = root.CreateChild("stranger");
        Action act = () => PackLayout.Configure(deep, new PackOptions { In = stranger.CreateChild("inner") });

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_rejects_packing_a_window_inside_itself()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        Action act = () => PackLayout.Configure(a, new PackOptions { In = a });

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_rejects_packing_into_its_own_descendant()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow inner = a.CreateChild("inner");

        //Act
        Action act = () => PackLayout.Configure(a, new PackOptions { In = inner });

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_rejects_before_target_that_is_not_packed()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");

        //Act
        Action act = () => PackLayout.Configure(a, new PackOptions { Before = b });

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_appends_to_the_packing_order()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");

        //Act
        PackLayout.Configure(a, null);
        PackLayout.Configure(b, null);

        //Assert
        PackLayout.Content(root).Should().Equal(a, b);
    }

    [Fact]
    public void Configure_before_and_after_position_within_the_packing_order()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        TkWindow c = root.CreateChild("c");
        TkWindow d = root.CreateChild("d");
        PackLayout.Configure(a, null);
        PackLayout.Configure(b, null);

        //Act
        PackLayout.Configure(c, new PackOptions { Before = a });
        PackLayout.Configure(d, new PackOptions { After = a });

        //Assert
        PackLayout.Content(root).Should().Equal(c, a, d, b);
    }

    [Fact]
    public void Configure_reconfigures_in_place_keeping_packing_order()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        PackLayout.Configure(a, null);
        PackLayout.Configure(b, null);

        //Act
        PackLayout.Configure(a, new PackOptions { Side = Side.Right, Expand = true });

        //Assert
        PackLayout.Content(root).Should().Equal(a, b);
        PackLayout.Info(a).Side.Should().Be(Side.Right);
        PackLayout.Info(a).Expand.Should().BeTrue();
    }

    [Fact]
    public void Configure_moves_a_window_between_containers()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow holder = root.CreateChild("holder");
        TkWindow w = root.CreateChild("w");
        PackLayout.Configure(holder, null);
        PackLayout.Configure(w, null);

        //Act
        PackLayout.Configure(w, new PackOptions { In = holder });

        //Assert
        PackLayout.Content(root).Should().Equal(holder);
        PackLayout.Content(holder).Should().Equal(w);
        PackLayout.Info(w).In.Should().BeSameAs(holder);
    }

    [Fact]
    public void Info_reports_the_configured_options()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        var options = new PackOptions
        {
            Side = Side.Bottom,
            Anchor = Anchor.NW,
            Fill = Fill.Both,
            Expand = true,
            PadLeft = 3,
            PadRight = 7,
            PadTop = 2,
            PadBottom = 5,
            IPadX = 4,
            IPadY = 6,
        };

        //Act
        PackLayout.Configure(a, options);
        PackOptions info = PackLayout.Info(a);

        //Assert
        info.Side.Should().Be(Side.Bottom);
        info.Anchor.Should().Be(Anchor.NW);
        info.Fill.Should().Be(Fill.Both);
        info.Expand.Should().BeTrue();
        info.PadLeft.Should().Be(3);
        info.PadRight.Should().Be(7);
        info.PadTop.Should().Be(2);
        info.PadBottom.Should().Be(5);
        info.IPadX.Should().Be(4);
        info.IPadY.Should().Be(6);
        info.In.Should().BeSameAs(root);
        info.Before.Should().BeNull();
        info.After.Should().BeNull();
    }

    [Fact]
    public void Info_throws_for_an_unpacked_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        Action act = () => PackLayout.Info(a);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Forget_makes_the_window_unmanaged_but_keeps_its_geometry()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        a.SetRequestedSize(50, 40);
        PackLayout.Configure(a, null);
        TkLayout.Update(root);

        //Act
        PackLayout.Forget(a);

        //Assert
        PackLayout.Content(root).Should().BeEmpty();
        a.IsDisplayed.Should().BeFalse();
        a.Width.Should().Be(50);
        a.Height.Should().Be(40);
    }

    [Fact]
    public void Forget_is_a_no_op_for_an_unpacked_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        PackLayout.Forget(a);

        //Assert
        a.IsDisplayed.Should().BeFalse();
    }

    [Fact]
    public void GetPropagate_defaults_to_true()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();

        //Act / Assert
        PackLayout.GetPropagate(root).Should().BeTrue();
    }

    [Fact]
    public void SetPropagate_false_is_reported_back()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        PackLayout.SetPropagate(a, false);

        //Assert
        PackLayout.GetPropagate(a).Should().BeFalse();
    }

    [Fact]
    public void Destroy_of_content_removes_it_from_the_packing_order()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        PackLayout.Configure(a, null);
        PackLayout.Configure(b, null);

        //Act
        a.Destroy();

        //Assert
        PackLayout.Content(root).Should().Equal(b);
    }

    [Fact]
    public void Destroy_of_a_container_releases_its_content()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow holder = root.CreateChild("holder");
        TkWindow w = root.CreateChild("w");
        PackLayout.Configure(w, new PackOptions { In = holder });

        //Act
        holder.Destroy();

        //Assert
        w.IsDestroyed.Should().BeFalse();
        PackLayout.Forget(w); // must be a harmless no-op: w is unmanaged now
        w.IsDisplayed.Should().BeFalse();
    }

    [Fact]
    public void Update_requires_the_root_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        Action act = () => TkLayout.Update(a);

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
