using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the <see cref="Interpreter.CacheParsedScripts" /> opt-in: the
/// property round-trips, repeated evaluation of the same script bodies (the
/// case the cache exists for) produces the same results as the default
/// re-parsing engine, and error reporting — including the error line numbers
/// recorded in <c>errorInfo</c> — is unchanged.  Every behavioral test runs
/// the interesting code path at least three times so the second-sighting
/// promotion has occurred and the cached replay path is genuinely exercised.
/// </summary>
public class CacheParsedScriptsTests
{
    [Fact]
    public void CacheParsedScripts_is_off_by_default()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Assert
        interpreter.CacheParsedScripts.Should().BeFalse();
    }

    [Fact]
    public void CacheParsedScripts_round_trips_on_and_off()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();

        //Act + Assert
        interpreter.CacheParsedScripts = true;
        interpreter.CacheParsedScripts.Should().BeTrue();

        interpreter.CacheParsedScripts = false;
        interpreter.CacheParsedScripts.Should().BeFalse();
    }

    [Theory]
    [InlineData("set total 0; foreach n {1 2 3 4 5 6 7 8} { incr total $n }; set total")]
    [InlineData("proc double {x} { return [expr {$x * 2}] }; set r {}; foreach n {1 2 3 4} { lappend r [double $n] }; set r")]
    [InlineData("set i 0; set out {}; while {$i < 6} { append out [string index abcdef $i]; incr i }; set out")]
    [InlineData("set r {}; foreach v {1 2 3 4 5} { if {$v % 2 == 0} { lappend r even } else { lappend r odd } }; set r")]
    [InlineData("set r {}; foreach v {a b c a b a} { switch -- $v { a { lappend r 1 } b { lappend r 2 } default { lappend r 0 } } }; set r")]
    [InlineData("set r {}; for {set i 0} {$i < 5} {incr i} { catch { error boom } msg; lappend r $msg$i }; set r")]
    [InlineData("proc fib {n} { if {$n < 2} { return $n }; return [expr {[fib [expr {$n - 1}]] + [fib [expr {$n - 2}]]}] }; fib 12")]
    public void CacheParsedScripts_scripts_produce_the_same_results(string script)
    {
        //Arrange
        using Interpreter defaultInterpreter = TclTkTest.CreateInterpreter();
        using Interpreter cachingInterpreter = TclTkTest.CreateInterpreter();
        cachingInterpreter.CacheParsedScripts = true;

        //Act
        string defaultResult = TclTkTest.Eval(defaultInterpreter, script);
        string cachingResult = TclTkTest.Eval(cachingInterpreter, script);

        //Assert
        cachingResult.Should().Be(defaultResult);
    }

    [Fact]
    public void CacheParsedScripts_repeated_evaluations_stay_stable()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        interpreter.CacheParsedScripts = true;
        TclTkTest.Eval(interpreter,
            "proc sum {items} { set total 0; foreach n $items { incr total $n }; return $total }");

        //Act + Assert: the first call parses live, the second builds the
        //cache, the rest replay from it; every call must agree.
        for (int i = 0; i < 5; i++)
        {
            TclTkTest.Eval(interpreter, "sum {10 20 30 40}").Should().Be("100");
        }
    }

    [Fact]
    public void CacheParsedScripts_error_line_numbers_match_the_default_engine()
    {
        //Arrange: a proc that errors on a known line, called repeatedly so
        //the caching interpreter is replaying when the error fires.
        const string setup =
            "proc failing {} {\n" +
            "    set a 1\n" +
            "    set b 2\n" +
            "    error boom\n" +
            "}";
        using Interpreter defaultInterpreter = TclTkTest.CreateInterpreter();
        using Interpreter cachingInterpreter = TclTkTest.CreateInterpreter();
        cachingInterpreter.CacheParsedScripts = true;
        TclTkTest.Eval(defaultInterpreter, setup);
        TclTkTest.Eval(cachingInterpreter, setup);

        for (int i = 0; i < 3; i++)
        {
            //Act
            string defaultInfo = TclTkTest.Eval(defaultInterpreter,
                "catch { failing } msg; set ::errorInfo");
            string cachingInfo = TclTkTest.Eval(cachingInterpreter,
                "catch { failing } msg; set ::errorInfo");

            //Assert
            cachingInfo.Should().Be(defaultInfo);
        }
    }

    [Fact]
    public void CacheParsedScripts_parse_errors_reproduce_identically_on_every_call()
    {
        //Arrange: the body's first command succeeds; the second command has a
        //deterministic parse error (unclosed bracket), so the cache holds only
        //a prefix and live parsing must reproduce the error on each call.
        const string setup =
            "proc broken {} {\n" +
            "    set a ok\n" +
            "    set b [oops\n" +
            "}";
        using Interpreter defaultInterpreter = TclTkTest.CreateInterpreter();
        using Interpreter cachingInterpreter = TclTkTest.CreateInterpreter();
        cachingInterpreter.CacheParsedScripts = true;
        TclTkTest.Eval(defaultInterpreter, setup);
        TclTkTest.Eval(cachingInterpreter, setup);

        string expectedError = null;

        for (int i = 0; i < 3; i++)
        {
            //Act
            (ReturnCode defaultCode, string defaultError) =
                TclTkTest.TryEval(defaultInterpreter, "broken");
            (ReturnCode cachingCode, string cachingError) =
                TclTkTest.TryEval(cachingInterpreter, "broken");

            //Assert
            defaultCode.Should().Be(ReturnCode.Error);
            cachingCode.Should().Be(ReturnCode.Error);
            cachingError.Should().Be(defaultError);

            if (expectedError == null) { expectedError = cachingError; }
            else { cachingError.Should().Be(expectedError); }
        }
    }

    [Fact]
    public void CacheParsedScripts_dynamically_built_scripts_still_evaluate()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        interpreter.CacheParsedScripts = true;

        //Act: eval dynamically concatenated one-shot scripts (which the
        //second-sighting rule keeps out of the cache) alongside a repeated
        //one (which it promotes).
        string result = TclTkTest.Eval(interpreter,
            "set out {}; foreach n {1 2 3 4} { eval \"lappend out [expr {$n * $n}]\" }; set out");

        //Assert
        result.Should().Be("1 4 9 16");
    }

    [Fact]
    public void CacheParsedScripts_can_be_enabled_mid_session()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "proc greet {} { return hello }; greet");

        //Act
        interpreter.CacheParsedScripts = true;
        TclTkTest.Eval(interpreter, "greet").Should().Be("hello");
        TclTkTest.Eval(interpreter, "greet").Should().Be("hello");

        //Assert
        TclTkTest.Eval(interpreter, "greet").Should().Be("hello");
    }

    [Fact]
    public void CacheParsedScripts_redefined_procedures_use_the_new_body()
    {
        //Arrange: the old body is cached (three calls), then the proc is
        //redefined; the new body text is a different cache key, so the new
        //behavior must appear immediately.
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        interpreter.CacheParsedScripts = true;
        TclTkTest.Eval(interpreter, "proc answer {} { return old }");

        for (int i = 0; i < 3; i++)
        {
            TclTkTest.Eval(interpreter, "answer").Should().Be("old");
        }

        //Act
        TclTkTest.Eval(interpreter, "proc answer {} { return new }");

        //Assert
        for (int i = 0; i < 3; i++)
        {
            TclTkTest.Eval(interpreter, "answer").Should().Be("new");
        }
    }
}
