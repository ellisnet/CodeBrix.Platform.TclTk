using System;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>entry</c> widget — INPUT-STUBBED for this sub-phase (the
/// plan's §18.3): the full painting, geometry, and selection/caret MODEL are
/// implemented (text buffer, insertion cursor, selection range, the
/// <c>index</c>/<c>insert</c>/<c>delete</c>/<c>icursor</c>/<c>selection</c>
/// surface, <c>-show</c> masking, click-to-place-caret), but there is NO
/// key-input path — real typing wires to the hidden-TextBox
/// <see cref="Text.ITextInputSink"/> in the later interactive sub-phase. The
/// widget draws its sunken white field, the (masked) text, the selection
/// band, and the caret when it holds focus.
/// </summary>
public sealed class EntryWidget : WidgetBase
{
    private string _text = "";
    private int _cursor;
    private int _selectFirst = -1;
    private int _selectLast = -1;
    private int _leftIndex; // first visible character (horizontal scroll)

    /// <summary>Creates an entry on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public EntryWidget(TkWindow window)
        : base(window, "Entry")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Entry"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 2; }
    }

    private protected override string DefaultRelief
    {
        get { return "sunken"; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    private protected override string DefaultBackground
    {
        get { return "white"; }
    }

    /// <summary>The entry's text content (the model source of truth).</summary>
    public string Text
    {
        get { return _text; }
    }

    /// <summary>The insertion-cursor character index (<c>icursor</c>/<c>index insert</c>).</summary>
    public int Cursor
    {
        get { return _cursor; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkTextFont");
        }
    }

    /// <summary>Replaces the whole text and clamps the cursor/selection — the model set path.</summary>
    /// <param name="text">The new text.</param>
    public void SetText(string text)
    {
        _text = text ?? "";
        if (_cursor > _text.Length) { _cursor = _text.Length; }
        ClearSelection();
        ScrollToCursor();
        Repaint();
    }

    /// <summary>Inserts text at a character index — <c>insert</c>.</summary>
    /// <param name="index">The insertion index (clamped to the text length).</param>
    /// <param name="text">The text to insert.</param>
    public void Insert(int index, string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }
        index = Clamp(index, 0, _text.Length);
        _text = _text.Substring(0, index) + text + _text.Substring(index);
        if (_cursor >= index) { _cursor += text.Length; }
        ScrollToCursor();
        Repaint();
    }

    /// <summary>Deletes the characters in <c>[first, last)</c> — <c>delete</c>.</summary>
    /// <param name="first">The first index to delete (inclusive).</param>
    /// <param name="last">The end index (exclusive); defaults to first+1.</param>
    public void Delete(int first, int last = -1)
    {
        if (last < 0) { last = first + 1; }
        first = Clamp(first, 0, _text.Length);
        last = Clamp(last, 0, _text.Length);
        if (last <= first) { return; }
        _text = _text.Substring(0, first) + _text.Substring(last);
        if (_cursor > last) { _cursor -= (last - first); }
        else if (_cursor > first) { _cursor = first; }
        ClearSelection();
        ScrollToCursor();
        Repaint();
    }

    /// <summary>Sets the insertion cursor — <c>icursor</c>.</summary>
    /// <param name="index">The new cursor index (clamped).</param>
    public void SetCursor(int index)
    {
        _cursor = Clamp(index, 0, _text.Length);
        ScrollToCursor();
        Repaint();
    }

    /// <summary>Sets the selection range <c>[first, last)</c> — <c>selection range</c>.</summary>
    /// <param name="first">The selection start (inclusive).</param>
    /// <param name="last">The selection end (exclusive).</param>
    public void SelectRange(int first, int last)
    {
        first = Clamp(first, 0, _text.Length);
        last = Clamp(last, 0, _text.Length);
        if (last <= first) { ClearSelection(); return; }
        _selectFirst = first;
        _selectLast = last;
        Repaint();
    }

    /// <summary>Clears the selection — <c>selection clear</c>.</summary>
    public void ClearSelection()
    {
        _selectFirst = _selectLast = -1;
    }

    /// <summary>The selected text, or empty when there is no selection.</summary>
    public string SelectedText
    {
        get
        {
            return (_selectFirst >= 0 && _selectLast > _selectFirst)
                    ? _text.Substring(_selectFirst, _selectLast - _selectFirst)
                    : "";
        }
    }

    /// <summary>The character index nearest a window x coordinate — <c>index @x</c>.</summary>
    /// <param name="x">The window-relative x in pixels.</param>
    /// <returns>The character index.</returns>
    public int IndexAt(int x)
    {
        TkFont font = Font;
        int inset = Inset + 1;
        int local = x - inset;
        string shown = Display();
        int i = _leftIndex;
        int acc = 0;
        while (i < shown.Length)
        {
            int cw = Fonts.Measure(font, shown.Substring(i, 1));
            if (acc + cw / 2 >= local) { break; }
            acc += cw;
            i++;
        }
        return i;
    }

    private string Display()
    {
        string show = Options.Get("-show", "");
        if (show.Length > 0 && _text.Length > 0)
        {
            return new string(show[0], _text.Length);
        }
        return _text;
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int chars = Options.GetInt("-width", 20);
        int charWidth = Fonts.Measure(font, "0");
        if (charWidth < 1) { charWidth = 1; }
        int lineHeight = Fonts.Metrics(font).LineSpace;

        int inset = Inset;
        int reqW = chars * charWidth + 2 * inset + 2; // +2 for the 1px text margin
        int reqH = lineHeight + 2 * inset + 2;
        Window.SetRequestedSize(reqW, reqH);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        SKColor background = BackgroundColor;
        if (Options.Get("-state", "normal") == "disabled" && Options.IsSet("-disabledbackground"))
        {
            SKColor dis;
            if (TkColor.TryParse(Options.Get("-disabledbackground"), out dis)) { background = dis; }
        }
        PaintBackgroundAndBorder(canvas, background);

        TkFont font = Font;
        FontMetrics metrics = Fonts.Metrics(font);
        int inset = Inset + 1;
        string shown = Display();
        float baseline = inset + (Window.Height - 2 * inset - metrics.LineSpace) / 2f + metrics.Ascent;

        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            // Selection band.
            if (_selectFirst >= 0 && _selectLast > _selectFirst)
            {
                float sx = inset + Fonts.Measure(font, Slice(shown, _leftIndex, _selectFirst));
                float ex = inset + Fonts.Measure(font, Slice(shown, _leftIndex, _selectLast));
                SKColor selBg;
                if (!TkColor.TryParse(Options.Get("-selectbackground", "#c3c3c3"), out selBg))
                {
                    selBg = new SKColor(0xC3, 0xC3, 0xC3);
                }
                paint.Style = SKPaintStyle.Fill;
                paint.Color = selBg;
                paint.IsAntialias = false;
                canvas.DrawRect(new SKRect(sx, inset, ex, Window.Height - inset), paint);
            }

            // Text.
            SKColor fg;
            string fgSpec = (Options.Get("-state", "normal") == "disabled")
                    ? Options.Get("-disabledforeground", "#a3a3a3")
                    : Options.Get("-foreground", Options.Get("-fg", "black"));
            if (!TkColor.TryParse(fgSpec, out fg)) { fg = SKColors.Black; }
            paint.Color = fg;
            paint.IsAntialias = true;
            if (shown.Length > _leftIndex)
            {
                canvas.DrawText(shown.Substring(_leftIndex), inset, baseline,
                        SKTextAlign.Left, skFont, paint);
            }

            // Caret (only when this entry holds focus).
            if (ReferenceEquals(Window.Tree.FocusWindow, Window)
                    && Options.Get("-state", "normal") != "disabled")
            {
                float cx = inset + Fonts.Measure(font, Slice(shown, _leftIndex, _cursor));
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Fill;
                paint.IsAntialias = false;
                canvas.DrawRect(new SKRect(cx, inset, cx + 2, Window.Height - inset), paint);
            }
        }
    }

    private static string Slice(string text, int from, int to)
    {
        from = Clamp(from, 0, text.Length);
        to = Clamp(to, 0, text.Length);
        return (to > from) ? text.Substring(from, to - from) : "";
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }

    /// <summary>
    /// Keeps the insertion cursor within the visible field by advancing the
    /// first-visible character index — the horizontal-scroll model. (Real
    /// key-driven editing arrives with the input sink; this already tracks the
    /// caret for programmatic model changes.)
    /// </summary>
    private void ScrollToCursor()
    {
        int fieldWidth = Window.Width - 2 * (Inset + 1);
        if (fieldWidth <= 0) { _leftIndex = 0; return; }
        if (_cursor < _leftIndex) { _leftIndex = _cursor; return; }

        TkFont font = Font;
        string shown = Display();
        while (_leftIndex < _cursor
                && Fonts.Measure(font, Slice(shown, _leftIndex, _cursor)) > fieldWidth)
        {
            _leftIndex++;
        }
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("Entry", "<ButtonPress-1>", OnPress);
    }

    private static DispatchResult OnPress(TkEvent tkEvent)
    {
        EntryWidget e = (tkEvent.Window != null) ? tkEvent.Window.Widget as EntryWidget : null;
        if (e == null || e.Options.Get("-state", "normal") == "disabled")
        {
            return DispatchResult.Continue;
        }
        e.Window.Tree.SetFocus(e.Window);
        e.SetCursor(e.IndexAt(tkEvent.X));
        e.ClearSelection();
        return DispatchResult.Continue;
    }
}
