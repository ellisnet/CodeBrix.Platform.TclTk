using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The option database's widget-facing behavior (B.12b): entries apply at
/// widget CREATION (for options not explicitly configured), later additions
/// never restyle existing widgets, and explicit configuration always wins.
/// The pattern-matching and priority semantics themselves are pinned by the
/// ThemingOracle fixtures.
/// </summary>
public class OptionDatabaseTests
{
    [Fact]
    public void Entries_apply_at_widget_creation()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        root.Tree.OptionDatabase.Add("*Button.background", "green", "widgetDefault");

        //Act
        var button = new ButtonWidget(root.CreateChild("b"));

        //Assert — the value reads back like a configured option (Tk's cget).
        button.Options.Get("-background").Should().Be("green");
    }

    [Fact]
    public void Later_additions_do_not_restyle_existing_widgets()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        root.Tree.OptionDatabase.Add("*Button.background", "green", "widgetDefault");
        var button = new ButtonWidget(root.CreateChild("b"));

        //Act — Tk-faithful: the database is consulted at creation only.
        root.Tree.OptionDatabase.Add("*Button.background", "red", "widgetDefault");

        //Assert
        button.Options.Get("-background").Should().Be("green");
    }

    [Fact]
    public void Explicit_configuration_wins_over_database_values()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        root.Tree.OptionDatabase.Add("*Button.background", "green", "widgetDefault");
        var button = new ButtonWidget(root.CreateChild("b"));

        //Act
        button.Configure(new System.Collections.Generic.Dictionary<string, string>
        {
            { "-background", "yellow" },
        });

        //Assert
        button.Options.Get("-background").Should().Be("yellow");
    }

    [Fact]
    public void Class_form_entries_feed_the_same_option()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        root.Tree.OptionDatabase.Add("*Foreground", "magenta", "widgetDefault");

        //Act
        var label = new LabelWidget(root.CreateChild("l"));

        //Assert — *Foreground (the option class) lands on -foreground.
        label.Options.Get("-foreground").Should().Be("magenta");
    }

    [Fact]
    public void Read_content_parses_resource_lines_comments_and_continuations()
    {
        //Arrange
        var database = new OptionDatabase();
        TkWindow root = TkWindow.CreateRoot();

        //Act
        database.ReadContent("! a comment\n*Button.foreground: dark\\\nred\n\n*background : #102030\n");

        //Assert
        database.Get(root, "background", "Background").Should().Be("#102030");
        TkWindow buttonWindow = root.CreateChild("b");
        buttonWindow.ClassName = "Button";
        database.Get(buttonWindow, "foreground", "Foreground").Should().Be("darkred");
    }

    [Fact]
    public void Clear_empties_the_database()
    {
        //Arrange
        var database = new OptionDatabase();
        database.Add("*foreground", "red", "widgetDefault");

        //Act
        database.Clear();

        //Assert
        database.IsEmpty.Should().BeTrue();
    }
}
