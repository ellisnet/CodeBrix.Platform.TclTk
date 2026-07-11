using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Text;

/// <summary>
/// The classic Tk <c>text</c> widget engine (the plan's §3.9 scope): the
/// <c>line.char</c> index model with marks (gravity included) and tags,
/// <c>insert</c>/<c>delete</c>/<c>get</c>/<c>index</c>/<c>see</c>,
/// <c>tag add/remove/configure/ranges</c> with painted attributes,
/// <c>mark set/unset/gravity</c>, line layout and wrapping through the
/// toolkit font seam, the scroll-fraction protocol, a selection model (the
/// <c>sel</c> tag) with click-caret and drag-select class behavior, and the
/// <see cref="ITextInputSink"/> seam for the deferred IME input element.
/// Index/mark/tag semantics were probed line-by-line against real Tk 8.6.16
/// (boundary gravity, tag stretch rules, line-merge adjustment). Out of
/// scope, per the plan: Tk undo and embedded windows (DRAKON does its own
/// undo at the model level).
/// </summary>
public sealed class TextWidget : IWidget, ITextInputTarget
{
    private sealed class Mark
    {
        public Mark(string name, TextPosition position, bool leftGravity)
        {
            Name = name;
            Position = position;
            LeftGravity = leftGravity;
        }

        public string Name { get; }

        public TextPosition Position { get; set; }

        public bool LeftGravity { get; set; }
    }

    private struct DisplaySegment
    {
        public int Line;       // 1-based logical line
        public int Start;      // first char of the segment
        public int End;        // one past the last char
    }

    private readonly List<string> _lines = new List<string> { "" };
    private readonly Dictionary<string, Mark> _marks =
            new Dictionary<string, Mark>(StringComparer.Ordinal);
    private readonly List<TextTag> _tags = new List<TextTag>();
    private readonly Dictionary<string, TextTag> _tagsByName =
            new Dictionary<string, TextTag>(StringComparer.Ordinal);

    private List<DisplaySegment> _display;
    private int _displayWidth = -1;
    private int _topDisplayLine;
    private TextPosition _selectionAnchor;
    private bool _hasSelectionAnchor;

    /// <summary>
    /// Creates a text widget on <paramref name="window"/>: sets the class to
    /// <c>Text</c>, creates the <c>sel</c> tag and the <c>insert</c>/<c>current</c>
    /// marks, registers the click-caret / drag-select class bindings, and
    /// requests the default 80x24-character size.
    /// </summary>
    /// <param name="window">The window the widget owns.</param>
    public TextWidget(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        Window = window;
        window.ClassName = "Text";
        window.Widget = this;
        window.Focusable = true;

        // The sel tag carries no explicit -background: painting falls back to
        // the tree theme's selection color, so a theme switch recolors the
        // selection live (an explicit "tag configure sel" still wins).
        var sel = new TextTag("sel");
        _tags.Add(sel);
        _tagsByName["sel"] = sel;

        _marks["insert"] = new Mark("insert", new TextPosition(1, 0), false);
        _marks["current"] = new Mark("current", new TextPosition(1, 0), false);

        Theming.OptionDatabase database = window.Tree.OptionDatabaseIfCreated;
        if (database != null && !database.IsEmpty)
        {
            database.ApplyTo(Options, window);
        }

        RegisterClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <inheritdoc/>
    public TkWindow Window { get; }

    /// <inheritdoc/>
    public string ClassName
    {
        get { return "Text"; }
    }

    /// <inheritdoc/>
    public WidgetOptions Options { get; } = new WidgetOptions();

    private ITextInputSink _inputSink;
    private string _composition = "";

    /// <summary>
    /// The hidden-input-element seam: a widget-level override, falling back
    /// to the tree-wide <see cref="WindowTree.InputSink"/> the host attaches;
    /// may stay null in headless use.
    /// </summary>
    public ITextInputSink InputSink
    {
        get { return (_inputSink != null) ? _inputSink : Window.Tree.InputSink; }
        set { _inputSink = value; }
    }

    /// <summary>The live IME pre-edit string, empty outside composition.</summary>
    public string Composition
    {
        get { return _composition; }
    }

    /// <inheritdoc/>
    public void CommitText(string text)
    {
        SetComposition(null);
        InsertAtCaret(text);
    }

    /// <inheritdoc/>
    public void SetComposition(string preedit)
    {
        string value = preedit ?? "";
        if (value == _composition) { return; }
        _composition = value;
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>Raised with the horizontal scroll fractions (<c>-xscrollcommand</c>).</summary>
    public event Action<double, double> XScrollChanged;

    /// <summary>Raised with the vertical scroll fractions (<c>-yscrollcommand</c>).</summary>
    public event Action<double, double> YScrollChanged;

    private FontManager Fonts
    {
        get { return Window.Tree.Fonts; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkFixedFont");
        }
    }

    private int LineHeight
    {
        get { return Fonts.Metrics(Font).LineSpace; }
    }

    private int Inset
    {
        get
        {
            return Options.GetInt("-borderwidth", 1) + Options.GetInt("-padx", 1)
                    + Options.GetInt("-highlightthickness", 0);
        }
    }

    /// <summary>The position after the final newline — Tk's <c>end</c>.</summary>
    public TextPosition EndPosition
    {
        get { return new TextPosition(_lines.Count + 1, 0); }
    }

    // ------------------------------------------------------------------
    // IWidget
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Measure()
    {
        int widthChars = Options.GetInt("-width", 80);
        int heightLines = Options.GetInt("-height", 24);
        int charWidth = Fonts.Measure(Font, "0");
        if (charWidth < 1) { charWidth = 1; }
        Window.SetRequestedSize(
                widthChars * charWidth + 2 * Inset,
                heightLines * LineHeight + 2 * Inset);
        Window.SetInternalBorder(Inset);
    }

    /// <inheritdoc/>
    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options != null)
        {
            foreach (KeyValuePair<string, string> option in options)
            {
                Options.Set(option.Key, option.Value);
            }
        }
        InvalidateLayout();
        Measure();
    }

    /// <inheritdoc/>
    public bool HitTest(SKPoint point)
    {
        return true;
    }

