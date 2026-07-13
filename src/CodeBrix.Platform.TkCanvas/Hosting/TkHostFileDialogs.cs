using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Tcl;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CodeBrix.Platform.TkCanvas.Hosting;

/// <summary>
/// The CodeBrix.Platform implementation of the bridge's
/// <see cref="ITkFileDialogProvider"/> seam: <c>tk_getOpenFile</c>,
/// <c>tk_getSaveFile</c>, and <c>tk_chooseDirectory</c> over the platform's
/// OS pickers — the ONLY native-escape dialogs in the toolkit. Runs on the
/// UI thread; completion delivers the picked path, or "" for cancel (Tk's
/// convention).
/// </summary>
public sealed class TkHostFileDialogs : ITkFileDialogProvider
{
    /// <inheritdoc/>
    public async void GetOpenFile(
        IReadOnlyDictionary<string, string> options, Action<string> completion)
    {
        try
        {
            var picker = new FileOpenPicker();
            bool anyFilter = false;
            foreach (string extension in ParseFileTypeExtensions(options))
            {
                picker.FileTypeFilter.Add(extension);
                anyFilter = true;
            }
            if (!anyFilter) { picker.FileTypeFilter.Add("*"); }

            StorageFile file = await picker.PickSingleFileAsync();
            completion(file != null ? file.Path : "");
        }
        catch (Exception)
        {
            completion("");
        }
    }

    /// <inheritdoc/>
    public async void GetSaveFile(
        IReadOnlyDictionary<string, string> options, Action<string> completion)
    {
        try
        {
            var picker = new FileSavePicker();

            string extension;
            if (options.TryGetValue("-defaultextension", out extension) && extension.Length > 0)
            {
                if (!extension.StartsWith(".", StringComparison.Ordinal))
                {
                    extension = "." + extension;
                }
                picker.DefaultFileExtension = extension;
            }

            string initialFile;
            if (options.TryGetValue("-initialfile", out initialFile) && initialFile.Length > 0)
            {
                picker.SuggestedFileName = initialFile;
            }

            bool anyChoice = false;
            foreach (string ext in ParseFileTypeExtensions(options))
            {
                picker.FileTypeChoices.Add(ext + " file", new List<string> { ext });
                anyChoice = true;
            }
            if (!anyChoice)
            {
                string fallback = picker.DefaultFileExtension;
                picker.FileTypeChoices.Add("All files",
                    new List<string> { string.IsNullOrEmpty(fallback) ? "." : fallback });
            }

            StorageFile file = await picker.PickSaveFileAsync();
            completion(file != null ? file.Path : "");
        }
        catch (Exception)
        {
            completion("");
        }
    }

    /// <inheritdoc/>
    public async void ChooseDirectory(
        IReadOnlyDictionary<string, string> options, Action<string> completion)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            StorageFolder folder = await picker.PickSingleFolderAsync();
            completion(folder != null ? folder.Path : "");
        }
        catch (Exception)
        {
            completion("");
        }
    }

    /// <summary>
    /// Extracts the extensions from a Tk <c>-filetypes</c> list —
    /// <c>{{Data files} {.drn}} {{All files} *}</c> — skipping the
    /// match-everything entries.
    /// </summary>
    private static IEnumerable<string> ParseFileTypeExtensions(
        IReadOnlyDictionary<string, string> options)
    {
        string fileTypes;
        if (!options.TryGetValue("-filetypes", out fileTypes) || fileTypes.Length == 0)
        {
            yield break;
        }

        foreach (string entry in TclString.SplitList(fileTypes))
        {
            List<string> parts = TclString.SplitList(entry);
            if (parts.Count < 2) { continue; }
            foreach (string extension in TclString.SplitList(parts[1]))
            {
                if (extension.StartsWith(".", StringComparison.Ordinal))
                {
                    yield return extension;
                }
            }
        }
    }
}
