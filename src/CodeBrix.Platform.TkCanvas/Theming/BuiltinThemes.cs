namespace CodeBrix.Platform.TkCanvas.Theming;

/// <summary>
/// The built-in TkCanvas color schemes (the plan's B.12d): fifteen palettes
/// derived — in a one-time development conversion, 2026-07-10 — from the
/// workbench color tables of well-known editor themes, with the relief and
/// shade math left to the toolkit's Tk-derivation rules at paint time. The
/// palettes are plain checked-in source: hand-tuning individual values is
/// allowed and expected. Derived-palette attribution lives in
/// THIRD-PARTY-NOTICES.txt.
/// </summary>
public static class BuiltinThemes
{
    /// <summary>Registers every built-in theme with <see cref="TkThemeRegistry"/>.</summary>
    internal static void RegisterAll()
    {
        TkThemeRegistry.Register("DarkNew", CreateDarkNew);
        TkThemeRegistry.Register("LightNew", CreateLightNew);
        TkThemeRegistry.Register("DarkPlus", CreateDarkPlus);
        TkThemeRegistry.Register("LightPlus", CreateLightPlus);
        TkThemeRegistry.Register("DarkModern", CreateDarkModern);
        TkThemeRegistry.Register("LightModern", CreateLightModern);
        TkThemeRegistry.Register("Monokai", CreateMonokai);
        TkThemeRegistry.Register("DimmedMonokai", CreateDimmedMonokai);
        TkThemeRegistry.Register("SolarizedDark", CreateSolarizedDark);
        TkThemeRegistry.Register("SolarizedLight", CreateSolarizedLight);
        TkThemeRegistry.Register("Abyss", CreateAbyss);
        TkThemeRegistry.Register("QuietLight", CreateQuietLight);
        TkThemeRegistry.Register("Red", CreateRed);
        TkThemeRegistry.Register("TomorrowNightBlue", CreateTomorrowNightBlue);
        TkThemeRegistry.Register("KimbieDark", CreateKimbieDark);
    }

    /// <summary>Creates the DarkNew theme — the CodeBrix.Develop house dark theme.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateDarkNew()
    {
        return new TkTheme
        {
            Name = "DarkNew",
            Background = "#191a1b",
            Foreground = "#bfbfbf",
            ActiveBackground = "#2b2c2d",
            ActiveForeground = "#bfbfbf",
            DisabledForeground = "#555555",
            HighlightBackground = "#191a1b",
            HighlightColor = "#2f708c",
            InsertBackground = "#bbbebf",
            SelectBackground = "#245c73",
            SelectForeground = "#bfbfbf",
            SelectColor = "#191a1b",
            IndicatorForeground = "#bfbfbf",
            TroughColor = "#606162",
            FieldBackground = "#191a1b",
            FieldForeground = "#bfbfbf",
            ListSelectBackground = "#323233",
            ListSelectForeground = "#ededed",
            HeadingBackground = "#191a1b",
            HeadingForeground = "#bfbfbf",
            MenuBackground = "#202122",
            MenuForeground = "#bfbfbf",
            MenuActiveBackground = "#18262d",
            MenuActiveForeground = "#bfbfbf",
            StageBackground = "#191a1b",
            TitleBarBackground = "#191a1b",
            TitleBarForeground = "#8c8c8c",
            ButtonBackground = "#297aa0",
            ButtonForeground = "#ffffff",
            ScrollbarBackground = "#191a1b",
            CanvasBackground = "#121314",
            DialogInfoAccent = "#2f708c",
            DialogWarningAccent = "#e5ba7d",
            DialogErrorAccent = "#f48771",
        };
    }

