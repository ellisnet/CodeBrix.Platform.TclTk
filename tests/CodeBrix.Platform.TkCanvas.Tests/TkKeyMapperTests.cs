using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Hosting;
using SilverAssertions;
using Windows.System;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The host-key half of the Command modifier. The CodeBrix.Platform macOS
/// head reports the Command key as <c>VirtualKey.LeftWindows</c> and the
/// <c>VirtualKeyModifiers.Windows</c> flag (the same flag its own text
/// controls treat as the macOS shortcut key); the Option key arrives as
/// <c>VirtualKeyModifiers.Menu</c>. These tests inject that modifier state
/// directly, so they run on any machine.
/// </summary>
public class TkKeyMapperTests
{
    [Fact]
    public void Windows_flag_is_the_command_modifier_under_aqua()
    {
        //Arrange
        string aqua = TkWindowingSystem.Aqua;

        //Act
        EventModifiers command = TkKeyMapper.FromVirtualKeyModifiers(VirtualKeyModifiers.Windows, aqua);
        EventModifiers all = TkKeyMapper.FromVirtualKeyModifiers(
            VirtualKeyModifiers.Windows | VirtualKeyModifiers.Shift |
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, aqua);

        //Assert
        command.Should().Be(EventModifiers.Command);
        all.Should().Be(EventModifiers.Command | EventModifiers.Shift |
            EventModifiers.Control | EventModifiers.Alt);
    }

    [Fact]
    public void Windows_flag_is_not_a_tk_modifier_under_x11_or_win32()
    {
        //Arrange — on Linux and Windows hosts, and on a Mac opted out to x11,
        //  the Windows/Super/Command key maps to no Tk modifier.
        foreach (string system in new[] { TkWindowingSystem.X11, TkWindowingSystem.Win32 })
        {
            //Act
            EventModifiers state = TkKeyMapper.FromVirtualKeyModifiers(
                VirtualKeyModifiers.Windows | VirtualKeyModifiers.Control, system);

            //Assert
            state.Should().Be(EventModifiers.Control);
            TkKeyMapper.FromVirtualKeyModifiers(VirtualKeyModifiers.Menu, system).Should().Be(EventModifiers.Alt);
        }
    }

    [Fact]
    public void View_key_with_command_maps_to_the_letter_keysym_without_text()
    {
        //Act
        string keySym;
        string character;
        bool mapped = TkKeyMapper.TryMapViewKey(VirtualKey.O, EventModifiers.Command,
            out keySym, out character);
        string shiftedKeySym;
        string shiftedCharacter;
        TkKeyMapper.TryMapViewKey(VirtualKey.Z, EventModifiers.Command | EventModifiers.Shift,
            out shiftedKeySym, out shiftedCharacter);
        string digitKeySym;
        string digitCharacter;
        TkKeyMapper.TryMapViewKey(VirtualKey.Number1, EventModifiers.Command,
            out digitKeySym, out digitCharacter);

        //Assert — like Control, a Command chord produces no %A text.
        mapped.Should().BeTrue();
        keySym.Should().Be("o");
        character.Should().Be("");
        shiftedKeySym.Should().Be("Z");
        shiftedCharacter.Should().Be("");
        digitKeySym.Should().Be("1");
        digitCharacter.Should().Be("");
    }

    [Fact]
    public void Text_input_forwards_command_letters_as_key_events()
    {
        //Act
        string commandKeySym;
        bool command = TkKeyMapper.TryMapSpecialOrShortcut(VirtualKey.Z, EventModifiers.Command,
            out commandKeySym);
        string controlKeySym;
        bool control = TkKeyMapper.TryMapSpecialOrShortcut(VirtualKey.Z, EventModifiers.Control,
            out controlKeySym);
        string plainKeySym;
        bool plain = TkKeyMapper.TryMapSpecialOrShortcut(VirtualKey.Z, EventModifiers.None,
            out plainKeySym);
        string specialKeySym;
        bool special = TkKeyMapper.TryMapSpecialOrShortcut(VirtualKey.Enter, EventModifiers.Command,
            out specialKeySym);

        //Assert — plain letters still arrive as text, not key events.
        command.Should().BeTrue();
        commandKeySym.Should().Be("z");
        control.Should().BeTrue();
        controlKeySym.Should().Be("z");
        plain.Should().BeFalse();
        special.Should().BeTrue();
        specialKeySym.Should().Be("Return");
    }
}
