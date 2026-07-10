using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the <c>binary format</c> sub-command of the ported <c>binary</c>
/// command. Every expected value in this file was captured from real
/// tclsh 8.6.16 (the development oracle); the tests compare the produced
/// binary string via <c>binary encode hex</c> so the byte content is asserted
/// exactly. Deliberate divergences from stock Tcl are individually commented.
/// </summary>
public class BinaryFormatTests
{
    private static string FormatHex(string formatArgs)
        => TclTkTest.EvalOnce("binary encode hex [binary format " + formatArgs + "]");

    [Theory]
    // a/A: fixed-size byte strings, null vs space padded.
    [InlineData("a5 abc", "6162630000")]
    [InlineData("A5 abc", "6162632020")]
    [InlineData("a abc", "61")]
    [InlineData("a* abc", "616263")]
    [InlineData("a0 abc", "")]
    [InlineData("a2 abcdef", "6162")]
    // b/B: bit strings, low-to-high vs high-to-low within each byte.
    [InlineData("b5 11100", "07")]
    [InlineData("B5 11100", "e0")]
    [InlineData("b* 1110000101", "8702")]
    [InlineData("b12 111000010100", "8702")]
    [InlineData("b2 1110", "03")]
    [InlineData("b0 110", "")]
    [InlineData("b19 1", "010000")]
    // h/H: hex strings, low vs high nibble first; case-insensitive input.
    [InlineData("h3 AB5", "ba05")]
    [InlineData("H3 ab5", "ab50")]
    [InlineData("H* deadbeef", "deadbeef")]
    [InlineData("H5 ab", "ab0000")]
    [InlineData("h3 a", "0a00")]
    public void Format_string_field_types(string formatArgs, string expected)
        => FormatHex(formatArgs).Should().Be(expected);

    [Theory]
    // c: 8-bit, values silently truncated to the low byte.
    [InlineData("c 65", "41")]
    [InlineData("c 300", "2c")]
    [InlineData("c -1", "ff")]
    [InlineData("c 0x80", "80")]
    // s/S/t: 16-bit little / big / native endian.
    [InlineData("s 1", "0100")]
    [InlineData("S 1", "0001")]
    [InlineData("t 1", "0100")]
    // i/I/n: 32-bit little / big / native endian; wider values truncate.
    [InlineData("i 1", "01000000")]
    [InlineData("I 1", "00000001")]
    [InlineData("n 1", "01000000")]
    [InlineData("i -1", "ffffffff")]
    [InlineData("i 1099511627776", "00000000")]
    [InlineData("i 0x123456789A", "9a785634")]
    // w/W/m: 64-bit little / big / native endian.
    [InlineData("w 1", "0100000000000000")]
    [InlineData("W 1", "0000000000000001")]
    [InlineData("m 1", "0100000000000000")]
    [InlineData("w 0x7FFFFFFFFFFFFFFF", "ffffffffffffff7f")]
    // With a count, the argument is a list.
    [InlineData("c2 {1 2}", "0102")]
    [InlineData("c* {1 2 3 4}", "01020304")]
    [InlineData("c2 {1 2 3}", "0102")] // extra list elements are ignored
    [InlineData("s2 {258 772}", "02010403")]
    [InlineData("w2 {1 -1}", "0100000000000000ffffffffffffffff")]
    // The 'u' flag is accepted and ignored by format.
    [InlineData("cu 65", "41")]
    [InlineData("su 1", "0100")]
    public void Format_integer_field_types(string formatArgs, string expected)
        => FormatHex(formatArgs).Should().Be(expected);

    [Theory]
    // f/r/R: single precision native / little / big endian.
    [InlineData("f 1.5", "0000c03f")]
    [InlineData("r 1.5", "0000c03f")]
    [InlineData("R 1.5", "3fc00000")]
    [InlineData("f 3", "00004040")]
    // d/q/Q: double precision native / little / big endian.
    [InlineData("d 1.5", "000000000000f83f")]
    [InlineData("q 1.5", "000000000000f83f")]
    [InlineData("Q 1.5", "3ff8000000000000")]
    [InlineData("d2 {1.5 2.5}", "000000000000f83f0000000000000440")]
    // Out-of-range singles clamp to the largest finite float; stock Tcl
    // clamps infinities the same way.
    [InlineData("f 1e100", "ffff7f7f")]
    [InlineData("f -1e100", "ffff7fff")]
    [InlineData("f Inf", "ffff7f7f")]
    [InlineData("f -Inf", "ffff7fff")]
    // NaN normalizes to the positive quiet NaN bit pattern, like stock Tcl
    // formatting a NaN parsed from a string.
    [InlineData("f NaN", "0000c07f")]
    [InlineData("d NaN", "000000000000f87f")]
    [InlineData("d Inf", "000000000000f07f")]
    public void Format_floating_point_field_types(string formatArgs, string expected)
        => FormatHex(formatArgs).Should().Be(expected);

