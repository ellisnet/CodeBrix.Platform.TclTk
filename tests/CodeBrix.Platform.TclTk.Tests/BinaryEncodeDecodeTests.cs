using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Tests;

/// <summary>
/// Tests for the <c>binary encode</c> / <c>binary decode</c> sub-commands
/// (<c>base64</c>, <c>hex</c>, <c>uuencode</c>) of the ported <c>binary</c>
/// command, plus the command-level ensemble behavior. Every expected value in
/// this file was captured from real tclsh 8.6.16 (the development oracle).
/// Binary content is asserted via hex so the bytes are exact.
/// </summary>
public class BinaryEncodeDecodeTests
{
    #region Hex
    [Theory]
    [InlineData("binary encode hex [binary format H* 01ff41]", "01ff41")]
    [InlineData("binary encode hex {}", "")]
    [InlineData("binary encode hex [binary decode hex 01FF41]", "01ff41")]
    [InlineData("binary encode hex [binary decode hex \"01 ff\\n41\\t\"]", "01ff41")]
    [InlineData("binary encode hex [binary decode hex 0f1]", "0f")] // odd digit dropped
    [InlineData("binary encode hex [binary decode hex -strict 0f1]", "0f")]
    [InlineData("binary decode hex {}", "")]
    public void Hex_encode_and_decode(string script, string expected)
        => TclTkTest.EvalOnce(script).Should().Be(expected);

