using System;
using System.Collections.Generic;

using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Tcl;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// The listbox <c>-listvariable</c> link, driven through the Tcl bridge in
/// DIRECT mode. Every expected value was probed on REAL Tk 8.6.16
/// (<c>wish</c>, Debian, x11) first; the probe output each assertion mirrors
/// is quoted beside it. Real Tk links the variable with C-level traces that
/// <c>trace info</c> never lists; the toolkit's link is a script-level trace,
/// so the <c>trace info</c> assertions below only check that a detached or
/// destroyed link leaves nothing behind.
/// </summary>
public class VariableLinksTests : IDisposable
{
    private readonly Interpreter _interpreter;
    private readonly TkWindow _root;
    private readonly TkTclBridge _bridge;

    public VariableLinksTests()
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

    [Fact]
    public void Listvariable_on_create_shows_the_variables_list_and_cget_returns_the_name()
    {
        //Arrange
        Eval("set ::v {a b {c d}}");

        //Act
        Eval("listbox .l -listvariable v -height 3");

        //Assert (wish: create: size=3 items=a b {c d} get2=c d cget=v)
        Eval(".l size").Should().Be("3");
        Eval(".l get 0 end").Should().Be("a b {c d}");
        Eval(".l get 2").Should().Be("c d");
        Eval(".l cget -listvariable").Should().Be("v");
    }

    [Fact]
    public void Writes_to_the_variable_update_the_listbox_from_any_scope()
    {
        //Arrange
        Eval("set ::v {a b {c d}}");
        Eval("listbox .l -listvariable v");

        //Act + Assert (wish: lappend: a b {c d} e; lset: Q b {c d} e)
        Eval("lappend ::v e");
        Eval(".l get 0 end").Should().Be("a b {c d} e");
        Eval("lset ::v 0 Q");
        Eval(".l get 0 end").Should().Be("Q b {c d} e");

        //wish: proc p {} { upvar #0 v w; set w {1 2 3 4 5 6} } -> upvar: 6
        Eval("proc p {} { upvar #0 v w; set w {1 2 3 4 5 6} }");
        Eval("p");
        Eval(".l size").Should().Be("6");
        Eval(".l get 0 end").Should().Be("1 2 3 4 5 6");

        //wish: proc g {} { global v; lappend v 7 } -> global: 7 end=7
        Eval("proc g {} { global v; lappend v 7 }");
        Eval("g");
        Eval(".l size").Should().Be("7");
        Eval(".l get end").Should().Be("7");
    }

    [Fact]
    public void Insert_and_delete_write_back_to_the_variable()
    {
        //Arrange
        Eval("set ::v {a b c d}");
        Eval("listbox .l -listvariable v");

        //Act + Assert (wish: ins end: a b c d e; ins 1: a X Y b c d e;
        //del 0: X Y b c d e; del 1 2: X c d e; ins spaced: X c d e {a b})
        Eval(".l insert end e");
        Eval("set ::v").Should().Be("a b c d e");
        Eval(".l insert 1 X Y");
        Eval("set ::v").Should().Be("a X Y b c d e");
        Eval(".l delete 0");
        Eval("set ::v").Should().Be("X Y b c d e");
        Eval(".l delete 1 2");
        Eval("set ::v").Should().Be("X c d e");
        Eval(".l insert end {a b}");
        Eval("set ::v").Should().Be("X c d e {a b}");
        Eval(".l get end").Should().Be("a b");
    }

    [Fact]
    public void Insert_write_back_fires_other_write_traces_on_the_variable()
    {
        //Arrange
        Eval("set ::v2 {a b}");
        Eval("listbox .l -listvariable v2");
        Eval("trace add variable ::v2 write {lappend ::tr}");
        Eval("set ::tr {}");

        //Act (wish: user trace on insert: v2 {} write)
        Eval(".l insert end c");

        //Assert
        Eval("llength $::tr").Should().Be("3");
        Eval("lindex $::tr 2").Should().Be("write");
        Eval("set ::v2").Should().Be("a b c");
    }