    /// <summary>Creates the LightNew theme — the CodeBrix.Develop house light theme.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateLightNew()
    {
        return new TkTheme
        {
            Name = "LightNew",
            Background = "#fafafd",
            Foreground = "#202020",
            ActiveBackground = "#e6e6e9",
            ActiveForeground = "#202020",
            DisabledForeground = "#bbbbbb",
            HighlightBackground = "#fafafd",
            HighlightColor = "#0069cc",
            InsertBackground = "#202020",
            SelectBackground = "#bfd9f2",
            SelectForeground = "#202020",
            SelectColor = "#ffffff",
            IndicatorForeground = "#202020",
            TroughColor = "#8a8a8a",
            FieldBackground = "#ffffff",
            FieldForeground = "#202020",
            ListSelectBackground = "#dadada",
            ListSelectForeground = "#202020",
            HeadingBackground = "#fafafd",
            HeadingForeground = "#202020",
            MenuBackground = "#fafafd",
            MenuForeground = "#202020",
            MenuActiveBackground = "#e5f0fa",
            MenuActiveForeground = "#202020",
            StageBackground = "#fafafd",
            TitleBarBackground = "#fafafd",
            TitleBarForeground = "#606060",
            ButtonBackground = "#0069cc",
            ButtonForeground = "#ffffff",
            ScrollbarBackground = "#fafafd",
            CanvasBackground = "#ffffff",
            DialogInfoAccent = "#0069cc",
            DialogWarningAccent = "#667309",
            DialogErrorAccent = "#ad0707",
        };
    }

