using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Windowing;
using CodeBrix.Platform.TkCanvas.Canvas;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The <c>bind</c> command: Tcl scripts bound to Tk event patterns on
/// windows, classes, or the <c>all</c> tag, with Tk's %-substitution.
/// Handlers fire on the UI thread and post the substituted script to the
/// Tcl thread (fire-and-forget — the UI never blocks on Tcl).
/// </summary>
internal static class BindCommands
{
    // (tag, sequence) -> script, so re-binding replaces and "+script" appends.
    private sealed class ScriptTable : Dictionary<string, Dictionary<string, string>>
    {
        internal ScriptTable() : base(StringComparer.Ordinal) { }
    }

    internal static void Register(BridgeContext ctx)
    {
        var scripts = new ScriptTable();

        BridgeRegistrar.Add(ctx, "bind", words => Bind(ctx, scripts, words));
        BridgeRegistrar.Add(ctx, "bindtags", words => BindTags(ctx, words));
        BridgeRegistrar.Add(ctx, "event", words => "");
    }

    private static string Bind(BridgeContext ctx, ScriptTable scripts, string[] words)
    {
        if (words.Length < 2 || words.Length > 4)
        {
            throw BridgeRegistrar.WrongArgs("bind window ?pattern? ?command?");
        }

        string tag = words[1];

        if (words.Length == 2)
        {
            Dictionary<string, string> forTag;
            if (!scripts.TryGetValue(tag, out forTag)) { return ""; }
            var patterns = new List<string>(forTag.Keys);
            return TclString.JoinList(patterns);
        }

        string sequence = words[2];

        if (words.Length == 3)
        {
            Dictionary<string, string> forTag;
            string script;
            return scripts.TryGetValue(tag, out forTag) &&
                forTag.TryGetValue(sequence, out script) ? script : "";
        }

        string newScript = words[3];
        return ctx.Ui(() =>
        {
            Dictionary<string, string> forTag;
            if (!scripts.TryGetValue(tag, out forTag))
            {
                forTag = new Dictionary<string, string>(StringComparer.Ordinal);
                scripts[tag] = forTag;
            }

            if (newScript.Length == 0)
            {
                forTag.Remove(sequence);
                ctx.Tree.Bindings.Unbind(tag, sequence);
                return "";
            }

            string body = newScript;
            if (body.StartsWith("+", StringComparison.Ordinal))
            {
                string existing;
                body = forTag.TryGetValue(sequence, out existing)
                    ? existing + "\n" + body.Substring(1)
                    : body.Substring(1);
            }

            forTag[sequence] = body;

            // One toolkit binding per (tag, sequence); the handler reads the
            // current script at fire time.
            ctx.Tree.Bindings.Bind(tag, sequence, tkEvent =>
            {
                string current;
                Dictionary<string, string> table;
                if (!scripts.TryGetValue(tag, out table) ||
                    !table.TryGetValue(sequence, out current))
                {
                    return DispatchResult.Continue;
                }

                string substituted = SubstitutePercent(ctx, current, tkEvent);
                ctx.EvalCallbackScript(substituted);
                return DispatchResult.Continue;
            });

            // X11 Tcl code binds the wheel as <Button-4>/<Button-5>; the
            // host delivers MouseWheel events. Mirror onto ONE MouseWheel
            // binding per tag that replays the 4-script on scroll-up and
            // the 5-script on scroll-down (resolved at fire time).
            if (sequence.IndexOf("utton-4", StringComparison.Ordinal) >= 0 ||
                sequence.IndexOf("utton-5", StringComparison.Ordinal) >= 0)
            {
                ctx.Tree.Bindings.Bind(tag, "<MouseWheel>", tkEvent =>
                {
                    Dictionary<string, string> table;
                    if (!scripts.TryGetValue(tag, out table))
                    {
                        return DispatchResult.Continue;
                    }

                    string wanted = tkEvent.Delta > 0 ? "utton-4" : "utton-5";
                    foreach (KeyValuePair<string, string> candidate in table)
                    {
                        if (candidate.Key.IndexOf(wanted, StringComparison.Ordinal) >= 0)
                        {
                            ctx.EvalCallbackScript(
                                SubstitutePercent(ctx, candidate.Value, tkEvent));
                            break;
                        }
                    }
                    return DispatchResult.Continue;
                });
            }

            // In real Tk a window's first <Configure> arrives when the
            // toplevel maps — AFTER application code has bound it. Here
            // layout sizes windows immediately, so a <Configure> binding
            // added to an already-displayed window would never see that
            // initial event; deliver it once, deferred (a canvas consumer
            // relies on it to learn its size).
            if (sequence.IndexOf("Configure", StringComparison.Ordinal) >= 0 &&
                tag.Length > 0 && tag[0] == '.')
            {
                TkWindow bound;
                if (ctx.WindowsByPath.TryGetValue(tag, out bound) &&
                    !bound.IsDestroyed && bound.IsDisplayed &&
                    (bound.Width > 1 || bound.Height > 1))
                {
                    TkWindow captured = bound;
                    ctx.Tree.Scheduler.ScheduleIdle(() =>
                    {
                        if (captured.IsDestroyed) { return; }
                        ctx.Tree.DispatchEvent(captured, new TkEvent
                        {
                            Type = TkEventType.Configure,
                            Window = captured,
                            Width = captured.Width,
                            Height = captured.Height,
                        });
                    });
                }
            }

            return "";
        });
    }

