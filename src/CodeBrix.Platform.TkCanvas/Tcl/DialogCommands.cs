using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Dialogs;
using CodeBrix.Platform.TkCanvas.Menus;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The dialog commands. <c>tk_messageBox</c> and <c>tk_dialog</c> show Skia
/// overlay dialogs (never native, per the plan's §11.1) and block the Tcl
/// thread until a button answers; <c>tk_popup</c> posts a menu; the file
/// pickers are the ONLY native escape, through the host's
/// <see cref="ITkFileDialogProvider"/> seam. A registered
/// <c>ModalAutoResponder</c> answers any of these without UI (headless and
/// scripted runs).
/// </summary>
internal static class DialogCommands
{
    internal static void Register(BridgeContext ctx)
    {
        BridgeRegistrar.Add(ctx, "tk_messageBox", words => MessageBox(ctx, words));
        BridgeRegistrar.Add(ctx, "tk_dialog", words => TkDialog(ctx, words));
        BridgeRegistrar.Add(ctx, "tk_popup", words => Popup(ctx, words));
        BridgeRegistrar.Add(ctx, "tk_getOpenFile", words => FilePicker(ctx, "tk_getOpenFile", words));
        BridgeRegistrar.Add(ctx, "tk_getSaveFile", words => FilePicker(ctx, "tk_getSaveFile", words));
        BridgeRegistrar.Add(ctx, "tk_chooseDirectory", words => FilePicker(ctx, "tk_chooseDirectory", words));
        BridgeRegistrar.Add(ctx, "tk_chooseColor", words => "");
    }

    private static string AutoRespond(BridgeContext ctx, string command, string[] words)
    {
        Func<string, string[], string> responder = ctx.Apartment.ModalAutoResponder;
        return responder != null ? responder(command, words) : null;
    }

    private static string MessageBox(BridgeContext ctx, string[] words)
    {
        string auto = AutoRespond(ctx, "tk_messageBox", words);
        if (auto != null) { return auto; }

        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 1);
        var dialogOptions = new MessageDialogOptions();

        string value;
        if (options.TryGetValue("-type", out value)) { dialogOptions.Type = value; }
        if (options.TryGetValue("-message", out value)) { dialogOptions.Message = value; }
        if (options.TryGetValue("-detail", out value)) { dialogOptions.Detail = value; }
        if (options.TryGetValue("-title", out value)) { dialogOptions.Title = value; }
        if (options.TryGetValue("-icon", out value)) { dialogOptions.Icon = value; }
        if (options.TryGetValue("-default", out value)) { dialogOptions.Default = value; }

        return ctx.Apartment.WaitForModal(complete =>
            MessageDialog.Show(ctx.Tree, dialogOptions, complete));
    }

    private static string TkDialog(BridgeContext ctx, string[] words)
    {
        // tk_dialog window title text bitmap default string ?string ...?
        if (words.Length < 7)
        {
            throw BridgeRegistrar.WrongArgs(
                "tk_dialog window title text bitmap default string ?string ...?");
        }

        string auto = AutoRespond(ctx, "tk_dialog", words);
        if (auto != null) { return auto; }

        var buttons = new List<string>();
        for (int i = 6; i < words.Length; i++) { buttons.Add(words[i]); }

        var dialogOptions = new MessageDialogOptions
        {
            Title = words[2],
            Message = words[3],
            Icon = words[4].Length > 0 ? words[4] : "question",
            CustomButtons = buttons,
        };

        string pressed = ctx.Apartment.WaitForModal(complete =>
            MessageDialog.Show(ctx.Tree, dialogOptions, complete));

        int index = buttons.IndexOf(pressed);
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private static string Popup(BridgeContext ctx, string[] words)
    {
        if (words.Length < 4)
        {
            throw BridgeRegistrar.WrongArgs("tk_popup menu x y ?entry?");
        }

        string menuPath = words[1];
        int x = int.Parse(words[2], CultureInfo.InvariantCulture);
        int y = int.Parse(words[3], CultureInfo.InvariantCulture);

        return ctx.Ui(() =>
        {
            MenuWidget menu;
            if (!ctx.MenusByPath.TryGetValue(menuPath, out menu))
            {
                throw new TkTclError("bad window path name \"" + menuPath + "\"");
            }
            ctx.Tree.Menus.Popup(menu, x, y);
            return "";
        });
    }

    private static string FilePicker(BridgeContext ctx, string command, string[] words)
    {
        string auto = AutoRespond(ctx, command, words);
        if (auto != null) { return auto; }

        Dictionary<string, string> options = BridgeRegistrar.ParseOptionPairs(words, 1);

        ITkFileDialogProvider dialogs = ctx.FileDialogs;
        if (dialogs == null)
        {
            // No host and no auto-responder: behave as a cancelled picker
            // (Tk returns "" on cancel) rather than failing the script.
            return "";
        }

        return ctx.Apartment.WaitForModal(complete =>
        {
            switch (command)
            {
                case "tk_getOpenFile":
                    dialogs.GetOpenFile(options, complete);
                    break;
                case "tk_getSaveFile":
                    dialogs.GetSaveFile(options, complete);
                    break;
                default:
                    dialogs.ChooseDirectory(options, complete);
                    break;
            }
        }) ?? "";
    }
}
