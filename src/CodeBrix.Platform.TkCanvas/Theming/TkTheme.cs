using System;
using System.Collections.Generic;
using System.Globalization;

using CodeBrix.Platform.TkCanvas.Canvas;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Theming;

/// <summary>
/// One color scheme for a whole window tree: every default color the toolkit
/// paints with, in one place (the plan's B.12a). Widgets and the renderer
/// consult the tree's theme at paint/measure time for any color the
/// application did not configure explicitly, so switching
/// <see cref="Events.WindowTree.Theme"/> recolors the running UI on the next
/// repaint with no per-widget reconfiguration. The default-constructed theme
/// IS the classic Tk look (the battleship-gray palette) — a tree that never
/// touches theming renders exactly as stock Tk does.
/// </summary>
/// <remarks>
/// Colors are stored as Tk color specifications (names or hex forms) so they
/// round-trip through option readback unchanged. Assigning a theme to a tree
/// repaints it; mutating a theme instance that is already assigned takes
/// effect on the next repaint but does not schedule one itself.
/// </remarks>
public sealed class TkTheme
{
    /// <summary>The theme's registry/display name (informational).</summary>
    public string Name { get; set; } = "Classic";

    /// <summary>The general widget background — Tk's classic gray.</summary>
    public string Background { get; set; } = "#d9d9d9";

    /// <summary>The general widget text color.</summary>
    public string Foreground { get; set; } = "black";

    /// <summary>The background of an active (pointer-over) element.</summary>
    public string ActiveBackground { get; set; } = "#d9d9d9";

    /// <summary>The text color of an active (pointer-over) element.</summary>
    public string ActiveForeground { get; set; } = "black";

    /// <summary>The text color of disabled elements.</summary>
    public string DisabledForeground { get; set; } = "#a3a3a3";

    /// <summary>The color of the focus highlight ring when unfocused.</summary>
    public string HighlightBackground { get; set; } = "#d9d9d9";

    /// <summary>The color of the focus highlight ring when focused.</summary>
    public string HighlightColor { get; set; } = "black";

    /// <summary>The text-insertion caret color (entry/text).</summary>
    public string InsertBackground { get; set; } = "black";

    /// <summary>The selection background in entry/text widgets.</summary>
    public string SelectBackground { get; set; } = "#c3c3c3";

    /// <summary>The selection text color in entry/text widgets.</summary>
    public string SelectForeground { get; set; } = "black";

    /// <summary>The check/radio indicator interior (Tk's <c>-selectcolor</c>).</summary>
    public string SelectColor { get; set; } = "white";

    /// <summary>The check mark / radio dot drawn inside the indicator.</summary>
    public string IndicatorForeground { get; set; } = "#1a1a1a";

    /// <summary>The scrollbar trough color.</summary>
    public string TroughColor { get; set; } = "#b3b3b3";

    /// <summary>The data-field background (entry, text, listbox, treeview, combobox).</summary>
    public string FieldBackground { get; set; } = "white";

    /// <summary>The data-field text color.</summary>
    public string FieldForeground { get; set; } = "black";

    /// <summary>The selection background in list-style widgets (listbox, treeview).</summary>
    public string ListSelectBackground { get; set; } = "#4a6984";

    /// <summary>The selection text color in list-style widgets.</summary>
    public string ListSelectForeground { get; set; } = "white";

    /// <summary>The treeview heading-band background.</summary>
    public string HeadingBackground { get; set; } = "#e0e0e0";

    /// <summary>The treeview heading text color.</summary>
    public string HeadingForeground { get; set; } = "black";

    /// <summary>The menu background.</summary>
    public string MenuBackground { get; set; } = "#d9d9d9";

    /// <summary>The menu entry text color.</summary>
    public string MenuForeground { get; set; } = "black";

    /// <summary>The background of the active (highlighted) menu entry.</summary>
    public string MenuActiveBackground { get; set; } = "#4a6984";

    /// <summary>The text color of the active (highlighted) menu entry.</summary>
    public string MenuActiveForeground { get; set; } = "white";

    /// <summary>The stage color the renderer clears with (behind everything).</summary>
    public string StageBackground { get; set; } = "#d9d9d9";

    /// <summary>The overlay-toplevel title-bar background.</summary>
    public string TitleBarBackground { get; set; } = "#4a6984";

