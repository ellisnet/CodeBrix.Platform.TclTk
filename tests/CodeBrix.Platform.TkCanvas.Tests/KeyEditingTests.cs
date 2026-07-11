using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Text;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// B.8a coverage: key-driven editing for the <c>text</c> and <c>entry</c>
/// widgets — caret movement (chars/words/lines/home/end), Shift-selection,
/// BackSpace/Delete, Return/Tab, clipboard cut/copy/paste through the tree
/// clipboard, the committed-text path (<see cref="ITextInputTarget"/>), the
/// IME composition state, and the focus-driven input-sink attach/detach.
/// </summary>
public class KeyEditingTests
{
    private static TkWindow Root()
    {
        TkWindow root = TkWindow.CreateRoot();
        root.SetForcedSize(400, 300);
        return root;
    }

    private static TextWidget FocusedText(out TkWindow root, string content = "hello world\nsecond line")
    {
        root = Root();
        TkWindow window = root.CreateChild("t");
        var text = new TextWidget(window);
        text.Insert("1.0", content);
        text.MarkSet("insert", "1.0");
        root.Tree.SetFocus(window);
        return text;
    }

    private static EntryWidget FocusedEntry(out TkWindow root, string content = "hello world")
    {
        root = Root();
        TkWindow window = root.CreateChild("e");
        var entry = new EntryWidget(window);
        entry.SetText(content);
        entry.SetCursor(0);
        root.Tree.SetFocus(window);
        return entry;
    }

    private static void Key(TkWindow root, string keySym, string character = "",
            EventModifiers state = EventModifiers.None)
    {
        root.Tree.KeyEvent(TkEventType.KeyPress, keySym, character, state);
    }

    private static void Type(TkWindow root, string textToType)
    {
        foreach (char c in textToType)
        {
            Key(root, c.ToString(), c.ToString());
        }
    }

    // ------------------------------------------------------------------
    // Text widget
    // ------------------------------------------------------------------

