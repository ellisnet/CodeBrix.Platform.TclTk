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

    // ------------------------------------------------- Command / Option

    [Fact]
    public void Command_is_mod1_under_x11()
    {
        //Arrange (wish 8.6.16, x11: <Command-KeyPress-x>, <Mod1-KeyPress-x>
        //  and <M1-KeyPress-x> are ONE pattern — "bind ." lists only
        //  <Mod1-Key-x> — and "<Command-KeyPress>" fired for -state 8 (Mod1,
        //  the Alt key) with %s = 8)
        string x11 = TkWindowingSystem.X11;

        //Act
        EventPattern command = EventPattern.Parse("<Command-KeyPress-x>", x11);

        //Assert
        command.Should().Be(EventPattern.Parse("<Mod1-KeyPress-x>", x11));
        command.Should().Be(EventPattern.Parse("<M1-KeyPress-x>", x11));
        command.Should().Be(EventPattern.Parse("<Alt-KeyPress-x>", x11));
        command.Modifiers.Should().Be(EventModifiers.Alt);
        command.Matches(Key("x", EventModifiers.Alt)).Should().BeTrue();
        command.Matches(Key("x")).Should().BeFalse();
    }

    [Fact]
    public void Cmd_is_not_a_modifier_name()
    {
        //Arrange (wish 8.6.16: bind . <Cmd-KeyPress-x> -> bad event type or
        //  keysym "Cmd"; the toolkit accepts unknown keysyms, which never match)
        foreach (string system in new[] { TkWindowingSystem.X11, TkWindowingSystem.Aqua, TkWindowingSystem.Win32 })
        {
            //Act
            EventPattern cmd = EventPattern.Parse("<Cmd-KeyPress-x>", system);

            //Assert
            cmd.Modifiers.Should().Be(EventModifiers.None);
            cmd.Matches(Key("x", EventModifiers.Command)).Should().BeFalse();
            cmd.Matches(Key("x", EventModifiers.Alt)).Should().BeFalse();
        }
    }

    [Fact]
    public void Command_and_option_are_the_mac_keys_under_aqua()
    {
        //Arrange — Tk on macOS (aqua): Command = Mod1 = M1 is the Command
        //  key, Option = Mod2 = M2 is the Option key, which Alt also names.
        string aqua = TkWindowingSystem.Aqua;

        //Act
        EventPattern command = EventPattern.Parse("<Command-KeyPress>", aqua);
        EventPattern option = EventPattern.Parse("<Option-KeyPress>", aqua);

        //Assert
        command.Modifiers.Should().Be(EventModifiers.Command);
        EventPattern.Parse("<Mod1-KeyPress>", aqua).Should().Be(command);
        EventPattern.Parse("<M1-KeyPress>", aqua).Should().Be(command);
        option.Modifiers.Should().Be(EventModifiers.Alt);
        EventPattern.Parse("<Mod2-KeyPress>", aqua).Should().Be(option);
        EventPattern.Parse("<M2-KeyPress>", aqua).Should().Be(option);
        EventPattern.Parse("<Alt-KeyPress>", aqua).Should().Be(option);

        command.Matches(Key("o", EventModifiers.Command)).Should().BeTrue();
        command.Matches(Key("o", EventModifiers.Alt)).Should().BeFalse();
        command.Matches(Key("o", EventModifiers.Control)).Should().BeFalse();
        EventPattern.Parse("<Control-KeyPress>", aqua).Matches(Key("o", EventModifiers.Command)).Should().BeFalse();
        EventPattern.Parse("<Command-Shift-KeyPress>", aqua)
            .Matches(Key("Z", EventModifiers.Command | EventModifiers.Shift)).Should().BeTrue();
        EventPattern.Parse("<Command-Return>", aqua)
            .Matches(Key("Return", EventModifiers.Command)).Should().BeTrue();
    }

    [Fact]
    public void Mod1_is_not_the_alt_key_under_win32()
    {
        //Arrange — Tk on Windows (win32; not probed here, the local wish is
        //  x11): Mod1 = M1 = Command is NumLock, which the toolkit never
        //  reports, so those patterns never fire; Alt is its own modifier;
        //  Option/Mod2/M2 stay plain words as under x11.
        string win32 = TkWindowingSystem.Win32;

        //Act
        EventPattern command = EventPattern.Parse("<Command-KeyPress-x>", win32);
        EventPattern alt = EventPattern.Parse("<Alt-KeyPress-x>", win32);

        //Assert
        EventPattern.Parse("<Mod1-KeyPress-x>", win32).Should().Be(command);
        command.Should().NotBe(alt);
        command.Matches(Key("x", EventModifiers.Alt)).Should().BeFalse();
        alt.Matches(Key("x", EventModifiers.Alt)).Should().BeTrue();
        EventPattern.Parse("<Option-KeyPress-x>", win32).Modifiers.Should().Be(EventModifiers.None);
    }

    [Fact]
    public void State_value_follows_the_windowing_system()
    {
        //Arrange / Act / Assert — %s: x11 keeps the toolkit's X-compatible
        //  bits; aqua reports Command as Mod1 (8) and Option as Mod2 (16);
        //  win32 reports Alt as 0x20000.
        string x11 = TkWindowingSystem.X11;
        TkWindowingSystem.StateValue(EventModifiers.Alt, x11).Should().Be(8);
        TkWindowingSystem.StateValue(EventModifiers.Control | EventModifiers.Shift, x11).Should().Be(5);
        TkWindowingSystem.StateValue(EventModifiers.Meta, x11).Should().Be(16);

        string aqua = TkWindowingSystem.Aqua;
        TkWindowingSystem.StateValue(EventModifiers.Command, aqua).Should().Be(8);
        TkWindowingSystem.StateValue(EventModifiers.Alt, aqua).Should().Be(16);
        TkWindowingSystem.StateValue(EventModifiers.Command | EventModifiers.Shift, aqua).Should().Be(9);
        TkWindowingSystem.StateValue(EventModifiers.Control | EventModifiers.Button1, aqua).Should().Be(4 | 256);

        string win32 = TkWindowingSystem.Win32;
        TkWindowingSystem.StateValue(EventModifiers.Alt, win32).Should().Be(131072);
        TkWindowingSystem.StateValue(EventModifiers.Control | EventModifiers.Shift, win32).Should().Be(5);
    }
}
