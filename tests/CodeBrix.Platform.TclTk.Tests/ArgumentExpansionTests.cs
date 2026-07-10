using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the Tcl 8.5 <c>{*}</c> argument expansion syntax added to the
/// ported parser and engine. Every expected value in this file was captured
/// from real tclsh 8.6.16 (the development oracle).
/// </summary>
public class ArgumentExpansionTests
{
    [Theory]
    // Basic expansion of variables, command results, and literals.
    [InlineData("set l {a b c}; llength [list {*}$l]", "3")]
    [InlineData("set l {a b c}; lindex [list {*}$l] 1", "b")]
    [InlineData("list {*}[list 1 2 3]", "1 2 3")]
    [InlineData("list {*}{x y z}", "x y z")]
    [InlineData("list {*}\"q w e\"", "q w e")]
    // The command name itself may come from an expansion.
    [InlineData("set c {string toupper}; {*}$c abc", "ABC")]
    [InlineData("set cmd {format %s-%s A B}; {*}$cmd", "A-B")]
    // Empty expansions contribute nothing.
    [InlineData("set e {}; list A {*}$e B", "A B")]
    // Multiple expansions in one command.
    [InlineData("list {*}{a b} mid {*}{c d}", "a b mid c d")]
    [InlineData("llength [concat {*}{a b} {*}{c d}]", "4")]
    // Substitutions inside the expanded word happen first.
    [InlineData("set n 2; list {*}[list $n [expr {$n * 2}]]", "2 4")]
    // Expansion works for expression words too.
    [InlineData("expr {*}{1 + 2}", "3")]
    // Nested list structure is preserved element-wise.
    [InlineData("list [list {*}{1 2}]", "{1 2}")]
    // A large expansion.
    [InlineData("proc take {args} { llength $args }; take {*}[lrepeat 1000 x]",
        "1000")]
    public void Expansion_produces_separate_words(string script, string expected)
        => TclTkTest.EvalOnce(script).Should().Be(expected);

    [Fact]
    public void Expansion_of_empty_word_alone_is_a_no_op()
        => TclTkTest.EvalOnce("set q 9; eval {set q 9; {*}{}}")
            .Should().Be("9");

    [Fact]
    public void Lone_expansion_prefix_is_a_literal_star()
        => TclTkTest.EvalOnce("list [llength {*}] [string length {*}]")
            .Should().Be("1 1");

    [Fact]
    public void Expansion_prefix_mid_word_is_literal()
        => TclTkTest.EvalOnce("list a{*}b").Should().Be("a{*}b");

    [Fact]
    public void Expansion_of_single_element_with_inner_brace_is_one_word()
        => TclTkTest.EvalOnce(
            "set bad \"un\\{balanced\"; llength [list {*}$bad]")
            .Should().Be("1");

    [Fact]
    public void Expansion_unblocks_the_drakon_export_pdf_idiom()
        // The exact idiom from DRAKON's export_pdf.tcl line 652 that
        // motivated this feature: build a command as a list, then invoke
        // it with expansion.
        => TclTkTest.EvalOnce(
            "proc make_pdf {a b} { return $a-$b };" +
            " set command [list make_pdf hello world];" +
            " set foo [ {*}$command ]; set foo").Should().Be("hello-world");

    [Fact]
    public void Expansion_of_an_invalid_list_is_an_error()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act
        (ReturnCode code, string value) = TclTkTest.TryEval(interpreter,
            "set bad \"\\{a\"; list {*}$bad");

        //Assert
        code.Should().Be(ReturnCode.Error);
    }
}
