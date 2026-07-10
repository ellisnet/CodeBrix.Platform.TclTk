using System;

using CodeBrix.Platform.TkCanvas.Events;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Unit tests for event-pattern parsing, matching, and specificity. Key
/// patterns are covered here rather than in the wish oracle: real Tk routes
/// key events through its focus filter (they need genuine X input focus), so
/// headless <c>event generate</c> drops them — verified while building the
/// bind oracle. The matching rules below mirror the Tk documentation and the
/// button-pattern behavior the oracle DOES verify.
/// </summary>
public class EventPatternTests
{
    private static TkEvent Key(string keySym, EventModifiers state = EventModifiers.None)
    {
        return new TkEvent { Type = TkEventType.KeyPress, KeySym = keySym, State = state };
    }

    [Fact]
    public void Parse_bare_keysym_is_a_keypress_pattern()
    {
        //Arrange / Act
        EventPattern pattern = EventPattern.Parse("<Escape>");

        //Assert
        pattern.Type.Should().Be(TkEventType.KeyPress);
        pattern.KeySym.Should().Be("Escape");
    }

    [Fact]
    public void Parse_button_shorthand_is_a_buttonpress_pattern()
    {
        //Arrange / Act
        EventPattern pattern = EventPattern.Parse("<1>");

        //Assert
        pattern.Type.Should().Be(TkEventType.ButtonPress);
        pattern.Button.Should().Be(1);
    }

    [Fact]
    public void Parse_double_shorthand_carries_the_click_count()
    {
        //Arrange / Act
        EventPattern pattern = EventPattern.Parse("<Double-1>");

        //Assert
        pattern.Type.Should().Be(TkEventType.ButtonPress);
        pattern.Button.Should().Be(1);
        (pattern.Modifiers & EventModifiers.Double).Should().Be(EventModifiers.Double);
    }

    [Fact]
    public void Parse_modifiers_and_keysym_detail()
    {
        //Arrange / Act
        EventPattern pattern = EventPattern.Parse("<Control-Shift-KeyPress-s>");

        //Assert
        pattern.Type.Should().Be(TkEventType.KeyPress);
        pattern.KeySym.Should().Be("s");
        pattern.Modifiers.Should().Be(EventModifiers.Control | EventModifiers.Shift);
    }

    [Fact]
    public void Parse_virtual_event()
    {
        //Arrange / Act
        EventPattern pattern = EventPattern.Parse("<<ListboxSelect>>");

        //Assert
        pattern.Type.Should().Be(TkEventType.Virtual);
        pattern.VirtualName.Should().Be("ListboxSelect");
    }

    [Fact]
    public void Parse_rejects_garbage()
    {
        //(An unknown word like <Bogus> is ACCEPTED as a keysym pattern — the
        // toolkit has no keysym table, so unknown keysyms simply never match;
        // that follows the accept-and-no-op deferral discipline.)
        ((Action)(() => EventPattern.Parse("noangles"))).Should().Throw<ArgumentException>();
        ((Action)(() => EventPattern.Parse("<ButtonPress-9>"))).Should().Throw<ArgumentException>();
        ((Action)(() => EventPattern.Parse("<>"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Match_keysym_detail_requires_the_keysym()
    {
        //Arrange
        EventPattern down = EventPattern.Parse("<KeyPress-Down>");

        //Act / Assert
        down.Matches(Key("Down")).Should().BeTrue();
        down.Matches(Key("Up")).Should().BeFalse();
    }

    [Fact]
    public void Match_generic_keypress_accepts_any_keysym()
    {
        //Arrange
        EventPattern any = EventPattern.Parse("<KeyPress>");

        //Act / Assert
        any.Matches(Key("Down")).Should().BeTrue();
        any.Matches(Key("x")).Should().BeTrue();
    }

    [Fact]
    public void Match_demanded_modifiers_must_be_present_but_extras_are_fine()
    {
        //Arrange
        EventPattern ctrl = EventPattern.Parse("<Control-KeyPress>");

        //Act / Assert
        ctrl.Matches(Key("x")).Should().BeFalse();
        ctrl.Matches(Key("x", EventModifiers.Control)).Should().BeTrue();
        ctrl.Matches(Key("x", EventModifiers.Control | EventModifiers.Shift)).Should().BeTrue();
    }

    [Fact]
    public void Match_double_requires_the_click_count()
    {
        //Arrange
        EventPattern dbl = EventPattern.Parse("<Double-ButtonPress-1>");
        var single = new TkEvent { Type = TkEventType.ButtonPress, Button = 1, ClickCount = 1 };
        var doubleClick = new TkEvent { Type = TkEventType.ButtonPress, Button = 1, ClickCount = 2 };

        //Act / Assert
        dbl.Matches(single).Should().BeFalse();
        dbl.Matches(doubleClick).Should().BeTrue();
    }

    [Fact]
    public void Specificity_detail_beats_modifiers_beats_generic()
    {
        //Arrange
        int generic = EventPattern.Parse("<KeyPress>").Specificity();
        int ctrl = EventPattern.Parse("<Control-KeyPress>").Specificity();
        int detail = EventPattern.Parse("<KeyPress-Down>").Specificity();
        int ctrlDetail = EventPattern.Parse("<Control-KeyPress-Down>").Specificity();

        //Act / Assert
        (ctrl > generic).Should().BeTrue();
        (detail > ctrl).Should().BeTrue();
        (ctrlDetail > detail).Should().BeTrue();
    }
}