    [Fact]
    public void A_missing_variable_is_created_from_the_listbox_items()
    {
        //Arrange (wish: listbox .l2 -listvariable w2 -> w2 exists=1 val={})
        Eval("listbox .l -listvariable ::w2");
        Eval("info exists ::w2").Should().Be("1");
        Eval("set ::w2").Should().Be("");

        //Act (wish: .l2 insert end aa bb -> w2=aa bb)
        Eval(".l insert end aa bb");

        //Assert
        Eval("set ::w2").Should().Be("aa bb");

        //Configure on a populated listbox (wish: nv=a b {c d} llength=3)
        Eval("listbox .m");
        Eval(".m insert end a b {c d}");
        Eval(".m configure -listvariable nv");
        Eval("set ::nv").Should().Be("a b {c d}");
        Eval("llength $::nv").Should().Be("3");
    }

    [Fact]
    public void Unsetting_the_variable_recreates_it_with_the_listbox_contents()
    {
        //Arrange
        Eval("set ::x {x y z w}");
        Eval("listbox .x -listvariable x");

        //Act (wish: after unset: exists=1 x={x y z w})
        Eval("unset ::x");

        //Assert
        Eval("info exists ::x").Should().Be("1");
        Eval("set ::x").Should().Be("x y z w");

        //The link survives the unset (wish: relinked: p q).
        Eval("set ::x {p q}");
        Eval(".x get 0 end").Should().Be("p q");

        //...and a second unset is handled the same way.
        Eval("unset ::x");
        Eval("set ::x").Should().Be("p q");
    }

    [Fact]
    public void An_invalid_list_value_is_rejected_and_the_variable_restored()
    {
        //Arrange
        Eval("set ::x {p q}");
        Eval("listbox .x -listvariable x");

        //Act (wish: invalid set: rc=1 msg={can't set "x": invalid listvar
        //value} items={p q} x={p q})
        string message;
        ReturnCode code = TryEval("set ::x \"a \\{b\"", out message);

        //Assert
        code.Should().Be(ReturnCode.Error);
        message.Should().Be("can't set \"::x\": invalid listvar value");
        Eval(".x get 0 end").Should().Be("p q");
        Eval("set ::x").Should().Be("p q");

        //The link is still live afterwards.
        Eval("set ::x {r s t}");
        Eval(".x size").Should().Be("3");
    }

