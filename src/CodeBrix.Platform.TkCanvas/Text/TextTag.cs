using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Widgets;

namespace CodeBrix.Platform.TkCanvas.Text;

/// <summary>
/// One text-widget tag: a name, an accept-and-store option bag (the painted
/// attributes — <c>-foreground</c>, <c>-background</c>, <c>-underline</c>,
/// <c>-overstrike</c>, <c>-font</c> — are the interpreted subset), and the
/// character ranges the tag covers. Ranges stay sorted and non-overlapping;
/// adding an adjacent or overlapping range merges, like Tk. Tag priority is
/// creation order (later tags override earlier ones where both set an
/// attribute); the selection tag <c>sel</c> is created first.
/// </summary>
public sealed class TextTag
{
    private readonly List<TextPosition> _boundaries = new List<TextPosition>();

    internal TextTag(string name)
    {
        Name = name;
    }

    /// <summary>The tag name.</summary>
    public string Name { get; }

    /// <summary>The tag's option bag (<c>tag configure</c>).</summary>
    public WidgetOptions Options { get; } = new WidgetOptions();

    /// <summary>
    /// The range boundaries, interleaved start/end pairs in ascending order
    /// (<c>tag ranges</c> reports them in this shape).
    /// </summary>
    public IReadOnlyList<TextPosition> Boundaries
    {
        get { return _boundaries; }
    }

    /// <summary>Whether the position is inside one of the tag's ranges.</summary>
    /// <param name="position">The position to test.</param>
    /// <returns>True when tagged (range starts are inclusive, ends exclusive).</returns>
    public bool Covers(TextPosition position)
    {
        for (int i = 0; i + 1 < _boundaries.Count; i += 2)
        {
            if (position >= _boundaries[i] && position < _boundaries[i + 1])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Adds a range, merging with existing overlapping/adjacent ranges.</summary>
    internal void AddRange(TextPosition start, TextPosition end)
    {
        if (start >= end) { return; }

        var starts = new List<TextPosition> { start };
        var ends = new List<TextPosition> { end };
        for (int i = 0; i + 1 < _boundaries.Count; i += 2)
        {
            starts.Add(_boundaries[i]);
            ends.Add(_boundaries[i + 1]);
        }

        // Rebuild by sweeping the sorted intervals and merging touches.
        var pairs = new List<KeyValuePair<TextPosition, TextPosition>>();
        for (int i = 0; i < starts.Count; i++)
        {
            pairs.Add(new KeyValuePair<TextPosition, TextPosition>(starts[i], ends[i]));
        }
        pairs.Sort((a, b) => a.Key.CompareTo(b.Key));

        _boundaries.Clear();
        TextPosition currentStart = pairs[0].Key;
        TextPosition currentEnd = pairs[0].Value;
        for (int i = 1; i < pairs.Count; i++)
        {
            if (pairs[i].Key <= currentEnd)
            {
                if (pairs[i].Value > currentEnd) { currentEnd = pairs[i].Value; }
            }
            else
            {
                _boundaries.Add(currentStart);
                _boundaries.Add(currentEnd);
                currentStart = pairs[i].Key;
                currentEnd = pairs[i].Value;
            }
        }
        _boundaries.Add(currentStart);
        _boundaries.Add(currentEnd);
    }

    /// <summary>Removes a range (splitting covering ranges as needed).</summary>
    internal void RemoveRange(TextPosition start, TextPosition end)
    {
        if (start >= end) { return; }

        var result = new List<TextPosition>();
        for (int i = 0; i + 1 < _boundaries.Count; i += 2)
        {
            TextPosition rangeStart = _boundaries[i];
            TextPosition rangeEnd = _boundaries[i + 1];

            if (end <= rangeStart || start >= rangeEnd)
            {
                result.Add(rangeStart);
                result.Add(rangeEnd);
                continue;
            }
            if (rangeStart < start)
            {
                result.Add(rangeStart);
                result.Add(start);
            }
            if (end < rangeEnd)
            {
                result.Add(end);
                result.Add(rangeEnd);
            }
        }
        _boundaries.Clear();
        _boundaries.AddRange(result);
    }

    /// <summary>Clears all ranges.</summary>
    internal void ClearRanges()
    {
        _boundaries.Clear();
    }

    /// <summary>Rewrites every boundary through an edit adjustment.</summary>
    /// <param name="adjustStart">The mapping for range starts.</param>
    /// <param name="adjustEnd">The mapping for range ends.</param>
    internal void AdjustBoundaries(
            System.Func<TextPosition, TextPosition> adjustStart,
            System.Func<TextPosition, TextPosition> adjustEnd)
    {
        var result = new List<TextPosition>();
        for (int i = 0; i + 1 < _boundaries.Count; i += 2)
        {
            TextPosition start = adjustStart(_boundaries[i]);
            TextPosition end = adjustEnd(_boundaries[i + 1]);
            if (start < end)
            {
                result.Add(start);
                result.Add(end);
            }
        }
        _boundaries.Clear();
        _boundaries.AddRange(result);
    }
}
