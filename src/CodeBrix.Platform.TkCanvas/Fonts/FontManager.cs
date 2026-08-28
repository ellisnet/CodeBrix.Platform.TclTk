using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Fonts;

/// <summary>
/// THE text-measurement seam (the plan's R2): the ONE service that resolves
/// Tk font specifications to Skia fonts, measures text, and reports metrics.
/// The Skia painter and the Tcl-facing <c>font measure</c>/<c>font
/// metrics</c> commands both go through this class and therefore through the
/// SAME <see cref="SKFont"/> — by construction they can never disagree, so
/// text-sized layouts (a consumer sizing its elements via
/// <c>font measure</c>) always fit what gets painted.
/// Also owns named fonts (<c>font create</c>/<c>configure</c>/<c>delete</c>)
/// and Tk's standard font names.
/// </summary>
public sealed class FontManager
{
    private readonly Dictionary<string, TkFont> _named =
            new Dictionary<string, TkFont>(StringComparer.Ordinal);
    private readonly Dictionary<string, SKTypeface> _typefaces =
            new Dictionary<string, SKTypeface>(StringComparer.Ordinal);
    private readonly HashSet<SKTypeface> _packagedFaces = new HashSet<SKTypeface>();
    private readonly Dictionary<string, FontChain> _chains =
            new Dictionary<string, FontChain>(StringComparer.Ordinal);
    private readonly Dictionary<SKTypeface, SKFont> _coverage =
            new Dictionary<SKTypeface, SKFont>();
    private readonly List<string> _fontDirectories = new List<string>();

    /// <summary>
    /// The packaged face behind each CSS generic — the ONLY fonts the toolkit
    /// draws with. Every entry is a dash-free file, which the fonts packages
    /// guarantee is never pruned, and each is a variable font carrying a
    /// <c>wght</c> axis.
    /// </summary>
    /// <remarks>
    /// NOTE THE MONOSPACE ENTRY. The package id says RobotoMono because it is a
    /// BUNDLE of monospace families, and we deliberately take Noto Sans Mono out
    /// of it rather than the Roboto Mono it is named for: Roboto Mono advances
    /// 1229/2048 em = 8.0013px at Tk's default size 10, and rounding that
    /// hair-over-8 up gives a 9px cell on Windows against Linux's 8. Noto Sans
    /// Mono is exactly 0.6 em and lands on 8 everywhere. Do not "fix" this by
    /// switching to the font in the package id.
    /// </remarks>
    private static readonly Dictionary<string, PackagedFace> PackagedFaces =
            new Dictionary<string, PackagedFace>(StringComparer.Ordinal)
            {
                ["monospace"] = new PackagedFace("CodeBrix.Platform.Fonts.RobotoMono", "NotoSansMono.ttf"),
                ["sans-serif"] = new PackagedFace("CodeBrix.Platform.Fonts.Roboto", "Roboto.ttf"),
                ["serif"] = new PackagedFace("CodeBrix.Platform.Fonts.Merriweather", "Merriweather.ttf"),
            };

    /// <summary>
    /// The PER-GLYPH FALLBACK CHAIN for each generic: extra packaged faces
    /// consulted, in order, for a codepoint the primary face does not carry.
    /// <para>
    /// TkCanvas targets the EUROPEAN scripts. The primaries cover Latin, Latin
    /// Extended, Cyrillic and modern Greek; these companions add what they lack —
    /// Armenian, Georgian and polytonic (Ancient) Greek. The font packages ship
    /// them for exactly this purpose. Hebrew, Arabic, CJK and other non-European
    /// scripts are deliberately NOT covered and render as tofu; nothing here does
    /// bidi or complex shaping.
    /// </para>
    /// <para>
    /// Coverage was measured, not assumed: Roboto has no polytonic Greek, and
    /// Merriweather carries no Greek at all, so both lean on Noto Serif for it.
    /// A fallback file that is missing is skipped rather than fatal — losing
    /// optional coverage degrades to tofu, which is what an uncovered script does
    /// anyway. Only a missing PRIMARY throws.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, PackagedFace[]> FallbackChains =
            new Dictionary<string, PackagedFace[]>(StringComparer.Ordinal)
            {
                ["monospace"] = new[]
                {
                    // Iosevka is the only packaged mono face with Armenian.
                    new PackagedFace("CodeBrix.Platform.Fonts.RobotoMono", "Iosevka.ttf"),
                    new PackagedFace("CodeBrix.Platform.Fonts.RobotoMono", "NotoSansGeorgian.ttf"),
                },
                ["sans-serif"] = new[]
                {
                    // Noto Sans sits directly behind Roboto: it carries the
                    // polytonic Greek Roboto lacks (233/233 against Roboto's 1),
                    // in matching sans letterforms, and it quietly covers Roboto's
                    // one missing Cyrillic codepoint too.
                    new PackagedFace("CodeBrix.Platform.Fonts.Roboto", "NotoSans.ttf"),
                    // Noto Sans is the Latin/Greek/Cyrillic core only — it has no
                    // Armenian and no Georgian, so those stay script-specific.
                    new PackagedFace("CodeBrix.Platform.Fonts.Roboto", "NotoSansArmenian.ttf"),
                    new PackagedFace("CodeBrix.Platform.Fonts.Roboto", "NotoSansGeorgian.ttf"),
                },
                ["serif"] = new[]
                {
                    // Merriweather has no Greek at all — Noto Serif supplies both
                    // modern and polytonic.
                    new PackagedFace("CodeBrix.Platform.Fonts.Merriweather", "NotoSerif.ttf"),
                    new PackagedFace("CodeBrix.Platform.Fonts.Merriweather", "NotoSerifArmenian.ttf"),
                    new PackagedFace("CodeBrix.Platform.Fonts.Merriweather", "NotoSerifGeorgian.ttf"),
                },
            };