    [Fact]
    public void An_invalid_list_at_create_or_configure_is_an_error()
    {
        //Arrange
        Eval("set ::bad \"x \\{y\"");

        //Act + Assert (wish: create bad: rc=1 msg={unmatched open brace in
        //list: invalid -listvariable value} exists=0)
        string message;
        TryEval("listbox .l3 -listvariable bad", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("unmatched open brace in list: invalid -listvariable value");
        Eval("winfo exists .l3").Should().Be("0");

        //wish: config bad: rc=1 msg={unmatched open brace in list: invalid
        //-listvariable value} cget=w2 items={aa bb}
        Eval("listbox .l2 -listvariable w2");
        Eval(".l2 insert end aa bb");
        TryEval(".l2 configure -listvariable bad", out message).Should().Be(ReturnCode.Error);
        message.Should().Be("unmatched open brace in list: invalid -listvariable value");
        Eval(".l2 cget -listvariable").Should().Be("w2");
        Eval(".l2 get 0 end").Should().Be("aa bb");

        //wish: w2 still linked after failed config: after fail
        Eval("set ::w2 {after fail}");
        Eval(".l2 get 0 end").Should().Be("after fail");
    }

    [Fact]
    public void Changing_or_clearing_the_listvariable_detaches_the_old_variable()
    {
        //Arrange
        Eval("listbox .l2 -listvariable w2");
        Eval(".l2 insert end aa bb");
        Eval("set ::o1 {o1a o1b}");

        //Act (wish: switch: items={o1a o1b} w2={aa bb})
        Eval(".l2 configure -listvariable o1");

        //Assert (wish: w2 write no effect: o1a o1b)
        Eval(".l2 get 0 end").Should().Be("o1a o1b");
        Eval("set ::w2").Should().Be("aa bb");
        Eval("set ::w2 zz");
        Eval(".l2 get 0 end").Should().Be("o1a o1b");
        Eval("trace info variable ::w2").Should().Be("");

        //wish: clear: items={o1a o1b} cget={}; o1 write after clear: o1a o1b;
        //insert after clear: o1=changed items=o1a o1b new
        Eval(".l2 configure -listvariable {}");
        Eval(".l2 cget -listvariable").Should().Be("");
        Eval(".l2 get 0 end").Should().Be("o1a o1b");
        Eval("set ::o1 changed");
        Eval(".l2 get 0 end").Should().Be("o1a o1b");
        Eval(".l2 insert end new");
        Eval("set ::o1").Should().Be("changed");
        Eval(".l2 get 0 end").Should().Be("o1a o1b new");
        Eval("trace info variable ::o1").Should().Be("");
    }

    [Fact]
    public void Destroying_the_listbox_removes_the_trace()
    {
        //Arrange
        Eval("set ::o1 {a b}");
        Eval("listbox .d -listvariable o1");

        //Act
        Eval("destroy .d");

        //Assert (wish: destroy: trace o1={}; o1=after destroy)
        Eval("trace info variable ::o1").Should().Be("");
        Eval("set ::o1 {after destroy}").Should().Be("after destroy");

        //A new listbox at the same path links afresh.
        Eval("listbox .d -listvariable o1");
        Eval(".d get 0 end").Should().Be("after destroy");
    }

    [Fact]
    public void A_listbox_destroyed_with_its_parent_drops_its_link()
    {
        //Arrange
        Eval("set ::o2 {a b}");
        Eval("frame .f");
        Eval("listbox .f.l -listvariable o2");

        //Act
        Eval("destroy .f");

        //Assert
        Eval("trace info variable ::o2").Should().Be("");
        Eval("set ::o2 {c d}").Should().Be("c d");
    }

    [Fact]
    public void Variable_change_drops_selection_past_the_new_end_and_keeps_the_rest()
    {
        //Arrange
        Eval("set ::v {1 2 3 4 5 6 7}");
        Eval("listbox .l -listvariable v -selectmode extended");
        Eval(".l selection set 2");

        //Act (wish: shrink: cur={})
        Eval("set ::v {x y}");

        //Assert
        Eval(".l curselection").Should().Be("");

        //wish: selection set 1; set v {x y z w} -> grow: cur={1}
        Eval(".l selection set 1");
        Eval("set ::v {x y z w}");
        Eval(".l curselection").Should().Be("1");
    }

    [Fact]
    public void Variable_change_does_not_fire_listboxselect()
    {
        //Arrange
        Eval("set ::v {a b c}");
        Eval("listbox .l -listvariable v");
        Eval(".l selection set 0");
        var fired = new List<string>();
        _root.Tree.Bindings.Bind(_root.FindDescendant(".l").PathName, "<<ListboxSelect>>", e =>
        {
            fired.Add(e.VirtualName);
            return DispatchResult.Continue;
        });

        //Act (wish: listboxselect on var change: {})
        Eval("set ::v {only}");

        //Assert
        fired.Count.Should().Be(0);
        Eval(".l get 0 end").Should().Be("only");
    }

    [Fact]
    public void Namespace_and_array_element_variables_link()
    {
        //wish: ns: n1 n2
        Eval("namespace eval ns { variable lst {n1 n2} }");
        Eval("listbox .l4 -listvariable ns::lst");
        Eval(".l4 get 0 end").Should().Be("n1 n2");
        Eval("set ::ns::lst {n3}");
        Eval(".l4 get 0 end").Should().Be("n3");

        //wish: arr: e1 e2 e3 (after lappend arr(k) e3)
        Eval("set ::arr(k) {e1 e2}");
        Eval("listbox .l5 -listvariable arr(k)");
        Eval(".l5 get 0 end").Should().Be("e1 e2");
        Eval("lappend ::arr(k) e3");
        Eval(".l5 get 0 end").Should().Be("e1 e2 e3");
        Eval(".l5 insert end e4");
        Eval("set ::arr(k)").Should().Be("e1 e2 e3 e4");
    }

    [Fact]
    public void Listvariable_named_inside_a_proc_is_a_global_variable()
    {
        //wish: proc mk {} { set loc {l1}; listbox .l6 -listvariable loc }
        //-> proc: loc={} size=0
        Eval("proc mk {} { set loc {l1}; listbox .l6 -listvariable loc }");
        Eval("mk");
        Eval("set ::loc").Should().Be("");
        Eval(".l6 size").Should().Be("0");
    }
}