    private static string BindTags(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("bindtags window ?tagList?");
        }

        return ctx.Ui(() =>
        {
            TkWindow window = ctx.ResolveWindow(words[1]);
            if (words.Length == 2)
            {
                return TclString.JoinList(new List<string>(window.EffectiveBindTags()));
            }

            window.BindTags.Clear();
            foreach (string tag in TclString.SplitList(words[2]))
            {
                window.BindTags.Add(tag);
            }
            return "";
        });
    }

    /// <summary>
    /// Tk's %-substitution over the bound script. Implements the
    /// substitutions real-world Tk code uses: %W %x %y %X %Y %b %K %A %D
    /// %w %h %s %T %t; unknown letters pass through unchanged.
    /// </summary>
    internal static string SubstitutePercent(BridgeContext ctx, string script, TkEvent tkEvent)
    {
        if (script.IndexOf('%') < 0) { return script; }

        var result = new StringBuilder(script.Length + 16);
        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            if (c != '%' || i + 1 >= script.Length)
            {
                result.Append(c);
                continue;
            }

            char code = script[++i];
            switch (code)
            {
                case '%':
                    result.Append('%');
                    break;
                case 'W':
                    result.Append(tkEvent.Window != null ? ctx.PathOf(tkEvent.Window) : "");
                    break;
                case 'x':
                    result.Append(tkEvent.X.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'y':
                    result.Append(tkEvent.Y.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'X':
                    result.Append(tkEvent.RootX.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'Y':
                    result.Append(tkEvent.RootY.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'b':
                    result.Append(tkEvent.Button.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'K':
                    result.Append(QuoteWord(tkEvent.KeySym ?? ""));
                    break;
                case 'A':
                    result.Append(QuoteWord(tkEvent.Character ?? ""));
                    break;
                case 'D':
                    result.Append(tkEvent.Delta.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'w':
                    result.Append(tkEvent.Width.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'h':
                    result.Append(tkEvent.Height.ToString(CultureInfo.InvariantCulture));
                    break;
                case 's':
                    result.Append(((int)tkEvent.State).ToString(CultureInfo.InvariantCulture));
                    break;
                case 'T':
                    result.Append(((int)tkEvent.Type).ToString(CultureInfo.InvariantCulture));
                    break;
                case 't':
                    result.Append('0');
                    break;
                default:
                    result.Append('%').Append(code);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Braces a substitution value so it stays one Tcl word.</summary>
    private static string QuoteWord(string value)
    {
        if (value.Length == 0) { return "{}"; }

        bool plain = true;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '\\' ||
                c == '[' || c == ']' || c == '$' || c == '"' || c == ';')
            {
                plain = false;
                break;
            }
        }
        if (plain) { return value; }

        // Braces are safe unless the value contains unbalanced braces or
        // a backslash; fall back to backslash-escaping in that case.
        if (value.IndexOf('{') < 0 && value.IndexOf('}') < 0 && value.IndexOf('\\') < 0)
        {
            return "{" + value + "}";
        }

        var escaped = new StringBuilder(value.Length * 2);
        foreach (char c in value)
        {
            escaped.Append('\\').Append(c);
        }
        return escaped.ToString();
    }
}