    /// <summary>A resolved primary face plus the fallbacks consulted after it.</summary>
    private sealed class FontChain
    {
        public FontChain(SKTypeface primary, SKTypeface[] fallbacks)
        {
            Primary = primary;
            Fallbacks = fallbacks;
        }

        /// <summary>The face that serves the generic itself.</summary>
        public SKTypeface Primary { get; }

        /// <summary>Faces consulted, in order, for codepoints the primary lacks.</summary>
        public SKTypeface[] Fallbacks { get; }

        /// <summary>Resolves a run's face index: 0 is the primary.</summary>
        /// <param name="index">The face index.</param>
        /// <returns>The typeface.</returns>
        public SKTypeface FaceAt(int index)
        {
            return (index == 0) ? Primary : Fallbacks[index - 1];
        }
    }

    /// <summary>A stretch of text served by one face of the chain.</summary>
    private readonly struct TextRun
    {
        public TextRun(int face, int start, int length)
        {
            Face = face;
            Start = start;
            Length = length;
        }

        /// <summary>Index into the chain — 0 is the primary.</summary>
        public int Face { get; }

        /// <summary>Start offset within the measured string.</summary>
        public int Start { get; }

        /// <summary>Length in UTF-16 code units.</summary>
        public int Length { get; }
    }

    /// <summary>One packaged font file: the package's folder and the file in it.</summary>
    private readonly struct PackagedFace
    {
        public PackagedFace(string folder, string file)
        {
            Folder = folder;
            File = file;
        }

        /// <summary>The <c>CodeBrix.Platform.Fonts.*</c> folder beside the assembly.</summary>
        public string Folder { get; }

        /// <summary>The font file within that folder's <c>Fonts</c> directory.</summary>
        public string File { get; }
    }

    /// <summary>The OpenType <c>wght</c> variation axis.</summary>
    private static readonly SKFourByteTag WeightAxisTag =
            new SKFourByteTag((uint)(('w' << 24) | ('g' << 16) | ('h' << 8) | 't'));

    /// <summary>The weight-axis value for a normal weight.</summary>
    /// <remarks>
    /// Set EXPLICITLY rather than left at the face's own default: Merriweather's
    /// wght axis defaults to 300, so regular body text would otherwise render in
    /// Light. Pinning both weights also keeps the faces consistent with each other.
    /// </remarks>
    private const float NormalWeight = 400f;

    /// <summary>The weight-axis value for bold.</summary>
    private const float BoldWeight = 700f;

    /// <summary>Shear applied to fake an italic the packaged face does not carry.</summary>
    private const float SyntheticObliqueSkew = -0.25f;

    /// <summary>The generic used for a family the toolkit does not ship.</summary>
    private const string DefaultGenericFamily = "sans-serif";

    /// <summary>
    /// Where the <c>CodeBrix.Platform.Fonts.*</c> folders live. Null means beside
    /// the assembly, which is where the build target and the CodeBrix.Platform
    /// asset pipeline both put them; the tests point it at an empty directory to
    /// exercise the missing-font failure.
    /// </summary>
    internal string PackagedFontBaseDirectory { get; set; }

    /// <summary>Arbitrary size for the cached coverage probes (coverage is size-free).</summary>
    private const float CoverageProbeSize = 12f;

    /// <summary>Glyphs sampled to decide whether a face really is fixed-pitch.</summary>
    private const string FixedPitchProbe = "0MiW.@";

    /// <summary>
    /// Creates the manager with Tk's standard named fonts pre-defined
    /// (TkDefaultFont, TkTextFont, TkFixedFont, TkMenuFont, TkHeadingFont,
    /// TkCaptionFont, TkSmallCaptionFont, TkIconFont, TkTooltipFont).
    /// </summary>
    public FontManager()
    {
        DefineStandard("TkDefaultFont", "sans-serif", 10, false);
        DefineStandard("TkTextFont", "sans-serif", 10, false);
        DefineStandard("TkFixedFont", "monospace", 10, false);
        DefineStandard("TkMenuFont", "sans-serif", 10, false);
        DefineStandard("TkHeadingFont", "sans-serif", 10, true);
        DefineStandard("TkCaptionFont", "sans-serif", 12, true);
        DefineStandard("TkSmallCaptionFont", "sans-serif", 9, false);
        DefineStandard("TkIconFont", "sans-serif", 10, false);
        DefineStandard("TkTooltipFont", "sans-serif", 9, false);
    }

    /// <summary>
    /// Pixels per point for positive (point) font sizes. Tk computes this
    /// from the screen (<c>tk scaling</c>); the default is the common
    /// 96 dpi / 72, and the host can adjust it.
    /// </summary>
    public double PixelsPerPoint { get; set; } = 96.0 / 72.0;

