using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Dialogs;

/// <summary>
/// The options of a <c>tk_messageBox</c>: which button set to show, the
/// message and title text, the icon hint, and which button is the default.
/// </summary>
public sealed class MessageDialogOptions
{
    /// <summary>The button set: <c>ok okcancel yesno yesnocancel retrycancel abortretryignore</c>.</summary>
    public string Type { get; set; } = "ok";

    /// <summary>The message body.</summary>
    public string Message { get; set; } = "";

    /// <summary>The detail text drawn under the message, or empty.</summary>
    public string Detail { get; set; } = "";

    /// <summary>The window title.</summary>
    public string Title { get; set; } = "";

    /// <summary>The icon hint: <c>info warning error question</c>.</summary>
    public string Icon { get; set; } = "info";

    /// <summary>The result name of the button that is pre-focused, or empty for the first.</summary>
    public string Default { get; set; } = "";
}

/// <summary>
/// Builds the classic Tk message/alert dialogs (<c>tk_messageBox</c>,
/// <c>tk_dialog</c>) as Skia OVERLAY toplevels drawn by the toolkit — never
/// native — for a consistent look (the plan's §3.12/§11.1: only file/folder
/// pickers escape to the OS). A dialog is a modal overlay (title chrome + a
/// grab) whose content is composed from B.6 widgets: an icon glyph, the
/// message label(s), and a row of buttons. Because there is no nested Tcl
/// event loop here, the result is delivered through a callback (the Phase-C
/// command bridge wraps this into the synchronous <c>tk_messageBox</c>
/// return); clicking a button fires the callback and tears the dialog down.
/// </summary>
public static class MessageDialog
{
    private static readonly Dictionary<string, string[]> ButtonSets =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        { "ok", new[] { "ok" } },
        { "okcancel", new[] { "ok", "cancel" } },
        { "yesno", new[] { "yes", "no" } },
        { "yesnocancel", new[] { "yes", "no", "cancel" } },
        { "retrycancel", new[] { "retry", "cancel" } },
        { "abortretryignore", new[] { "abort", "retry", "ignore" } },
    };

    /// <summary>
    /// Shows a message dialog and invokes <paramref name="onResult"/> with the
    /// clicked button's result name (<c>ok</c>, <c>cancel</c>, <c>yes</c>, …)
    /// when the user dismisses it. Returns the dialog's toplevel window.
    /// </summary>
    /// <param name="tree">The window tree to host the dialog in.</param>
    /// <param name="options">The dialog options.</param>
    /// <param name="onResult">Called with the result name when a button is clicked.</param>
    /// <returns>The dialog's overlay toplevel window.</returns>
    public static TkWindow Show(WindowTree tree, MessageDialogOptions options, Action<string> onResult)
    {
        if (tree == null) { throw new ArgumentNullException(nameof(tree)); }
        if (options == null) { throw new ArgumentNullException(nameof(options)); }

        WindowManager wm = tree.WindowManager;
        TkWindow dialog = wm.CreateToplevel("__dialog");
        wm.SetTitle(dialog, options.Title);

        string[] buttons;
        if (!ButtonSets.TryGetValue(options.Type, out buttons)) { buttons = ButtonSets["ok"]; }

        // Top row: icon glyph + message.
        TkWindow top = dialog.CreateChild("top");
        var topFrame = new FrameWidget(top);
        topFrame.Configure(Empty());
        PackLayout.Configure(top, new PackOptions { Side = Side.Top, Fill = Fill.X, PadTop = 12, PadBottom = 6, PadLeft = 16, PadRight = 16 });

        TkWindow iconWin = top.CreateChild("icon");
        var icon = new LabelWidget(iconWin);
        icon.Configure(new Dictionary<string, string>
        {
            { "-text", IconGlyph(options.Icon) },
            { "-font", "{DejaVu Sans} 22 bold" },
            { "-foreground", IconColor(options.Icon) },
        });
        PackLayout.Configure(iconWin, new PackOptions { Side = Side.Left, PadRight = 14 });

        TkWindow msgWin = top.CreateChild("msg");
        var msg = new LabelWidget(msgWin);
        var msgOpts = new Dictionary<string, string>
        {
            { "-text", Compose(options.Message, options.Detail) },
            { "-justify", "left" },
            { "-anchor", "w" },
        };
        msg.Configure(msgOpts);
        PackLayout.Configure(msgWin, new PackOptions { Side = Side.Left });

        // Bottom row: buttons (right-aligned, default first from the right in Tk order).
        TkWindow buttonRow = dialog.CreateChild("buttons");
        var buttonFrame = new FrameWidget(buttonRow);
        buttonFrame.Configure(Empty());
        PackLayout.Configure(buttonRow, new PackOptions { Side = Side.Bottom, Fill = Fill.X, PadTop = 6, PadBottom = 10, PadLeft = 12, PadRight = 12 });

        for (int i = 0; i < buttons.Length; i++)
        {
            string result = buttons[i];
            TkWindow bw = buttonRow.CreateChild("b" + i);
            var button = new ButtonWidget(bw);
            button.Configure(new Dictionary<string, string>
            {
                { "-text", Capitalize(result) },
                { "-padx", "10" },
                { "-width", "6" },
            });
            button.Invoked += () =>
            {
                if (onResult != null) { onResult(result); }
                wm.ReleaseGrab();
                dialog.Destroy();
                tree.Scheduler.ScheduleRepaint();
            };
            PackLayout.Configure(bw, new PackOptions { Side = Side.Right, PadLeft = 6 });
        }

        // Size from content, then centre in the root and grab (modal).
        TkLayout.Update(tree.Root);
        int w = dialog.RequestedWidth;
        int h = dialog.RequestedHeight;
        int x = (tree.Root.Width - w) / 2;
        int y = (tree.Root.Height - h) / 3;
        wm.SetGeometry(dialog, w, h, x < 0 ? 0 : x, y < 0 ? 0 : y);
        wm.Grab(dialog);
        TkLayout.Update(tree.Root);
        return dialog;
    }

    private static Dictionary<string, string> Empty()
    {
        return new Dictionary<string, string>();
    }

    private static string Compose(string message, string detail)
    {
        return (detail.Length > 0) ? message + "\n\n" + detail : message;
    }

    private static string IconGlyph(string icon)
    {
        switch (icon)
        {
            case "warning": return "⚠";
            case "error": return "✕";
            case "question": return "?";
            default: return "ⓘ";
        }
    }

    private static string IconColor(string icon)
    {
        switch (icon)
        {
            case "warning": return "#c08000";
            case "error": return "#c00000";
            case "question": return "#204a87";
            default: return "#204a87";
        }
    }

    private static string Capitalize(string result)
    {
        if (result.Length == 0) { return result; }
        if (result == "ok") { return "OK"; }
        return char.ToUpperInvariant(result[0]) + result.Substring(1);
    }
}
