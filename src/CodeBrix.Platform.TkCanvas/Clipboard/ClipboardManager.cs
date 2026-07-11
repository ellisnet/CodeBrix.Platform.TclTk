using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Platform.TkCanvas.Clipboard;

/// <summary>
/// The toolkit-wide Tk <c>clipboard</c> command model: <c>clear</c> starts a
/// fresh clipboard claim, <c>append</c> accumulates text onto it (publishing
/// through the <see cref="ITkClipboard"/> host seam when one is attached),
/// and <c>get</c> reads it back — from the host clipboard when attached (so
/// text copied by other applications is visible), else from the in-process
/// buffer. Reached lazily via
/// <see cref="Events.WindowTree.Clipboard"/>.
/// </summary>
public sealed class ClipboardManager
{
    private readonly StringBuilder _buffer = new StringBuilder();
    private bool _owned;

    /// <summary>
    /// The host clipboard bridge, or null to stay in-process (headless and
    /// tests). The host-integration layer attaches this.
    /// </summary>
    public ITkClipboard Host { get; set; }

    /// <summary>Claims the clipboard and empties it — <c>clipboard clear</c>.</summary>
    public void Clear()
    {
        _buffer.Clear();
        _owned = true;
    }

    /// <summary>
    /// Appends text to the clipboard — <c>clipboard append</c> — and
    /// publishes the accumulated content through the host seam.
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void Append(string text)
    {
        _buffer.Append(text ?? "");
        _owned = true;
        ITkClipboard host = Host;
        if (host != null)
        {
            host.SetText(_buffer.ToString());
        }
    }

    /// <summary>
    /// Reads the clipboard — <c>clipboard get</c>. Prefers the host
    /// clipboard (other applications' copies included); falls back to the
    /// in-process buffer.
    /// </summary>
    /// <returns>The clipboard text.</returns>
    /// <exception cref="InvalidOperationException">When no content exists (Tk's error).</exception>
    public string Get()
    {
        ITkClipboard host = Host;
        if (host != null)
        {
            string text = host.GetText();
            if (text != null) { return text; }
        }
        else if (_owned)
        {
            return _buffer.ToString();
        }
        throw new InvalidOperationException(
                "CLIPBOARD selection doesn't exist or form \"STRING\" not defined");
    }

    /// <summary>
    /// Executes a <c>clipboard</c> command with the Tcl argument shapes
    /// verbatim — <c>clear</c>, <c>append</c> (with <c>-type</c>/
    /// <c>-format</c>/<c>-displayof</c> accepted and <c>--</c> honored), and
    /// <c>get</c> — returning what Tk returns.
    /// </summary>
    /// <param name="words">The subcommand and its arguments.</param>
    /// <returns>The Tcl result string.</returns>
    public string Execute(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            throw new InvalidOperationException(
                    "wrong # args: should be \"clipboard option ?arg ...?\"");
        }

        switch (words[0])
        {
            case "clear":
                Clear();
                return "";
            case "append":
            {
                // Options come first; "--" ends them; the LAST word is the data.
                int i = 1;
                while (i < words.Count)
                {
                    if (words[i] == "--") { i++; break; }
                    if ((words[i] == "-type" || words[i] == "-format" || words[i] == "-displayof")
                            && i + 1 < words.Count)
                    {
                        i += 2;
                        continue;
                    }
                    break;
                }
                if (i >= words.Count)
                {
                    throw new InvalidOperationException(
                            "wrong # args: should be \"clipboard append ?-option value ...? data\"");
                }
                Append(words[i]);
                return "";
            }
            case "get":
                return Get();
            default:
                throw new InvalidOperationException("bad option \"" + words[0]
                        + "\": must be append, clear, or get");
        }
    }
}
