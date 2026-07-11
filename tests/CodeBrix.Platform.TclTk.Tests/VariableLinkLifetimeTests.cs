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
}