    /// <summary>The overlay-toplevel title-bar text color.</summary>
    public string TitleBarForeground { get; set; } = "white";

    /// <summary>The button background.</summary>
    public string ButtonBackground { get; set; } = "#d9d9d9";

    /// <summary>The button text color.</summary>
    public string ButtonForeground { get; set; } = "black";

    /// <summary>The scrollbar slider/arrow-box background.</summary>
    public string ScrollbarBackground { get; set; } = "#d9d9d9";

    /// <summary>The canvas widget's default background.</summary>
    public string CanvasBackground { get; set; } = "#d9d9d9";

    /// <summary>The accent color of info/question message-dialog icons.</summary>
    public string DialogInfoAccent { get; set; } = "#204a87";

    /// <summary>The accent color of warning message-dialog icons.</summary>
    public string DialogWarningAccent { get; set; } = "#c08000";

    /// <summary>The accent color of error message-dialog icons.</summary>
    public string DialogErrorAccent { get; set; } = "#c00000";

    /// <summary>
    /// Resolves a themed color specification to a paintable color (black when
    /// the specification is unknown, like <see cref="TkColor"/>).
    /// </summary>
    /// <param name="spec">The color specification.</param>
    /// <returns>The resolved color.</returns>
    public static SKColor Color(string spec)
    {
        SKColor color;
        TkColor.TryParse(spec, out color);
        return color;
    }

    /// <summary>Creates the classic Tk theme (the default battleship-gray look).</summary>
    /// <returns>A new classic theme instance.</returns>
    public static TkTheme CreateClassic()
    {
        return new TkTheme();
    }

    /// <summary>
    /// Creates the legacy bisque theme — the toolkit-side model of Tk's
    /// <c>tk_bisque</c> command (the exact colors from Tk's palette.tcl).
    /// </summary>
    /// <returns>A new bisque theme instance.</returns>
    public static TkTheme CreateBisque()
    {
        TkTheme theme = FromPalette(new[]
        {
            "activeBackground", "#e6ceb1", "activeForeground", "black",
            "background", "#ffe4c4", "disabledForeground", "#b0b0b0",
            "foreground", "black", "highlightBackground", "#ffe4c4",
            "highlightColor", "black", "insertBackground", "black",
            "selectBackground", "#e6ceb1", "selectForeground", "black",
            "troughColor", "#cdb79e",
        });
        theme.Name = "Bisque";
        return theme;
    }

    /// <summary>
    /// Builds a whole theme from a base color (or explicit palette entries) —
    /// the toolkit-side model of Tk's <c>tk_setPalette</c>. A single argument
    /// is the new background; otherwise the arguments are option-name/value
    /// pairs using the option-database names (<c>activeForeground</c>, not
    /// <c>-activeforeground</c>). Every color not given is derived from the
    /// ones that are, using Tk's own derivation math (verified against wish
    /// 8.6.16).
    /// </summary>
    /// <param name="args">The palette arguments.</param>
    /// <returns>A new theme carrying the derived palette.</returns>
    /// <exception cref="ArgumentException">No background color was specified.</exception>
    public static TkTheme FromPalette(IReadOnlyList<string> args)
    {
        Dictionary<string, string> palette = DerivePalette(args);

        var theme = new TkTheme();
        theme.Name = "Palette";

        string background = palette["background"];
        string foreground = palette["foreground"];

        theme.Background = background;
        theme.StageBackground = background;
        theme.MenuBackground = background;
        theme.FieldBackground = background;
        theme.ButtonBackground = background;
        theme.ScrollbarBackground = background;
        theme.CanvasBackground = background;

        theme.Foreground = foreground;
        theme.MenuForeground = foreground;
        theme.FieldForeground = foreground;
        theme.ButtonForeground = foreground;

        theme.ActiveBackground = palette["activeBackground"];
        theme.ActiveForeground = palette["activeForeground"];
        theme.DisabledForeground = palette["disabledForeground"];
        theme.HighlightBackground = palette["highlightBackground"];
        theme.HighlightColor = palette["highlightColor"];
        theme.InsertBackground = palette["insertBackground"];
        theme.SelectBackground = palette["selectBackground"];
        theme.SelectForeground = palette["selectForeground"];
        theme.ListSelectBackground = palette["selectBackground"];
        theme.ListSelectForeground = palette["selectForeground"];
        theme.MenuActiveBackground = palette["activeBackground"];
        theme.MenuActiveForeground = palette["activeForeground"];
        theme.TitleBarBackground = palette["activeBackground"];
        theme.TitleBarForeground = palette["activeForeground"];
        theme.TroughColor = palette["troughColor"];

        string selectColor;
        if (palette.TryGetValue("selectColor", out selectColor))
        {
            theme.SelectColor = selectColor;
        }
        return theme;
    }

