using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// End-to-end coverage of the Tcl command bridge in DIRECT mode: Tcl
/// scripts create widgets, lay them out with pack/grid, bind events,
/// read geometry after <c>update</c>, and drive the canvas — all through
/// the interpreter, exactly as an unmodified Tcl/Tk application would.
/// </summary>
public class TkTclBridgeTests : IDisposable
{
    private readonly Interpreter _interpreter;
    private readonly TkWindow _root;
    private readonly TkTclBridge _bridge;

    public TkTclBridgeTests()
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

    // ------------------------------------------------------------ creation

    [Fact]
    public void Widget_creation_returns_the_path_and_registers_the_instance_command()
    {
        Eval("frame .f").Should().Be(".f");
        Eval("button .f.b -text Hello").Should().Be(".f.b");
        Eval(".f.b cget -text").Should().Be("Hello");
    }

    [Fact]
    public void Ttk_widget_commands_create_the_unified_widgets()
    {
        Eval("ttk::frame .f").Should().Be(".f");
        Eval("ttk::button .f.b -text Go").Should().Be(".f.b");
        Eval("ttk::entry .f.e").Should().Be(".f.e");
        Eval("winfo class .f.b").Should().Be("Button");
    }

    [Fact]
    public void Creating_a_duplicate_path_fails_with_the_tk_error()
    {
        Eval("frame .f");
        string message;
        TryEval("frame .f", out message).Should().Be(ReturnCode.Error);
        message.Should().Contain("already exists in parent");
    }

    [Fact]
    public void Package_require_Tk_succeeds_after_bootstrap()
    {
        Eval("package require Tk").Should().Be("8.6");
        Eval("set tk_version").Should().Be("8.6");
    }

    // -------------------------------------------------------------- layout

