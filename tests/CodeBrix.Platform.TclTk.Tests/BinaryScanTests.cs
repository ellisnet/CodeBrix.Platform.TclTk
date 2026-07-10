using CodeBrix.Platform.TclTk._Components.Public;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the <c>binary scan</c> sub-command of the ported <c>binary</c>
/// command. Every expected value in this file was captured from real
/// tclsh 8.6.16 (the development oracle). Scans run against bytes built with
/// <c>binary format H*</c> so the input is byte-exact; scanned values whose
/// content may not be printable are re-encoded with <c>binary encode hex</c>.
/// </summary>
public class BinaryScanTests
{
    private static string ScanOne(string inputHex, string template)
        => TclTkTest.EvalOnce(
            "binary scan [binary format H* " + inputHex + "] " + template +
            " x; list $x");

    private static string ScanCount(string inputHex, string template, string variables)
        => TclTkTest.EvalOnce(
            "binary scan [binary format H* {" + inputHex + "}] " + template +
            " " + variables);

    [Theory]
    // a/A: fixed-size byte strings; A trims trailing spaces and nulls.
    [InlineData("616263", "a3", "abc")]
    [InlineData("616263", "a", "a")]
    [InlineData("61620020", "A4", "ab")]
    [InlineData("6162", "a0", "{}")]
    // b/B: bit strings.
    [InlineData("07", "b5", "11100")]
    [InlineData("8702", "b*", "1110000101000000")]
    [InlineData("8702", "b12", "111000010100")]
    [InlineData("e0", "B5", "11100")]
    // h/H: hex strings, always lowercase output.
    [InlineData("ba05", "h3", "ab5")]
    [InlineData("ab50", "H3", "ab5")]
    [InlineData("deadbeef", "H*", "deadbeef")]
    [InlineData("ABCDEF", "H*", "abcdef")]
    public void Scan_string_field_types(string inputHex, string template, string expected)
        => ScanOne(inputHex, template).Should().Be(expected);

    [Theory]
    // c: 8-bit, sign-extended by default, unsigned with the 'u' flag.
    [InlineData("ff", "c", "-1")]
    [InlineData("ff", "cu", "255")]
    [InlineData("01ff", "c2", "{1 -1}")]
    [InlineData("01ff02", "c*", "{1 -1 2}")]
    [InlineData("01", "c0", "{}")]
    [InlineData("01ff", "cu2", "{1 255}")]
    // s/S/t: 16-bit little / big / native endian.
    [InlineData("fffe", "s", "-257")]
    [InlineData("fffe", "su", "65279")]
    [InlineData("fffe", "S", "-2")]
    [InlineData("0100", "t", "1")]
    // i/I/n: 32-bit.
    [InlineData("ffffffff", "i", "-1")]
    [InlineData("ffffffff", "iu", "4294967295")]
    [InlineData("00000001", "I", "1")]
    [InlineData("01000000", "n", "1")]
    // w/W/m: 64-bit.
    [InlineData("ffffffffffffffff", "w", "-1")]
    [InlineData("ffffffffffffffff", "wu", "18446744073709551615")]
    [InlineData("0000000000000001", "W", "1")]
    [InlineData("0100000000000000", "m", "1")]
    // Counts and "*" on integer types.
    [InlineData("010002000300", "s*", "{1 2 3}")]
    [InlineData("010002000300", "s2", "{1 2}")]
    public void Scan_integer_field_types(string inputHex, string template, string expected)
        => ScanOne(inputHex, template).Should().Be(expected);

    [Theory]
    // Floating point: values are widened to double and formatted Tcl-style.
    [InlineData("0000c03f", "f", "1.5")]
    [InlineData("0000c03f", "r", "1.5")]
    [InlineData("3fc00000", "R", "1.5")]
    [InlineData("000000000000f83f", "d", "1.5")]
    [InlineData("000000000000f83f", "q", "1.5")]
    [InlineData("3ff8000000000000", "Q", "1.5")]
    // Special values use the Tcl spellings.
    [InlineData("000000000000f07f", "q", "Inf")]
    [InlineData("000000000000f0ff", "q", "-Inf")]
    [InlineData("0000c07f", "r", "NaN")]
    public void Scan_floating_point_field_types(string inputHex, string template, string expected)
        => ScanOne(inputHex, template).Should().Be(expected);

    [Theory]
    // Whole numbers gain ".0"; shortest round-trip representation; lowercase
    // exponent with sign, matching stock Tcl output for the same bytes.
    [InlineData("f 3", "f", "3.0")]
    [InlineData("d -0.0", "d", "-0.0")]
    [InlineData("f 3.14159", "f", "3.141590118408203")]
    [InlineData("f 0.1", "f", "0.10000000149011612")]
    [InlineData("d 0.3333333333333333", "d", "0.3333333333333333")]
    [InlineData("d 12345.6789", "d", "12345.6789")]
    [InlineData("d 1e300", "d", "1e+300")]
    [InlineData("d 123456789012345678", "d", "1.2345678901234568e+17")]
    [InlineData("d2 {1.5 2.25}", "d2", "{1.5 2.25}")]
    public void Scan_formats_doubles_like_tcl(string formatArgs, string template, string expected)
        => TclTkTest.EvalOnce(
            "binary scan [binary format " + formatArgs + "] " + template +
            " x; list $x").Should().Be(expected);

