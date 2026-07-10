using System;

using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Unit tests for the grid manager's public configure/forget/info/constraint
/// surface, including the Tk "geometry fight" guard between pack and grid in
/// one container — semantics the geometry oracle cannot observe.
/// </summary>
public class GridLayoutTests
{
    [Fact]
    public void Configure_rejects_the_root_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();

        //Act
        Action act = () => GridLayout.Configure(root, null);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_rejects_negative_cell_and_bad_spans()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act / Assert
        ((Action)(() => GridLayout.Configure(a, new GridOptions { Row = -1 })))
            .Should().Throw<ArgumentException>();
        ((Action)(() => GridLayout.Configure(a, new GridOptions { Column = -1 })))
            .Should().Throw<ArgumentException>();
        ((Action)(() => GridLayout.Configure(a, new GridOptions { RowSpan = 0 })))
            .Should().Throw<ArgumentException>();
        ((Action)(() => GridLayout.Configure(a, new GridOptions { ColumnSpan = 0 })))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Grid_into_a_pack_managed_container_is_a_geometry_fight()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        PackLayout.Configure(a, null);

        //Act
        Action act = () => GridLayout.Configure(b, null);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already has slaves managed by pack*");
    }

    [Fact]
    public void Pack_into_a_grid_managed_container_is_a_geometry_fight()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        GridLayout.Configure(a, null);

        //Act
        Action act = () => PackLayout.Configure(b, null);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already has slaves managed by grid*");
    }

    [Fact]
    public void Emptying_a_container_releases_it_to_the_other_manager()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        TkWindow b = root.CreateChild("b");
        GridLayout.Configure(a, null);
        GridLayout.Forget(a);

        //Act (must not throw: the grid claim was released with its last content)
        PackLayout.Configure(b, null);

        //Assert
        PackLayout.Content(root).Should().Equal(b);
    }

    [Fact]
    public void Different_containers_may_use_different_managers()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow outer = root.CreateChild("outer");
        TkWindow inner = outer.CreateChild("inner");

        //Act (pack in root, grid inside outer: legal, like Tk)
        PackLayout.Configure(outer, null);
        GridLayout.Configure(inner, null);

        //Assert
        PackLayout.Content(root).Should().Equal(outer);
        GridLayout.Content(outer).Should().Equal(inner);
    }

    [Fact]
    public void Info_reports_the_configured_options()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        GridLayout.Configure(a, new GridOptions
        {
            Row = 2,
            Column = 3,
            RowSpan = 2,
            ColumnSpan = 4,
            Sticky = Sticky.N | Sticky.E,
            PadLeft = 1,
            PadRight = 2,
            PadTop = 3,
            PadBottom = 4,
            IPadX = 5,
            IPadY = 6,
        });
        GridOptions info = GridLayout.Info(a);

        //Assert
        info.Row.Should().Be(2);
        info.Column.Should().Be(3);
        info.RowSpan.Should().Be(2);
        info.ColumnSpan.Should().Be(4);
        info.Sticky.Should().Be(Sticky.N | Sticky.E);
        info.PadLeft.Should().Be(1);
        info.PadRight.Should().Be(2);
        info.PadTop.Should().Be(3);
        info.PadBottom.Should().Be(4);
        info.IPadX.Should().Be(5);
        info.IPadY.Should().Be(6);
        info.In.Should().BeSameAs(root);
    }

    [Fact]
    public void Info_throws_for_an_ungridded_window()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");

        //Act
        Action act = () => GridLayout.Info(a);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Regridding_moves_a_window_from_pack_to_grid()
    {
        //Arrange (probed against real Tk 8.6.16: gridding pack's ONLY content
        //         of a container steals it and succeeds — the pack claim is
        //         released before grid claims)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow holder = root.CreateChild("holder");
        TkWindow w = holder.CreateChild("w");
        PackLayout.Configure(w, null);

        //Act
        GridLayout.Configure(w, null);

        //Assert
        PackLayout.Content(holder).Should().BeEmpty();
        GridLayout.Content(holder).Should().Equal(w);
    }

    [Fact]
    public void Regridding_fails_while_other_pack_content_remains()
    {
        //Arrange (probed against real Tk 8.6.16: with ANOTHER window still
        //         packed in the container, the geometry fight stands)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow holder = root.CreateChild("holder");
        TkWindow a = holder.CreateChild("a");
        TkWindow w = holder.CreateChild("w");
        PackLayout.Configure(a, null);
        PackLayout.Configure(w, null);

        //Act
        Action act = () => GridLayout.Configure(w, null);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already has slaves managed by pack*");
    }

    [Fact]
    public void Forget_makes_the_window_unmanaged_but_keeps_its_geometry()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        a.SetRequestedSize(50, 40);
        GridLayout.Configure(a, null);
        TkLayout.Update(root);

        //Act
        GridLayout.Forget(a);

        //Assert
        GridLayout.Content(root).Should().BeEmpty();
        a.IsDisplayed.Should().BeFalse();
        a.Width.Should().Be(50);
        a.Height.Should().Be(40);
    }

    [Fact]
    public void Size_counts_occupied_and_configured_slots()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        GridLayout.Configure(a, new GridOptions { Row = 1, Column = 2, ColumnSpan = 2 });
        GridLayout.ColumnConfigure(root, 6, minSize: 10);
        GridLayout.RowConfigure(root, 4, weight: 1);

        //Act
        int columns, rows;
        GridLayout.Size(root, out columns, out rows);

        //Assert
        columns.Should().Be(7);
        rows.Should().Be(5);
    }

    [Fact]
    public void GetPropagate_defaults_to_true_and_GetAnchor_to_nw()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();

        //Act / Assert
        GridLayout.GetPropagate(root).Should().BeTrue();
        GridLayout.GetAnchor(root).Should().Be(Anchor.NW);
    }

    [Fact]
    public void Destroy_of_a_container_releases_its_content()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow holder = root.CreateChild("holder");
        TkWindow w = root.CreateChild("w");
        GridLayout.Configure(w, new GridOptions { In = holder });

        //Act
        holder.Destroy();

        //Assert
        w.IsDestroyed.Should().BeFalse();
        GridLayout.Forget(w); // must be a harmless no-op: w is unmanaged now
        w.IsDisplayed.Should().BeFalse();
    }
}