    [Theory]
    // x writes null bytes; X moves the cursor back; @ is absolute.
    [InlineData("x", "00")]
    [InlineData("x3", "000000")]
    [InlineData("a3X2a1 abc z", "617a63")]
    [InlineData("a3X*a1 abc z", "7a6263")]
    [InlineData("a3X5a1 abc z", "7a6263")] // X clamps at position 0
    [InlineData("Xa1 z", "7a")]
    [InlineData("a3@1a1 abc z", "617a63")]
    [InlineData("a3@*a1 abc z", "6162637a")]
    [InlineData("a2@5a1 ab z", "61620000007a")] // forward gap null-filled
    [InlineData("a1@5 a", "6100000000")] // @ alone extends the result
    [InlineData("a1x5 a", "610000000000")]
    [InlineData("a2@0a1 ab c", "6362")]
    public void Format_cursor_field_types(string formatArgs, string expected)
        => FormatHex(formatArgs).Should().Be(expected);

    [Theory]
    [InlineData("{} ", "")] // empty template
    [InlineData("{a2 a2} ab cd", "61626364")] // whitespace allowed in template
    [InlineData("a0x1 abc", "00")]
    [InlineData("a2 ab cd", "6162")] // extra arguments are ignored
    [InlineData("{a2 c s} ab 1 2", "6162010200")]
    [InlineData("a2b4H2cs2iwfd AB 1010 ff 7 {258 3} -2 9 1.5 2.5",
        "414205ff0702010300feffffff09000000000000000000c03f0000000000000440")]
    public void Format_template_handling(string formatArgs, string expected)
        => FormatHex(formatArgs).Should().Be(expected);

    [Theory]
    [InlineData("binary format z 1", "bad field specifier \"z\"")]
    [InlineData("binary format a2a2 ab",
        "not enough arguments for all format specifiers")]
    [InlineData("binary format c2 {1}",
        "number of elements in list does not match count")]
    [InlineData("binary format c foo", "expected integer but got \"foo\"")]
    [InlineData("binary format c {1 2}", "expected integer but got \"1 2\"")]
    [InlineData("binary format f foo",
        "expected floating-point number but got \"foo\"")]
    [InlineData("binary format d2 {1.5 foo}",
        "expected floating-point number but got \"foo\"")]
    [InlineData("binary format b5 11a00",
        "expected binary string but got \"11a00\" instead")]
    [InlineData("binary format h2 xy",
        "expected hexadecimal string but got \"xy\" instead")]
    [InlineData("binary format x*",
        "cannot use \"*\" in format string with \"x\"")]
    [InlineData("binary format a3@ abc",
        "missing count for \"@\" field specifier")]
    [InlineData("binary format a-1 ab", "bad field specifier \"-\"")]
    [InlineData("binary format",
        "wrong # args: should be \"binary format formatString ?arg ...?\"")]
    public void Format_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);

    [Fact]
    public void Format_X_with_count_zero_is_safe()
        // Stock tclsh 8.6.16 SEGFAULTS on this exact input ("binary format
        // a2X0a1 ab c"); this port takes the sane interpretation: back up
        // zero bytes, then write.
        => FormatHex("a2X0a1 ab c").Should().Be("616263");

    [Fact]
    public void Format_huge_count_is_a_clean_error()
        // Stock tclsh 8.6.16 PANICS (aborts the process) on a count that
        // overflows a signed 32-bit int; this port reports a clean error.
        => TclTkTest.EvalOnceError("binary format x2147483648")
            .Should().Be("integer value too large to represent");

    [Fact]
    public void Format_wide_overflow_wraps_instead_of_erroring()
        // DIVERGENCE from stock Tcl (which reports "integer value too large
        // to represent"): this engine has no arbitrary-precision integers and
        // wraps 65-bit hex input to 64 bits, consistent with how the engine
        // parses integers everywhere else.
        => FormatHex("w 0x1FFFFFFFFFFFFFFFF").Should().Be("ffffffffffffffff");

    [Fact]
    public void Format_high_unicode_characters_truncate_to_low_byte()
        => TclTkTest.EvalOnce(
            "binary encode hex [binary format a5 a❤b]")
            .Should().Be("6164620000");
}
