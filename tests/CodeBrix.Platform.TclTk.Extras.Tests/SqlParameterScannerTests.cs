using System.Collections.Generic;

using CodeBrix.Platform.TclTk.Extras.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TclTk.Extras.Tests;

/// <summary>
/// Tests for the SQL host-parameter scanner: token recognition for all three
/// prefixes, and correct skipping of literals, identifiers, and comments.
/// </summary>
public class SqlParameterScannerTests
{
    [Fact]
    public void FindParameters_finds_colon_parameters()
    {
        //Arrange / Act
        IList<string> found = SqlParameterScanner.FindParameters(
            "insert into t (a, b) values (:first, :second)");

        //Assert
        found.Should().Equal(":first", ":second");
    }

    [Fact]
    public void FindParameters_finds_at_and_dollar_parameters()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "select * from t where a = @one and b = $two");

        found.Should().Equal("@one", "$two");
    }

    [Fact]
    public void FindParameters_returns_distinct_tokens_in_first_appearance_order()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "update t set a = :v, b = :w where a = :v");

        found.Should().Equal(":v", ":w");
    }

    [Fact]
    public void FindParameters_skips_single_quoted_string_literals()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "insert into t values ('at 10:30', :real)");

        found.Should().Equal(":real");
    }

    [Fact]
    public void FindParameters_handles_doubled_quote_escapes_inside_literals()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "insert into t values ('it''s :not_a_bind', :yes_a_bind)");

        found.Should().Equal(":yes_a_bind");
    }

    [Fact]
    public void FindParameters_skips_double_quoted_and_bracketed_identifiers()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "select \"weird:col\", [other:col] from t where x = :p");

        found.Should().Equal(":p");
    }

    [Fact]
    public void FindParameters_skips_line_and_block_comments()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "select a -- not :here\nfrom t /* nor :here */ where b = :bind");

        found.Should().Equal(":bind");
    }

    [Fact]
    public void FindParameters_ignores_a_bare_colon_with_no_name()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "select a : b from t");

        found.Should().BeEmpty();
    }

    [Fact]
    public void FindParameters_returns_empty_for_null_or_empty_sql()
    {
        SqlParameterScanner.FindParameters(null).Should().BeEmpty();
        SqlParameterScanner.FindParameters("").Should().BeEmpty();
    }

    [Fact]
    public void FindParameters_accepts_underscores_and_digits_in_names()
    {
        IList<string> found = SqlParameterScanner.FindParameters(
            "select * from t where a = :field_2 and b = :x9");

        found.Should().Equal(":field_2", ":x9");
    }
}
