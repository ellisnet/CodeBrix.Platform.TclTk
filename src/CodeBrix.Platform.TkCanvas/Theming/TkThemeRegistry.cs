using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.TkCanvas.Theming;

/// <summary>
/// The named-theme registry: maps scheme names (case-insensitive) to theme
/// factories. Ships the classic default, the legacy bisque palette, and the
/// built-in schemes from <see cref="BuiltinThemes"/>; applications may
/// register their own. <c>Default</c> is accepted as an alias for
/// <c>Classic</c>, and the standard ttk theme names (<c>default</c>,
/// <c>clam</c>, <c>alt</c>, <c>classic</c>) all resolve to the classic look
/// so ported Tcl code that selects them keeps working.
/// </summary>
public static class TkThemeRegistry
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, Func<TkTheme>> Factories =
            new Dictionary<string, Func<TkTheme>>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Default", "Classic" },
                { "Clam", "Classic" },
                { "Alt", "Classic" },
            };

    static TkThemeRegistry()
    {
        Factories["Classic"] = TkTheme.CreateClassic;
        Factories["Bisque"] = TkTheme.CreateBisque;
        BuiltinThemes.RegisterAll();
    }

    /// <summary>Registers (or replaces) a named theme factory.</summary>
    /// <param name="name">The scheme name.</param>
    /// <param name="factory">Creates a fresh theme instance per call.</param>
    public static void Register(string name, Func<TkTheme> factory)
    {
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("empty theme name", nameof(name)); }
        if (factory == null) { throw new ArgumentNullException(nameof(factory)); }
        lock (SyncRoot)
        {
            Factories[name] = factory;
        }
    }

    /// <summary>
    /// Creates a fresh instance of a named theme, resolving aliases.
    /// </summary>
    /// <param name="name">The scheme name (case-insensitive).</param>
    /// <returns>The theme, or null when the name is not registered.</returns>
    public static TkTheme TryCreate(string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }
        lock (SyncRoot)
        {
            string target;
            if (Aliases.TryGetValue(name, out target)) { name = target; }
            Func<TkTheme> factory;
            return Factories.TryGetValue(name, out factory) ? factory() : null;
        }
    }

    /// <summary>The registered scheme names, sorted (aliases not included).</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (SyncRoot)
            {
                return Factories.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }
}
