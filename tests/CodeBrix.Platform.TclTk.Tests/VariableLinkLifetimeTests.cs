using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the lifetime of a variable that is the target of a [variable] or
/// [upvar] link when it is unset by its own (qualified) name while the link is
/// still live. Stock Tcl keeps the target alive-but-undefined so the link
/// survives and a later write through it revives the variable; it must stay
/// invisible to introspection while undefined. All expected values were probed
/// on real tclsh 8.6.16.
/// </summary>
public class VariableLinkLifetimeTests
{
    [Fact]
    public void Write_through_link_survives_array_unset_of_the_target()
    {
        //Arrange — a proc links a local to a namespace array, then the array
        //  is unset by its qualified name (NOT through the link).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval hie { variable names }");
        TclTkTest.Eval(interpreter, @"
namespace eval hie {
    proc test {} {
        variable names
        array set names {a 1}
        array unset ::hie::names
        set names(b) 2
        return [array get ::hie::names]
    }
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "hie::test");

        //Assert — tclsh: the write through the link lands on the revived
        //  namespace variable and is visible by name.
        result.Should().Be("b 2");
    }

    [Fact]
    public void Unset_target_with_live_link_is_invisible_to_introspection()
    {
        //Arrange — while the target is kept alive for the link, it must stay
        //  undefined: [info exists], [array exists], and [info vars] must all
        //  report it gone (tclsh: 0 0 0).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval hie { variable names }");
        TclTkTest.Eval(interpreter, @"
namespace eval hie {
    proc probe {} {
        variable names
        array set names {a 1}
        array unset ::hie::names
        set r {}
        lappend r [expr {[info exists ::hie::names] ? 1 : 0}]
        lappend r [expr {[array exists ::hie::names] ? 1 : 0}]
        lappend r [expr {[lsearch [info vars ::hie::*] ::hie::names] >= 0 ? 1 : 0}]
        return $r
    }
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "hie::probe");

        //Assert
        result.Should().Be("0 0 0");
    }

    [Fact]
    public void Array_set_recreate_by_name_reuses_the_unset_target()
    {
        //Arrange — the DRAKON Editor clear_array idiom: [array unset] on the
        //  qualified name, then [array set ... {}] to recreate it, while a
        //  caller's frame holds a live link from before the clear. The
        //  recreate must reuse the SAME variable object, so writes through
        //  the pre-clear link and by-name reads stay one variable
        //  (tclsh: 0 main helper main 2).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval hie_engine { variable names }");
        TclTkTest.Eval(interpreter, @"
proc clear_array { name } {
    array unset $name
    array set $name {}
}
namespace eval hie_engine {
    proc build_graph {} {
        variable names
        set names(1) old
        clear_array hie_engine::names
        set names(7) main
        set names(9) helper
        set r {}
        lappend r [expr {[info exists names(1)] ? 1 : 0}]
        lappend r $names(7) $names(9)
        lappend r [set ::hie_engine::names(7)]
        lappend r [array size ::hie_engine::names]
        return $r
    }
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "hie_engine::build_graph");

        //Assert
        result.Should().Be("0 main helper main 2");
    }

    [Fact]
    public void Set_through_link_revives_the_unset_target()
    {
        //Arrange — a write through the link after the unset must revive the
        //  target: defined again, one element, same data via link and by name
        //  (tclsh: 1 1 {b 2} {b 2}).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval hie { variable names }");
        TclTkTest.Eval(interpreter, @"
namespace eval hie {
    proc revive {} {
        variable names
        array set names {a 1}
        array unset ::hie::names
        set names(b) 2
        set r {}
        lappend r [expr {[info exists ::hie::names] ? 1 : 0}]
        lappend r [array size ::hie::names]
        lappend r [array get names]
        lappend r [array get ::hie::names]
        return $r
    }
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "hie::revive");

        //Assert
        result.Should().Be("1 1 {b 2} {b 2}");
    }

    // ------------------------------------------------------------------
    // Unset THROUGH the link (the alias itself is the unset target). Stock
    // Tcl keeps the alias, undefined, still pointing at its target; a later
    // write through it revives the target. Probed on tclsh 8.6.16.
    // ------------------------------------------------------------------

    [Fact]
    public void Array_unset_through_upvar_link_keeps_the_alias_and_revives_the_target()
    {
        //Arrange — the proc unsets the namespace array THROUGH its upvar alias
        //  (DRAKON-style clear_array on the local name), then writes through it.
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval hie { variable names; array set names {a 1} }");
        TclTkTest.Eval(interpreter, @"
proc q {} {
    upvar ::hie::names names
    array unset names
    set names(b) 2
    return [list [array get ::hie::names] [expr {[info exists names] ? 1 : 0}] [expr {[info exists ::hie::names] ? 1 : 0}]]
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "q");

        //Assert — tclsh: {b 2} 1 1
        result.Should().Be("{b 2} 1 1");
    }

    [Fact]
    public void Scalar_unset_through_upvar_link_is_undefined_then_revived_by_a_write()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set ::g 1");
        TclTkTest.Eval(interpreter, @"
proc s {} {
    upvar ::g g
    unset g
    set r1 [expr {[info exists g] ? 1 : 0}]
    set r2 [expr {[info exists ::g] ? 1 : 0}]
    set g 5
    return [list $r1 $r2 $::g [expr {[info exists g] ? 1 : 0}]]
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "s");

        //Assert — tclsh: 0 0 5 1
        result.Should().Be("0 0 5 1");
    }

    [Fact]
    public void Unset_through_global_link_then_write_lands_on_the_global()
    {
        //Arrange — [global] is the same link mechanism as [upvar]; the dead
        //  alias must not show up in [info locals] either (tclsh: empty).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set ::g2 1");
        TclTkTest.Eval(interpreter, @"
proc gg {} {
    global g2
    unset g2
    set locals [info locals]
    set g2 7
    return [list $::g2 [llength $locals]]
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "gg");

        //Assert — tclsh: 7 {} (""locals"" itself is created after the snapshot)
        result.Should().Be("7 0");
    }

    [Fact]
    public void Unset_through_variable_link_then_write_lands_on_the_namespace_variable()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "namespace eval ns { variable v 1 }");
        TclTkTest.Eval(interpreter, @"
proc vv {} {
    namespace eval ::ns { variable v; unset v; set v 9 }
    return $::ns::v
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "vv");

        //Assert — tclsh: 9
        result.Should().Be("9");
    }

    [Fact]
    public void Second_unset_through_the_link_errors_like_tclsh_and_the_link_still_revives()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set ::h 1");
        TclTkTest.Eval(interpreter, @"
proc twice {} {
    upvar ::h h
    unset h
    set c [catch {unset h} msg]
    set h 3
    return [list $c $msg $::h]
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "twice");

        //Assert — tclsh: 1 {can't unset "h": no such variable} 3
        result.Should().Be("1 {can't unset \"h\": no such variable} 3");
    }

    [Fact]
    public void Array_unset_through_link_leaves_no_names_and_array_exists_false_until_revived()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set ::arr(x) 1");
        TclTkTest.Eval(interpreter, @"
proc names {} {
    upvar ::arr a
    array unset a
    set n [array names a]
    set e [expr {[array exists a] ? 1 : 0}]
    set a(y) 2
    return [list $n $e [array get ::arr]]
}");

        //Act
        string result = TclTkTest.Eval(interpreter, "names");

        //Assert — tclsh: {} 0 {y 2}
        result.Should().Be("{} 0 {y 2}");
    }

    [Fact]
    public void Dead_alias_does_not_leak_out_of_the_proc_frame()
    {
        //Arrange — after the proc returns, the target is simply gone; a fresh
        //  by-name set works and nothing lingers (tclsh: 0, 0, then 4).
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set ::zz 1");
        TclTkTest.Eval(interpreter, "proc leak {} { upvar ::zz z; unset z; return [llength [info locals]] }");

        //Act
        string inProc = TclTkTest.Eval(interpreter, "leak");
        string existsAfter = TclTkTest.Eval(interpreter, "expr {[info exists ::zz] ? 1 : 0}");
        string reset = TclTkTest.Eval(interpreter, "set ::zz 4; set ::zz");

        //Assert
        inProc.Should().Be("0");
        existsAfter.Should().Be("0");
        reset.Should().Be("4");
    }
}
