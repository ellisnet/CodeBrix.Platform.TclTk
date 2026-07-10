using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// A canvas tag expression (Tk 8.6.16 tkCanvas.c <c>TagSearchScanExpr</c>/
/// <c>TagSearchEvalExpr</c>): a boolean combination of tag names with the
/// operators <c>!</c> (not), <c>&amp;&amp;</c> (and), <c>||</c> (or),
/// <c>^</c> (xor), and parenthesised sub-expressions. A bare tag name is true
/// for an item that carries that tag. Precedence is <c>!</c> highest, then
/// <c>&amp;&amp;</c>, then <c>||</c>/<c>^</c> at the lowest level (left
/// associative), matching Tk. A spec that contains none of the operator
/// characters is a plain tag and does not need this evaluator.
/// </summary>
internal sealed class CanvasTagExpression
{
    private readonly List<string> _tokens;

    private CanvasTagExpression(List<string> tokens)
    {
        _tokens = tokens;
    }

    /// <summary>Whether a search spec is a tag expression (contains an operator character).</summary>
    /// <param name="spec">The search specification.</param>
    /// <returns>True when the spec uses <c>! &amp; | ^ ( )</c>.</returns>
    public static bool IsExpression(string spec)
    {
        if (string.IsNullOrEmpty(spec)) { return false; }
        foreach (char c in spec)
        {
            if (c == '!' || c == '&' || c == '|' || c == '^' || c == '(' || c == ')')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parses a tag expression. Throws <see cref="InvalidOperationException"/>
    /// with Tk's message shape on a malformed expression.
    /// </summary>
    /// <param name="spec">The expression text.</param>
    /// <returns>The parsed expression, ready to evaluate.</returns>
    public static CanvasTagExpression Parse(string spec)
    {
        return new CanvasTagExpression(Tokenize(spec));
    }

    private static List<string> Tokenize(string spec)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < spec.Length)
        {
            char c = spec[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(' || c == ')' || c == '^')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (c == '!')
            {
                tokens.Add("!");
                i++;
            }
            else if (c == '&')
            {
                if (i + 1 < spec.Length && spec[i + 1] == '&') { tokens.Add("&&"); i += 2; }
                else { throw new InvalidOperationException("Singleton '&' in tag search expression"); }
            }
            else if (c == '|')
            {
                if (i + 1 < spec.Length && spec[i + 1] == '|') { tokens.Add("||"); i += 2; }
                else { throw new InvalidOperationException("Singleton '|' in tag search expression"); }
            }
            else
            {
                int start = i;
                while (i < spec.Length)
                {
                    char d = spec[i];
                    if (char.IsWhiteSpace(d) || d == '(' || d == ')' || d == '^'
                            || d == '!' || d == '&' || d == '|')
                    {
                        break;
                    }
                    i++;
                }
                tokens.Add("t:" + spec.Substring(start, i - start));
            }
        }
        return tokens;
    }

    /// <summary>
    /// Evaluates the expression for one item, given a predicate that reports
    /// whether the item carries a given tag.
    /// </summary>
    /// <param name="hasTag">Tag-membership predicate for the item.</param>
    /// <returns>The boolean result.</returns>
    public bool Evaluate(Func<string, bool> hasTag)
    {
        int pos = 0;
        bool result = ParseOr(hasTag, ref pos);
        return result;
    }

    private bool ParseOr(Func<string, bool> hasTag, ref int pos)
    {
        bool left = ParseAnd(hasTag, ref pos);
        while (pos < _tokens.Count && (_tokens[pos] == "||" || _tokens[pos] == "^"))
        {
            string op = _tokens[pos++];
            bool right = ParseAnd(hasTag, ref pos);
            left = (op == "^") ? (left ^ right) : (left || right);
        }
        return left;
    }

    private bool ParseAnd(Func<string, bool> hasTag, ref int pos)
    {
        bool left = ParseUnary(hasTag, ref pos);
        while (pos < _tokens.Count && _tokens[pos] == "&&")
        {
            pos++;
            bool right = ParseUnary(hasTag, ref pos);
            left = left && right;
        }
        return left;
    }

    private bool ParseUnary(Func<string, bool> hasTag, ref int pos)
    {
        if (pos < _tokens.Count && _tokens[pos] == "!")
        {
            pos++;
            return !ParseUnary(hasTag, ref pos);
        }
        return ParsePrimary(hasTag, ref pos);
    }

    private bool ParsePrimary(Func<string, bool> hasTag, ref int pos)
    {
        if (pos >= _tokens.Count)
        {
            throw new InvalidOperationException("Unexpected end of tag search expression");
        }
        string token = _tokens[pos++];
        if (token == "(")
        {
            bool inner = ParseOr(hasTag, ref pos);
            if (pos >= _tokens.Count || _tokens[pos] != ")")
            {
                throw new InvalidOperationException("Missing endparen in tag search expression");
            }
            pos++;
            return inner;
        }
        if (token.Length > 2 && token[0] == 't' && token[1] == ':')
        {
            return hasTag(token.Substring(2));
        }
        throw new InvalidOperationException("Unexpected operator in tag search expression");
    }
}
