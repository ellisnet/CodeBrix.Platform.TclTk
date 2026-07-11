using System;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The classic Tk <c>entry</c> widget: a single-line text field with the
/// full model surface (text buffer, insertion cursor, selection range, the
/// <c>index</c>/<c>insert</c>/<c>delete</c>/<c>icursor</c>/<c>selection</c>
/// surface, <c>-show</c> masking), click-to-place-caret and drag-select, and
/// key-driven editing (arrows, Home/End, BackSpace/Delete, word movement,
/// Shift-selection, clipboard cut/copy/paste). As an
/// <see cref="ITextInputTarget"/> it receives committed text and live IME
/// composition from the host's hidden input element
/// (<see cref="ITextInputSink"/>). The widget draws its sunken white field,
/// the (masked) text, the selection band, the pre-edit string, and the caret
/// when it holds focus.
/// </summary>
public sealed class EntryWidget : WidgetBase, ITextInputTarget
{
    private string _text = "";
    private int _cursor;
    private int _selectFirst = -1;
    private int _selectLast = -1;
    private int _leftIndex; // first visible character (horizontal scroll)
    private int _selectAnchor = -1;
    private string _composition = "";

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
        get { return Theme.FieldBackground; }
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

    /// <summary>The live IME pre-edit string, empty outside composition.</summary>
    public string Composition
    {
        get { return _composition; }
    }

    /// <inheritdoc/>
    public void CommitText(string text)
    {
        SetComposition(null);
        if (string.IsNullOrEmpty(text) || Options.Get("-state", "normal") == "disabled")
        {
            return;
        }
        if (_selectFirst >= 0 && _selectLast > _selectFirst)
        {
            int first = _selectFirst;
            Delete(_selectFirst, _selectLast);
            _cursor = first;
        }
        Insert(_cursor, text);
        NotifySinkCaret();
    }