    [Theory]
    [InlineData("binary decode hex \"0fx1\"",
        "invalid hexadecimal digit \"x\" at position 2")]
    [InlineData("binary decode hex -strict \"01 ff41\"",
        "invalid hexadecimal digit \" \" at position 2")]
    [InlineData("binary encode hex",
        "wrong # args: should be \"binary encode hex data\"")]
    [InlineData("binary encode hex ab cd",
        "wrong # args: should be \"binary encode hex data\"")]
    [InlineData("binary decode hex",
        "wrong # args: should be \"binary decode hex ?options? data\"")]
    public void Hex_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);
    #endregion

    #region Base64
    [Theory]
    [InlineData("binary encode base64 [binary format H* 010203040506070809]",
        "AQIDBAUGBwgJ")]
    [InlineData("binary encode base64 ab", "YWI=")]
    [InlineData("binary encode base64 a", "YQ==")]
    [InlineData("binary encode base64 {}", "")]
    [InlineData("binary encode base64 -maxlen 8 [binary format H* 010203040506070809]",
        "AQIDBAUG\nBwgJ")]
    [InlineData("binary encode base64 -maxlen 8 -wrapchar | [binary format H* 010203040506070809]",
        "AQIDBAUG|BwgJ")]
    [InlineData("binary encode base64 -maxlen 0 [binary format H* 0102030405]",
        "AQIDBAU=")]
    [InlineData("binary encode base64 -maxlen 4 abcdef", "YWJj\nZGVm")]
    [InlineData("binary encode base64 -maxlen 5 abcdef", "YWJjZ\nGVm")]
    [InlineData("binary encode base64 -maxlen 4 -wrapchar ab abcdef", "YWJjabZGVm")]
    [InlineData("binary encode base64 -maxlen 4 -wrapchar {} abcdef", "YWJjZGVm")]
    // With no option pairs before it, "-maxlen" itself is the data.
    [InlineData("binary encode base64 -maxlen", "LW1heGxlbg==")]
    public void Base64_encode(string script, string expected)
        => TclTkTest.EvalOnce(script).Should().Be(expected);

    [Theory]
    [InlineData("binary decode base64 AQIDBAUGBwgJ", "010203040506070809")]
    [InlineData("binary decode base64 YWI=", "6162")]
    [InlineData("binary decode base64 \"AQ ID\\nBAU=\\n\"", "0102030405")]
    [InlineData("binary decode base64 YWI", "6162")] // missing padding accepted
    [InlineData("binary decode base64 -strict YWI=", "6162")]
    [InlineData("binary decode base64 -strict YWI", "6162")]
    [InlineData("binary decode base64 {}", "")]
    [InlineData("binary decode base64 Y", "")] // lone character discarded
    // Non-strict mode skips characters outside the alphabet; "=" ends the data.
    [InlineData("binary decode base64 \"A*B=\"", "00")]
    [InlineData("binary decode base64 \"Y=WI\"", "")]
    public void Base64_decode(string script, string expectedHex)
        => TclTkTest.EvalOnce(
            "binary encode hex [" + script + "]").Should().Be(expectedHex);

    [Theory]
    [InlineData("binary encode base64 -foo 1 ab",
        "bad option \"-foo\": must be -maxlen or -wrapchar")]
    // Option names are exact; abbreviations are rejected.
    [InlineData("binary encode base64 -max 4 abcdef",
        "bad option \"-max\": must be -maxlen or -wrapchar")]
    [InlineData("binary encode base64 -maxlen -1 ab", "line length out of range")]
    [InlineData("binary encode base64 -maxlen foo ab",
        "expected integer but got \"foo\"")]
    [InlineData("binary encode base64",
        "wrong # args: should be \"binary encode base64 ?-maxlen len? ?-wrapchar char? data\"")]
    [InlineData("binary encode base64 -maxlen 3 -wrapchar ab",
        "wrong # args: should be \"binary encode base64 ?-maxlen len? ?-wrapchar char? data\"")]
    [InlineData("binary decode base64 -strict \"AQID\\n\"",
        "invalid base64 character \"\\n\" at position 4")]
    [InlineData("binary decode base64 AQID extra",
        "bad option \"AQID\": must be -strict")]
    [InlineData("binary decode base64 -strict -strict AQID",
        "wrong # args: should be \"binary decode base64 ?options? data\"")]
    [InlineData("binary decode base64",
        "wrong # args: should be \"binary decode base64 ?options? data\"")]
    public void Base64_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script)
            .Should().Be(expected.Replace("\\n", "\n"));
    #endregion

    #region Uuencode
    [Theory]
    // Expected values are the hex of the encoded output (captured from the
    // oracle), because uuencoded text contains backticks/quotes/newlines.
    [InlineData("binary encode uuencode abc", "23383629430a")]
    [InlineData("binary encode uuencode [binary format H* 000102fdfeff]",
        "26606024225f3f5b5f0a")]
    [InlineData("binary encode uuencode -maxlen 16 abcdefghijklmnop",
        "293836294339263546395641490a273a464d4c3b36594f3c600a")]
    [InlineData("binary encode uuencode -maxlen 9 abcdef",
        "2638362943392635460a")]
    [InlineData("binary encode uuencode -maxlen 5 ab", "223836280a")]
    [InlineData("binary encode uuencode -maxlen 6 abcdef",
        "23383629430a23392635460a")]
    [InlineData("binary encode uuencode {}", "")]
    [InlineData("binary encode uuencode -wrapchar \\r abc", "23383629430d")]
    [InlineData("binary encode uuencode -wrapchar \\r\\n abc", "23383629430d0a")]
    [InlineData("binary encode uuencode -wrapchar \\t abc", "233836294309")]
    [InlineData("binary encode uuencode -wrapchar {} abc", "2338362943")]
    public void Uuencode_encode(string script, string expectedHex)
        => TclTkTest.EvalOnce(
            "binary encode hex [" + script + "]").Should().Be(expectedHex);

    [Theory]
    [InlineData("binary decode uuencode \"#86)C\"", "616263")]
    [InlineData("binary decode uuencode -strict \"#86)C\\n\"", "616263")]
    [InlineData("binary decode uuencode {}", "")]
    // Characters missing at the end of a line decode as six-bit value 32.
    [InlineData("binary decode uuencode \"#86\"", "616820")]
    [InlineData("binary decode uuencode \"#\"", "820820")]
    // A zero count (space) produces nothing; the rest of the line is skipped.
    [InlineData("binary decode uuencode \" 86)C\"", "")]
    // Non-strict mode skips characters outside the uuencode range.
    [InlineData("binary decode uuencode \"#8~)C\"", "6098e0")]
    [InlineData("binary decode uuencode \"#86)C \\n\"", "616263")]
    public void Uuencode_decode(string script, string expectedHex)
        => TclTkTest.EvalOnce(
            "binary encode hex [" + script + "]").Should().Be(expectedHex);

    [Fact]
    public void Uuencode_round_trips_text()
        => TclTkTest.EvalOnce(
            "binary decode uuencode [binary encode uuencode" +
            " {hello world test data}]").Should().Be("hello world test data");

    [Fact]
    public void Uuencode_round_trips_multiple_lines()
        => TclTkTest.EvalOnce(
            "binary decode uuencode [binary encode uuencode -maxlen 9" +
            " abcdefgh]").Should().Be("abcdefgh");

    [Fact]
    public void Uuencode_round_trips_all_byte_values()
        => TclTkTest.EvalOnce(
            "set data {}; for {set i 0} {$i < 256} {incr i}" +
            " {append data [binary format c $i]};" +
            " set rt [binary decode uuencode [binary encode uuencode $data]];" +
            " if {$rt eq $data} { set r ok } else { set r bad }")
            .Should().Be("ok");

    [Theory]
    [InlineData("binary encode uuencode -maxlen 4 ab", "line length out of range")]
    [InlineData("binary encode uuencode -wrapchar ab abc",
        "invalid wrapchar; will defeat decoding")]
    [InlineData("binary decode uuencode -strict \"#8~)C\"",
        "invalid uuencode character \"~\" at position 2")]
    [InlineData("binary encode uuencode",
        "wrong # args: should be \"binary encode uuencode ?-maxlen len? ?-wrapchar char? data\"")]
    [InlineData("binary decode uuencode",
        "wrong # args: should be \"binary decode uuencode ?options? data\"")]
    public void Uuencode_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);
    #endregion

    #region Ensemble Behavior
    [Fact]
    public void Base64_round_trips_all_byte_values()
        => TclTkTest.EvalOnce(
            "set data {}; for {set i 0} {$i < 256} {incr i}" +
            " {append data [binary format c $i]};" +
            " set rt [binary decode base64 [binary encode base64 $data]];" +
            " if {$rt eq $data} { set r ok } else { set r bad }")
            .Should().Be("ok");

    [Theory]
    // Sub-command names may be abbreviated (the encoding names may not).
    [InlineData("binary fo a3 abc", "abc")]
    [InlineData("binary s abc a3 x; set x", "abc")]
    public void Binary_ensemble_behavior(string script, string expected)
        => TclTkTest.EvalOnce(script).Should().Be(expected);

    [Theory]
    [InlineData("binary",
        "wrong # args: should be \"binary subcommand ?arg ...?\"")]
    [InlineData("binary frobnicate",
        "unknown or ambiguous subcommand \"frobnicate\": must be decode, encode, format, or scan")]
    [InlineData("binary encode rot13 ab",
        "unknown subcommand \"rot13\": must be base64, hex, or uuencode")]
    [InlineData("binary decode rot13 ab",
        "unknown subcommand \"rot13\": must be base64, hex, or uuencode")]
    // Encoding names must be exact.
    [InlineData("binary encode base abc",
        "unknown subcommand \"base\": must be base64, hex, or uuencode")]
    public void Binary_ensemble_errors(string script, string expected)
        => TclTkTest.EvalOnceError(script).Should().Be(expected);

    [Fact]
    public void Binary_is_registered_as_a_command()
        => TclTkTest.EvalOnce("expr {[llength [info commands binary]] == 1}")
            .Should().Be("True");
    #endregion
}
