using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Rendering;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The Tk <c>ttk::combobox</c> widget on Skia: a value field (an entry-style
/// sunken box) with a drop-down arrow that opens a <see cref="ListboxWidget"/>
/// of <c>-values</c> in an override-redirect overlay. Choosing a value sets
/// the field and fires <c>&lt;&lt;ComboboxSelected&gt;&gt;</c>; the drop-down
/// is a modal overlay (grabbed) that closes on selection or on a press
/// outside it. <c>-state readonly</c> shows the value without a text caret
/// (DRAKON's usage); <c>normal</c> also allows the field to carry typed text
/// once the input sink is wired.
/// </summary>
public sealed class ComboboxWidget : WidgetBase
{
    private readonly List<string> _values = new List<string>();
    private string _value = "";
    private TkWindow _dropdown;
    private ListboxWidget _dropdownList;

    /// <summary>Creates a combobox on <paramref name="window"/>.</summary>
    /// <param name="window">The window the widget owns.</param>
    public ComboboxWidget(TkWindow window)
        : base(window, "TCombobox")
    {
        window.Focusable = true;
        EnsureClassBindings(window.Tree.Bindings);
        Measure();
    }

    /// <summary>Raised when a value is chosen from the drop-down (<c>&lt;&lt;ComboboxSelected&gt;&gt;</c>).</summary>
    public event Action Selected;

    /// <inheritdoc/>
    public override string ClassName
    {
        get { return "TCombobox"; }
    }

    private protected override int DefaultBorderWidth
    {
        get { return 2; }
    }

    private protected override string DefaultRelief
    {
        get { return "sunken"; }
    }

    private protected override int DefaultHighlightThickness
    {
        get { return 1; }
    }

    private protected override string DefaultBackground
    {
        get { return "white"; }
    }

    private const int ArrowWidth = 18;

    /// <summary>The current value shown in the field.</summary>
    public string Value
    {
        get { return _value; }
    }

    /// <summary>Whether the drop-down list is open.</summary>
    public bool IsDropDownOpen
    {
        get { return _dropdown != null; }
    }

    private TkFont Font
    {
        get
        {
            string spec = Options.Get("-font", "");
            return (spec.Length > 0) ? Fonts.Parse(spec) : Fonts.GetNamed("TkTextFont");
        }
    }

    private protected override void OnConfigured()
    {
        string values = Options.Get("-values", "");
        if (values.Length > 0)
        {
            _values.Clear();
            foreach (string v in TclString.SplitList(values)) { _values.Add(v); }
        }
    }

    /// <summary>Replaces the value list — <c>configure -values {...}</c>.</summary>
    /// <param name="values">The new value list.</param>
    public void SetValues(params string[] values)
    {
        _values.Clear();
        _values.AddRange(values);
        Repaint();
    }

    /// <summary>The value list.</summary>
    public IReadOnlyList<string> Values
    {
        get { return _values; }
    }

    /// <summary>Sets the field value — <c>set value</c> (no <c>&lt;&lt;ComboboxSelected&gt;&gt;</c>).</summary>
    /// <param name="value">The new value.</param>
    public void SetValue(string value)
    {
        _value = value ?? "";
        Repaint();
    }

    /// <inheritdoc/>
    public override void Measure()
    {
        TkFont font = Font;
        int chars = Options.GetInt("-width", 20);
        int charWidth = Fonts.Measure(font, "0");
        if (charWidth < 1) { charWidth = 1; }
        int inset = Inset;
        int reqW = chars * charWidth + ArrowWidth + 2 * inset + 4;
        int reqH = Fonts.Metrics(font).LineSpace + 2 * inset + 4;
        Window.SetRequestedSize(reqW, reqH);
        Window.SetInternalBorder(inset);
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        PaintBackgroundAndBorder(canvas, BackgroundColor);

        TkFont font = Font;
        FontMetrics metrics = Fonts.Metrics(font);
        int inset = Inset;
        float baseline = inset + (Window.Height - 2 * inset - metrics.LineSpace) / 2f + metrics.Ascent;

        using (SKFont skFont = Fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            SKColor fg;
            if (!TkColor.TryParse(Options.Get("-foreground", "black"), out fg)) { fg = SKColors.Black; }
            paint.Color = fg;
            paint.IsAntialias = true;
            canvas.DrawText(_value, inset + 2, baseline, SKTextAlign.Left, skFont, paint);
        }

        // Drop-down arrow button on the right edge.
        var arrow = new SKRect(Window.Width - inset - ArrowWidth, inset,
                Window.Width - inset, Window.Height - inset);
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(0xD9, 0xD9, 0xD9);
            canvas.DrawRect(arrow, paint);
        }
        ReliefPainter.DrawBorder(canvas, arrow, 2, Relief.Raised, new SKColor(0xD9, 0xD9, 0xD9));

        var tri = new SKPathBuilder();
        float cx = arrow.MidX;
        float cy = arrow.MidY;
        tri.MoveTo(cx - 4, cy - 2);
        tri.LineTo(cx + 4, cy - 2);
        tri.LineTo(cx, cy + 3);
        tri.Close();
        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColors.Black;
            using (SKPath path = tri.Detach()) { canvas.DrawPath(path, paint); }
        }
    }

    /// <summary>Toggles the drop-down open/closed.</summary>
    public void ToggleDropDown()
    {
        if (_dropdown != null) { CloseDropDown(); } else { OpenDropDown(); }
    }

    /// <summary>Opens the drop-down list below the field.</summary>
    public void OpenDropDown()
    {
        if (_dropdown != null || Options.Get("-state", "normal") == "disabled") { return; }

        WindowManager wm = Window.Tree.WindowManager;
        _dropdown = wm.CreateToplevel(Window.Name + "-popdown");
        wm.SetOverrideRedirect(_dropdown, true);
        _dropdownList = new ListboxWidget(_dropdown);
        var lbOpts = new Dictionary<string, string>
        {
            { "-height", Math.Min(_values.Count == 0 ? 1 : _values.Count, 10).ToString() },
            { "-selectmode", "browse" },
        };
        _dropdownList.Configure(lbOpts);
        foreach (string v in _values) { _dropdownList.Insert(-1, v); }

        // Instance bindings on the drop-down listbox: a press outside closes it,
        // a release inside commits the row under the pointer.
        string path = _dropdown.PathName;
        Window.Tree.Bindings.Bind(path, "<ButtonPress-1>", DropDownPress);
        Window.Tree.Bindings.Bind(path, "<ButtonRelease-1>", DropDownRelease);

        int gx = Window.X;
        int gy = Window.Y + Window.Height;
        TkLayout.Update(Window.Tree.Root);
        wm.SetGeometry(_dropdown, Window.Width, _dropdownList.Window.RequestedHeight, gx, gy);
        wm.Deiconify(_dropdown);
        wm.Grab(_dropdown);

        // Pre-select the current value.
        for (int i = 0; i < _values.Count; i++)
        {
            if (_values[i] == _value) { _dropdownList.SelectionSet(i); _dropdownList.See(i); break; }
        }
        Repaint();
    }

    /// <summary>Closes the drop-down list without committing.</summary>
    public void CloseDropDown()
    {
        if (_dropdown == null) { return; }
        WindowManager wm = Window.Tree.WindowManager;
        wm.ReleaseGrab();
        TkWindow dd = _dropdown;
        _dropdown = null;
        _dropdownList = null;
        dd.Destroy();
        Repaint();
    }

    private void Commit(int index)
    {
        if (index >= 0 && index < _values.Count)
        {
            _value = _values[index];
            CloseDropDown();
            Action handler = Selected;
            if (handler != null) { handler(); }
            if (!Window.IsDestroyed)
            {
                Window.Tree.DispatchEvent(Window, new TkEvent
                {
                    Type = TkEventType.Virtual,
                    VirtualName = "ComboboxSelected",
                    KeySym = string.Empty,
                    Character = string.Empty,
                });
            }
        }
        else
        {
            CloseDropDown();
        }
    }

    private bool PointInDropDown(TkEvent e)
    {
        TkWindow w = _dropdown;
        return w != null && e.RootX >= w.X && e.RootX < w.X + w.Width
                && e.RootY >= w.Y && e.RootY < w.Y + w.Height;
    }

    private void Repaint()
    {
        if (!Window.IsDestroyed) { Window.Tree.Scheduler.ScheduleRepaint(); }
    }

    private static void EnsureClassBindings(BindingTable bindings)
    {
        bindings.Bind("TCombobox", "<ButtonPress-1>", OnFieldPress);
    }

    private static ComboboxWidget From(TkEvent e)
    {
        return (e.Window != null) ? e.Window.Widget as ComboboxWidget : null;
    }

    private static DispatchResult OnFieldPress(TkEvent e)
    {
        ComboboxWidget cb = From(e);
        if (cb != null) { cb.ToggleDropDown(); }
        return DispatchResult.Continue;
    }

    private DispatchResult DropDownPress(TkEvent e)
    {
        // A press outside the drop-down dismisses it (and is swallowed).
        if (!PointInDropDown(e)) { CloseDropDown(); return DispatchResult.Break; }
        return DispatchResult.Continue;
    }

    private DispatchResult DropDownRelease(TkEvent e)
    {
        if (_dropdownList != null && PointInDropDown(e))
        {
            Commit(_dropdownList.Nearest(e.Y));
            return DispatchResult.Break;
        }
        return DispatchResult.Continue;
    }
}