    // ------------------------------------------------------------------
    // Index model
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves a Tk text index expression to its normalized
    /// <c>line.char</c> form — the <c>index</c> subcommand. Supported bases:
    /// <c>line.char</c>, <c>end</c>, mark names, <c>tag.first</c>/<c>tag.last</c>,
    /// <c>@x,y</c>; modifiers: <c>+/- N chars/lines</c>, <c>linestart</c>,
    /// <c>lineend</c>, <c>wordstart</c>, <c>wordend</c>.
    /// </summary>
    /// <param name="indexExpr">The index expression.</param>
    /// <returns>The normalized index text.</returns>
    public string Index(string indexExpr)
    {
        return ParseIndex(indexExpr).ToString();
    }

    /// <summary>Parses a Tk text index expression to a position.</summary>
    /// <param name="indexExpr">The index expression.</param>
    /// <returns>The clamped position.</returns>
    public TextPosition ParseIndex(string indexExpr)
    {
        if (string.IsNullOrEmpty(indexExpr))
        {
            throw new ArgumentException("bad text index \"\"", nameof(indexExpr));
        }

        string text = indexExpr.Trim();

        // Split the base from trailing modifiers: the base runs to the first
        // space, '+' or '-' that is not inside the base itself.
        int baseEnd = text.Length;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '+' || c == '-')
            {
                baseEnd = i;
                break;
            }
        }

        TextPosition position = ParseBase(text.Substring(0, baseEnd).Trim());
        string rest = text.Substring(baseEnd);

        int cursor = 0;
        while (cursor < rest.Length)
        {
            while (cursor < rest.Length && rest[cursor] == ' ') { cursor++; }
            if (cursor >= rest.Length) { break; }

            char op = rest[cursor];
            if (op == '+' || op == '-')
            {
                cursor++;
                while (cursor < rest.Length && rest[cursor] == ' ') { cursor++; }
                int numStart = cursor;
                while (cursor < rest.Length && char.IsDigit(rest[cursor])) { cursor++; }
                int count = int.Parse(rest.Substring(numStart, cursor - numStart), CultureInfo.InvariantCulture);
                while (cursor < rest.Length && rest[cursor] == ' ') { cursor++; }
                int unitStart = cursor;
                while (cursor < rest.Length && char.IsLetter(rest[cursor])) { cursor++; }
                string unit = rest.Substring(unitStart, cursor - unitStart);

                // "display"/"any" submodifiers are accepted and treated as
                // plain units (accept-and-no-op discipline).
                if (unit == "display" || unit == "any")
                {
                    while (cursor < rest.Length && rest[cursor] == ' ') { cursor++; }
                    unitStart = cursor;
                    while (cursor < rest.Length && char.IsLetter(rest[cursor])) { cursor++; }
                    unit = rest.Substring(unitStart, cursor - unitStart);
                }

                int signedCount = (op == '-') ? -count : count;
                if (unit.StartsWith("l", StringComparison.Ordinal))
                {
                    position = AddLines(position, signedCount);
                }
                else
                {
                    position = AddChars(position, signedCount);
                }
            }
            else
            {
                int wordStart = cursor;
                while (cursor < rest.Length && char.IsLetter(rest[cursor])) { cursor++; }
                string word = rest.Substring(wordStart, cursor - wordStart);
                switch (word)
                {
                    case "linestart": position = new TextPosition(position.Line, 0); break;
                    case "lineend": position = Clamp(new TextPosition(position.Line, int.MaxValue)); break;
                    case "wordstart": position = WordStart(position); break;
                    case "wordend": position = WordEnd(position); break;
                    default:
                        throw new ArgumentException("bad text index modifier \"" + word + "\"");
                }
            }
        }
        return position;
    }

    private TextPosition ParseBase(string baseText)
    {
        if (baseText == "end") { return EndPosition; }

        Mark mark;
        if (_marks.TryGetValue(baseText, out mark)) { return mark.Position; }

        if (baseText.Length > 0 && baseText[0] == '@')
        {
            return PositionAt(baseText);
        }

        int dot = baseText.LastIndexOf('.');
        if (dot > 0)
        {
            string first = baseText.Substring(0, dot);
            string second = baseText.Substring(dot + 1);

            // tag.first / tag.last
            if (second == "first" || second == "last")
            {
                TextTag tag;
                if (_tagsByName.TryGetValue(first, out tag) && tag.Boundaries.Count > 0)
                {
                    return (second == "first")
                            ? tag.Boundaries[0]
                            : tag.Boundaries[tag.Boundaries.Count - 1];
                }
                throw new ArgumentException("text doesn't contain any characters tagged with \"" + first + "\"");
            }

            int line, charIndex;
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out line))
            {
                if (second == "end")
                {
                    return Clamp(new TextPosition(line, int.MaxValue));
                }
                if (int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out charIndex))
                {
                    return Clamp(new TextPosition(line, charIndex));
                }
            }
        }
        throw new ArgumentException("bad text index \"" + baseText + "\"");
    }

    /// <summary>
    /// Clamps a position into the buffer: lines beyond the last become
    /// <c>end</c>, characters beyond a line's length stop at its newline —
    /// real Tk's normalization (<c>1.99</c> → <c>1.5</c>, <c>99.0</c> → end).
    /// </summary>
    /// <param name="position">The raw position.</param>
    /// <returns>The clamped position.</returns>
    public TextPosition Clamp(TextPosition position)
    {
        if (position.Line < 1) { return new TextPosition(1, 0); }
        if (position.Line > _lines.Count) { return EndPosition; }
        int length = _lines[position.Line - 1].Length;
        int charIndex = position.Char;
        if (charIndex < 0) { charIndex = 0; }
        if (charIndex > length) { charIndex = length; }
        return new TextPosition(position.Line, charIndex);
    }

    private TextPosition ClampForEdit(TextPosition position)
    {
        // "end" edits happen just before the final newline.
        if (position >= EndPosition)
        {
            return new TextPosition(_lines.Count, _lines[_lines.Count - 1].Length);
        }
        return Clamp(position);
    }

    private TextPosition AddChars(TextPosition position, int count)
    {
        position = (position >= EndPosition) ? EndPosition : Clamp(position);
        int line = position.Line;
        int charIndex = position.Char;

        while (count > 0)
        {
            if (line > _lines.Count) { return EndPosition; }
            int length = _lines[line - 1].Length;
            int remaining = length - charIndex; // to the newline
            if (count <= remaining)
            {
                charIndex += count;
                count = 0;
            }
            else if (charIndex + count == length + 1 && line == _lines.Count)
            {
                return EndPosition;
            }
            else
            {
                count -= remaining + 1; // step over the newline
                line++;
                charIndex = 0;
                if (line > _lines.Count) { return EndPosition; }
            }
        }
        while (count < 0)
        {
            if (line > _lines.Count)
            {
                line = _lines.Count;
                charIndex = _lines[line - 1].Length;
                count++;
                continue;
            }
            if (charIndex + count >= 0)
            {
                charIndex += count;
                count = 0;
            }
            else
            {
                count += charIndex + 1; // step over the previous newline
                line--;
                if (line < 1) { return new TextPosition(1, 0); }
                charIndex = _lines[line - 1].Length;
            }
        }
        return new TextPosition(line, charIndex);
    }

    private TextPosition AddLines(TextPosition position, int count)
    {
        int line = position.Line + count;
        if (line < 1) { line = 1; }
        if (line > _lines.Count) { line = _lines.Count; }
        return Clamp(new TextPosition(line, position.Char));
    }

    private static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private TextPosition WordStart(TextPosition position)
    {
        position = Clamp(position);
        string line = _lines[position.Line - 1];
        int i = position.Char;
        if (i >= line.Length || !IsWordChar(line[i])) { return position; }
        while (i > 0 && IsWordChar(line[i - 1])) { i--; }
        return new TextPosition(position.Line, i);
    }

    private TextPosition WordEnd(TextPosition position)
    {
        position = Clamp(position);
        string line = _lines[position.Line - 1];
        int i = position.Char;
        if (i >= line.Length || !IsWordChar(line[i]))
        {
            return AddChars(position, 1);
        }
        while (i < line.Length && IsWordChar(line[i])) { i++; }
        return new TextPosition(position.Line, i);
    }

    // ------------------------------------------------------------------
    // Content editing
    // ------------------------------------------------------------------

    /// <summary>
    /// Inserts text — the <c>insert</c> subcommand. Marks, tag ranges, and
    /// the display adjust exactly as probed against real Tk: right-gravity
    /// marks at the point move with the text, tag starts at the point shift
    /// past it, tag ends at the point stay.
    /// </summary>
    /// <param name="index">The insertion index expression.</param>
    /// <param name="text">The text (may contain newlines).</param>
    /// <param name="tags">Tags to apply to the inserted range, or null.</param>
    public void Insert(string index, string text, IReadOnlyList<string> tags = null)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        TextPosition at = ClampForEdit(ParseIndex(index));
        string[] parts = text.Split('\n');
        TextPosition endOfInsert;

        if (parts.Length == 1)
        {
            string line = _lines[at.Line - 1];
            _lines[at.Line - 1] = line.Substring(0, at.Char) + text + line.Substring(at.Char);
            AdjustAfterInsert(at, 0, text.Length);
            endOfInsert = new TextPosition(at.Line, at.Char + text.Length);
        }
        else
        {
            string line = _lines[at.Line - 1];
            string tail = line.Substring(at.Char);
            _lines[at.Line - 1] = line.Substring(0, at.Char) + parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                _lines.Insert(at.Line - 1 + i, parts[i]);
            }
            int lastLine = at.Line + parts.Length - 1;
            int lastLength = parts[parts.Length - 1].Length;
            _lines[lastLine - 1] += tail;
            AdjustAfterInsert(at, parts.Length - 1, lastLength);
            endOfInsert = new TextPosition(lastLine, lastLength);
        }

        if (tags != null)
        {
            foreach (string tagName in tags)
            {
                TagAdd(tagName, at.ToString(), endOfInsert.ToString());
            }
        }
        InvalidateLayout();
    }

    private void AdjustAfterInsert(TextPosition at, int lineDelta, int lastLength)
    {
        Func<TextPosition, bool, TextPosition> map = (p, moveAtPoint) =>
        {
            if (p.Line < at.Line) { return p; }
            if (p.Line > at.Line)
            {
                return (lineDelta == 0) ? p : new TextPosition(p.Line + lineDelta, p.Char);
            }
            if (p.Char < at.Char || (p.Char == at.Char && !moveAtPoint)) { return p; }
            if (lineDelta == 0)
            {
                return new TextPosition(p.Line, p.Char + lastLength);
            }
            return new TextPosition(p.Line + lineDelta, lastLength + (p.Char - at.Char));
        };

        foreach (Mark mark in _marks.Values)
        {
            mark.Position = map(mark.Position, !mark.LeftGravity);
        }
        foreach (TextTag tag in _tags)
        {
            // Probed: a start AT the point shifts (insertion lands outside);
            // an end AT the point stays (insertion lands outside).
            tag.AdjustBoundaries(
                    start => map(start, true),
                    end => map(end, false));
        }
    }

    /// <summary>
    /// Deletes a range — the <c>delete</c> subcommand (a single character
    /// when <paramref name="end"/> is null). Marks inside the range collapse
    /// to its start; tag ranges shrink.
    /// </summary>
    /// <param name="start">The range start expression.</param>
    /// <param name="end">The range end expression, or null.</param>
    public void Delete(string start, string end = null)
    {
        TextPosition from = ClampForEdit(ParseIndex(start));
        TextPosition to = (end != null)
                ? ParseIndex(end)
                : AddChars(from, 1);
        if (to >= EndPosition)
        {
            to = new TextPosition(_lines.Count, _lines[_lines.Count - 1].Length);
        }
        else
        {
            to = Clamp(to);
        }
        if (from >= to) { return; }

        if (from.Line == to.Line)
        {
            string line = _lines[from.Line - 1];
            _lines[from.Line - 1] = line.Substring(0, from.Char) + line.Substring(to.Char);
        }
        else
        {
            string prefix = _lines[from.Line - 1].Substring(0, from.Char);
            string suffix = _lines[to.Line - 1].Substring(to.Char);
            _lines[from.Line - 1] = prefix + suffix;
            _lines.RemoveRange(from.Line, to.Line - from.Line);
        }
        AdjustAfterDelete(from, to);
        InvalidateLayout();
    }

    private void AdjustAfterDelete(TextPosition from, TextPosition to)
    {
        int lineDelta = to.Line - from.Line;
        Func<TextPosition, TextPosition> map = p =>
        {
            if (p <= from) { return p; }
            if (p <= to) { return from; }
            if (p.Line == to.Line)
            {
                return new TextPosition(from.Line, from.Char + (p.Char - to.Char));
            }
            return new TextPosition(p.Line - lineDelta, p.Char);
        };

        foreach (Mark mark in _marks.Values)
        {
            mark.Position = map(mark.Position);
        }
        foreach (TextTag tag in _tags)
        {
            tag.AdjustBoundaries(map, map);
        }
    }

    /// <summary>
    /// Reads a range of text — the <c>get</c> subcommand. Reading to
    /// <c>end</c> includes the final newline, like Tk.
    /// </summary>
    /// <param name="start">The range start expression.</param>
    /// <param name="end">The range end expression, or null for one character.</param>
    /// <returns>The text in the range.</returns>
    public string Get(string start, string end = null)
    {
        TextPosition from = Clamp(ParseIndex(start));
        TextPosition to = (end != null) ? ParseIndex(end) : AddChars(from, 1);
        if (from >= to && !(to >= EndPosition)) { return string.Empty; }

        var builder = new StringBuilder();
        TextPosition final = EndPosition;
        bool toEnd = to >= final;
        TextPosition stop = toEnd
                ? new TextPosition(_lines.Count, _lines[_lines.Count - 1].Length)
                : Clamp(to);

        for (int line = from.Line; line <= stop.Line; line++)
        {
            string content = _lines[line - 1];
            int first = (line == from.Line) ? from.Char : 0;
            int last = (line == stop.Line) ? stop.Char : content.Length;
            builder.Append(content, first, last - first);
            if (line != stop.Line) { builder.Append('\n'); }
        }
        if (toEnd) { builder.Append('\n'); }
        return builder.ToString();
    }

    /// <summary>
    /// Inserts committed text at the insert mark (replacing any selection) —
    /// the path the input sink drives when the user types.
    /// </summary>
    /// <param name="text">The committed text.</param>
    public void InsertAtCaret(string text)
    {
        TextTag sel = _tagsByName["sel"];
        if (sel.Boundaries.Count >= 2)
        {
            Delete(sel.Boundaries[0].ToString(), sel.Boundaries[sel.Boundaries.Count - 1].ToString());
            sel.ClearRanges();
        }
        Insert("insert", text);
        See("insert");
        NotifySinkCaret();
    }

    // ------------------------------------------------------------------
    // Marks
    // ------------------------------------------------------------------

    /// <summary>Places (or creates) a mark — <c>mark set</c>.</summary>
    /// <param name="name">The mark name.</param>
    /// <param name="index">The position expression.</param>
    public void MarkSet(string name, string index)
    {
        TextPosition position = ParseIndex(index);
        if (position > EndPosition) { position = EndPosition; }
        Mark mark;
        if (_marks.TryGetValue(name, out mark))
        {
            mark.Position = position;
        }
        else
        {
            _marks[name] = new Mark(name, position, false);
        }
        if (name == "insert")
        {
            Window.Tree.Scheduler.ScheduleRepaint();
            NotifySinkCaret();
        }
    }

    /// <summary>Removes a mark — <c>mark unset</c> (<c>insert</c>/<c>current</c> stay).</summary>
    /// <param name="name">The mark name.</param>
    public void MarkUnset(string name)
    {
        if (name == "insert" || name == "current") { return; }
        _marks.Remove(name);
    }

    /// <summary>Reads or changes a mark's gravity — <c>mark gravity</c>.</summary>
    /// <param name="name">The mark name.</param>
    /// <param name="gravity">Null to read; <c>left</c> or <c>right</c> to set.</param>
    /// <returns>The gravity in effect.</returns>
    public string MarkGravity(string name, string gravity = null)
    {
        Mark mark;
        if (!_marks.TryGetValue(name, out mark))
        {
            throw new ArgumentException("there is no mark named \"" + name + "\"");
        }
        if (gravity != null)
        {
            mark.LeftGravity = gravity == "left";
        }
        return mark.LeftGravity ? "left" : "right";
    }

    /// <summary>The current mark names — <c>mark names</c>.</summary>
    public IReadOnlyCollection<string> MarkNames()
    {
        return _marks.Keys;
    }

    // ------------------------------------------------------------------
    // Tags
    // ------------------------------------------------------------------

    private TextTag GetOrCreateTag(string name)
    {
        TextTag tag;
        if (!_tagsByName.TryGetValue(name, out tag))
        {
            tag = new TextTag(name);
            _tags.Add(tag);
            _tagsByName[name] = tag;
        }
        return tag;
    }

    /// <summary>Tags a range — <c>tag add</c> (overlaps/adjacency merge).</summary>
    /// <param name="name">The tag name (created on first use).</param>
    /// <param name="start">The range start expression.</param>
    /// <param name="end">The range end expression, or null for one character.</param>
    public void TagAdd(string name, string start, string end = null)
    {
        TextPosition from = Clamp(ParseIndex(start));
        TextPosition to = (end != null) ? ParseIndex(end) : AddChars(from, 1);
        if (to >= EndPosition)
        {
            to = new TextPosition(_lines.Count, _lines[_lines.Count - 1].Length);
        }
        GetOrCreateTag(name).AddRange(from, Clamp(to));
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>Untags a range — <c>tag remove</c> (the tag itself survives).</summary>
    /// <param name="name">The tag name.</param>
    /// <param name="start">The range start expression.</param>
    /// <param name="end">The range end expression, or null for one character.</param>
    public void TagRemove(string name, string start, string end = null)
    {
        TextTag tag;
        if (!_tagsByName.TryGetValue(name, out tag)) { return; }
        TextPosition from = Clamp(ParseIndex(start));
        TextPosition to = (end != null) ? ParseIndex(end) : AddChars(from, 1);
        if (to >= EndPosition)
        {
            to = new TextPosition(_lines.Count, _lines[_lines.Count - 1].Length);
        }
        tag.RemoveRange(from, Clamp(to));
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>Deletes a tag entirely — <c>tag delete</c> (<c>sel</c> only clears).</summary>
    /// <param name="name">The tag name.</param>
    public void TagDelete(string name)
    {
        TextTag tag;
        if (!_tagsByName.TryGetValue(name, out tag)) { return; }
        if (name == "sel")
        {
            tag.ClearRanges();
            return;
        }
        _tags.Remove(tag);
        _tagsByName.Remove(name);
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>Configures a tag's painted attributes — <c>tag configure</c>.</summary>
    /// <param name="name">The tag name (created on first use).</param>
    /// <param name="options">The option name/value pairs.</param>
    public void TagConfigure(string name, IReadOnlyDictionary<string, string> options)
    {
        TextTag tag = GetOrCreateTag(name);
        if (options != null)
        {
            foreach (KeyValuePair<string, string> option in options)
            {
                tag.Options.Set(option.Key, option.Value);
            }
        }
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>A tag's range boundaries — <c>tag ranges</c>.</summary>
    /// <param name="name">The tag name.</param>
    /// <returns>Start/end index texts, interleaved (empty when untagged).</returns>
    public IReadOnlyList<string> TagRanges(string name)
    {
        var result = new List<string>();
        TextTag tag;
        if (_tagsByName.TryGetValue(name, out tag))
        {
            foreach (TextPosition boundary in tag.Boundaries)
            {
                result.Add(boundary.ToString());
            }
        }
        return result;
    }

    /// <summary>The tag names in priority order — <c>tag names</c>.</summary>
    public IReadOnlyList<string> TagNames()
    {
        var result = new List<string>();
        foreach (TextTag tag in _tags) { result.Add(tag.Name); }
        return result;
    }

    /// <summary>Looks up a tag object, or null.</summary>
    /// <param name="name">The tag name.</param>
    /// <returns>The tag, or null.</returns>
    public TextTag GetTag(string name)
    {
        TextTag tag;
        return _tagsByName.TryGetValue(name, out tag) ? tag : null;
    }

    // ------------------------------------------------------------------
    // Layout, view, and painting
    // ------------------------------------------------------------------

    private void InvalidateLayout()
    {
        _display = null;
        if (!Window.IsDestroyed)
        {
            Window.Tree.Scheduler.ScheduleRepaint();
        }
        NotifyScroll();
    }

    private int ContentWidth
    {
        get
        {
            int width = Window.Width - 2 * Inset;
            return (width < 1) ? 1 : width;
        }
    }

    private int VisibleLines
    {
        get
        {
            int lines = (Window.Height - 2 * Inset) / LineHeight;
            return (lines < 1) ? 1 : lines;
        }
    }

    private List<DisplaySegment> DisplayLines()
    {
        // The wrap layout depends on the window width: recompute when the
        // window has been resized since the cache was built (a text widget
        // filled before its first layout pass must re-flow afterwards).
        if (_display != null && _displayWidth == ContentWidth) { return _display; }
        _displayWidth = ContentWidth;

        var display = new List<DisplaySegment>();
        string wrap = Options.Get("-wrap", "char");
        FontManager fonts = Fonts;
        TkFont font = Font;
        int width = ContentWidth;

        for (int line = 1; line <= _lines.Count; line++)
        {
            string content = _lines[line - 1];
            if (wrap == "none" || content.Length == 0
                    || fonts.Measure(font, content) <= width)
            {
                display.Add(new DisplaySegment { Line = line, Start = 0, End = content.Length });
                continue;
            }

            int start = 0;
            while (start < content.Length)
            {
                int fit = content.Length - start;
                while (fit > 1 && fonts.Measure(font, content.Substring(start, fit)) > width)
                {
                    fit--;
                }
                int end = start + fit;
                if (end < content.Length && wrap == "word")
                {
                    int space = content.LastIndexOf(' ', end - 1, fit);
                    if (space > start) { end = space + 1; }
                }
                display.Add(new DisplaySegment { Line = line, Start = start, End = end });
                start = end;
            }
        }

        _display = display;
        return display;
    }

    private int DisplayLineOf(TextPosition position)
    {
        List<DisplaySegment> display = DisplayLines();
        for (int i = 0; i < display.Count; i++)
        {
            DisplaySegment segment = display[i];
            if (segment.Line != position.Line) { continue; }
            bool lastOfLine = (i + 1 >= display.Count) || (display[i + 1].Line != position.Line);
            if (position.Char < segment.End || (lastOfLine && position.Char >= segment.End))
            {
                return i;
            }
        }
        return (position.Line > _lines.Count) ? display.Count - 1 : 0;
    }

    /// <summary>
    /// Scrolls so a position is visible — the <c>see</c> subcommand (jumps
    /// far-off targets toward the middle of the view, like Tk).
    /// </summary>
    /// <param name="index">The position expression.</param>
    public void See(string index)
    {
        TextPosition position = Clamp(ParseIndex(index));
        int displayLine = DisplayLineOf(position);
        int visible = VisibleLines;
        int total = DisplayLines().Count;

        if (displayLine < _topDisplayLine || displayLine >= _topDisplayLine + visible)
        {
            if (displayLine < _topDisplayLine - visible || displayLine >= _topDisplayLine + 2 * visible)
            {
                _topDisplayLine = displayLine - visible / 2;
            }
            else if (displayLine < _topDisplayLine)
            {
                _topDisplayLine = displayLine;
            }
            else
            {
                _topDisplayLine = displayLine - visible + 1;
            }
        }

        if (_topDisplayLine > total - 1) { _topDisplayLine = total - 1; }
        if (_topDisplayLine < 0) { _topDisplayLine = 0; }
        Window.Tree.Scheduler.ScheduleRepaint();
        NotifyScroll();
    }

    /// <summary>The vertical scroll fractions — <c>yview</c> with no arguments.</summary>
    /// <param name="first">The fraction above the view.</param>
    /// <param name="last">The fraction at the bottom of the view.</param>
    public void YViewFractions(out double first, out double last)
    {
        int total = DisplayLines().Count;
        if (total <= 0)
        {
            first = 0;
            last = 1;
            return;
        }
        first = (double)_topDisplayLine / total;
        last = (double)(_topDisplayLine + VisibleLines) / total;
        if (last > 1.0) { last = 1.0; }
        if (first < 0) { first = 0; }
    }

    /// <summary>Scrolls to a fraction — <c>yview moveto</c>.</summary>
    /// <param name="fraction">The target fraction of display lines above the view.</param>
    public void YViewMoveTo(double fraction)
    {
        int total = DisplayLines().Count;
        _topDisplayLine = (int)(fraction * total + 0.5);
        if (_topDisplayLine > total - 1) { _topDisplayLine = total - 1; }
        if (_topDisplayLine < 0) { _topDisplayLine = 0; }
        Window.Tree.Scheduler.ScheduleRepaint();
        NotifyScroll();
    }

    /// <summary>Scrolls by lines or pages — <c>yview scroll</c>.</summary>
    /// <param name="count">The signed count.</param>
    /// <param name="pages">True for pages (a view-full), false for lines.</param>
    public void YViewScroll(int count, bool pages)
    {
        int amount = pages ? count * VisibleLines : count;
        int total = DisplayLines().Count;
        _topDisplayLine += amount;
        if (_topDisplayLine > total - 1) { _topDisplayLine = total - 1; }
        if (_topDisplayLine < 0) { _topDisplayLine = 0; }
        Window.Tree.Scheduler.ScheduleRepaint();
        NotifyScroll();
    }

    private void NotifyScroll()
    {
        Action<double, double> yHandler = YScrollChanged;
        if (yHandler != null)
        {
            double first, last;
            YViewFractions(out first, out last);
            yHandler(first, last);
        }
        Action<double, double> xHandler = XScrollChanged;
        if (xHandler != null)
        {
            // Horizontal scrolling ships with wrap=none refinement; report
            // the whole width for now.
            xHandler(0.0, 1.0);
        }
    }

    /// <summary>
    /// Resolves an <c>@x,y</c> index (window-relative pixels) to a position
    /// via the current layout and view.
    /// </summary>
    /// <param name="atExpr">The <c>@x,y</c> text.</param>
    /// <returns>The position under the point.</returns>
    public TextPosition PositionAt(string atExpr)
    {
        int comma = atExpr.IndexOf(',');
        int x = int.Parse(atExpr.Substring(1, comma - 1), CultureInfo.InvariantCulture);
        int y = int.Parse(atExpr.Substring(comma + 1), CultureInfo.InvariantCulture);
        return PositionAtPoint(x, y);
    }

    /// <summary>Resolves window-relative pixel coordinates to a position.</summary>
    /// <param name="x">The window-relative x.</param>
    /// <param name="y">The window-relative y.</param>
    /// <returns>The position under the point.</returns>
    public TextPosition PositionAtPoint(int x, int y)
    {
        List<DisplaySegment> display = DisplayLines();
        if (display.Count == 0) { return new TextPosition(1, 0); }

        int inset = Inset;
        int displayLine = _topDisplayLine + (y - inset) / LineHeight;
        if (displayLine < 0) { displayLine = 0; }
        if (displayLine >= display.Count) { displayLine = display.Count - 1; }

        DisplaySegment segment = display[displayLine];
        string content = _lines[segment.Line - 1].Substring(segment.Start, segment.End - segment.Start);

        FontManager fonts = Fonts;
        TkFont font = Font;
        int targetX = x - inset;
        int charIndex = content.Length;
        for (int i = 0; i <= content.Length; i++)
        {
            int prefix = fonts.Measure(font, content.Substring(0, i));
            if (prefix > targetX)
            {
                charIndex = (i > 0) ? i - 1 : 0;
                break;
            }
        }
        return new TextPosition(segment.Line, segment.Start + charIndex);
    }

    /// <inheritdoc/>
    public void Paint(SKCanvas canvas)
    {
        int inset = Inset;
        Theming.TkTheme theme = Window.Tree.Theme;
        SKColor background;
        if (!TkColor.TryParse(Options.Get("-background", theme.FieldBackground), out background))
        {
            background = SKColors.White;
        }

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background;
            canvas.DrawRect(new SKRect(0, 0, Window.Width, Window.Height), paint);
        }

        ReliefPainter.DrawBorder(canvas,
                new SKRect(0, 0, Window.Width, Window.Height),
                Options.GetInt("-borderwidth", 1),
                ReliefPainter.Parse(Options.Get("-relief", "sunken")),
                Theming.TkTheme.Color(theme.Background));

        FontManager fonts = Fonts;
        TkFont font = Font;
        FontMetrics metrics = fonts.Metrics(font);
        int lineHeight = metrics.LineSpace;
        List<DisplaySegment> display = DisplayLines();
        int visible = VisibleLines;
        TextPosition caret = _marks["insert"].Position;
        bool focused = Window.Tree.FocusWindow == Window;

        using (SKFont skFont = fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            for (int row = 0; row < visible; row++)
            {
                int displayIndex = _topDisplayLine + row;
                if (displayIndex < 0 || displayIndex >= display.Count) { break; }
                DisplaySegment segment = display[displayIndex];
                int top = inset + row * lineHeight;

                // Split the segment into style runs at tag boundaries.
                int cursor = segment.Start;
                float xPixel = inset;
                while (cursor < segment.End)
                {
                    int runEnd = segment.End;
                    string foreground = Options.Get("-foreground", theme.FieldForeground);
                    string runBackground = null;
                    bool underline = false;
                    bool overstrike = false;

                    var position = new TextPosition(segment.Line, cursor);
                    foreach (TextTag tag in _tags)
                    {
                        if (!tag.Covers(position)) { continue; }
                        if (tag.Options.IsSet("-foreground")) { foreground = tag.Options.Get("-foreground"); }
                        if (tag.Options.IsSet("-background")) { runBackground = tag.Options.Get("-background"); }
                        else if (tag.Name == "sel") { runBackground = theme.SelectBackground; }
                        if (tag.Options.GetBool("-underline")) { underline = true; }
                        if (tag.Options.GetBool("-overstrike")) { overstrike = true; }
                    }

                    // The run ends where any tag's coverage changes.
                    foreach (TextTag tag in _tags)
                    {
                        foreach (TextPosition boundary in tag.Boundaries)
                        {
                            if (boundary.Line == segment.Line && boundary.Char > cursor
                                    && boundary.Char < runEnd)
                            {
                                runEnd = boundary.Char;
                            }
                        }
                    }

                    string run = _lines[segment.Line - 1].Substring(cursor, runEnd - cursor);
                    float runWidth = fonts.Measure(font, run);

                    if (runBackground != null)
                    {
                        SKColor runBg;
                        if (TkColor.TryParse(runBackground, out runBg))
                        {
                            paint.Color = runBg;
                            paint.Style = SKPaintStyle.Fill;
                            canvas.DrawRect(new SKRect(xPixel, top, xPixel + runWidth, top + lineHeight), paint);
                        }
                    }

                    SKColor textColor;
                    TkColor.TryParse(foreground, out textColor);
                    paint.Color = textColor;
                    paint.Style = SKPaintStyle.Fill;
                    paint.IsAntialias = true;
                    canvas.DrawText(run, xPixel, top + metrics.Ascent, SKTextAlign.Left, skFont, paint);
                    paint.IsAntialias = false;

                    if (underline)
                    {
                        canvas.DrawRect(new SKRect(xPixel, top + metrics.Ascent + 1,
                                xPixel + runWidth, top + metrics.Ascent + 2), paint);
                    }
                    if (overstrike)
                    {
                        float mid = top + metrics.Ascent - metrics.Ascent / 3f;
                        canvas.DrawRect(new SKRect(xPixel, mid, xPixel + runWidth, mid + 1), paint);
                    }

                    xPixel += runWidth;
                    cursor = runEnd;
                }

                // The caret — preceded, during IME composition, by the live
                // pre-edit string drawn as separate state at the caret
                // position (underlined, on the widget background; a
                // first-pass rendering that overlays rather than reflows the
                // rest of the line).
                if (focused && caret.Line == segment.Line
                        && caret.Char >= segment.Start
                        && (caret.Char < segment.End
                            || (caret.Char == segment.End && IsLastSegmentOfLine(display, displayIndex))))
                {
                    string prefix = _lines[segment.Line - 1]
                            .Substring(segment.Start, caret.Char - segment.Start);
                    float caretX = inset + fonts.Measure(font, prefix);

                    if (_composition.Length > 0)
                    {
                        float compWidth = fonts.Measure(font, _composition);
                        SKColor compBg;
                        if (!TkColor.TryParse(Options.Get("-background", theme.FieldBackground), out compBg))
                        {
                            compBg = SKColors.White;
                        }
                        paint.Color = compBg;
                        paint.Style = SKPaintStyle.Fill;
                        canvas.DrawRect(new SKRect(caretX, top, caretX + compWidth, top + lineHeight), paint);

                        paint.Color = Theming.TkTheme.Color(
                                Options.Get("-foreground", theme.FieldForeground));
                        paint.IsAntialias = true;
                        canvas.DrawText(_composition, caretX, top + metrics.Ascent,
                                SKTextAlign.Left, skFont, paint);
                        paint.IsAntialias = false;
                        canvas.DrawRect(new SKRect(caretX, top + metrics.Ascent + 1,
                                caretX + compWidth, top + metrics.Ascent + 2), paint);
                        caretX += compWidth;
                    }

                    paint.Color = Theming.TkTheme.Color(
                            Options.Get("-insertbackground", theme.InsertBackground));
                    canvas.DrawRect(new SKRect(caretX, top, caretX + 2, top + lineHeight), paint);
                }
            }
        }
    }

    private static bool IsLastSegmentOfLine(List<DisplaySegment> display, int index)
    {
        return index + 1 >= display.Count || display[index + 1].Line != display[index].Line;
    }

    // ------------------------------------------------------------------
    // Class behavior: click-caret and drag-select
    // ------------------------------------------------------------------

    private void RegisterClassBindings(BindingTable bindings)
    {
        bindings.Bind("Text", "<ButtonPress-1>", HandleButtonPress);
        bindings.Bind("Text", "<B1-Motion>", HandleDragSelect);
        bindings.Bind("Text", "<KeyPress>", HandleKeyPress);
        bindings.Bind("Text", "<FocusIn>", HandleFocusIn);
        bindings.Bind("Text", "<FocusOut>", HandleFocusOut);
        bindings.Bind("Text", "<Configure>", HandleConfigure);
    }

    private static DispatchResult HandleConfigure(TkEvent tkEvent)
    {
        // The wrap layout depends on the widget width; when the widget is
        // resized (its content flows through nested grids only after the
        // toplevel settles its size), drop the cached line layout and
        // repaint so text does not stay wrapped at a stale width.
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null) { return DispatchResult.Continue; }
        widget._display = null;
        widget.Window.Tree.Scheduler.ScheduleRepaint();
        return DispatchResult.Continue;
    }

    private static DispatchResult HandleFocusIn(TkEvent tkEvent)
    {
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null) { return DispatchResult.Continue; }
        ITextInputSink sink = widget.InputSink;
        if (sink != null)
        {
            sink.Attach(widget);
            widget.NotifySinkCaret();
        }
        widget.Window.Tree.Scheduler.ScheduleRepaint();
        return DispatchResult.Continue;
    }

    private static DispatchResult HandleFocusOut(TkEvent tkEvent)
    {
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null) { return DispatchResult.Continue; }
        widget.SetComposition(null);
        ITextInputSink sink = widget.InputSink;
        if (sink != null) { sink.Detach(); }
        widget.Window.Tree.Scheduler.ScheduleRepaint();
        return DispatchResult.Continue;
    }

    // ------------------------------------------------------------------
    // Class behavior: key-driven editing (the Tk Text-class key bindings
    // DRAKON's editing relies on; committed PRINTABLE text normally arrives
    // through the input sink — the Character path here serves hosts and
    // tests that route plain keystrokes as key events)
    // ------------------------------------------------------------------

    private static DispatchResult HandleKeyPress(TkEvent tkEvent)
    {
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null) { return DispatchResult.Continue; }

        bool shift = (tkEvent.State & EventModifiers.Shift) != 0;
        bool control = (tkEvent.State & EventModifiers.Control) != 0;

        switch (tkEvent.KeySym)
        {
            case "Left":
                widget.MoveCaret(control ? "insert - 1 chars wordstart" : "insert - 1 chars", shift);
                return DispatchResult.Break;
            case "Right":
                widget.MoveCaret(control ? "insert + 1 chars wordend" : "insert + 1 chars", shift);
                return DispatchResult.Break;
            case "Up":
                widget.MoveCaret("insert - 1 lines", shift);
                return DispatchResult.Break;
            case "Down":
                widget.MoveCaret("insert + 1 lines", shift);
                return DispatchResult.Break;
            case "Home":
                widget.MoveCaret(control ? "1.0" : "insert linestart", shift);
                return DispatchResult.Break;
            case "End":
                widget.MoveCaret(control ? "end - 1 chars" : "insert lineend", shift);
                return DispatchResult.Break;
            case "Prior":
                widget.MoveCaret("insert - " + Math.Max(1, widget.VisibleLines) + " lines", shift);
                return DispatchResult.Break;
            case "Next":
                widget.MoveCaret("insert + " + Math.Max(1, widget.VisibleLines) + " lines", shift);
                return DispatchResult.Break;
            case "BackSpace":
                if (!widget.DeleteSelectionIfAny())
                {
                    TextPosition caret = widget._marks["insert"].Position;
                    if (caret > new TextPosition(1, 0))
                    {
                        widget.Delete("insert - 1 chars", "insert");
                    }
                }
                widget.See("insert");
                return DispatchResult.Break;
            case "Delete":
                if (!widget.DeleteSelectionIfAny())
                {
                    if (widget._marks["insert"].Position < widget.ParseIndex("end - 1 chars"))
                    {
                        widget.Delete("insert", "insert + 1 chars");
                    }
                }
                widget.See("insert");
                return DispatchResult.Break;
            case "Return":
            case "KP_Enter":
                widget.CommitText("\n");
                return DispatchResult.Break;
            case "Tab":
                widget.CommitText("\t");
                return DispatchResult.Break;
            default:
                break;
        }

        if (control)
        {
            switch (tkEvent.KeySym)
            {
                case "c":
                    widget.CopySelectionToClipboard(false);
                    return DispatchResult.Break;
                case "x":
                    widget.CopySelectionToClipboard(true);
                    return DispatchResult.Break;
                case "v":
                    widget.PasteFromClipboard();
                    return DispatchResult.Break;
                default:
                    return DispatchResult.Continue;
            }
        }

        if (!string.IsNullOrEmpty(tkEvent.Character)
                && tkEvent.Character[0] >= ' ' && tkEvent.Character[0] != (char)0x7F)
        {
            widget.CommitText(tkEvent.Character);
            return DispatchResult.Break;
        }
        return DispatchResult.Continue;
    }

    /// <summary>
    /// Moves the insert mark to an index expression, either collapsing the
    /// selection (plain movement) or extending it from the anchor
    /// (Shift-movement) — the model under the Text-class arrow bindings.
    /// </summary>
    /// <param name="indexExpr">The target index expression.</param>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveCaret(string indexExpr, bool extend)
    {
        TextPosition target;
        try
        {
            target = ParseIndex(indexExpr);
        }
        catch (ArgumentException)
        {
            return;
        }
        TextPosition last = ParseIndex("end - 1 chars");
        if (target > last) { target = last; }
        if (target < new TextPosition(1, 0)) { target = new TextPosition(1, 0); }

        TextTag sel = GetTag("sel");
        if (extend)
        {
            if (!_hasSelectionAnchor)
            {
                _selectionAnchor = _marks["insert"].Position;
                _hasSelectionAnchor = true;
            }
            sel.ClearRanges();
            if (target != _selectionAnchor)
            {
                TextPosition start = (target < _selectionAnchor) ? target : _selectionAnchor;
                TextPosition end = (target < _selectionAnchor) ? _selectionAnchor : target;
                sel.AddRange(start, end);
            }
        }
        else
        {
            sel.ClearRanges();
            _hasSelectionAnchor = false;
        }

        MarkSet("insert", target.ToString());
        See("insert");
        Window.Tree.Scheduler.ScheduleRepaint();
    }

    /// <summary>Deletes the selection when one exists.</summary>
    /// <returns>True when a selection was deleted.</returns>
    public bool DeleteSelectionIfAny()
    {
        TextTag sel = GetTag("sel");
        if (sel.Boundaries.Count < 2) { return false; }
        Delete(sel.Boundaries[0].ToString(), sel.Boundaries[sel.Boundaries.Count - 1].ToString());
        sel.ClearRanges();
        _hasSelectionAnchor = false;
        return true;
    }

    /// <summary>
    /// Copies (or, for a cut, also deletes) the selection through the
    /// tree's clipboard — the Control-c/Control-x class behavior.
    /// </summary>
    /// <param name="cut">Whether to delete the selection after copying.</param>
    public void CopySelectionToClipboard(bool cut)
    {
        TextTag sel = GetTag("sel");
        if (sel.Boundaries.Count < 2) { return; }
        string selected = Get(sel.Boundaries[0].ToString(),
                sel.Boundaries[sel.Boundaries.Count - 1].ToString());
        Clipboard.ClipboardManager clipboard = Window.Tree.Clipboard;
        clipboard.Clear();
        clipboard.Append(selected);
        if (cut) { DeleteSelectionIfAny(); }
    }

    /// <summary>Pastes the clipboard text at the caret — the Control-v class behavior.</summary>
    public void PasteFromClipboard()
    {
        string text;
        try
        {
            text = Window.Tree.Clipboard.Get();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (text.Length > 0) { CommitText(text); }
    }

    private static DispatchResult HandleButtonPress(TkEvent tkEvent)
    {
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null) { return DispatchResult.Continue; }

        TextPosition position = widget.PositionAtPoint(tkEvent.X, tkEvent.Y);
        widget.MarkSet("insert", position.ToString());
        widget._selectionAnchor = position;
        widget._hasSelectionAnchor = true;
        widget.GetTag("sel").ClearRanges();
        widget.Window.Tree.SetFocus(widget.Window);
        widget.Window.Tree.Scheduler.ScheduleRepaint();
        return DispatchResult.Continue;
    }

    private static DispatchResult HandleDragSelect(TkEvent tkEvent)
    {
        var widget = tkEvent.Window.Widget as TextWidget;
        if (widget == null || !widget._hasSelectionAnchor) { return DispatchResult.Continue; }

        TextPosition position = widget.PositionAtPoint(tkEvent.X, tkEvent.Y);
        TextTag sel = widget.GetTag("sel");
        sel.ClearRanges();
        if (position != widget._selectionAnchor)
        {
            TextPosition start = (position < widget._selectionAnchor) ? position : widget._selectionAnchor;
            TextPosition end = (position < widget._selectionAnchor) ? widget._selectionAnchor : position;
            sel.AddRange(start, end);
        }
        widget.MarkSet("insert", position.ToString());
        widget.Window.Tree.Scheduler.ScheduleRepaint();
        return DispatchResult.Continue;
    }

    private void NotifySinkCaret()
    {
        ITextInputSink sink = InputSink;
        if (sink == null) { return; }

        TextPosition caret = _marks["insert"].Position;
        int displayLine = DisplayLineOf(caret);
        List<DisplaySegment> display = DisplayLines();
        if (displayLine < 0 || displayLine >= display.Count) { return; }
        DisplaySegment segment = display[displayLine];
        string prefix = _lines[segment.Line - 1]
                .Substring(segment.Start, Math.Max(0, caret.Char - segment.Start));
        int x = Inset + Fonts.Measure(Font, prefix);
        int y = Inset + (displayLine - _topDisplayLine) * LineHeight;
        sink.UpdateCaret(x, y, LineHeight);
    }
}
