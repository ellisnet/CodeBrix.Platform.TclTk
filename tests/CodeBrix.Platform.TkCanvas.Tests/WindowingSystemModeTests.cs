using System;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Windows.System;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The bridge's <see cref="WindowingSystemMode"/>: the host-native default
/// (<c>aqua</c> on macOS, <c>win32</c> on Windows, <c>x11</c> elsewhere) and
/// the <see cref="WindowingSystemMode.X11"/> opt-out, which must move the
/// reported <c>tk windowingsystem</c>, the modifier-name layout, the
/// <c>%s</c> encoding and the host Command-key mapping TOGETHER. The host
/// operating system is injected (<c>TkWindowingSystem.HostOverride</c>), so
/// every host runs here on Linux.
/// </summary>
public class WindowingSystemModeTests
{
    private sealed class Session : IDisposable
    {
        internal Interpreter Interpreter;
        internal TkWindow Root;
        internal TkTclBridge Bridge;

        internal Session(string host, WindowingSystemMode mode)
        {
            TkWindowingSystem.HostOverride = host;
            Result result = null;
            Interpreter = Interpreter.Create(ref result, BooleanModeForTests.Mode);
            Root = TkWindow.CreateRoot();
            Root.SetForcedSize(640, 480);
            Result error = null;
            TkBootstrap.Register(Interpreter, ref error);
            Bridge = TkTclBridge.Register(Interpreter, Root.Tree, mode);
        }

        internal string Eval(string script)
        {
            Result result = null;
            ReturnCode code = Interpreter.EvaluateScript(script, ref result);
            if (code != ReturnCode.Ok)
            {
                throw new InvalidOperationException(
                    "script failed: " + (result != null ? result.ToString() : "(null)"));
            }
            return result != null ? result.ToString() : string.Empty;
        }

        internal void Key(string keySym, EventModifiers state)
        {
            Root.Tree.KeyEvent(TkEventType.KeyPress, keySym, "", state);
        }

        public void Dispose()
        {
            TkWindowingSystem.HostOverride = null;
            Bridge.Dispose();
            Interpreter.Dispose();
        }
    }

    [Fact]
    public void Mac_host_defaults_to_aqua_and_the_command_key()
    {
        //Arrange
        using var session = new Session(TkWindowingSystem.Aqua, WindowingSystemMode.HostNative);
        session.Eval("bind . <Command-KeyPress> {set ::hit %K:%s}");
        session.Eval("bind . <Command-Return> {set ::ret %K:%s}");
        session.Eval("bind . <Option-KeyPress> {set ::opt %K:%s}");
        session.Eval("set ::hit none; set ::ret none; set ::opt none");

        //Act
        session.Key("o", EventModifiers.Command);
        session.Key("Return", EventModifiers.Command);
        session.Key("x", EventModifiers.Alt);

        //Assert — Tk on macOS reports Command as Mod1 (8), Option as Mod2 (16).
        session.Eval("tk windowingsystem").Should().Be("aqua");
        session.Eval("set ::hit").Should().Be("o:8");
        session.Eval("set ::ret").Should().Be("Return:8");
        session.Eval("set ::opt").Should().Be("x:16");
        session.Root.Tree.WindowingSystem.Should().Be("aqua");
        TkKeyMapper.FromVirtualKeyModifiers(VirtualKeyModifiers.Windows, session.Root.Tree.WindowingSystem)
            .Should().Be(EventModifiers.Command);
    }

    [Fact]
    public void Mac_host_opted_out_behaves_as_x11_throughout()
    {
        //Arrange
        using var session = new Session(TkWindowingSystem.Aqua, WindowingSystemMode.X11);
        session.Eval("bind . <Command-KeyPress> {set ::hit %K:%s}");
        session.Eval("set ::hit none");

        //Act — the Command key is not a modifier; Command = Mod1 = Alt.
        session.Key("o", EventModifiers.Command);
        string afterCommandKey = session.Eval("set ::hit");
        session.Key("y", EventModifiers.Alt);

        //Assert (wish 8.6.16, x11: <Command-KeyPress> with -state 8 -> 8)
        session.Eval("tk windowingsystem").Should().Be("x11");
        afterCommandKey.Should().Be("none");
        session.Eval("set ::hit").Should().Be("y:8");
        TkKeyMapper.FromVirtualKeyModifiers(VirtualKeyModifiers.Windows, session.Root.Tree.WindowingSystem)
            .Should().Be(EventModifiers.None);
    }

    [Fact]
    public void Linux_host_is_x11_with_or_without_the_opt_out()
    {
        foreach (WindowingSystemMode mode in new[] { WindowingSystemMode.HostNative, WindowingSystemMode.X11 })
        {
            //Arrange
            using var session = new Session(TkWindowingSystem.X11, mode);
            session.Eval("bind . <Command-KeyPress> {set ::any %s}");
            session.Eval("set ::any none");

            //Act
            session.Key("y", EventModifiers.Alt);

            //Assert (wish 8.6.16, x11: <Command-KeyPress> state 8 -> 8)
            session.Eval("tk windowingsystem").Should().Be("x11");
            session.Eval("set ::any").Should().Be("8");
        }
    }

    [Fact]
    public void Windows_host_reports_win32_and_keeps_alt_separate_from_mod1()
    {
        //Arrange
        using var session = new Session(TkWindowingSystem.Win32, WindowingSystemMode.HostNative);
        session.Eval("bind . <Command-KeyPress> {set ::cmd %K}");
        session.Eval("bind . <Alt-KeyPress> {set ::alt %K:%s}");
        session.Eval("bind . <Control-KeyPress> {set ::ctl %K:%s}");
        session.Eval("set ::cmd none; set ::alt none; set ::ctl none");

        //Act
        session.Key("x", EventModifiers.Alt);
        session.Key("s", EventModifiers.Control);

        //Assert — Tk on Windows: Mod1 (= Command) is NumLock, never reported
        //  here; Alt is its own mask, 0x20000 in %s; Control is 4.
        session.Eval("tk windowingsystem").Should().Be("win32");
        session.Eval("set ::cmd").Should().Be("none");
        session.Eval("set ::alt").Should().Be("x:131072");
        session.Eval("set ::ctl").Should().Be("s:4");
    }

    [Fact]
    public void Windows_host_opted_out_reports_x11()
    {
        //Arrange
        using var session = new Session(TkWindowingSystem.Win32, WindowingSystemMode.X11);
        session.Eval("bind . <Command-KeyPress> {set ::any %s}");
        session.Eval("set ::any none");

        //Act
        session.Key("y", EventModifiers.Alt);

        //Assert
        session.Eval("tk windowingsystem").Should().Be("x11");
        session.Eval("set ::any").Should().Be("8");
    }

    [Fact]
    public void A_tree_keeps_the_windowing_system_its_first_bridge_chose()
    {
        //Arrange
        using var session = new Session(TkWindowingSystem.Aqua, WindowingSystemMode.X11);
        Result result = null;
        using Interpreter second = Interpreter.Create(ref result, BooleanModeForTests.Mode);

        //Act
        Action conflicting = () => TkTclBridge.Register(second, session.Root.Tree, WindowingSystemMode.HostNative);

        //Assert
        conflicting.Should().Throw<InvalidOperationException>();
        session.Eval("tk windowingsystem").Should().Be("x11");
    }
}
