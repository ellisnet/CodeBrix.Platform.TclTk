using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Text;

/// <summary>
/// A widget that accepts text input through the hidden-input-element seam —
/// implemented by the <c>text</c> widget (<see cref="TextWidget"/>) and the
/// <c>entry</c> widget. The host's <see cref="ITextInputSink"/> routes
/// committed text and live IME composition (pre-edit) here; the widget's own
/// buffer stays the source of truth (the plan's §3.13 model).
/// </summary>
public interface ITextInputTarget
{
    /// <summary>The window the widget owns (for focus/geometry bookkeeping).</summary>
    TkWindow Window { get; }

    /// <summary>
    /// Inserts committed text at the caret, replacing any selection —
    /// keystrokes and completed IME compositions arrive here.
    /// </summary>
    /// <param name="text">The committed text.</param>
    void CommitText(string text);

    /// <summary>
    /// Shows (or, with null/empty, clears) the in-progress IME composition
    /// string, drawn by the widget at its caret as separate pre-edit state —
    /// never part of the text buffer.
    /// </summary>
    /// <param name="preedit">The composition text, or null/empty when composition ends.</param>
    void SetComposition(string preedit);
}