    [Fact]
    public void Pack_and_update_produce_final_geometry_for_winfo()
    {
        //Arrange
        Eval("frame .f");
        Eval("pack .f -side top -fill both -expand 1");
        Eval("label .f.l -text Hi");
        Eval("pack .f.l -side left");

        //Act
        Eval("update");

        //Assert
        int.Parse(Eval("winfo width .f")).Should().Be(640);
        int.Parse(Eval("winfo height .f")).Should().Be(480);
        int.Parse(Eval("winfo reqwidth .f.l")).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Grid_with_weights_and_sticky_lays_out_through_the_oracle_verified_engine()
    {
        //Arrange
        Eval("frame .g");
        Eval("pack .g -fill both -expand 1");
        Eval("frame .g.a -width 50 -height 40");
        Eval("frame .g.b -width 50 -height 40");
        Eval("grid .g.a -row 0 -column 0 -sticky nsew");
        Eval("grid .g.b -row 0 -column 1 -sticky nsew");
        Eval("grid columnconfigure .g 0 -weight 1");
        Eval("grid columnconfigure .g 1 -weight 1");

        //Act
        Eval("update");

        //Assert
        int a = int.Parse(Eval("winfo width .g.a"));
        int b = int.Parse(Eval("winfo width .g.b"));
        (a + b).Should().Be(640);
    }

    [Fact]
    public void Pack_forget_removes_the_window_from_layout()
    {
        Eval("frame .f -width 100 -height 50");
        Eval("pack .f");
        Eval("update");
        Eval("pack forget .f");
        Eval("pack slaves .").Should().Be("");
    }

    // ------------------------------------------------------------ commands

    [Fact]
    public void Button_command_fires_through_invoke()
    {
        //Arrange
        Eval("set ::clicked 0");
        Eval("button .b -text Go -command {set ::clicked 1}");

        //Act
        Eval(".b invoke");

        //Assert
        Eval("set ::clicked").Should().Be("1");
    }

    [Fact]
    public void Bind_script_fires_with_percent_substitution_on_synthetic_input()
    {
        //Arrange
        Eval("frame .f -width 200 -height 100");
        Eval("pack .f");
        Eval("bind .f <ButtonPress-1> {set ::hit %W:%x:%y}");
        Eval("update");

        //Act — synthetic pointer press through the tree (root coordinates;
        //the 200x100 frame packs centered at the top: x = (640-200)/2 = 220).
        _root.Tree.PointerEvent(TkEventType.ButtonPress, 230, 20, 1);

        //Assert (%x/%y are window-relative)
        Eval("set ::hit").Should().Be(".f:10:20");
    }

    [Fact]
    public void Destroy_removes_the_window_and_its_instance_command()
    {
        Eval("frame .f");
        Eval("button .f.b");
        Eval("destroy .f");
        Eval("winfo exists .f").Should().Be("0");
        Eval("winfo exists .f.b").Should().Be("0");

        string message;
        TryEval(".f.b cget -text", out message).Should().Be(ReturnCode.Error);
    }

    // -------------------------------------------------------------- canvas

    [Fact]
    public void Canvas_subcommands_route_to_the_verbatim_execute_layer()
    {
        //Arrange
        Eval("canvas .c -width 300 -height 200");
        Eval("pack .c");

        //Act
        string id = Eval(".c create rectangle 10 10 60 40 -fill red -tags box");

        //Assert
        id.Should().Be("1");
        Eval(".c coords box").Should().Be("10.0 10.0 60.0 40.0");
        Eval(".c bbox box").Should().Be("9 9 61 41");
        Eval(".c itemcget box -fill").Should().Be("red");
    }

    // ----------------------------------------------------------- variables

    [Fact]
    public void Textvariable_write_updates_the_entry_and_read_pulls_it_back()
    {
        //Arrange
        Eval("entry .e -textvariable ::name");
        Eval("set ::name Inanna");

        //Act + Assert (write trace pushed the value into the widget)
        Eval(".e get").Should().Be("Inanna");

        //Widget-side edit flows back on variable read.
        Eval(".e insert end !");
        Eval("set ::name").Should().Be("Inanna!");
    }

    [Fact]
    public void Combobox_selection_writes_the_textvariable_immediately()
    {
        //Arrange — a readonly combobox linked to a variable, as DRAKON's
        //File-properties Language picker is.
        Eval("set ::lang <none>");
        Eval("ttk::combobox .cb -values {<none> C C# C++} -state readonly -textvariable ::lang");
        Eval("pack .cb");
        Eval("update");
        Eval("set ::lang").Should().Be("<none>");

        //Act — pick the third row from the drop-down, exactly as a mouse
        //release over the list does.
        var cb = (ComboboxWidget)_root.FindDescendant(".cb").Widget;
        cb.OpenDropDown();
        TkWindow dd = _root.Tree.WindowManager.Overlays[_root.Tree.WindowManager.Overlays.Count - 1].Window;
        int rowH = _root.Tree.Fonts.Metrics(_root.Tree.Fonts.GetNamed("TkTextFont")).LineSpace + 2;
        int y = dd.Y + 2 + 2 * rowH + rowH / 2; // row index 2 == "C#"
        _root.Tree.PointerEvent(TkEventType.ButtonRelease, dd.X + 5, y, 1);
        cb.Value.Should().Be("C#");

        //Assert — the variable reflects the choice on the very first read,
        //with no intervening read to trigger a pull (the bug: it stayed at
        //the old value until something else read it first).
        Eval("set ::lang").Should().Be("C#");
    }

    [Fact]
    public void Configure_single_option_returns_the_query_record()
    {
        //Arrange
        Eval("canvas .c -width 500 -height 300");

        //Act — the single-option query form (Tk returns
        //{-name dbName dbClass default current}); programs read [lindex .. end].
        string record = Eval(".c configure -width");

        //Assert
        record.Should().Be("-width width Width {} 500");
        Eval("lindex [.c configure -width] end").Should().Be("500");
    }

    [Fact]
    public void Raise_and_lower_error_on_an_unknown_window()
    {
        //Tk errors on an unknown window path; DRAKON's "raise this dialog or
        //build it if absent" idiom relies on catching it.
        TryEval("raise .nope", out string msg).Should().Be(ReturnCode.Error);
        msg.Should().Be("bad window path name \".nope\"");
        TryEval("lower .nope", out _).Should().Be(ReturnCode.Error);
    }

    [Fact]
    public void Checkbutton_variable_reflects_toggle_state_both_ways()
    {
        //Arrange
        Eval("checkbutton .c -variable ::flag -onvalue yes -offvalue no");

        //Assert — Tk initializes an unset variable to the off value.
        Eval("set ::flag").Should().Be("no");

        //Act — invoke toggles on.
        Eval(".c invoke");
        Eval("set ::flag").Should().Be("yes");

        //Tcl-side write drives the widget off again.
        Eval("set ::flag no");
        Eval(".c invoke");
        Eval("set ::flag").Should().Be("yes");
    }

    // ------------------------------------------------------------ menus/wm

    [Fact]
    public void Menus_build_configure_and_report_entries()
    {
        Eval("menu .m -tearoff 0");
        Eval(".m add command -label Open -accelerator Ctrl+O");
        Eval(".m add separator");
        Eval(".m add command -label Quit");
        Eval(".m entryconfigure 0 -state disabled");
        Eval(".m entrycget 0 -label").Should().Be("Open");
        Eval(".m entrycget 0 -state").Should().Be("disabled");
        Eval(".m index end").Should().Be("2");
        Eval(".m delete 0 1000");
        Eval(".m index end").Should().Be("none");
    }

    [Fact]
    public void Wm_title_and_geometry_work_on_a_toplevel()
    {
        Eval("toplevel .dlg");
        Eval("wm title .dlg {My Dialog}");
        Eval("wm title .dlg").Should().Be("My Dialog");
        Eval("wm geometry .dlg 300x200+50+60");
        Eval("update");
        Eval("winfo width .dlg").Should().Be("300");
        Eval("wm withdraw .dlg");
        Eval("wm deiconify .dlg");
    }

    [Fact]
    public void Wm_title_on_root_reaches_the_window_manager()
    {
        Eval("wm title . {DRAKON Editor}");
        _root.Tree.WindowManager.RootTitle.Should().Be("DRAKON Editor");
    }

    [Fact]
    public void Root_menu_configure_builds_the_menubar_presentation()
    {
        //Arrange — DRAKON's exact idiom.
        Eval("menu .mainmenu -tearoff 0");
        Eval("menu .mainmenu.file -tearoff 0");
        Eval(".mainmenu add cascade -label File -underline 0 -menu .mainmenu.file");
        Eval(".mainmenu.file add command -label Quit");

        //Act
        Eval(". configure -menu .mainmenu");
        Eval("update");

        //Assert — a bar window is packed across the top of the root and
        //  shares the menu's entries.
        _root.Children.Should().NotBeEmpty();
        var barWidget = _root.Children[_root.Children.Count - 1].Widget
            as CodeBrix.Platform.TkCanvas.Menus.MenuWidget;
        bool found = false;
        foreach (TkWindow child in _root.Children)
        {
            var menu = child.Widget as CodeBrix.Platform.TkCanvas.Menus.MenuWidget;
            if (menu != null && menu.IsMenubar)
            {
                found = true;
                menu.Entries.Count.Should().Be(1);
                menu.Entries[0].Label.Should().Be("File");
                child.Width.Should().Be(640);

                //entryconfigure on the Tcl path stays live in the bar.
                Eval(".mainmenu entryconfigure 0 -label Datei");
                menu.Entries[0].Label.Should().Be("Datei");
            }
        }
        found.Should().BeTrue();
        _ = barWidget;
    }

    [Fact]
    public void Root_menubar_packs_above_already_packed_content()
    {
        //Arrange — DRAKON's actual order: content packs FIRST, then
        //  ". configure -menu" attaches the bar (mwindow.tcl:259 vs :503).
        Eval("ttk::frame .root");
        Eval("pack .root -fill both -expand 1");
        Eval("menu .mainmenu -tearoff 0");
        Eval(".mainmenu add cascade -label File");

        //Act
        Eval(". configure -menu .mainmenu");
        Eval("update");

        //Assert — the bar sits at y=0 across the width; content starts below.
        TkWindow bar = null;
        foreach (TkWindow child in _root.Children)
        {
            var menu = child.Widget as CodeBrix.Platform.TkCanvas.Menus.MenuWidget;
            if (menu != null && menu.IsMenubar) { bar = child; }
        }
        bar.Should().NotBeNull();
        bar.Y.Should().Be(0);
        bar.Width.Should().Be(640);
        bar.Height.Should().BeGreaterThan(0);

        TkWindow content = ctxWindow(".root");
        content.Y.Should().Be(bar.Height);
        content.Height.Should().Be(480 - bar.Height);
    }

    [Fact]
    public void Text_window_dialog_sequence_keeps_one_buffer_line_and_measures_sanely()
    {
        //Arrange — DRAKON's text_window.tcl flow: a named main_font and a
        //  word-wrapped text widget filled before its first layout pass.
        Eval("font create main_font -family {Liberation Mono} -size 10");
        Eval("text .t -wrap word -font main_font -bd 0 -highlightthickness 0");
        Eval(".t insert 1.0 {Make salad}");
        Eval("pack .t -fill both -expand 1");
        Eval("update");

        //Assert — one buffer line, the -- guard accepted, and a sane measure.
        Eval(".t index end").Should().Be("2.0");
        Eval(".t get -- 1.0 end").Should().Be("Make salad\n");
        int measured = int.Parse(Eval("font measure main_font {Make salad}"));
        measured.Should().BeGreaterThan(20);
        measured.Should().BeLessThan(300);
    }

    [Fact]
    public void Toplevel_grid_with_columnspan_over_weighted_column_stretches_content()
    {
        //Reproduces DRAKON's text_window.tcl nesting: a toplevel whose grid
        //  content spans a weighted column must stretch to fill, so a text
        //  widget inside gets a real width (not 1px → vertical rendering).
        Eval("toplevel .tw");
        Eval("wm geometry .tw +40+40");

        Eval("ttk::frame .tw.root");
        Eval("grid .tw.root -column 0 -row 0 -sticky nwse");
        Eval("grid columnconfigure .tw 0 -weight 1");
        Eval("grid rowconfigure .tw 0 -weight 1");

        Eval("frame .tw.root.entry -borderwidth 1 -relief sunken");
        Eval("text .tw.root.entry.text -wrap word -width 80 -height 6");
        Eval("grid columnconfigure .tw.root.entry 1 -weight 1");
        Eval("grid rowconfigure .tw.root.entry 1 -weight 1");
        Eval("grid .tw.root.entry.text -row 1 -column 1 -sticky nswe");

        Eval("ttk::button .tw.root.ok -text Ok");
        Eval("grid columnconfigure .tw.root 2 -weight 1 -minsize 50");
        Eval("grid rowconfigure .tw.root 1 -weight 1 -minsize 50");
        Eval("grid .tw.root.entry -row 1 -column 1 -sticky nwse -columnspan 3 -padx 5 -pady 5");
        Eval("grid .tw.root.ok -row 2 -column 1 -padx 10 -pady 10");

        Eval("update");

        //The text widget must be wide enough to hold "Make salad" on one
        //  line (its requested 80-char width flows down through both grids).
        int width = int.Parse(Eval("winfo width .tw.root.entry.text"));
        width.Should().BeGreaterThan(200);
    }

    private TkWindow ctxWindow(string path)
    {
        Eval("winfo exists " + path).Should().Be("1");
        foreach (TkWindow child in _root.Children)
        {
            if (child.PathName == path) { return child; }
        }
        return null;
    }

    // ----------------------------------------------------- event loop misc

    [Fact]
    public void After_idle_scripts_run_during_update()
    {
        Eval("set ::ran 0");
        Eval("after idle {set ::ran 1}");
        Eval("update");
        Eval("set ::ran").Should().Be("1");
    }

    [Fact]
    public void After_cancel_prevents_the_script()
    {
        Eval("set ::ran 0");
        Eval("set id [after idle {set ::ran 1}]");
        Eval("after cancel $id");
        Eval("update");
        Eval("set ::ran").Should().Be("0");
    }

    [Fact]
    public void Tkwait_visibility_flushes_and_returns()
    {
        Eval("frame .f -width 10 -height 10");
        Eval("pack .f");
        Eval("tkwait visibility .f");
        int.Parse(Eval("winfo width .f")).Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------- dialogs

    [Fact]
    public void MessageBox_uses_the_modal_auto_responder_when_set()
    {
        _bridge.ModalAutoResponder = (command, words) =>
            command == "tk_messageBox" ? "yes" : null;

        Eval("tk_messageBox -type yesno -message {Save?}").Should().Be("yes");
    }

    [Fact]
    public void Tk_dialog_returns_the_pressed_button_index_via_responder()
    {
        _bridge.ModalAutoResponder = (command, words) =>
            command == "tk_dialog" ? "1" : null;

        Eval("tk_dialog .foo {Choose} {Open or create?} {} 0 Open Create Cancel")
            .Should().Be("1");
    }

    // ----------------------------------------------------- fonts and misc

    [Fact]
    public void Font_create_measure_and_metrics_flow_through_the_seam()
    {
        //Arrange
        Eval("font create big -family TkDefault -size 20");

        //Act
        int width = int.Parse(Eval("font measure big {Hello}"));
        string metrics = Eval("font metrics big");

        //Assert
        width.Should().BeGreaterThan(0);
        metrics.Should().Contain("-ascent");
        metrics.Should().Contain("-linespace");
    }

    [Fact]
    public void Clipboard_round_trips_through_the_tree()
    {
        Eval("clipboard clear");
        Eval("clipboard append {holy }");
        Eval("clipboard append Inanna");
        Eval("clipboard get").Should().Be("holy Inanna");
    }

    [Fact]
    public void Option_database_applies_at_widget_creation()
    {
        Eval("option add *Button.background green");
        Eval("button .b");
        Eval(".b cget -background").Should().Be("green");
    }

    [Fact]
    public void Selection_command_is_accepted_and_ignored()
    {
        Eval("selection clear").Should().Be("");
    }

    [Fact]
    public void Text_widget_core_operations_work_through_the_bridge()
    {
        //Arrange
        Eval("text .t");
        Eval("pack .t -fill both -expand 1");

        //Act
        Eval(".t insert end {hello world\nsecond line}");

        //Assert
        Eval(".t get 1.0 1.5").Should().Be("hello");
        Eval(".t index end").Should().Be("3.0");
        Eval(".t compare 1.0 < 2.0").Should().Be("1");

        Eval(".t tag add hot 1.0 1.5");
        Eval(".t tag configure hot -foreground red");
        Eval(".t tag ranges hot").Should().Be("1.0 1.5");

        Eval(".t mark set here 1.3");
        Eval(".t index here").Should().Be("1.3");

        Eval(".t delete 1.0 1.6");
        Eval(".t get 1.0 {1.0 lineend}").Should().Be("world");
    }
}
