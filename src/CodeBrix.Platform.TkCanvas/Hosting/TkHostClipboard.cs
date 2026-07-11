using System;

using Windows.ApplicationModel.DataTransfer;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// The <see cref="Clipboard.ITkClipboard"/> seam implemented over the
/// CodeBrix.Platform clipboard, so the Tk <c>clipboard</c> command exchanges
/// text with the rest of the desktop. Reads bridge the platform's async
/// clipboard API synchronously and simply report no content on failure —
/// the Tk command then raises its usual empty-clipboard error.
/// </summary>
public sealed class TkHostClipboard : Clipboard.ITkClipboard
{
    /// <inheritdoc/>
    public void SetText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text ?? "");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // A clipboard that refuses the write (headless session, no
            // display ownership) must not break the Tcl caller.
        }
    }

    /// <inheritdoc/>
    public string GetText()
    {
        try
        {
            DataPackageView view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view == null || !view.Contains(StandardDataFormats.Text)) { return null; }
            return view.GetTextAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