    /// <summary>
    /// OPT IN to letting the toolkit fall back to fonts installed on the host.
    /// Off by default, and leaving it off is the supported configuration.
    /// <para>
    /// With this <see langword="false"/>, TkCanvas draws with its packaged fonts
    /// and NOTHING ELSE: a font file that failed to reach the output directory
    /// raises <see cref="InvalidOperationException"/> rather than quietly
    /// substituting whatever the machine happens to have, and a family the
    /// toolkit does not ship (say <c>{Segoe UI 12}</c>) is served by the packaged
    /// sans face. That keeps geometry identical on every machine, which a toolkit
    /// that measures its own layout depends on.
    /// </para>
    /// <para>
    /// Turn it on only when a consumer deliberately wants host fonts — to pick up
    /// a corporate typeface, or to keep an application alive on a deployment whose
    /// font assets are known to be missing. Set it before anything renders; faces
    /// already resolved are cached and are not re-resolved.
    /// </para>
    /// </summary>
    public bool AllowSystemFontFallback { get; set; }

    private void DefineStandard(string name, string family, int size, bool bold)
    {
        _named[name] = new TkFont { Name = name, Family = family, Size = size, Bold = bold };
    }

    /// <summary>
    /// Creates a named font — <c>font create NAME ?options?</c>.
    /// </summary>
    /// <param name="name">The font name; must not exist yet.</param>
    /// <param name="template">The attributes to copy, or null for defaults.</param>
    /// <returns>The created (mutable, shared) font.</returns>
    public TkFont CreateNamed(string name, TkFont template = null)
    {
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("empty font name", nameof(name)); }
        if (_named.ContainsKey(name))
        {
            throw new InvalidOperationException("named font \"" + name + "\" already exists");
        }

