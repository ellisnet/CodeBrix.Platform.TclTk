using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Images;
using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Canvas;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The resource commands: <c>image</c> (photo names become instance
/// commands, like Tk), <c>font</c> over the R2 measurement seam,
/// <c>clipboard</c>, <c>option</c> (the option database),
/// <c>ttk::style</c>, and the theme appliers (<c>tk_setPalette</c>,
/// <c>tk_bisque</c>, <c>tk_classic</c>, and the <c>tk_&lt;theme&gt;</c>
/// family).
/// </summary>
internal static class ResourceCommands
{
    internal static void Register(BridgeContext ctx)
    {
        RegisterImage(ctx);
        RegisterFont(ctx);

        BridgeRegistrar.Add(ctx, "clipboard", words =>
            ctx.Ui(() => ctx.Tree.Clipboard.Execute(Rest(words))));

        BridgeRegistrar.Add(ctx, "option", words => ctx.Ui(() => Option(ctx, words)));

        BridgeRegistrar.Add(ctx, "ttk::style", words =>
            ctx.Ui(() => ctx.Tree.Styles.Execute(Rest(words))));

        RegisterThemeAppliers(ctx);
    }

    private static string[] Rest(string[] words)
    {
        var rest = new string[words.Length - 1];
        Array.Copy(words, 1, rest, 0, rest.Length);
        return rest;
    }

    // --------------------------------------------------------------- image

    private static void RegisterImage(BridgeContext ctx)
    {
        var imageTokens = new Dictionary<string, long>(StringComparer.Ordinal);

        BridgeRegistrar.Add(ctx, "image", words =>
        {
            string result = ctx.Ui(() => ctx.Tree.Images.Execute(Rest(words)));

            if (words.Length >= 3 && words[1] == "create")
            {
                // The created image's name is the result; it becomes an
                // instance command, exactly like a widget path.
                string imageName = result;
                if (!imageTokens.ContainsKey(imageName))
                {
                    long token = BridgeRegistrar.AddRemovable(ctx, imageName, imageWords =>
                        ctx.Ui(() =>
                        {
                            PhotoImage photo = ctx.Tree.Images.Find(imageName);
                            if (photo == null)
                            {
                                throw new TkTclError("image \"" + imageName + "\" doesn't exist");
                            }
                            return photo.Execute(Rest(imageWords));
                        }));
                    imageTokens[imageName] = token;
                }
            }
            else if (words.Length >= 3 && words[1] == "delete")
            {
                for (int i = 2; i < words.Length; i++)
                {
                    long token;
                    if (imageTokens.TryGetValue(words[i], out token))
                    {
                        imageTokens.Remove(words[i]);
                        TclTk._Components.Public.Result error = null;
                        ctx.Interpreter.RemoveCommand(token, null, ref error);
                    }
                }
            }

            return result;
        });
    }

    // ---------------------------------------------------------------- font

    private static void RegisterFont(BridgeContext ctx)
    {
#if PERFORMANCE_DIAGNOSIS
        BridgeRegistrar.Add(ctx, "font", words =>
        {
            long __probe = CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Now;
            try { return ctx.Ui(() => Font(ctx, words)); }
            finally { CodeBrix.Platform.TclTk.Diagnostics.PerfProbe.Add("font." + (words.Length > 1 ? words[1] : "?"), __probe); }
        });
#else
        BridgeRegistrar.Add(ctx, "font", words => ctx.Ui(() => Font(ctx, words)));
#endif
    }

    private static string Font(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("font option ?arg ...?");
        }

        FontManager fonts = ctx.Tree.Fonts;
        switch (words[1])
        {
            case "create":
            {
                string name = words.Length >= 3 && !words[2].StartsWith("-", StringComparison.Ordinal)
                    ? words[2]
                    : null;
                int optionStart = name != null ? 3 : 2;
                TkFont created = fonts.CreateNamed(name ?? AutoFontName(fonts));
                ApplyFontOptions(created, BridgeRegistrar.ParseOptionPairs(words, optionStart));
                return created.Name;
            }

            case "configure":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("font configure fontname ?-option value ...?"); }
                TkFont font = fonts.GetNamed(words[2]);
                if (font == null)
                {
                    throw new TkTclError("named font \"" + words[2] + "\" doesn't exist");
                }
                if (words.Length == 3)
                {
                    return FontActualString(font);
                }
                ApplyFontOptions(font, BridgeRegistrar.ParseOptionPairs(words, 3));
                return "";
            }

            case "delete":
                for (int i = 2; i < words.Length; i++) { fonts.DeleteNamed(words[i]); }
                return "";

            case "measure":
            {
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs("font measure font ?-displayof window? text"); }
                TkFont font = ResolveFont(fonts, words[2]);
                return fonts.Measure(font, words[words.Length - 1])
                    .ToString(CultureInfo.InvariantCulture);
            }

            case "metrics":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("font metrics font ?-option?"); }
                TkFont font = ResolveFont(fonts, words[2]);
                FontMetrics metrics = fonts.Metrics(font);
                if (words.Length >= 4)
                {
                    switch (words[3])
                    {
                        case "-ascent": return metrics.Ascent.ToString(CultureInfo.InvariantCulture);
                        case "-descent": return metrics.Descent.ToString(CultureInfo.InvariantCulture);
                        case "-linespace": return metrics.LineSpace.ToString(CultureInfo.InvariantCulture);
                        case "-fixed": return metrics.IsFixed ? "1" : "0";
                        default: throw new TkTclError("bad metric \"" + words[3] + "\"");
                    }
                }
                return "-ascent " + metrics.Ascent.ToString(CultureInfo.InvariantCulture) +
                    " -descent " + metrics.Descent.ToString(CultureInfo.InvariantCulture) +
                    " -linespace " + metrics.LineSpace.ToString(CultureInfo.InvariantCulture) +
                    " -fixed " + (metrics.IsFixed ? "1" : "0");
            }

            case "names":
                return TclString.JoinList(fonts.Names.ToList());

            case "families":
                return "";

            case "actual":
            {
                if (words.Length < 3) { throw BridgeRegistrar.WrongArgs("font actual font ?-option?"); }
                TkFont font = ResolveFont(fonts, words[2]);
                if (words.Length >= 4)
                {
                    switch (words[3])
                    {
                        case "-family": return font.Family;
                        case "-size": return font.Size.ToString(CultureInfo.InvariantCulture);
                        case "-weight": return font.Bold ? "bold" : "normal";
                        case "-slant": return font.Italic ? "italic" : "roman";
                        case "-underline": return font.Underline ? "1" : "0";
                        case "-overstrike": return font.Overstrike ? "1" : "0";
                        default: return "";
                    }
                }
                return FontActualString(font);
            }

            default:
                return "";
        }
    }

    private static TkFont ResolveFont(FontManager fonts, string descriptor)
    {
        TkFont named = fonts.GetNamed(descriptor);
        return named ?? fonts.Parse(descriptor);
    }

    private static string AutoFontName(FontManager fonts)
    {
        int serial = 1;
        while (fonts.GetNamed("font" + serial.ToString(CultureInfo.InvariantCulture)) != null)
        {
            serial++;
        }
        return "font" + serial.ToString(CultureInfo.InvariantCulture);
    }

    private static void ApplyFontOptions(TkFont font, Dictionary<string, string> options)
    {
        string family;
        if (options.TryGetValue("-family", out family)) { font.Family = family; }

        string sizeText;
        if (options.TryGetValue("-size", out sizeText))
        {
            int size;
            if (int.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
            {
                font.Size = size;
            }
        }

        string weight;
        if (options.TryGetValue("-weight", out weight)) { font.Bold = weight == "bold"; }

        string slant;
        if (options.TryGetValue("-slant", out slant)) { font.Italic = slant == "italic"; }

        string underline;
        if (options.TryGetValue("-underline", out underline)) { font.Underline = IsTclTrue(underline); }

        string overstrike;
        if (options.TryGetValue("-overstrike", out overstrike)) { font.Overstrike = IsTclTrue(overstrike); }
    }

    private static string FontActualString(TkFont font)
    {
        return "-family {" + font.Family + "} -size " +
            font.Size.ToString(CultureInfo.InvariantCulture) +
            " -weight " + (font.Bold ? "bold" : "normal") +
            " -slant " + (font.Italic ? "italic" : "roman") +
            " -underline " + (font.Underline ? "1" : "0") +
            " -overstrike " + (font.Overstrike ? "1" : "0");
    }

    // -------------------------------------------------------------- option

    private static string Option(BridgeContext ctx, string[] words)
    {
        if (words.Length < 2)
        {
            throw BridgeRegistrar.WrongArgs("option cmd arg ?arg ...?");
        }

        OptionDatabase database = ctx.Tree.OptionDatabase;
        switch (words[1])
        {
            case "add":
                if (words.Length < 4) { throw BridgeRegistrar.WrongArgs("option add pattern value ?priority?"); }
                if (words.Length >= 5)
                {
                    database.Add(words[2], words[3], words[4]);
                }
                else
                {
                    database.Add(words[2], words[3]);
                }
                return "";

            case "get":
                if (words.Length < 5) { throw BridgeRegistrar.WrongArgs("option get window name class"); }
                return database.Get(ctx.ResolveWindow(words[2]), words[3], words[4]) ?? "";

            case "clear":
                database.Clear();
                return "";

            case "readfile":
                // File access belongs to the app layer; accept-and-no-op.
                return "";

            default:
                return "";
        }
    }

    // ------------------------------------------------------------- theming

    private static void RegisterThemeAppliers(BridgeContext ctx)
    {
        BridgeRegistrar.Add(ctx, "tk_setPalette", words => ctx.Ui(() =>
        {
            ctx.Tree.SetPalette(Rest(words));
            return "";
        }));

        BridgeRegistrar.Add(ctx, "tk_bisque", words => ctx.Ui(() =>
        {
            ctx.Tree.Theme = TkTheme.CreateBisque();
            return "";
        }));

        foreach (string themeName in TkThemeRegistry.Names)
        {
            string name = themeName;
            string commandName = "tk_" + char.ToLowerInvariant(name[0]) + name.Substring(1);
            BridgeRegistrar.Add(ctx, commandName, words => ctx.Ui(() =>
            {
                TkTheme theme = TkThemeRegistry.TryCreate(name);
                if (theme != null) { ctx.Tree.Theme = theme; }
                return "";
            }));
        }
    }

    private static bool IsTclTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            default: return false;
        }
    }
}
