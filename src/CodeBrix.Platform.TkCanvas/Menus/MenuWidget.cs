using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Menus;

/// <summary>
/// The classic Tk <c>menu</c> widget drawn on Skia (the plan's §3.11): a list
/// of <see cref="MenuEntry"/> items — command, cascade, separator, and (for
/// generality) checkbutton/radiobutton — laid out vertically as a popup or
/// horizontally as a menubar (<c>-type menubar</c>). It measures its natural
/// size through the font seam, paints each entry (mnemonic underline,
/// right-aligned accelerator, cascade arrow, selection marker, the active
/// highlight), maps a point to an entry, and invokes an entry's command. The
/// posting/cascade/dismiss choreography lives in <see cref="MenuManager"/>.
/// </summary>
public sealed class MenuWidget : WidgetBase
{
    private const int VPad = 2;
    private const int HPad = 6;
    private const int SeparatorHeight = 6;
    private const int ArrowColumn = 14;
    private const int AccelGap = 20;

    private readonly List<MenuEntry> _entries = new List<MenuEntry>();
    private int _active = -1;

    /// <summary>Creates a menu on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public MenuWidget(TkWindow window)
        : base(window, "Menu")
    {
        Measure();
    }

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "Menu"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return IsMenubar ? 0 : 1; }
    }

    private protected override string DefaultRelief
    {
        get { return IsMenubar ? "flat" : "raised"; }
    }

    /// <summary>Whether this menu is a horizontal menubar (<c>-type menubar</c>).</summary>
    public bool IsMenubar
    {
        get { return Options.Get("-type", "normal") == "menubar"; }
    }

    /// <summary>The menu's entries in order.</summary>
    public IReadOnlyList<MenuEntry> Entries
    {
        get { return _entries; }
    }

    /// <summary>The active (highlighted) entry index, or -1 for none.</summary>
    public int ActiveIndex
    {
        get { return _active; }
        set { SetActive(value); }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkMenuFont");
        }
    }

    private int EntryHeight
    {
        get { return Fonts.Metrics(Font).LineSpace + 2 * VPad; }
    }

    /// <summary>Adds a command entry — <c>add command</c>.</summary>
    /// <param name="label">The entry label.</param>
    /// <param name="command">The action to fire when invoked.</param>
    /// <param name="accelerator">The accelerator hint, or null.</param>
    /// <param name="underline">The mnemonic index, or -1.</param>
    /// <returns>The created entry.</returns>
    public MenuEntry AddCommand(string label, Action command = null, string accelerator = null, int underline = -1)
    {
        var entry = new MenuEntry
        {
            Type = MenuEntryType.Command,
            Label = label ?? "",
            Command = command,
            Accelerator = accelerator ?? "",
            Underline = underline,
        };
        return Append(entry);
    }

    /// <summary>Adds a cascade entry that opens <paramref name="submenu"/> — <c>add cascade</c>.</summary>
    /// <param name="label">The entry label.</param>
    /// <param name="submenu">The submenu to open.</param>
    /// <param name="underline">The mnemonic index, or -1.</param>
    /// <returns>The created entry.</returns>
    public MenuEntry AddCascade(string label, MenuWidget submenu, int underline = -1)
    {
        var entry = new MenuEntry
        {
            Type = MenuEntryType.Cascade,
            Label = label ?? "",
            Submenu = submenu,
            Underline = underline,
        };
        return Append(entry);
    }

    /// <summary>Adds a separator — <c>add separator</c>.</summary>
    /// <returns>The created entry.</returns>
    public MenuEntry AddSeparator()
    {
        return Append(new MenuEntry { Type = MenuEntryType.Separator });
    }

    /// <summary>Adds a checkbutton entry — <c>add checkbutton</c>.</summary>
    /// <param name="label">The entry label.</param>
    /// <param name="command">The action to fire when toggled.</param>
    /// <returns>The created entry.</returns>
    public MenuEntry AddCheckbutton(string label, Action command = null)
    {
        return Append(new MenuEntry { Type = MenuEntryType.Checkbutton, Label = label ?? "", Command = command });
    }

    /// <summary>Adds a radiobutton entry — <c>add radiobutton</c>.</summary>
    /// <param name="label">The entry label.</param>
    /// <param name="command">The action to fire when selected.</param>
    /// <returns>The created entry.</returns>
    public MenuEntry AddRadiobutton(string label, Action command = null)
    {
        return Append(new MenuEntry { Type = MenuEntryType.Radiobutton, Label = label ?? "", Command = command });
    }

    private MenuEntry Append(MenuEntry entry)
    {
        _entries.Add(entry);
        Measure();
        return entry;
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int inset = Inset;

        if (IsMenubar)
        {
            int width = inset;
            int h = EntryHeight;
            foreach (MenuEntry entry in _entries)
            {
                width += Fonts.Measure(font, entry.Label) + 2 * HPad;
            }
            Window.SetRequestedSize(width + inset, h + 2 * inset);
            Window.SetInternalBorder(inset);
            return;
        }

        int labelWidth = 0;
        int accelWidth = 0;
        int height = 0;
        foreach (MenuEntry entry in _entries)
        {
            if (entry.Type == MenuEntryType.Separator) { height += SeparatorHeight; continue; }
            int lw = Fonts.Measure(font, entry.Label);
            if (lw > labelWidth) { labelWidth = lw; }
            int aw = Fonts.Measure(font, entry.Accelerator);
            if (aw > accelWidth) { accelWidth = aw; }
            height += EntryHeight;
        }
        int contentWidth = ArrowColumn + labelWidth + (accelWidth > 0 ? AccelGap + accelWidth : 0) + ArrowColumn;
        Window.SetRequestedSize(contentWidth + 2 * inset + 2 * HPad, height + 2 * inset);
        Window.SetInternalBorder(inset);
    }

    /// <summary>The entry index at a window point, or -1 (menubar uses x, popup uses y).</summary>
    /// <param name="x">Window x.</param>
    /// <param name="y">Window y.</param>
    /// <returns>The entry index, or -1.</returns>
    public int EntryIndexAt(int x, int y)
    {
        int inset = Inset;
        if (IsMenubar)
        {
            TkFont font = Font;
            int cx = inset;
            for (int i = 0; i < _entries.Count; i++)
            {
                int w = Fonts.Measure(font, _entries[i].Label) + 2 * HPad;
                if (x >= cx && x < cx + w) { return i; }
                cx += w;
            }
            return -1;
        }

        int cy = inset;
        for (int i = 0; i < _entries.Count; i++)
        {
            int h = (_entries[i].Type == MenuEntryType.Separator) ? SeparatorHeight : EntryHeight;
            if (y >= cy && y < cy + h)
            {
                return (_entries[i].Type == MenuEntryType.Separator) ? -1 : i;
            }
            cy += h;
        }
        return -1;
    }

    /// <summary>The rectangle of an entry in window coordinates (used to place submenus).</summary>
    /// <param name="index">The entry index.</param>
    /// <returns>The rectangle, or empty.</returns>
    public SKRectI EntryRect(int index)
    {
        if (index < 0 || index >= _entries.Count) { return SKRectI.Empty; }
        int inset = Inset;
        if (IsMenubar)
        {
            TkFont font = Font;
            int cx = inset;
            for (int i = 0; i < index; i++) { cx += Fonts.Measure(font, _entries[i].Label) + 2 * HPad; }
            int w = Fonts.Measure(font, _entries[index].Label) + 2 * HPad;
            return new SKRectI(cx, inset, cx + w, inset + EntryHeight);
        }

        int cy = inset;
        for (int i = 0; i < index; i++)
        {
            cy += (_entries[i].Type == MenuEntryType.Separator) ? SeparatorHeight : EntryHeight;
        }
        int eh = (_entries[index].Type == MenuEntryType.Separator) ? SeparatorHeight : EntryHeight;
        return new SKRectI(inset, cy, Window.Width - inset, cy + eh);
    }

    private void SetActive(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].Type == MenuEntryType.Separator)
        {
            index = -1;
        }
        if (index == _active) { return; }
        _active = index;
        // Tk generates <<MenuSelect>> when the active entry changes.
        if (!Window.IsDestroyed)
        {
            Window.Tree.DispatchEvent(Window, new TkEvent
            {
                Type = TkEventType.Virtual,
                VirtualName = "MenuSelect",
                KeySym = string.Empty,
                Character = string.Empty,
            });
            Window.Tree.Scheduler.ScheduleRepaint();
        }
    }

    /// <summary>Invokes an entry — fires its command (command/check/radio), toggling as needed.</summary>
    /// <param name="index">The entry index.</param>
    public void Invoke(int index)
    {
        if (index < 0 || index >= _entries.Count) { return; }
        MenuEntry entry = _entries[index];
        if (entry.Disabled || entry.Type == MenuEntryType.Separator || entry.Type == MenuEntryType.Cascade)
        {
            return;
        }
        if (entry.Type == MenuEntryType.Checkbutton) { entry.Selected = !entry.Selected; }
        else if (entry.Type == MenuEntryType.Radiobutton)
        {
            foreach (MenuEntry other in _entries)
            {
                if (other.Type == MenuEntryType.Radiobutton) { other.Selected = false; }
            }
            entry.Selected = true;
        }
        Action command = entry.Command;
        if (command != null) { command(); }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        SKColor background = BackgroundColor;
        PaintBackgroundAndBorder(canvas, background);

        TkFont font = Font;
        SKColor activeBg;
        if (!TkColor.TryParse(Options.Get("-activebackground", "#4a6984"), out activeBg))
        {
            activeBg = new SKColor(0x4A, 0x69, 0x84);
        }

        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            FontMetrics metrics = Fonts.Metrics(font);
            for (int i = 0; i < _entries.Count; i++)
            {
                MenuEntry entry = _entries[i];
                SKRectI r = EntryRect(i);
                if (entry.Type == MenuEntryType.Separator)
                {
                    float sy = r.Top + r.Height / 2f;
                    paint.Color = ReliefPainter.DarkShadow(background);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1;
                    paint.IsAntialias = false;
                    canvas.DrawLine(r.Left + 2, sy, r.Right - 2, sy, paint);
                    continue;
                }

                bool active = (i == _active) && !entry.Disabled;
                if (active)
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = activeBg;
                    paint.IsAntialias = false;
                    canvas.DrawRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom), paint);
                }

                SKColor fg;
                string fgSpec = entry.Disabled ? "#a3a3a3"
                        : active ? Options.Get("-activeforeground", "white")
                        : Options.Get("-foreground", "black");
                if (!TkColor.TryParse(fgSpec, out fg)) { fg = SKColors.Black; }
                paint.Color = fg;
                paint.Style = SKPaintStyle.Fill;
                paint.IsAntialias = true;

                float textLeft = IsMenubar ? r.Left + HPad : r.Left + ArrowColumn;
                float baseline = r.Top + VPad + metrics.Ascent;

                // Selection marker for check/radio.
                if ((entry.Type == MenuEntryType.Checkbutton || entry.Type == MenuEntryType.Radiobutton)
                        && entry.Selected)
                {
                    canvas.DrawText(entry.Type == MenuEntryType.Radiobutton ? "•" : "✓",
                            r.Left + 3, baseline, SKTextAlign.Left, skFont, paint);
                }

                canvas.DrawText(entry.Label, textLeft, baseline, SKTextAlign.Left, skFont, paint);

                // Mnemonic underline.
                if (entry.Underline >= 0 && entry.Underline < entry.Label.Length)
                {
                    float ux = textLeft + Fonts.Measure(font, entry.Label.Substring(0, entry.Underline));
                    float uw = Fonts.Measure(font, entry.Label.Substring(entry.Underline, 1));
                    float uy = baseline + 1;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1;
                    canvas.DrawLine(ux, uy, ux + uw, uy, paint);
                    paint.Style = SKPaintStyle.Fill;
                }

                if (!IsMenubar && entry.Accelerator.Length > 0)
                {
                    float ax = r.Right - ArrowColumn - Fonts.Measure(font, entry.Accelerator);
                    canvas.DrawText(entry.Accelerator, ax, baseline, SKTextAlign.Left, skFont, paint);
                }

                if (!IsMenubar && entry.Type == MenuEntryType.Cascade)
                {
                    canvas.DrawText("▶", r.Right - ArrowColumn + 2, baseline, SKTextAlign.Left, skFont, paint);
                }
            }
        }
    }
}
