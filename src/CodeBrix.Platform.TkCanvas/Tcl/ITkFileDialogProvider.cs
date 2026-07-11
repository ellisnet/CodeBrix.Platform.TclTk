using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The host seam for the ONLY native-escape dialogs in the toolkit: the OS
/// file and folder pickers backing <c>tk_getOpenFile</c>,
/// <c>tk_getSaveFile</c>, and <c>tk_chooseDirectory</c>. Implementations run
/// on the UI thread and deliver the chosen path (or an empty string for
/// cancel) through the completion callback. <c>Hosting.TkHostView</c> wires
/// the CodeBrix.Platform implementation; headless bridges have none and the
/// commands fail cleanly.
/// </summary>
public interface ITkFileDialogProvider
{
    /// <summary>Shows the OS open-file dialog.</summary>
    /// <param name="options">The Tk option words (e.g. -initialdir, -filetypes), name→value.</param>
    /// <param name="completion">Receives the chosen path, or "" when cancelled.</param>
    void GetOpenFile(IReadOnlyDictionary<string, string> options, Action<string> completion);

    /// <summary>Shows the OS save-file dialog.</summary>
    /// <param name="options">The Tk option words (e.g. -defaultextension, -initialdir), name→value.</param>
    /// <param name="completion">Receives the chosen path, or "" when cancelled.</param>
    void GetSaveFile(IReadOnlyDictionary<string, string> options, Action<string> completion);

    /// <summary>Shows the OS folder picker.</summary>
    /// <param name="options">The Tk option words, name→value.</param>
    /// <param name="completion">Receives the chosen directory, or "" when cancelled.</param>
    void ChooseDirectory(IReadOnlyDictionary<string, string> options, Action<string> completion);
}
