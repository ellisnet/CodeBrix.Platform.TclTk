using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// What a fired binding tells the dispatcher: keep walking the remaining
/// bind tags, or stop (the Tk <c>break</c> command inside a binding script).
/// </summary>
public enum DispatchResult
{
    /// <summary>Continue with the next bind tag.</summary>
    Continue,

    /// <summary>Stop processing this event (Tk's <c>break</c>).</summary>
    Break,
}

/// <summary>The callback form of a binding.</summary>
/// <param name="tkEvent">The event being dispatched.</param>
/// <returns>Whether to continue with the remaining bind tags.</returns>
public delegate DispatchResult TkEventHandler(TkEvent tkEvent);

/// <summary>
/// The binding table of one window tree: bind tag (a window path name, a
/// widget class name, or <c>all</c>) x event pattern -> handler. Binding the
/// same tag and pattern again REPLACES the handler, exactly like the Tk
/// <c>bind</c> command; and when an event is dispatched to a tag, only that
/// tag's MOST SPECIFIC matching binding fires.
/// </summary>
public sealed class BindingTable
{
    private readonly Dictionary<string, Dictionary<EventPattern, TkEventHandler>> _byTag =
            new Dictionary<string, Dictionary<EventPattern, TkEventHandler>>(StringComparer.Ordinal);

    /// <summary>
    /// Registers (or replaces) the binding of <paramref name="pattern"/> on
    /// <paramref name="tag"/> — the analogue of <c>bind TAG PATTERN script</c>.
    /// </summary>
    /// <param name="tag">The bind tag (path name, class name, or <c>all</c>).</param>
    /// <param name="pattern">The event pattern text, e.g. <c>&lt;ButtonPress-1&gt;</c>.</param>
    /// <param name="handler">The handler to fire.</param>
    public void Bind(string tag, string pattern, TkEventHandler handler)
    {
        if (string.IsNullOrEmpty(tag)) { throw new ArgumentException("empty bind tag", nameof(tag)); }
        if (handler == null) { throw new ArgumentNullException(nameof(handler)); }

        EventPattern parsed = EventPattern.Parse(pattern);
        Dictionary<EventPattern, TkEventHandler> patterns;
        if (!_byTag.TryGetValue(tag, out patterns))
        {
            patterns = new Dictionary<EventPattern, TkEventHandler>();
            _byTag[tag] = patterns;
        }
        patterns[parsed] = handler;
    }

    /// <summary>
    /// Removes the binding of <paramref name="pattern"/> on
    /// <paramref name="tag"/> — the analogue of <c>bind TAG PATTERN {}</c>.
    /// Removing an absent binding is a no-op.
    /// </summary>
    /// <param name="tag">The bind tag.</param>
    /// <param name="pattern">The event pattern text.</param>
    public void Unbind(string tag, string pattern)
    {
        Dictionary<EventPattern, TkEventHandler> patterns;
        if (_byTag.TryGetValue(tag, out patterns))
        {
            patterns.Remove(EventPattern.Parse(pattern));
        }
    }

    /// <summary>
    /// Lists the pattern texts currently bound on <paramref name="tag"/> —
    /// the analogue of <c>bind TAG</c> with no pattern.
    /// </summary>
    /// <param name="tag">The bind tag.</param>
    /// <returns>The bound pattern texts (empty when none).</returns>
    public IReadOnlyList<string> BoundPatterns(string tag)
    {
        var result = new List<string>();
        Dictionary<EventPattern, TkEventHandler> patterns;
        if (_byTag.TryGetValue(tag, out patterns))
        {
            foreach (EventPattern pattern in patterns.Keys)
            {
                result.Add(pattern.Text);
            }
        }
        return result;
    }

    /// <summary>
    /// Drops every binding whose tag is <paramref name="tag"/> (used when a
    /// window is destroyed: its path-name bindings die with it).
    /// </summary>
    /// <param name="tag">The bind tag.</param>
    public void RemoveTag(string tag)
    {
        _byTag.Remove(tag);
    }

    /// <summary>
    /// Finds the most specific binding of <paramref name="tag"/> matching
    /// <paramref name="tkEvent"/>, or null.
    /// </summary>
    /// <param name="tag">The bind tag.</param>
    /// <param name="tkEvent">The event.</param>
    /// <returns>The handler to fire, or null.</returns>
    internal TkEventHandler FindBest(string tag, TkEvent tkEvent)
    {
        Dictionary<EventPattern, TkEventHandler> patterns;
        if (!_byTag.TryGetValue(tag, out patterns)) { return null; }

        TkEventHandler best = null;
        int bestScore = -1;
        foreach (KeyValuePair<EventPattern, TkEventHandler> entry in patterns)
        {
            if (!entry.Key.Matches(tkEvent)) { continue; }
            int score = entry.Key.Specificity();
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Value;
            }
        }
        return best;
    }
}