    /// <summary>Creates the DarkPlus theme — the familiar dark default.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateDarkPlus()
    {
        return new TkTheme
        {
            Name = "DarkPlus",
            Background = "#121212",
            Foreground = "#d4d4d4",
            ActiveBackground = "#616161",
            ActiveForeground = "#d4d4d4",
            DisabledForeground = "#424242",
            HighlightBackground = "#121212",
            HighlightColor = "#d4d4d4",
            InsertBackground = "#d4d4d4",
            SelectBackground = "#1b1b1b",
            SelectForeground = "#d4d4d4",
            SelectColor = "#1e1e1e",
            IndicatorForeground = "#d4d4d4",
            TroughColor = "#101010",
            FieldBackground = "#1e1e1e",
            FieldForeground = "#d4d4d4",
            ListSelectBackground = "#1b1b1b",
            ListSelectForeground = "#d4d4d4",
            HeadingBackground = "#1e1e1e",
            HeadingForeground = "#d4d4d4",
            MenuBackground = "#252526",
            MenuForeground = "#cccccc",
            MenuActiveBackground = "#0078d4",
            MenuActiveForeground = "#d4d4d4",
            StageBackground = "#121212",
            TitleBarBackground = "#121212",
            TitleBarForeground = "#d4d4d4",
            ButtonBackground = "#121212",
            ButtonForeground = "#d4d4d4",
            ScrollbarBackground = "#121212",
            CanvasBackground = "#1e1e1e",
            DialogInfoAccent = "#204a87",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the LightPlus theme — the familiar light default.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateLightPlus()
    {
        return new TkTheme
        {
            Name = "LightPlus",
            Background = "#f3f3f3",
            Foreground = "#000000",
            ActiveBackground = "#e8e8e8",
            ActiveForeground = "#000000",
            DisabledForeground = "#b6b6b6",
            HighlightBackground = "#f3f3f3",
            HighlightColor = "#000000",
            InsertBackground = "#000000",
            SelectBackground = "#e6e6e6",
            SelectForeground = "#000000",
            SelectColor = "#ffffff",
            IndicatorForeground = "#000000",
            TroughColor = "#dbdbdb",
            FieldBackground = "#ffffff",
            FieldForeground = "#000000",
            ListSelectBackground = "#e6e6e6",
            ListSelectForeground = "#000000",
            HeadingBackground = "#ffffff",
            HeadingForeground = "#000000",
            MenuBackground = "#f3f3f3",
            MenuForeground = "#000000",
            MenuActiveBackground = "#e6e6e6",
            MenuActiveForeground = "#000000",
            StageBackground = "#f3f3f3",
            TitleBarBackground = "#f3f3f3",
            TitleBarForeground = "#000000",
            ButtonBackground = "#f3f3f3",
            ButtonForeground = "#000000",
            ScrollbarBackground = "#f3f3f3",
            CanvasBackground = "#ffffff",
            DialogInfoAccent = "#204a87",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the DarkModern theme — the modern dark look.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateDarkModern()
    {
        return new TkTheme
        {
            Name = "DarkModern",
            Background = "#181818",
            Foreground = "#cccccc",
            ActiveBackground = "#656565",
            ActiveForeground = "#cccccc",
            DisabledForeground = "#454545",
            HighlightBackground = "#181818",
            HighlightColor = "#0078d4",
            InsertBackground = "#cccccc",
            SelectBackground = "#2c2c2c",
            SelectForeground = "#cccccc",
            SelectColor = "#313131",
            IndicatorForeground = "#cccccc",
            TroughColor = "#151515",
            FieldBackground = "#313131",
            FieldForeground = "#cccccc",
            ListSelectBackground = "#2c2c2c",
            ListSelectForeground = "#cccccc",
            HeadingBackground = "#181818",
            HeadingForeground = "#cccccc",
            MenuBackground = "#1f1f1f",
            MenuForeground = "#cccccc",
            MenuActiveBackground = "#0078d4",
            MenuActiveForeground = "#cccccc",
            StageBackground = "#181818",
            TitleBarBackground = "#181818",
            TitleBarForeground = "#cccccc",
            ButtonBackground = "#0078d4",
            ButtonForeground = "#ffffff",
            ScrollbarBackground = "#181818",
            CanvasBackground = "#1f1f1f",
            DialogInfoAccent = "#0078d4",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the LightModern theme — the modern light look.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateLightModern()
    {
        return new TkTheme
        {
            Name = "LightModern",
            Background = "#f8f8f8",
            Foreground = "#3b3b3b",
            ActiveBackground = "#f2f2f2",
            ActiveForeground = "#3b3b3b",
            DisabledForeground = "#c9c9c9",
            HighlightBackground = "#f8f8f8",
            HighlightColor = "#005fb8",
            InsertBackground = "#3b3b3b",
            SelectBackground = "#e6e6e6",
            SelectForeground = "#3b3b3b",
            SelectColor = "#ffffff",
            IndicatorForeground = "#3b3b3b",
            TroughColor = "#e0e0e0",
            FieldBackground = "#ffffff",
            FieldForeground = "#3b3b3b",
            ListSelectBackground = "#e8e8e8",
            ListSelectForeground = "#000000",
            HeadingBackground = "#f8f8f8",
            HeadingForeground = "#3b3b3b",
            MenuBackground = "#ffffff",
            MenuForeground = "#3b3b3b",
            MenuActiveBackground = "#005fb8",
            MenuActiveForeground = "#ffffff",
            StageBackground = "#f8f8f8",
            TitleBarBackground = "#f8f8f8",
            TitleBarForeground = "#1e1e1e",
            ButtonBackground = "#005fb8",
            ButtonForeground = "#ffffff",
            ScrollbarBackground = "#f8f8f8",
            CanvasBackground = "#ffffff",
            DialogInfoAccent = "#005fb8",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the Monokai theme — the classic Monokai palette.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateMonokai()
    {
        return new TkTheme
        {
            Name = "Monokai",
            Background = "#1e1f1c",
            Foreground = "#f8f8f2",
            ActiveBackground = "#3e3d32",
            ActiveForeground = "#f8f8f2",
            DisabledForeground = "#545552",
            HighlightBackground = "#1e1f1c",
            HighlightColor = "#99947c",
            InsertBackground = "#f8f8f0",
            SelectBackground = "#575a5a",
            SelectForeground = "#f8f8f2",
            SelectColor = "#414339",
            IndicatorForeground = "#f8f8f2",
            TroughColor = "#1b1c19",
            FieldBackground = "#414339",
            FieldForeground = "#f8f8f2",
            ListSelectBackground = "#75715e",
            ListSelectForeground = "#f8f8f2",
            HeadingBackground = "#272822",
            HeadingForeground = "#f8f8f2",
            MenuBackground = "#1e1f1c",
            MenuForeground = "#cccccc",
            MenuActiveBackground = "#75715e",
            MenuActiveForeground = "#f8f8f2",
            StageBackground = "#1e1f1c",
            TitleBarBackground = "#1e1f1c",
            TitleBarForeground = "#f8f8f2",
            ButtonBackground = "#75715e",
            ButtonForeground = "#f8f8f2",
            ScrollbarBackground = "#1e1f1c",
            CanvasBackground = "#272822",
            DialogInfoAccent = "#99947c",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the DimmedMonokai theme — Monokai, dimmed.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateDimmedMonokai()
    {
        return new TkTheme
        {
            Name = "DimmedMonokai",
            Background = "#272727",
            Foreground = "#c5c8c6",
            ActiveBackground = "#444444",
            ActiveForeground = "#c5c8c6",
            DisabledForeground = "#4e4f4f",
            HighlightBackground = "#272727",
            HighlightColor = "#3655b5",
            InsertBackground = "#c07020",
            SelectBackground = "#434548",
            SelectForeground = "#c5c8c6",
            SelectColor = "#1e1e1e",
            IndicatorForeground = "#c5c8c6",
            TroughColor = "#232323",
            FieldBackground = "#1e1e1e",
            FieldForeground = "#c5c8c6",
            ListSelectBackground = "#707070",
            ListSelectForeground = "#c5c8c6",
            HeadingBackground = "#505050",
            HeadingForeground = "#c5c8c6",
            MenuBackground = "#272727",
            MenuForeground = "#cccccc",
            MenuActiveBackground = "#707070",
            MenuActiveForeground = "#c5c8c6",
            StageBackground = "#272727",
            TitleBarBackground = "#505050",
            TitleBarForeground = "#c5c8c6",
            ButtonBackground = "#565656",
            ButtonForeground = "#c5c8c6",
            ScrollbarBackground = "#272727",
            CanvasBackground = "#1e1e1e",
            DialogInfoAccent = "#3655b5",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the SolarizedDark theme — Solarized, dark.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateSolarizedDark()
    {
        return new TkTheme
        {
            Name = "SolarizedDark",
            Background = "#00212b",
            Foreground = "#839496",
            ActiveBackground = "#003846",
            ActiveForeground = "#839496",
            DisabledForeground = "#213e46",
            HighlightBackground = "#00212b",
            HighlightColor = "#196e6c",
            InsertBackground = "#d30102",
            SelectBackground = "#274642",
            SelectForeground = "#93a1a1",
            SelectColor = "#003847",
            IndicatorForeground = "#93a1a1",
            TroughColor = "#001d26",
            FieldBackground = "#003847",
            FieldForeground = "#93a1a1",
            ListSelectBackground = "#005a6f",
            ListSelectForeground = "#93a1a1",
            HeadingBackground = "#004052",
            HeadingForeground = "#839496",
            MenuBackground = "#00212b",
            MenuForeground = "#839496",
            MenuActiveBackground = "#005a6f",
            MenuActiveForeground = "#93a1a1",
            StageBackground = "#00212b",
            TitleBarBackground = "#002c39",
            TitleBarForeground = "#839496",
            ButtonBackground = "#196e6c",
            ButtonForeground = "#839496",
            ScrollbarBackground = "#00212b",
            CanvasBackground = "#002b36",
            DialogInfoAccent = "#196e6c",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the SolarizedLight theme — Solarized, light.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateSolarizedLight()
    {
        return new TkTheme
        {
            Name = "SolarizedLight",
            Background = "#eee8d5",
            Foreground = "#657b83",
            ActiveBackground = "#eae0c0",
            ActiveForeground = "#657b83",
            DisabledForeground = "#cccdc0",
            HighlightBackground = "#eee8d5",
            HighlightColor = "#b49471",
            InsertBackground = "#657b83",
            SelectBackground = "#eee8d5",
            SelectForeground = "#586e75",
            SelectColor = "#ddd6c1",
            IndicatorForeground = "#586e75",
            TroughColor = "#d7d1c0",
            FieldBackground = "#ddd6c1",
            FieldForeground = "#586e75",
            ListSelectBackground = "#dfca88",
            ListSelectForeground = "#6c6c6c",
            HeadingBackground = "#d9d2c2",
            HeadingForeground = "#657b83",
            MenuBackground = "#eee8d5",
            MenuForeground = "#657b83",
            MenuActiveBackground = "#dfca88",
            MenuActiveForeground = "#6c6c6c",
            StageBackground = "#eee8d5",
            TitleBarBackground = "#eee8d5",
            TitleBarForeground = "#657b83",
            ButtonBackground = "#ac9d57",
            ButtonForeground = "#657b83",
            ScrollbarBackground = "#eee8d5",
            CanvasBackground = "#fdf6e3",
            DialogInfoAccent = "#b49471",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the Abyss theme — deep blue-black.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateAbyss()
    {
        return new TkTheme
        {
            Name = "Abyss",
            Background = "#060621",
            Foreground = "#6688cc",
            ActiveBackground = "#061940",
            ActiveForeground = "#6688cc",
            DisabledForeground = "#1e264c",
            HighlightBackground = "#060621",
            HighlightColor = "#596f99",
            InsertBackground = "#ddbb88",
            SelectBackground = "#770811",
            SelectForeground = "#6688cc",
            SelectColor = "#181f2f",
            IndicatorForeground = "#6688cc",
            TroughColor = "#151b28",
            FieldBackground = "#181f2f",
            FieldForeground = "#6688cc",
            ListSelectBackground = "#08286b",
            ListSelectForeground = "#6688cc",
            HeadingBackground = "#10192c",
            HeadingForeground = "#6688cc",
            MenuBackground = "#181f2f",
            MenuForeground = "#6688cc",
            MenuActiveBackground = "#08286b",
            MenuActiveForeground = "#6688cc",
            StageBackground = "#060621",
            TitleBarBackground = "#10192c",
            TitleBarForeground = "#6688cc",
            ButtonBackground = "#2b3c5d",
            ButtonForeground = "#6688cc",
            ScrollbarBackground = "#060621",
            CanvasBackground = "#000c18",
            DialogInfoAccent = "#596f99",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the QuietLight theme — gentle pastel light.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateQuietLight()
    {
        return new TkTheme
        {
            Name = "QuietLight",
            Background = "#f2f2f2",
            Foreground = "#000000",
            ActiveBackground = "#e0e0e0",
            ActiveForeground = "#000000",
            DisabledForeground = "#b6b6b6",
            HighlightBackground = "#f2f2f2",
            HighlightColor = "#9769dc",
            InsertBackground = "#54494b",
            SelectBackground = "#c9d0d9",
            SelectForeground = "#000000",
            SelectColor = "#f5f5f5",
            IndicatorForeground = "#000000",
            TroughColor = "#dadada",
            FieldBackground = "#f5f5f5",
            FieldForeground = "#000000",
            ListSelectBackground = "#c4d9b1",
            ListSelectForeground = "#6c6c6c",
            HeadingBackground = "#ede8ef",
            HeadingForeground = "#000000",
            MenuBackground = "#f5f5f5",
            MenuForeground = "#000000",
            MenuActiveBackground = "#c4d9b1",
            MenuActiveForeground = "#6c6c6c",
            StageBackground = "#f2f2f2",
            TitleBarBackground = "#c4b7d7",
            TitleBarForeground = "#000000",
            ButtonBackground = "#705697",
            ButtonForeground = "#000000",
            ScrollbarBackground = "#f2f2f2",
            CanvasBackground = "#f5f5f5",
            DialogInfoAccent = "#9769dc",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the Red theme — the red monochrome.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateRed()
    {
        return new TkTheme
        {
            Name = "Red",
            Background = "#330000",
            Foreground = "#f8f8f8",
            ActiveBackground = "#800000",
            ActiveForeground = "#f8f8f8",
            DisabledForeground = "#643e3e",
            HighlightBackground = "#330000",
            HighlightColor = "#bb4444",
            InsertBackground = "#970000",
            SelectBackground = "#750000",
            SelectForeground = "#f8f8f8",
            SelectColor = "#580000",
            IndicatorForeground = "#f8f8f8",
            TroughColor = "#2e0000",
            FieldBackground = "#580000",
            FieldForeground = "#f8f8f8",
            ListSelectBackground = "#880000",
            ListSelectForeground = "#f8f8f8",
            HeadingBackground = "#330000",
            HeadingForeground = "#f8f8f8",
            MenuBackground = "#580000",
            MenuForeground = "#f8f8f8",
            MenuActiveBackground = "#880000",
            MenuActiveForeground = "#f8f8f8",
            StageBackground = "#330000",
            TitleBarBackground = "#770000",
            TitleBarForeground = "#f8f8f8",
            ButtonBackground = "#883333",
            ButtonForeground = "#f8f8f8",
            ScrollbarBackground = "#330000",
            CanvasBackground = "#390000",
            DialogInfoAccent = "#bb4444",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the TomorrowNightBlue theme — Tomorrow Night Blue.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateTomorrowNightBlue()
    {
        return new TkTheme
        {
            Name = "TomorrowNightBlue",
            Background = "#001c40",
            Foreground = "#ffffff",
            ActiveBackground = "#304764",
            ActiveForeground = "#ffffff",
            DisabledForeground = "#405570",
            HighlightBackground = "#001c40",
            HighlightColor = "#bbdaff",
            InsertBackground = "#ffffff",
            SelectBackground = "#003f8e",
            SelectForeground = "#ffffff",
            SelectColor = "#001733",
            IndicatorForeground = "#ffffff",
            TroughColor = "#001939",
            FieldBackground = "#001733",
            FieldForeground = "#ffffff",
            ListSelectBackground = "#607693",
            ListSelectForeground = "#ffffff",
            HeadingBackground = "#001733",
            HeadingForeground = "#ffffff",
            MenuBackground = "#001733",
            MenuForeground = "#ffffff",
            MenuActiveBackground = "#607693",
            MenuActiveForeground = "#ffffff",
            StageBackground = "#001c40",
            TitleBarBackground = "#001126",
            TitleBarForeground = "#ffffff",
            ButtonBackground = "#001c40",
            ButtonForeground = "#ffffff",
            ScrollbarBackground = "#001c40",
            CanvasBackground = "#002451",
            DialogInfoAccent = "#bbdaff",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }

    /// <summary>Creates the KimbieDark theme — warm earth-tone dark.</summary>
    /// <returns>A new theme instance.</returns>
    public static TkTheme CreateKimbieDark()
    {
        return new TkTheme
        {
            Name = "KimbieDark",
            Background = "#362712",
            Foreground = "#d3af86",
            ActiveBackground = "#523718",
            ActiveForeground = "#d3af86",
            DisabledForeground = "#5d492f",
            HighlightBackground = "#362712",
            HighlightColor = "#a57a4c",
            InsertBackground = "#d3af86",
            SelectBackground = "#63492e",
            SelectForeground = "#d3af86",
            SelectColor = "#51412c",
            IndicatorForeground = "#d3af86",
            TroughColor = "#302310",
            FieldBackground = "#51412c",
            FieldForeground = "#d3af86",
            ListSelectBackground = "#7c5021",
            ListSelectForeground = "#d3af86",
            HeadingBackground = "#131510",
            HeadingForeground = "#d3af86",
            MenuBackground = "#362712",
            MenuForeground = "#cccccc",
            MenuActiveBackground = "#7c5021",
            MenuActiveForeground = "#d3af86",
            StageBackground = "#362712",
            TitleBarBackground = "#423523",
            TitleBarForeground = "#d3af86",
            ButtonBackground = "#6e583b",
            ButtonForeground = "#d3af86",
            ScrollbarBackground = "#362712",
            CanvasBackground = "#221a0f",
            DialogInfoAccent = "#a57a4c",
            DialogWarningAccent = "#c08000",
            DialogErrorAccent = "#c00000",
        };
    }
}