    /// <summary>
    /// Computes the eleven standard palette entries exactly as Tk's
    /// <c>tk_setPalette</c> does (integer math on 16-bit color components,
    /// byte-compatible with the values wish writes into the option database).
    /// Explicitly-given entries pass through verbatim; extra option names are
    /// carried through untouched for callers that want them.
    /// </summary>
    /// <param name="args">A single background color, or option-name/value pairs.</param>
    /// <returns>Option-database-name to color-value entries.</returns>
    /// <exception cref="ArgumentException">No background color was specified.</exception>
    public static Dictionary<string, string> DerivePalette(IReadOnlyList<string> args)
    {
        if (args == null) { throw new ArgumentNullException(nameof(args)); }

        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Count == 1)
        {
            palette["background"] = args[0];
        }
        else
        {
            if (args.Count % 2 != 0)
            {
                throw new ArgumentException("palette arguments must be a single color or name-value pairs", nameof(args));
            }
            for (int i = 0; i < args.Count; i += 2)
            {
                palette[args[i]] = args[i + 1];
            }
        }
        if (!palette.ContainsKey("background"))
        {
            throw new ArgumentException("must specify a background color", nameof(args));
        }

        long bgR, bgG, bgB;
        Rgb16(palette["background"], out bgR, out bgG, out bgB);

        if (!palette.ContainsKey("foreground"))
        {
            // Tk's brightness rule on the 16-bit components: eyes weigh
            // green over red over blue.
            palette["foreground"] = (bgR + 1.5 * bgG + 0.5 * bgB > 100000) ? "black" : "white";
        }
        long fgR, fgG, fgB;
        Rgb16(palette["foreground"], out fgR, out fgG, out fgB);

        string darkerBg = Hex(9 * bgR / 2560, 9 * bgG / 2560, 9 * bgB / 2560);

        foreach (string option in new[] { "activeForeground", "insertBackground", "selectForeground", "highlightColor" })
        {
            if (!palette.ContainsKey(option)) { palette[option] = palette["foreground"]; }
        }
        if (!palette.ContainsKey("disabledForeground"))
        {
            palette["disabledForeground"] = Hex(
                    (3 * bgR + fgR) / 1024, (3 * bgG + fgG) / 1024, (3 * bgB + fgB) / 1024);
        }
        if (!palette.ContainsKey("highlightBackground"))
        {
            palette["highlightBackground"] = palette["background"];
        }
        if (!palette.ContainsKey("activeBackground"))
        {
            // Lighten each component by 15% or one third of the way to full
            // white, whichever is greater.
            palette["activeBackground"] = Hex(
                    Lighten(bgR), Lighten(bgG), Lighten(bgB));
        }
        if (!palette.ContainsKey("selectBackground")) { palette["selectBackground"] = darkerBg; }
        if (!palette.ContainsKey("troughColor")) { palette["troughColor"] = darkerBg; }
        return palette;
    }

    private static long Lighten(long component16)
    {
        long light = component16 / 256;
        long inc1 = light * 15 / 100;
        long inc2 = (255 - light) / 3;
        light += (inc1 > inc2) ? inc1 : inc2;
        return (light > 255) ? 255 : light;
    }

    private static void Rgb16(string spec, out long r, out long g, out long b)
    {
        SKColor color;
        if (!TkColor.TryParse(spec, out color))
        {
            throw new ArgumentException("unknown color name \"" + spec + "\"");
        }
        // What [winfo rgb] reports for an 8-bit color on a true-color display.
        r = color.Red * 257L;
        g = color.Green * 257L;
        b = color.Blue * 257L;
    }

    private static string Hex(long r, long g, long b)
    {
        return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", r, g, b);
    }
}
