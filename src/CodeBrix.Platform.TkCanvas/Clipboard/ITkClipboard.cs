namespace CodeBrix.Platform.TkCanvas.Clipboard;

/// <summary>
/// The seam between the toolkit's <c>clipboard</c> command and the host
/// platform's real clipboard. TkCanvas itself has no host-framework
/// dependency; the host application (or the host-integration layer) supplies
/// an implementation bridging to the OS clipboard. Headless scenarios and
/// tests run without one — <see cref="ClipboardManager"/> then keeps the
/// content in-process.
/// </summary>
public interface ITkClipboard
{
    /// <summary>Publishes text to the host clipboard.</summary>
    /// <param name="text">The text to publish.</param>
    void SetText(string text);

    /// <summary>
    /// Reads text from the host clipboard.
    /// </summary>
    /// <returns>The clipboard text, or null when the clipboard holds none.</returns>
    string GetText();
}