    [Theory]
    // Cursor movement: x skips, X backs up, @ is absolute.
    [InlineData("6162", "xa1 x", "1 b")]
    [InlineData("6162", "a2Xa1 x y", "2 ab b")]
    [InlineData("6162", "a2X*a1 x y", "2 ab a")]
    [InlineData("6162", "a1X5a1 x y", "2 a a")] // X clamps at position 0
    [InlineData("6162", "@1a1 x", "1 b")]
    [InlineData("6162", "@0a1 x", "1 a")]
    [InlineData("41000042", "a1x2a1 x y", "2 A B")]
    [InlineData("ba0541", "h3a1 x y", "2 ab5 A")] // h3 consumes two bytes
    [InlineData("0741", "b5a1 x y", "2 11100 A")]
    [InlineData("070141", "b9a1 x y", "2 111000001 A")]
    public void Scan_cursor_field_types(string inputHex, string templateAndVars, string expected)
    {
        //Arrange
        string[] parts = templateAndVars.Split(' ');
        string dollars = string.Empty;

        for (int index = 1; index < parts.Length; index++)
            dollars += " $" + parts[index];

        string script =
            "set n [binary scan [binary format H* {" + inputHex + "}] " +
            templateAndVars + "]; list $n" + dollars;

        //Act
        string result = TclTkTest.EvalOnce(script);

        //Assert
        result.Should().Be(expected);
    }

    [Theory]
    // A field that cannot be satisfied stops the scan; the return value is
    // the number of conversions performed so far.
    [InlineData("616263", "a5 x", "0")]
    [InlineData("07", "b9 x", "0")]
    [InlineData("01", "c2 x", "0")]
    [InlineData("6162", "x3a1 x", "0")] // x past the end stops the scan
    [InlineData("6162", "x*a1 x", "0")]
    [InlineData("6162", "@9a1 x", "0")]
    [InlineData("6162", "@*a1 x", "0")]
    public void Scan_stops_when_a_field_cannot_be_satisfied(string inputHex, string templateAndVars, string expected)
        => ScanCount(inputHex, templateAndVars.Split(' ')[0], templateAndVars.Substring(templateAndVars.IndexOf(' ') + 1))
            .Should().Be(expected);

    [Fact]
    public void Scan_failed_field_leaves_variable_untouched()
    {
        //Arrange
        using Interpreter interpreter = TclTkTest.CreateInterpreter();
        TclTkTest.Eval(interpreter, "set y KEEP");

        //Act
        string count = TclTkTest.Eval(interpreter,
            "binary scan AB a1a5 x y");

        //Assert
        count.Should().Be("1");
        TclTkTest.Eval(interpreter, "set x").Should().Be("A");
        TclTkTest.Eval(interpreter, "set y").Should().Be("KEEP");
    }

    [Fact]
    public void Scan_failed_field_does_not_create_variable()
        => TclTkTest.EvalOnce(
            "binary scan AB a1a5a1 x y z; list [info exists y] [info exists z]")
            .Should().Be("0 0");

    [Fact]
    public void Scan_empty_input_with_all_count_produces_empty_values()
        => TclTkTest.EvalOnce(
            "list [binary scan {} c* x] $x [binary scan {} a* y] $y")
            .Should().Be("1 {} 1 {}");

    [Fact]
    public void Scan_empty_template_returns_zero()
        => TclTkTest.EvalOnce("binary scan AB {}").Should().Be("0");

    [Fact]
    public void Scan_unsigned_flag_is_ignored_on_non_integer_types()
        => TclTkTest.EvalOnce("binary scan AB au x; list $x").Should().Be("A");

    [Fact]
    public void Scan_high_unicode_characters_truncate_to_low_byte()
        => TclTkTest.EvalOnce("binary scan ❤ c x; list $x").Should().Be("100");

    [Fact]
    public void Scan_A_trims_only_trailing_spaces_and_nulls()
        => TclTkTest.EvalOnce(
            "binary scan [binary format H* 6100206220000020] A8 x;" +
            " binary encode hex $x").Should().Be("61002062");

    [Fact]
    public void Scan_round_trips_a_mixed_template()
        => TclTkTest.EvalOnce(
            "binary scan [binary format a2b4H2cs2iwfd AB 1010 ff 7 {258 3}" +
            " -2 9 1.5 2.5] a2b4H2cs2iwfd a b c d e f g h i;" +
            " list $a $b $c $d $e $f $g $h $i")
            .Should().Be("AB 1010 ff 7 {258 3} -2 9 1.5 2.5");

    [Theory]
    [InlineData("binary scan AB z x", "bad field specifier \"z\"")]
    [InlineData("binary scan AB a1a1 x",
        "not enough arguments for all format specifiers")]
    [InlineData("binary scan AB a1",
        "not enough arguments for all format specifiers")]
    [InlineData("binary scan AB @ x",
        "missing count for \"@\" field specifier")]
    [InlineData("binary scan abc",
        "wrong # args: should be \"binary scan value formatString ?varName ...?\"")]
    [InlineData("binary scan",
        "wrong # args: should be \"binary scan value formatString ?varName ...?\"")]
    public void Scan_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);
}
