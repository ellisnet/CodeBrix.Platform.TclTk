using System.Collections.Generic;

namespace CodeBrix.Platform.TclTk.Extras.Sqlite;

/// <summary>
/// Scans SQL text for host-parameter tokens (<c>:name</c>, <c>@name</c>, <c>$name</c>)
/// while skipping string literals, quoted identifiers, bracketed identifiers, and
/// comments — so a colon inside <c>'a text literal'</c> is never mistaken for a bind
/// parameter. SQLite's grammar forbids parameters in identifier position, so every
/// token this scanner finds is guaranteed to be a value-position bind.
/// </summary>
internal static class SqlParameterScanner
{
    /// <summary>
    /// Returns the distinct parameter tokens found in <paramref name="sql"/>, in order of
    /// first appearance. Each token includes its prefix character (e.g. <c>":name"</c>).
    /// </summary>
    public static IList<string> FindParameters(string sql)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(sql)) { return found; }

        var seen = new HashSet<string>();
        int i = 0;
        int length = sql.Length;

        while (i < length)
        {
            char c = sql[i];

            if (c == '\'' || c == '"' || c == '`')
            {
                // String literal or quoted identifier: runs to the matching quote;
                // a doubled quote character is an escape, not a terminator.
                char quote = c;
                i++;
                while (i < length)
                {
                    if (sql[i] == quote)
                    {
                        if ((i + 1) < length && sql[i + 1] == quote) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            if (c == '[')
            {
                // Bracketed identifier: runs to the closing bracket.
                i++;
                while (i < length && sql[i] != ']') { i++; }
                if (i < length) { i++; }
                continue;
            }

            if (c == '-' && (i + 1) < length && sql[i + 1] == '-')
            {
                // Line comment: runs to end of line.
                i += 2;
                while (i < length && sql[i] != '\n') { i++; }
                continue;
            }

            if (c == '/' && (i + 1) < length && sql[i + 1] == '*')
            {
                // Block comment: runs to the closing marker.
                i += 2;
                while ((i + 1) < length && !(sql[i] == '*' && sql[i + 1] == '/')) { i++; }
                i = ((i + 1) < length) ? i + 2 : length;
                continue;
            }

            if (c == ':' || c == '@' || c == '$')
            {
                int start = i;
                i++;
                int nameStart = i;
                while (i < length && IsNameChar(sql[i])) { i++; }
                if (i > nameStart)
                {
                    string token = sql.Substring(start, i - start);
                    if (seen.Add(token)) { found.Add(token); }
                }
                continue;
            }

            i++;
        }

        return found;
    }

    private static bool IsNameChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
}