    /// <inheritdoc/>
    public void SetComposition(string preedit)
    {
        string value = preedit ?? "";
        if (value == _composition) { return; }
        _composition = value;
        Repaint();
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
                if (!TkColor.TryParse(ResolveOption("-selectbackground", Theme.SelectBackground), out selBg))
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
                    ? ResolveOption("-disabledforeground", Theme.DisabledForeground)
                    : Options.IsSet("-fg") && !Options.IsSet("-foreground")
                            ? Options.Get("-fg")
                            : ResolveOption("-foreground", Theme.FieldForeground);
            if (!TkColor.TryParse(fgSpec, out fg)) { fg = SKColors.Black; }
            paint.Color = fg;
            paint.IsAntialias = true;
            if (shown.Length > _leftIndex)
            {
                canvas.DrawText(shown.Substring(_leftIndex), inset, baseline,
                        SKTextAlign.Left, skFont, paint);
            }

            // Caret (only when this entry holds focus) — preceded, during
            // IME composition, by the live pre-edit string drawn as
            // separate state at the caret (underlined; a first-pass overlay
            // rendering).
            if (ReferenceEquals(Window.Tree.FocusWindow, Window)
                    && Options.Get("-state", "normal") != "disabled")
            {
                float cx = inset + Fonts.Measure(font, Slice(shown, _leftIndex, _cursor));

                if (_composition.Length > 0)
                {
                    float compWidth = Fonts.Measure(font, _composition);
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = background;
                    paint.IsAntialias = false;
                    canvas.DrawRect(new SKRect(cx, inset, cx + compWidth, Window.Height - inset), paint);
                    paint.Color = fg;
                    paint.IsAntialias = true;
                    canvas.DrawText(_composition, cx, baseline, SKTextAlign.Left, skFont, paint);
                    paint.IsAntialias = false;
                    canvas.DrawRect(new SKRect(cx, baseline + 1, cx + compWidth, baseline + 2), paint);
                    cx += compWidth;
                }

                paint.Color = ResolveColor("-insertbackground", Theme.InsertBackground);
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
        bindings.Bind("Entry", "<B1-Motion>", OnDragSelect);
        bindings.Bind("Entry", "<KeyPress>", OnKeyPress);
        bindings.Bind("Entry", "<FocusIn>", OnFocusIn);
        bindings.Bind("Entry", "<FocusOut>", OnFocusOut);
    }

    private static EntryWidget From(TkEvent tkEvent)
    {
        return (tkEvent.Window != null) ? tkEvent.Window.Widget as EntryWidget : null;
    }

    private static DispatchResult OnPress(TkEvent tkEvent)
    {
        EntryWidget e = From(tkEvent);
        if (e == null || e.Options.Get("-state", "normal") == "disabled")
        {
            return DispatchResult.Continue;
        }
        e.Window.Tree.SetFocus(e.Window);
        e.SetCursor(e.IndexAt(tkEvent.X));
        e._selectAnchor = e._cursor;
        e.ClearSelection();
        e.NotifySinkCaret();
        return DispatchResult.Continue;
    }

    private static DispatchResult OnDragSelect(TkEvent tkEvent)
    {
        EntryWidget e = From(tkEvent);
        if (e == null || e._selectAnchor < 0) { return DispatchResult.Continue; }
        int index = e.IndexAt(tkEvent.X);
        e.SetCursor(index);
        if (index != e._selectAnchor)
        {
            e.SelectRange(Math.Min(index, e._selectAnchor), Math.Max(index, e._selectAnchor));
        }
        else
        {
            e.ClearSelection();
            e.Repaint();
        }
        return DispatchResult.Continue;
    }

    private static DispatchResult OnFocusIn(TkEvent tkEvent)
    {
        EntryWidget e = From(tkEvent);
        if (e == null) { return DispatchResult.Continue; }
        ITextInputSink sink = e.Window.Tree.InputSink;
        if (sink != null)
        {
            sink.Attach(e);
            e.NotifySinkCaret();
        }
        e.Repaint();
        return DispatchResult.Continue;
    }

    private static DispatchResult OnFocusOut(TkEvent tkEvent)
    {
        EntryWidget e = From(tkEvent);
        if (e == null) { return DispatchResult.Continue; }
        e.SetComposition(null);
        ITextInputSink sink = e.Window.Tree.InputSink;
        if (sink != null) { sink.Detach(); }
        e.Repaint();
        return DispatchResult.Continue;
    }

    // ------------------------------------------------------------------
    // Class behavior: key-driven editing (committed printable text normally
    // arrives through the input sink; the Character path serves hosts and
    // tests that route plain keystrokes as key events)
    // ------------------------------------------------------------------

    private static DispatchResult OnKeyPress(TkEvent tkEvent)
    {
        EntryWidget e = From(tkEvent);
        if (e == null || e.Options.Get("-state", "normal") == "disabled")
        {
            return DispatchResult.Continue;
        }

        bool shift = (tkEvent.State & EventModifiers.Shift) != 0;
        bool control = (tkEvent.State & EventModifiers.Control) != 0;

        switch (tkEvent.KeySym)
        {
            case "Left":
                e.MoveCursor(control ? e.PreviousWord(e._cursor) : e._cursor - 1, shift);
                return DispatchResult.Break;
            case "Right":
                e.MoveCursor(control ? e.NextWord(e._cursor) : e._cursor + 1, shift);
                return DispatchResult.Break;
            case "Home":
                e.MoveCursor(0, shift);
                return DispatchResult.Break;
            case "End":
                e.MoveCursor(e._text.Length, shift);
                return DispatchResult.Break;
            case "BackSpace":
                if (e.DeleteSelectionIfAny()) { return DispatchResult.Break; }
                if (e._cursor > 0) { e.Delete(e._cursor - 1); }
                e.NotifySinkCaret();
                return DispatchResult.Break;
            case "Delete":
                if (e.DeleteSelectionIfAny()) { return DispatchResult.Break; }
                if (e._cursor < e._text.Length) { e.Delete(e._cursor); }
                e.NotifySinkCaret();
                return DispatchResult.Break;
            default:
                break;
        }

        if (control)
        {
            switch (tkEvent.KeySym)
            {
                case "c":
                    e.CopySelectionToClipboard(false);
                    return DispatchResult.Break;
                case "x":
                    e.CopySelectionToClipboard(true);
                    return DispatchResult.Break;
                case "v":
                    e.PasteFromClipboard();
                    return DispatchResult.Break;
                default:
                    return DispatchResult.Continue;
            }
        }

        if (!string.IsNullOrEmpty(tkEvent.Character)
                && tkEvent.Character[0] >= ' ' && tkEvent.Character[0] != (char)0x7F)
        {
            e.CommitText(tkEvent.Character);
            return DispatchResult.Break;
        }
        return DispatchResult.Continue;
    }

    /// <summary>
    /// Moves the insertion cursor, either collapsing the selection (plain
    /// movement) or extending it from the anchor (Shift-movement).
    /// </summary>
    /// <param name="index">The target character index (clamped).</param>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveCursor(int index, bool extend)
    {
        index = Clamp(index, 0, _text.Length);
        if (extend)
        {
            if (_selectAnchor < 0) { _selectAnchor = _cursor; }
            SetCursor(index);
            if (index != _selectAnchor)
            {
                SelectRange(Math.Min(index, _selectAnchor), Math.Max(index, _selectAnchor));
            }
            else
            {
                ClearSelection();
                Repaint();
            }
        }
        else
        {
            _selectAnchor = index;
            ClearSelection();
            SetCursor(index);
        }
        NotifySinkCaret();
    }

    /// <summary>Deletes the selection when one exists.</summary>
    /// <returns>True when a selection was deleted.</returns>
    public bool DeleteSelectionIfAny()
    {
        if (_selectFirst < 0 || _selectLast <= _selectFirst) { return false; }
        int first = _selectFirst;
        Delete(_selectFirst, _selectLast);
        SetCursor(first);
        NotifySinkCaret();
        return true;
    }

    /// <summary>
    /// Copies (or, for a cut, also deletes) the selection through the
    /// tree's clipboard — the Control-c/Control-x class behavior. Like Tk,
    /// an entry under <c>-show</c> masking copies the masked display
    /// string, never the underlying secret.
    /// </summary>
    /// <param name="cut">Whether to delete the selection after copying.</param>
    public void CopySelectionToClipboard(bool cut)
    {
        if (_selectFirst < 0 || _selectLast <= _selectFirst) { return; }
        string shown = Display();
        string selected = Slice(shown, _selectFirst, _selectLast);
        Clipboard.ClipboardManager clipboard = Window.Tree.Clipboard;
        clipboard.Clear();
        clipboard.Append(selected);
        if (cut) { DeleteSelectionIfAny(); }
    }

    /// <summary>Pastes the clipboard text at the cursor — the Control-v class behavior.</summary>
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

    private int PreviousWord(int index)
    {
        int i = Math.Min(index, _text.Length) - 1;
        while (i > 0 && !IsWordChar(_text[i])) { i--; }
        while (i > 0 && IsWordChar(_text[i - 1])) { i--; }
        return (i < 0) ? 0 : i;
    }

    private int NextWord(int index)
    {
        int i = index;
        while (i < _text.Length && IsWordChar(_text[i])) { i++; }
        while (i < _text.Length && !IsWordChar(_text[i])) { i++; }
        return i;
    }

    private static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Reports the caret rectangle to the attached input sink, so the host
    /// anchors its hidden input element (and the IME candidate window).
    /// </summary>
    private void NotifySinkCaret()
    {
        ITextInputSink sink = Window.Tree.InputSink;
        if (sink == null) { return; }
        TkFont font = Font;
        int inset = Inset + 1;
        int x = inset + Fonts.Measure(font, Slice(Display(), _leftIndex, _cursor));
        int lineHeight = Fonts.Metrics(font).LineSpace;
        int y = Math.Max(inset, (Window.Height - lineHeight) / 2);
        sink.UpdateCaret(x, y, lineHeight);
    }
}
