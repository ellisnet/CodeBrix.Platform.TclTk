using System;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Menus;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The menu geometry subcommands (<c>yposition</c>, <c>xposition</c>, and
/// the <c>@y</c> index form) through the Tcl bridge. The rules were probed on
/// REAL Tk 8.6.16 (<c>wish</c>) with a -tearoff 0 menu holding
/// {command One, separator, command Two, cascade Three}:
/// <code>
/// ypos 0 = 1  xpos 0 = 1        ypos 1 = 25 (the separator)
/// ypos 2 = 44                   ypos 3 = 68   ypos end = 68
/// ypos 4: 68 | ypos 99: 68      (past the end clamps to the last entry)
/// ypos -1: bad menu entry index "-1"   ypos bogus: bad menu entry index "bogus"
/// ypos none: 0 | ypos active: 0 (no active entry)
/// noarg / toomany: wrong # args: should be ".m yposition index"
/// label: ypos Two = 44
/// index @0 none, @1 0, @24 0, @25 1, @30 1 (separators are indexable),
///   @44 2, @999 none, @-5 none, @3,30 1, @abc: bad menu entry index "@abc"
/// empty -tearoff 0 menu: ypos 0 = 0, ypos end = 0, xpos 0 = 0
/// </code>
/// Pixel values differ (fonts differ from wish); the asserted rules are that
/// each entry's position is the top (left) edge of the rectangle the menu
/// draws it in, plus the clamping, error and none/active cases verbatim.
/// </summary>
public class MenuDispatchTests : IDisposable
{
    private readonly Interpreter _interpreter;
    private readonly TkWindow _root;
    private readonly TkTclBridge _bridge;

    public MenuDispatchTests()
    {
        Result result = null;
        _interpreter = Interpreter.Create(ref result, BooleanModeForTests.Mode);
        _interpreter.Should().NotBeNull();

        _root = TkWindow.CreateRoot();
        _root.SetForcedSize(640, 480);

        Result error = null;
        TkBootstrap.Register(_interpreter, ref error);
        _bridge = TkTclBridge.Register(_interpreter, _root.Tree);
    }

    public void Dispose()
    {
        _bridge.Dispose();
        _interpreter.Dispose();
    }

    private string Eval(string script)
    {
        Result result = null;
        ReturnCode code = _interpreter.EvaluateScript(script, ref result);
        if (code != ReturnCode.Ok)
        {
            throw new InvalidOperationException(
                "script failed: " + (result != null ? result.ToString() : "(null)"));
        }
        return result != null ? result.ToString() : string.Empty;
    }

    private ReturnCode TryEval(string script, out string message)
    {
        Result result = null;
        ReturnCode code = _interpreter.EvaluateScript(script, ref result);
        message = result != null ? result.ToString() : string.Empty;
        return code;
    }

    private MenuWidget BuildMenu()
    {
        Eval("menu .m -tearoff 0");
        Eval(".m add command -label One");
        Eval(".m add separator");
        Eval(".m add command -label Two");
        Eval("menu .m.s -tearoff 0");
        Eval(".m add cascade -label Three -menu .m.s");
        return _bridge.Context.MenusByPath[".m"];
    }

    private static string Num(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Yposition_returns_the_top_of_each_entry()
    {
        //Arrange
        MenuWidget menu = BuildMenu();

        //Act + Assert
        for (int i = 0; i < 4; i++)
        {
            SKRectI rect = menu.EntryRect(i);
            Eval(".m yposition " + Num(i)).Should().Be(Num(rect.Top));
        }
        Eval(".m yposition end").Should().Be(Num(menu.EntryRect(3).Top));
        Eval(".m yposition Two").Should().Be(Num(menu.EntryRect(2).Top));

        // Entries stack: the separator is shorter than a command entry.
        int y0 = int.Parse(Eval(".m yposition 0"));
        int y1 = int.Parse(Eval(".m yposition 1"));
        int y2 = int.Parse(Eval(".m yposition 2"));
        (y1 > y0).Should().BeTrue();
        (y2 - y1 < y1 - y0).Should().BeTrue();
    }

    [Fact]
    public void Yposition_clamps_past_the_end_and_reports_zero_for_none()
    {
        //Arrange
        MenuWidget menu = BuildMenu();
        string last = Num(menu.EntryRect(3).Top);

        //Act + Assert (wish: ypos 4 = 68, ypos 99 = 68, none = 0, active = 0)
        Eval(".m yposition 4").Should().Be(last);
        Eval(".m yposition 99").Should().Be(last);
        Eval(".m yposition none").Should().Be("0");
        Eval(".m yposition active").Should().Be("0");
        Eval(".m xposition none").Should().Be("0");
    }

    [Fact]
    public void Yposition_errors_match_tk()
    {
        //Arrange
        BuildMenu();
        string message;

        //Act + Assert
        TryEval(".m yposition -1", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("bad menu entry index \"-1\"");
        TryEval(".m yposition", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("wrong # args: should be \".m yposition index\"");
        TryEval(".m yposition 1 2", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("wrong # args: should be \".m yposition index\"");
        TryEval(".m xposition", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("wrong # args: should be \".m xposition index\"");
    }

    [Fact]
    public void Positions_in_an_empty_menu_are_zero()
    {
        //Arrange
        Eval("menu .e -tearoff 0");

        //Act + Assert (wish: empty: ypos 0 = 0 ypos end = 0; xpos 0 = 0)
        Eval(".e yposition 0").Should().Be("0");
        Eval(".e yposition end").Should().Be("0");
        Eval(".e xposition 0").Should().Be("0");
        Eval(".e index end").Should().Be("none");
    }

    [Fact]
    public void Unknown_labels_are_bad_entry_indexes()
    {
        //Arrange
        BuildMenu();
        string message;

        //Act + Assert (wish: .m yposition bogus -> bad menu entry index
        //"bogus"; .m xposition -5 -> bad menu entry index "-5")
        TryEval(".m yposition bogus", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("bad menu entry index \"bogus\"");
        TryEval(".m xposition -5", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("bad menu entry index \"-5\"");
    }

    [Fact]
    public void Xposition_of_a_popup_entry_is_its_left_edge()
    {
        //Arrange
        MenuWidget menu = BuildMenu();

        //Act + Assert (wish: xpos of every popup entry = 1, the border)
        Eval(".m xposition 0").Should().Be(Num(menu.EntryRect(0).Left));
        Eval(".m xposition end").Should().Be(Num(menu.EntryRect(3).Left));
    }

    [Fact]
    public void Index_at_y_resolves_entries_including_separators()
    {
        //Arrange
        MenuWidget menu = BuildMenu();
        SKRectI separator = menu.EntryRect(1);
        SKRectI two = menu.EntryRect(2);

        //Act + Assert (wish: @0 none, @5 0, @30 1, @44 2, @43 1, @999 none)
        Eval(".m index @0").Should().Be("none");
        Eval(".m index @" + Num(menu.EntryRect(0).Top + 3)).Should().Be("0");
        Eval(".m index @" + Num(separator.Top + 1)).Should().Be("1");
        Eval(".m index @" + Num(two.Top)).Should().Be("2");
        Eval(".m index @" + Num(two.Top - 1)).Should().Be("1");
        Eval(".m index @999").Should().Be("none");
        Eval(".m index @-5").Should().Be("none");

        //wish: index @3,30 = 1 (the y after the comma is used for a popup)
        Eval(".m index @3," + Num(separator.Top + 1)).Should().Be("1");

        //wish: index @abc -> bad menu entry index "@abc"
        string message;
        TryEval(".m index @abc", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("bad menu entry index \"@abc\"");
    }
}
