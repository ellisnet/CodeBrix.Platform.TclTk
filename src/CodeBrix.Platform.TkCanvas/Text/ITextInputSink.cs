namespace CodeBrix.Platform.TkCanvas.Text;

/// <summary>
/// The seam between text-entry widgets and the host's hidden native input
/// element (the plan's §3.13 model: draw the visible text yourself, back it
/// with an invisible input control that exists only to capture keyboards and
/// IME composition). The host-integration layer implements this over its
/// framework's input element and assigns it to
/// <see cref="Events.WindowTree.InputSink"/>; the <c>text</c> and
/// <c>entry</c> widgets attach themselves on focus. Committed text flows
/// back through <see cref="ITextInputTarget.CommitText"/>, live pre-edit
/// through <see cref="ITextInputTarget.SetComposition"/>.
/// </summary>
public interface ITextInputSink
{
    /// <summary>
    /// Attaches the sink to a widget that gained the input focus: the host
    /// positions its hidden input element over the widget and routes
    /// committed text and composition into it.
    /// </summary>
    /// <param name="target">The focused text-input widget.</param>
    void Attach(ITextInputTarget target);

    /// <summary>Detaches the sink when the widget loses focus.</summary>
    void Detach();

    /// <summary>
    /// Reports the caret rectangle in widget-window coordinates, so the
    /// host can anchor the hidden input element (and thereby the OS IME
    /// candidate window) at the caret.
    /// </summary>
    /// <param name="x">The caret's left edge.</param>
    /// <param name="y">The caret's top edge.</param>
    /// <param name="height">The caret height (the line height).</param>
    void UpdateCaret(int x, int y, int height);
}