    [Fact]
    public void Text_typing_inserts_at_caret()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "");

        Type(root, "abc");

        text.Get("1.0", "end - 1 chars").Should().Be("abc");
        text.Index("insert").Should().Be("1.3");
    }

    [Fact]
    public void Text_arrows_move_the_insert_mark()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root);

        Key(root, "Right");
        Key(root, "Right");
        text.Index("insert").Should().Be("1.2");

        Key(root, "Left");
        text.Index("insert").Should().Be("1.1");

        Key(root, "Down");
        text.Index("insert").Should().Be("2.1");

        Key(root, "Up");
        text.Index("insert").Should().Be("1.1");
    }

    [Fact]
    public void Text_home_end_and_control_variants()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root);
        text.MarkSet("insert", "2.4");

        Key(root, "Home");
        text.Index("insert").Should().Be("2.0");

        Key(root, "End");
        text.Index("insert").Should().Be("2.11");

        Key(root, "Home", "", EventModifiers.Control);
        text.Index("insert").Should().Be("1.0");

        Key(root, "End", "", EventModifiers.Control);
        text.Index("insert").Should().Be("2.11");
    }

    [Fact]
    public void Text_control_arrows_move_by_words()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root);

        Key(root, "Right", "", EventModifiers.Control);
        text.Index("insert").Should().Be("1.5");

        Key(root, "Right", "", EventModifiers.Control);
        text.Index("insert").Should().Be("1.11");

        Key(root, "Left", "", EventModifiers.Control);
        text.Index("insert").Should().Be("1.6");
    }

    [Fact]
    public void Text_shift_arrows_extend_the_selection_and_typing_replaces_it()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root);

        Key(root, "Right", "", EventModifiers.Shift);
        Key(root, "Right", "", EventModifiers.Shift);
        text.GetTag("sel").Boundaries.Count.Should().Be(2);
        text.GetTag("sel").Boundaries[0].ToString().Should().Be("1.0");
        text.GetTag("sel").Boundaries[1].ToString().Should().Be("1.2");

        Type(root, "X");

        text.Get("1.0", "1.0 lineend").Should().Be("Xllo world");
    }

    [Fact]
    public void Text_backspace_and_delete_edit_around_the_caret()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "abcd");
        text.MarkSet("insert", "1.2");

        Key(root, "BackSpace");
        text.Get("1.0", "1.0 lineend").Should().Be("acd");
        text.Index("insert").Should().Be("1.1");

        Key(root, "Delete");
        text.Get("1.0", "1.0 lineend").Should().Be("ad");
        text.Index("insert").Should().Be("1.1");
    }

    [Fact]
    public void Text_backspace_at_line_start_merges_lines()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root);
        text.MarkSet("insert", "2.0");

        Key(root, "BackSpace");

        text.Get("1.0", "end - 1 chars").Should().Be("hello worldsecond line");
    }

    [Fact]
    public void Text_return_and_tab_insert_their_characters()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "ab");
        text.MarkSet("insert", "1.1");

        Key(root, "Return");
        text.Get("1.0", "end - 1 chars").Should().Be("a\nb");

        Key(root, "Tab");
        text.Get("2.0", "2.0 lineend").Should().Be("\tb");
    }

    [Fact]
    public void Text_clipboard_cut_copy_paste_round_trip()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "hello");
        text.MoveCaret("1.0", false);
        Key(root, "Right", "", EventModifiers.Shift);
        Key(root, "Right", "", EventModifiers.Shift);

        Key(root, "c", "", EventModifiers.Control);
        root.Tree.Clipboard.Get().Should().Be("he");

        Key(root, "x", "", EventModifiers.Control);
        text.Get("1.0", "1.0 lineend").Should().Be("llo");

        text.MoveCaret("1.0 lineend", false);
        Key(root, "v", "", EventModifiers.Control);
        text.Get("1.0", "1.0 lineend").Should().Be("llohe");
    }

    [Fact]
    public void Text_composition_is_separate_state_cleared_by_commit()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "");

        text.SetComposition("にほ");
        text.Composition.Should().Be("にほ");
        text.Get("1.0", "end - 1 chars").Should().Be("");

        text.CommitText("日本");
        text.Composition.Should().Be("");
        text.Get("1.0", "end - 1 chars").Should().Be("日本");
    }

    [Fact]
    public void Focus_changes_attach_and_detach_the_tree_input_sink()
    {
        TkWindow root;
        TextWidget text = FocusedText(out root, "");
        var sink = new RecordingSink();
        root.Tree.InputSink = sink;

        TkWindow entryWindow = root.CreateChild("e2");
        var entry = new EntryWidget(entryWindow);

        root.Tree.SetFocus(null);
        root.Tree.SetFocus(text.Window);
        sink.Attached.Should().BeSameAs(text);

        root.Tree.SetFocus(entryWindow);
        sink.Detaches.Should().BeGreaterThan(0);
        sink.Attached.Should().BeSameAs(entry);
    }

    // ------------------------------------------------------------------
    // Entry widget
    // ------------------------------------------------------------------

    [Fact]
    public void Entry_typing_moves_cursor_and_builds_text()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "");

        Type(root, "abc");

        entry.Text.Should().Be("abc");
        entry.Cursor.Should().Be(3);
    }

    [Fact]
    public void Entry_arrows_home_end_and_word_moves()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root);

        Key(root, "Right");
        entry.Cursor.Should().Be(1);

        Key(root, "End");
        entry.Cursor.Should().Be(11);

        Key(root, "Home");
        entry.Cursor.Should().Be(0);

        Key(root, "Right", "", EventModifiers.Control);
        entry.Cursor.Should().Be(6);

        Key(root, "Left", "", EventModifiers.Control);
        entry.Cursor.Should().Be(0);
    }

    [Fact]
    public void Entry_shift_selection_and_replacement_typing()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root);

        Key(root, "Right", "", EventModifiers.Shift);
        Key(root, "Right", "", EventModifiers.Shift);
        entry.SelectedText.Should().Be("he");

        Type(root, "X");

        entry.Text.Should().Be("Xllo world");
        entry.Cursor.Should().Be(1);
    }

    [Fact]
    public void Entry_backspace_and_delete()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "abcd");
        entry.SetCursor(2);

        Key(root, "BackSpace");
        entry.Text.Should().Be("acd");
        entry.Cursor.Should().Be(1);

        Key(root, "Delete");
        entry.Text.Should().Be("ad");
        entry.Cursor.Should().Be(1);
    }

    [Fact]
    public void Entry_clipboard_cut_copy_paste_round_trip()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "hello");
        entry.MoveCursor(0, false);
        Key(root, "Right", "", EventModifiers.Shift);
        Key(root, "Right", "", EventModifiers.Shift);

        Key(root, "c", "", EventModifiers.Control);
        root.Tree.Clipboard.Get().Should().Be("he");

        Key(root, "x", "", EventModifiers.Control);
        entry.Text.Should().Be("llo");

        Key(root, "End");
        Key(root, "v", "", EventModifiers.Control);
        entry.Text.Should().Be("llohe");
    }

    [Fact]
    public void Entry_show_masking_copies_the_masked_string()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "secret");
        entry.Configure(new Dictionary<string, string> { { "-show", "*" } });
        entry.SelectRange(0, 6);

        entry.CopySelectionToClipboard(false);

        root.Tree.Clipboard.Get().Should().Be("******");
    }

    [Fact]
    public void Entry_disabled_state_ignores_keys()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "fixed");
        entry.Configure(new Dictionary<string, string> { { "-state", "disabled" } });

        Type(root, "abc");
        Key(root, "BackSpace");

        entry.Text.Should().Be("fixed");
    }

    [Fact]
    public void Entry_composition_is_separate_state_cleared_by_commit()
    {
        TkWindow root;
        EntryWidget entry = FocusedEntry(out root, "");

        entry.SetComposition("かん");
        entry.Composition.Should().Be("かん");
        entry.Text.Should().Be("");

        entry.CommitText("漢字");
        entry.Composition.Should().Be("");
        entry.Text.Should().Be("漢字");
    }

    private sealed class RecordingSink : ITextInputSink
    {
        public ITextInputTarget Attached;

        public int Detaches;

        public void Attach(ITextInputTarget target)
        {
            Attached = target;
        }

        public void Detach()
        {
            Detaches++;
        }

        public void UpdateCaret(int x, int y, int height)
        {
        }
    }
}