        var font = new TkFont { Name = name };
        if (template != null) { font.CopyAttributesFrom(template); }
        _named[name] = font;
        return font;
    }

    /// <summary>Looks up a named font, or null — <c>font names</c> membership.</summary>
    /// <param name="name">The font name.</param>
    /// <returns>The shared font instance, or null.</returns>
    public TkFont GetNamed(string name)
    {
        TkFont font;
        return _named.TryGetValue(name, out font) ? font : null;
    }

    /// <summary>Deletes a named font — <c>font delete NAME</c>.</summary>
    /// <param name="name">The font name.</param>
    public void DeleteNamed(string name)
    {
        if (!_named.Remove(name))
        {
            throw new InvalidOperationException("named font \"" + name + "\" doesn't exist");
        }
    }

    /// <summary>The currently defined named-font names — <c>font names</c>.</summary>
    public IReadOnlyCollection<string> Names
    {
        get { return _named.Keys; }
    }

    /// <summary>
    /// Resolves a Tk font descriptor to a font: a named font (shared
    /// instance), a <c>{family size ?styles?}</c> list, a
    /// <c>-family ... -size ...</c> option string, or an X core font name
    /// (accepted and mapped to the default font — accept-and-no-op).
    /// </summary>
    /// <param name="descriptor">The descriptor text.</param>
    /// <returns>The resolved font (never null).</returns>
    public TkFont Parse(string descriptor)
    {
        if (string.IsNullOrEmpty(descriptor)) { return _named["TkDefaultFont"]; }

        TkFont named = GetNamed(descriptor);
        if (named != null) { return named; }

        // X core font names ("-adobe-helvetica-...") are legacy: accept them
        // and fall back to the default font rather than erroring.
        if (descriptor[0] == '-' && descriptor.IndexOf(' ') < 0)
        {
            return _named["TkDefaultFont"];
        }

        List<string> words = SplitTclList(descriptor);
        if (words.Count == 0) { return _named["TkDefaultFont"]; }

        var font = new TkFont();
        if (words[0].Length > 0 && words[0][0] == '-')
        {
            // Option form: -family F -size N -weight bold -slant italic ...
            for (int i = 0; i + 1 < words.Count; i += 2)
            {
                string value = words[i + 1];
                switch (words[i])
                {
                    case "-family": font.Family = value; break;
                    case "-size": font.Size = ParseInt(value); break;
                    case "-weight": font.Bold = (value == "bold"); break;
                    case "-slant": font.Italic = (value == "italic"); break;
                    case "-underline": font.Underline = IsTrue(value); break;
                    case "-overstrike": font.Overstrike = IsTrue(value); break;
                    default: break; // accept-and-ignore unknown options
                }
            }
        }
        else
        {
            // List form: family ?size? ?style style ...?
            font.Family = words[0];
            int index = 1;
            if (words.Count > 1)
            {
                int size;
                if (int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                {
                    font.Size = size;
                    index = 2;
                }
            }
            for (; index < words.Count; index++)
            {
                switch (words[index])
                {
                    case "bold": font.Bold = true; break;
                    case "normal": font.Bold = false; break;
                    case "italic": font.Italic = true; break;
                    case "roman": font.Italic = false; break;
                    case "underline": font.Underline = true; break;
                    case "overstrike": font.Overstrike = true; break;
                    default: break; // accept-and-ignore unknown styles
                }
            }
        }
        return font;
    }

    /// <summary>
    /// Materializes the Skia font for a Tk font — the object the PAINTER
    /// draws with and every measurement is taken from.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>A configured <see cref="SKFont"/> (typefaces are cached).</returns>
    public SKFont GetSkFont(TkFont font)
    {
        if (font == null) { throw new ArgumentNullException(nameof(font)); }
        return MakeSkFont(GetChain(font).Primary, font);
    }

    /// <summary>
    /// Builds the Skia font for one face at a Tk font's size, applying the
    /// settings a PACKAGED face needs.
    /// </summary>
    /// <param name="typeface">The face to build on.</param>
    /// <param name="font">The Tk font supplying size and style.</param>
    /// <returns>The configured font.</returns>
    private SKFont MakeSkFont(SKTypeface typeface, TkFont font)
    {
        var skFont = new SKFont(typeface, PixelSize(font));

        if (_packagedFaces.Contains(typeface))
        {
            // Hinting OFF for a packaged font. Two reasons, both load-bearing:
            //
            //  * IT IS THE ONLY WAY A MONO FACE STAYS MONOSPACED. FreeType (Linux)
            //    hints each glyph to its own whole pixel, and only forces one
            //    uniform advance when fontconfig tags the face as mono — which
            //    never happens for a face we load from a FILE. Hinted, packaged
            //    Noto Sans Mono measures '0'=8 but 'M'=9, 'i'=7, '@'=10, and the
            //    column grid the text widget is built on collapses. Unhinted,
            //    every glyph is the design advance and the face is uniform again.
            //  * It also makes the platforms agree. DirectWrite/CoreText never
            //    hint, so switching FreeType off puts all three on the font's own
            //    design advances instead of one platform's rasterizer opinion.
            skFont.Hinting = SKFontHinting.None;
            skFont.Subpixel = false;
            skFont.LinearMetrics = false;

            // The packaged faces are variable fonts with a weight axis but NO
            // italic axis, so slant has to be synthesised. This shears the
            // outlines only — advances are untouched, so the grid survives.
            if (font.Italic && !typeface.IsItalic) { skFont.SkewX = SyntheticObliqueSkew; }
        }

        return skFont;
    }

    /// <summary>
    /// Resolves a mapped family name to a concrete Skia typeface, preferring the
    /// font SHIPPED WITH THE TOOLKIT over anything installed on the host.
    /// <para>
    /// A toolkit that measures its own layout must not gamble on the machine's
    /// font set: the same Tcl program has to produce the same geometry on a
    /// developer's Windows box and in a stripped Linux container. So
    /// every CSS generic is served from a packaged face (see
    /// <see cref="PackagedFaces"/>), never from the host.
    /// </para>
    /// <para>
    /// The system-font path below is a LAST RESORT for a deployment where the
    /// font files did not make it next to the assembly — a broken install is
    /// better served by a wrong-looking font than by no text at all. It is dead
    /// code in a correct deployment; <c>Metrics(...).IsFixed</c> reports what the
    /// face actually does, so a caller can still tell.
    /// </para>
    /// <para>
    /// <c>sans-serif</c> and <c>serif</c> still fall through to the host: the
    /// fonts package carries monospace families only. Closing that gap needs a
    /// packaged sans and serif face as well.
    /// </para>
    /// </summary>
    /// <param name="family">The mapped family name (a CSS generic or a real family).</param>
    /// <param name="style">The requested weight/slant.</param>
    /// <param name="bold">True when a bold weight was asked for.</param>
    /// <returns>A typeface (never null).</returns>
    private SKTypeface ResolveTypeface(string family, SKFontStyle style, bool bold)
    {
        PackagedFace face;
        bool isGeneric = PackagedFaces.TryGetValue(family, out face);

        if (isGeneric)
        {
            SKTypeface packaged = LoadPackagedFace(face, bold);
            if (packaged != null) { return packaged; }
            if (!AllowSystemFontFallback) { throw MissingPackagedFont(family, face); }
        }
        else if (!AllowSystemFontFallback)
        {
            // A family the toolkit does not ship — an explicit "{Segoe UI 12}", or
            // a name that means nothing. Without the opt-in we must not go looking
            // on the host, so serve it from the packaged sans face.
            PackagedFace sans = PackagedFaces[DefaultGenericFamily];
            SKTypeface substitute = LoadPackagedFace(sans, bold);
            if (substitute != null) { return substitute; }
            throw MissingPackagedFont(DefaultGenericFamily, sans);
        }

        SKTypeface typeface = SKTypeface.FromFamilyName(family, style);

        if (string.Equals(family, "monospace", StringComparison.Ordinal)
                && (typeface == null || !typeface.IsFixedPitch))
        {
            foreach (string candidate in MonospaceFallbacks)
            {
                SKTypeface concrete = SKTypeface.FromFamilyName(candidate, style);
                if (concrete != null && concrete.IsFixedPitch)
                {
                    return concrete;
                }
            }
        }

        return typeface ?? SKTypeface.Default;
    }

    /// <summary>
    /// Loads one face from the fonts package laid down beside the assembly,
    /// applying <paramref name="bold"/> through the variable font's weight axis.
    /// Returns <see langword="null"/> when the file is not there.
    /// </summary>
    /// <param name="packaged">The packaged face to load.</param>
    /// <param name="bold">True for the bold weight.</param>
    /// <returns>The face, or null when the packaged font is unavailable.</returns>
    private SKTypeface LoadPackagedFace(PackagedFace packaged, bool bold)
    {
        string path = FindPackagedFont(packaged);
        if (path == null) { return null; }

        SKTypeface face = SKTypeface.FromFile(path);
        if (face == null) { return null; }

        // Weight comes off the "wght" axis rather than from a second file, because
        // the static weight instances are pruned on platforms without font-manifest
        // support. Both weights are pinned explicitly — the face's own default is
        // not necessarily 400 (Merriweather's is 300) — and for the monospace face
        // the advance is identical at every weight, so the column grid is safe.
        if (HasWeightAxis(face))
        {
            var arguments = new SKFontArguments
            {
                VariationDesignPosition = new[]
                {
                    new SKFontVariationPositionCoordinate
                    {
                        Axis = WeightAxisTag,
                        Value = bold ? BoldWeight : NormalWeight,
                    },
                },
            };
            SKTypeface adjusted = face.Clone(arguments);
            if (adjusted != null) { face = adjusted; }
        }

        _packagedFaces.Add(face);
        return face;
    }

    /// <summary>
    /// Builds the failure for a packaged font file that is not where it should be.
    /// Deliberately loud: a silent substitution would mean the same program lays
    /// out differently on the next machine, which is the whole thing the packaged
    /// fonts exist to prevent.
    /// </summary>
    /// <param name="family">The generic being resolved.</param>
    /// <param name="face">The packaged face that could not be found.</param>
    /// <returns>The exception to throw.</returns>
    private Exception MissingPackagedFont(string family, PackagedFace face)
    {
        string expected = Path.Combine(
                PackagedFontBaseDirectory ?? AppContext.BaseDirectory,
                face.Folder, "Fonts", face.File);
        var probed = new List<string>(_fontDirectories) { expected };

        return new InvalidOperationException(
                "TkCanvas could not load its packaged font for \"" + family + "\": "
                + face.File + " was not found (looked in "
                + string.Join(", ", probed) + "). The font ships in the "
                + face.Folder + " nuget package and is normally copied beside the "
                + "assembly by the CodeBrix.Platform asset pipeline or by the "
                + "_CodeBrixTkCanvasCollectPackageFonts build target; check that the "
                + "package is referenced and that the copy was not disabled with "
                + "CodeBrixTkCanvasDisableFontCopy. Call AddFontDirectory(...) if the "
                + "fonts live elsewhere, or set AllowSystemFontFallback = true to "
                + "deliberately allow host fonts instead.");
    }

    /// <summary>Reports whether a face exposes the <c>wght</c> variation axis.</summary>
    /// <param name="face">The face to inspect.</param>
    /// <returns>True when the weight axis is present.</returns>
    private static bool HasWeightAxis(SKTypeface face)
    {
        SKFontVariationAxis[] axes = face.VariationDesignParameters;
        if (axes == null) { return false; }

        foreach (SKFontVariationAxis axis in axes)
        {
            if (axis.Tag == WeightAxisTag) { return true; }
        }
        return false;
    }

    /// <summary>
    /// Locates a file inside the fonts package folder. The build target
    /// <c>_CodeBrixTkCanvasCollectPackageFonts</c> (and, in a CodeBrix.Platform
    /// app, the platform asset pipeline) puts them beside the assembly under
    /// <c>CodeBrix.Platform.Fonts.&lt;Name&gt;/Fonts/</c>; a host with a different
    /// layout can add its own location through <see cref="AddFontDirectory"/>.
    /// </summary>
    /// <param name="face">The packaged face to locate.</param>
    /// <returns>The full path, or null when it cannot be found.</returns>
    private string FindPackagedFont(PackagedFace face)
    {
        foreach (string directory in _fontDirectories)
        {
            string candidate = Path.Combine(directory, face.File);
            if (File.Exists(candidate)) { return candidate; }
        }

        string packaged = Path.Combine(
                PackagedFontBaseDirectory ?? AppContext.BaseDirectory,
                face.Folder, "Fonts", face.File);
        return File.Exists(packaged) ? packaged : null;
    }

    /// <summary>
    /// Adds a directory to probe for packaged font files, ahead of the default
    /// location beside the assembly. For hosts that lay the fonts out elsewhere.
    /// Call before anything renders; faces already resolved are not re-resolved.
    /// </summary>
    /// <param name="directory">The directory holding the .ttf files.</param>
    public void AddFontDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Value cannot be null or blank.", nameof(directory));
        }

        if (!_fontDirectories.Contains(directory, StringComparer.Ordinal))
        {
            _fontDirectories.Add(directory);
        }
    }

    /// <summary>
    /// Concrete monospace families to try, in order, when the CSS generic
    /// <c>monospace</c> does not resolve to a fixed-pitch face (Windows/macOS).
    /// Each is verified as actually installed via
    /// <see cref="SKTypeface.IsFixedPitch"/> before use, so families absent on a
    /// given host are skipped rather than silently substituted. The list spans
    /// the common Windows, macOS, and Linux monospace fonts; a host with none of
    /// them falls through to Skia's default face.
    /// </summary>
    private static readonly string[] MonospaceFallbacks =
    {
        "Consolas",           // Windows (Vista+)
        "Cascadia Mono",      // Windows Terminal / VS
        "Courier New",        // Windows, macOS
        "Lucida Console",     // Windows
        "Menlo",              // macOS (10.6+)
        "Monaco",             // macOS
        "SF Mono",            // macOS
        "DejaVu Sans Mono",   // Linux
        "Liberation Mono",    // Linux
        "Noto Sans Mono",     // Linux
        "FreeMono",           // Linux
    };

    /// <summary>
    /// Measures the advance width of <paramref name="text"/> — the analogue
    /// of <c>font measure FONT text</c>, in pixels, from the same
    /// <see cref="SKFont"/> the painter uses.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The advance width in pixels (whole-pixel glyph advances).</returns>
    public int Measure(TkFont font, string text)
    {
        if (string.IsNullOrEmpty(text)) { return 0; }

        FontChain chain = GetChain(font);
        using (SKFont skFont = MakeSkFont(chain.Primary, font))
        {
            List<TextRun> runs = ResolveRuns(chain, text);
            if (runs == null) { return MeasureOnGrid(skFont, text); }

            int cell = FixedCell(skFont);
            int total = 0;
            foreach (TextRun run in runs)
            {
                string slice = text.Substring(run.Start, run.Length);
                if (cell > 0)
                {
                    // A fixed-pitch primary keeps its column grid even where the
                    // glyphs come from a proportional fallback: a Georgian line in
                    // a text widget still lands on the same columns as the Latin
                    // one above it, so xview/see/index @x,y stay truthful.
                    total += CountCodepoints(slice) * cell;
                    continue;
                }

                using (SKFont runFont = MakeSkFont(chain.FaceAt(run.Face), font))
                {
                    total += MeasureOnGrid(runFont, slice);
                }
            }
            return total;
        }
    }

    /// <summary>
    /// Reports whether the font can actually DRAW a codepoint — whether the
    /// primary face or any face in its packaged fallback chain carries a glyph
    /// for it. False means it will render as a tofu box.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <param name="codepoint">The Unicode codepoint.</param>
    /// <returns>True when some face in the chain has the glyph.</returns>
    public bool HasGlyph(TkFont font, int codepoint)
    {
        FontChain chain = GetChain(font);
        if (Covers(chain.Primary, codepoint)) { return true; }

        foreach (SKTypeface fallback in chain.Fallbacks)
        {
            if (Covers(fallback, codepoint)) { return true; }
        }
        return false;
    }

    /// <summary>
    /// Resolves (and caches) the face chain behind a Tk font.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>The chain; never null.</returns>
    private FontChain GetChain(TkFont font)
    {
        if (font == null) { throw new ArgumentNullException(nameof(font)); }

        string family = MapFamily(font.Family);
        string key = family + "|" + (font.Bold ? "b" : "-") + (font.Italic ? "i" : "-");
        FontChain chain;
        if (_chains.TryGetValue(key, out chain)) { return chain; }

        var style = new SKFontStyle(
                font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        SKTypeface primary = ResolveTypeface(family, style, font.Bold);

        PackagedFace[] chainSpec;
        var fallbacks = new List<SKTypeface>();
        if (FallbackChains.TryGetValue(family, out chainSpec))
        {
            foreach (PackagedFace spec in chainSpec)
            {
                // Optional coverage: a missing fallback degrades to tofu rather
                // than taking the application down.
                SKTypeface face = LoadPackagedFace(spec, font.Bold);
                if (face != null) { fallbacks.Add(face); }
            }
        }

        chain = new FontChain(primary, fallbacks.ToArray());
        _chains[key] = chain;
        return chain;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into runs by which face of the chain can
    /// draw each codepoint. Returns <see langword="null"/> when the primary
    /// serves every codepoint — the overwhelmingly common case, which then costs
    /// no extra allocation or font switching.
    /// </summary>
    /// <param name="chain">The resolved chain.</param>
    /// <param name="text">The text to split.</param>
    /// <returns>The runs, or null when no fallback is needed.</returns>
    private List<TextRun> ResolveRuns(FontChain chain, string text)
    {
        if (chain.Fallbacks.Length == 0) { return null; }

        List<TextRun> runs = null;
        bool usedFallback = false;
        int runFace = 0;
        int runStart = 0;
        int index = 0;

        while (index < text.Length)
        {
            int length = CodepointLength(text, index);
            int face = SelectFace(chain, Codepoint(text, index, length));
            if (face != 0) { usedFallback = true; }

            if (runs == null)
            {
                runs = new List<TextRun>();
                runFace = face;
            }
            else if (face != runFace)
            {
                runs.Add(new TextRun(runFace, runStart, index - runStart));
                runFace = face;
                runStart = index;
            }

            index += length;
        }

        if (runs == null) { return null; }
        runs.Add(new TextRun(runFace, runStart, text.Length - runStart));
        return usedFallback ? runs : null;
    }

    /// <summary>
    /// Picks the first face in the chain that carries a codepoint. Falls back to
    /// the primary (index 0) when nothing does, so the text renders as tofu
    /// rather than disappearing.
    /// </summary>
    /// <param name="chain">The resolved chain.</param>
    /// <param name="codepoint">The Unicode codepoint.</param>
    /// <returns>The chain index; 0 is the primary.</returns>
    private int SelectFace(FontChain chain, int codepoint)
    {
        if (Covers(chain.Primary, codepoint)) { return 0; }

        for (int i = 0; i < chain.Fallbacks.Length; i++)
        {
            if (Covers(chain.Fallbacks[i], codepoint)) { return i + 1; }
        }
        return 0;
    }

    /// <summary>
    /// Reports whether a face carries a codepoint. Coverage does not depend on
    /// size, so one probe font per face is cached and reused.
    /// </summary>
    /// <param name="typeface">The face to test.</param>
    /// <param name="codepoint">The Unicode codepoint.</param>
    /// <returns>True when the face has a glyph for it.</returns>
    private bool Covers(SKTypeface typeface, int codepoint)
    {
        SKFont probe;
        if (!_coverage.TryGetValue(typeface, out probe))
        {
            probe = new SKFont(typeface, CoverageProbeSize);
            _coverage[typeface] = probe;
        }
        return probe.ContainsGlyph(codepoint);
    }

    /// <summary>The UTF-16 length of the codepoint at an index (1, or 2 for a pair).</summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The offset.</param>
    /// <returns>1 or 2.</returns>
    private static int CodepointLength(string text, int index)
    {
        return (char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1])) ? 2 : 1;
    }

    /// <summary>The codepoint at an index, tolerating an unpaired surrogate.</summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The offset.</param>
    /// <param name="length">The codepoint length from <see cref="CodepointLength"/>.</param>
    /// <returns>The Unicode codepoint.</returns>
    private static int Codepoint(string text, int index, int length)
    {
        return (length == 2) ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
    }

    /// <summary>
    /// The single column width of a fixed-pitch font, or 0 when the font is
    /// proportional and glyphs should keep their own advances.
    /// </summary>
    /// <param name="skFont">The primary font.</param>
    /// <returns>The cell width in pixels, or 0.</returns>
    private static int FixedCell(SKFont skFont)
    {
        return IsFixedPitch(skFont) ? GridAdvance(skFont.MeasureText("0")) : 0;
    }

    /// <summary>Counts Unicode codepoints (surrogate pairs count once).</summary>
    /// <param name="text">The text to count.</param>
    /// <returns>The number of codepoints.</returns>
    private static int CountCodepoints(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i += CodepointLength(text, i)) { count++; }
        return count;
    }

    /// <summary>
    /// Rounds one Skia glyph advance up to Tk's whole-pixel grid.
    /// <para>
    /// Tk's font layer measures in WHOLE PIXELS on every platform, and the
    /// toolkit assumes measurement is additive — a caret is placed by
    /// measuring the prefix, a display line's width is the sum of its runs,
    /// and a text widget's <c>-width N</c> is <c>N * Measure("0")</c>. Skia
    /// only honours that when the backend hands back integral advances,
    /// which is true of FreeType (Linux) but NOT of DirectWrite (Windows) or
    /// CoreText (macOS): Consolas at 10pt advances 7.3307px there, so
    /// <c>Measure(40 chars)</c> came to 294 while <c>40 * Measure("0")</c>
    /// came to 320 and every column-derived position drifted.
    /// </para>
    /// <para>
    /// Rounding each advance UP (rather than to nearest) matches what the
    /// single-character path always did, and never reports a width narrower
    /// than the ink it must hold. On a backend that already yields integral
    /// advances this is the identity function, so Linux geometry is
    /// unchanged to the pixel.
    /// </para>
    /// </summary>
    /// <param name="advance">The Skia advance, in pixels.</param>
    /// <returns>The advance on the whole-pixel grid.</returns>
    internal static int GridAdvance(float advance)
    {
        if (!(advance > 0)) { return 0; }
        return (int)Math.Ceiling(advance);
    }

    /// <summary>
    /// Sums <paramref name="text"/>'s glyph advances on the whole-pixel grid
    /// (see <see cref="GridAdvance"/>) — the measurement every widget, the
    /// painter, and <c>font measure</c> share.
    /// </summary>
    /// <param name="skFont">The Skia font to measure with.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The advance width in pixels.</returns>
    internal static int MeasureOnGrid(SKFont skFont, string text)
    {
        if (string.IsNullOrEmpty(text)) { return 0; }

        ushort[] glyphs = skFont.GetGlyphs(text);
        if (glyphs.Length == 0) { return 0; }

        float[] advances = skFont.GetGlyphWidths(glyphs);
        int total = 0;
        for (int i = 0; i < advances.Length; i++)
        {
            total += GridAdvance(advances[i]);
        }
        return total;
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its glyphs pinned to the same
    /// whole-pixel grid <see cref="Measure"/> reports, left-aligned with the
    /// first glyph at <paramref name="x"/>.
    /// <para>
    /// The painter MUST go through here rather than
    /// <see cref="SKCanvas.DrawText(string, float, float, SKTextAlign, SKFont, SKPaint)"/>:
    /// Skia would lay the run out on its own fractional advances, and on
    /// Windows/macOS the glyphs would then walk away from the measured
    /// positions — by 27px across a 40-character line in Consolas — putting
    /// the caret, the selection, and <c>index @x,y</c> in the wrong place.
    /// </para>
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">The left edge of the first glyph.</param>
    /// <param name="baseline">The baseline to sit the glyphs on.</param>
    /// <param name="font">The Tk font (its chain supplies any fallback faces).</param>
    /// <param name="paint">The paint to draw with.</param>
    public void DrawText(SKCanvas canvas, string text, float x, float baseline,
            TkFont font, SKPaint paint)
    {
        if (canvas == null) { throw new ArgumentNullException(nameof(canvas)); }
        if (string.IsNullOrEmpty(text)) { return; }

        FontChain chain = GetChain(font);
        using (SKFont skFont = MakeSkFont(chain.Primary, font))
        {
            List<TextRun> runs = ResolveRuns(chain, text);
            if (runs == null)
            {
                DrawTextOnGrid(canvas, text, x, baseline, skFont, paint);
                return;
            }

            int cell = FixedCell(skFont);
            float pen = x;
            foreach (TextRun run in runs)
            {
                string slice = text.Substring(run.Start, run.Length);
                if (run.Face == 0)
                {
                    DrawTextOnGrid(canvas, slice, pen, baseline, skFont, paint);
                    pen += (cell > 0) ? CountCodepoints(slice) * cell : MeasureOnGrid(skFont, slice);
                    continue;
                }

                using (SKFont runFont = MakeSkFont(chain.FaceAt(run.Face), font))
                {
                    // A fixed-pitch primary keeps its column grid: fallback glyphs
                    // are laid on the SAME cell width so a Georgian line still sits
                    // on the columns the Latin line above it uses.
                    if (cell > 0)
                    {
                        DrawTextOnCells(canvas, slice, pen, baseline, runFont, paint, cell);
                        pen += CountCodepoints(slice) * cell;
                    }
                    else
                    {
                        DrawTextOnGrid(canvas, slice, pen, baseline, runFont, paint);
                        pen += MeasureOnGrid(runFont, slice);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a run with every codepoint pinned to one fixed cell width, for a
    /// fallback face inside a monospace font.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="text">The run text.</param>
    /// <param name="x">The left edge of the first cell.</param>
    /// <param name="baseline">The baseline.</param>
    /// <param name="skFont">The fallback font.</param>
    /// <param name="paint">The paint.</param>
    /// <param name="cell">The column width in pixels.</param>
    private static void DrawTextOnCells(SKCanvas canvas, string text, float x, float baseline,
            SKFont skFont, SKPaint paint, int cell)
    {
        int index = 0;
        float pen = x;
        while (index < text.Length)
        {
            int length = CodepointLength(text, index);
            DrawTextOnGrid(canvas, text.Substring(index, length), pen, baseline, skFont, paint);
            pen += cell;
            index += length;
        }
    }

    /// <summary>
    /// Draws one run with a single face, glyphs pinned to the whole-pixel grid
    /// <see cref="Measure"/> reports.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">The left edge of the first glyph.</param>
    /// <param name="baseline">The baseline to sit the glyphs on.</param>
    /// <param name="skFont">The Skia font to draw with.</param>
    /// <param name="paint">The paint to draw with.</param>
    private static void DrawTextOnGrid(SKCanvas canvas, string text, float x, float baseline,
            SKFont skFont, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        ushort[] glyphs = skFont.GetGlyphs(text);
        if (glyphs.Length == 0) { return; }

        float[] advances = skFont.GetGlyphWidths(glyphs);
        var positions = new float[glyphs.Length];
        float pen = x;
        for (int i = 0; i < glyphs.Length; i++)
        {
            positions[i] = pen;
            pen += GridAdvance(advances[i]);
        }

        using (SKTextBlob blob = SKTextBlob.CreateHorizontal(text, skFont, positions, baseline))
        {
            if (blob != null) { canvas.DrawText(blob, 0, 0, paint); }
        }
    }

    /// <summary>
    /// Reports the vertical metrics — the analogue of
    /// <c>font metrics FONT</c>.
    /// </summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>Ascent, descent, linespace, and fixed-pitch flag.</returns>
    public FontMetrics Metrics(TkFont font)
    {
        using (SKFont skFont = GetSkFont(font))
        {
            SKFontMetrics metrics;
            skFont.GetFontMetrics(out metrics);
            int ascent = (int)Math.Ceiling(-metrics.Ascent);
            int descent = (int)Math.Ceiling(metrics.Descent);
            return new FontMetrics(ascent, descent, IsFixedPitch(skFont));
        }
    }

    /// <summary>
    /// Decides whether a face really lays out on one column width, by MEASURING a
    /// spread of narrow and wide glyphs rather than trusting
    /// <see cref="SKTypeface.IsFixedPitch"/>.
    /// <para>
    /// That flag cannot be relied on: the very same Noto Sans Mono reports
    /// fixed-pitch when fontconfig hands it over on Linux and NOT fixed-pitch when
    /// it is loaded from a file — and callers here care about the behaviour, not
    /// the metadata bit.
    /// </para>
    /// </summary>
    /// <param name="skFont">The font to probe.</param>
    /// <returns>True when every probed glyph shares one advance.</returns>
    private static bool IsFixedPitch(SKFont skFont)
    {
        float[] advances = skFont.GetGlyphWidths(skFont.GetGlyphs(FixedPitchProbe));
        if (advances.Length == 0) { return false; }

        for (int i = 1; i < advances.Length; i++)
        {
            if (Math.Abs(advances[i] - advances[0]) > 0.001f) { return false; }
        }
        return true;
    }

    /// <summary>The pixel size of a Tk font (positive size = points, negative = pixels).</summary>
    /// <param name="font">The Tk font.</param>
    /// <returns>The size in pixels.</returns>
    public float PixelSize(TkFont font)
    {
        int size = (font.Size != 0) ? font.Size : 10;
        if (size < 0) { return -size; }
        return (float)(size * PixelsPerPoint);
    }

    private static string MapFamily(string family)
    {
        if (string.IsNullOrEmpty(family) || family == "TkDefault") { return "sans-serif"; }
        switch (family.ToLowerInvariant())
        {
            case "helvetica": case "arial": return "sans-serif";
            case "courier": case "courier new": return "monospace";
            case "times": case "times new roman": return "serif";
            default: return family;
        }
    }

    private static bool IsTrue(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": return true;
            default: return false;
        }
    }

    private static int ParseInt(string value)
    {
        int parsed;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    /// <summary>
    /// Splits a Tcl list the small way font descriptors need: whitespace
    /// separation with brace grouping (<c>{DejaVu Sans} 12 bold</c>).
    /// </summary>
    private static List<string> SplitTclList(string text)
    {
        var words = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) { i++; }
            if (i >= text.Length) { break; }

            if (text[i] == '{')
            {
                int depth = 1;
                int start = ++i;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '{') { depth++; }
                    else if (text[i] == '}') { depth--; }
                    if (depth > 0) { i++; }
                }
                words.Add(text.Substring(start, i - start));
                if (i < text.Length) { i++; } // consume the closing brace
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) { i++; }
                words.Add(text.Substring(start, i - start));
            }
        }
        return words;
    }
}
