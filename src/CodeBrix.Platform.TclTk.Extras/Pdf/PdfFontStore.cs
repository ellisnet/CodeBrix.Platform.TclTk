using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.PdfDocuments.Fonts;

namespace CodeBrix.Platform.TclTk.Extras.Pdf;

/// <summary>
/// Process-wide store backing <c>pdf4tcl::loadBaseTrueTypeFont</c> and
/// <c>pdf4tcl::createFont</c>: base TrueType font bytes loaded from disk, and the
/// created font names that scripts pass to <c>setFont</c>. Created fonts are served
/// to CodeBrix.PdfDocuments through <see cref="MetaFontResolver"/> registrations
/// (the font-resolver subsystem is global/static by design, so this store is too).
/// </summary>
internal static class PdfFontStore
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, byte[]> BaseFonts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private static readonly HashSet<string> CreatedFonts = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Loads the TrueType file at <paramref name="path"/> as base font <paramref name="baseFontName"/>.</summary>
    public static void LoadBaseTrueTypeFont(string baseFontName, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        lock (SyncRoot)
        {
            BaseFonts[baseFontName] = bytes;
        }
    }

    /// <summary>
    /// Creates usable font <paramref name="fontName"/> from previously loaded base font
    /// <paramref name="baseFontName"/> and registers it with the PDF font resolver.
    /// The pdf4tcl encoding/subset argument is not needed here: CodeBrix.PdfDocuments
    /// embeds fonts with Unicode encoding and subsets automatically.
    /// </summary>
    /// <returns>False when the base font has not been loaded.</returns>
    public static bool TryCreateFont(string baseFontName, string fontName)
    {
        byte[] bytes;
        lock (SyncRoot)
        {
            if (!BaseFonts.TryGetValue(baseFontName, out bytes)) { return false; }
            CreatedFonts.Add(fontName);
        }

        // NOTE: MetaFontResolver ignores duplicate registrations, so re-creating a
        // font NAME with different bytes keeps the first registration. pdf4tcl
        // consumers (DRAKON included) use a fresh name per creation, so this does
        // not arise in practice.
        MetaFontResolver.Instance.RegisterFontResolver(
            fontName, new StoredFontResolver(fontName, bytes));
        return true;
    }

    /// <summary>True when <paramref name="fontName"/> was created via <see cref="TryCreateFont"/>.</summary>
    public static bool IsCreatedFont(string fontName)
    {
        lock (SyncRoot)
        {
            return CreatedFonts.Contains(fontName);
        }
    }

    /// <summary>Serves one created font's bytes to the PDF library by family/face name.</summary>
    private sealed class StoredFontResolver : IFontResolver
    {
        private readonly string _name;
        private readonly byte[] _bytes;

        public StoredFontResolver(string name, byte[] bytes)
        {
            _name = name;
            _bytes = bytes;
        }

        public string DefaultFontName => _name;

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => string.Equals(familyName, _name, StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(_name)
                : null;

        public byte[] GetFont(string faceName)
            => string.Equals(faceName, _name, StringComparison.OrdinalIgnoreCase) ? _bytes : null;
    }
}
