/*
 * ScriptParseCache.cs --
 *
 * The opt-in, interpreter-lifetime cache of parsed script commands used by
 * the engine when Interpreter.CacheParsedScripts is enabled.  Original code
 * for CodeBrix.Platform.TclTk (not ported from the upstream project).
 *
 * Copyright (c) 2026 Jeremy Ellis and contributors.
 * Licensed under the same terms as the rest of this library.
 */

using System;
using System.Collections.Generic;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk._Components.Private
{
    /// <summary>
    /// This class holds one fully parsed (tokenized) command of a cached
    /// script: its immutable parser-state snapshot plus, per word, either a
    /// pre-evaluated argument (for a static word — a single text token with
    /// no expansion, whose value is identical on every execution) or the
    /// index of the word's token (for a dynamic word, which must be
    /// re-substituted on every execution).  Everything here must be treated
    /// as strictly read-only; it may be replayed concurrently and
    /// re-entrantly.  Sharing one <see cref="Argument" /> instance across
    /// many argument lists follows the same rule the engine's own argument
    /// cache already relies on.
    /// </summary>
    internal sealed class CachedScriptCommand
    {
        private readonly IParseState parseState;
        private readonly int[] wordTokenIndexes;
        private readonly Argument[] staticWords;

        /// <summary>
        /// Constructs an instance of this class.
        /// </summary>
        /// <param name="parseState">
        /// The immutable parser-state snapshot for the command.
        /// </param>
        /// <param name="wordTokenIndexes">
        /// The token index of each word, or null when the word walk could
        /// not be precomputed (the engine then falls back to walking the
        /// tokens itself).
        /// </param>
        /// <param name="staticWords">
        /// The pre-evaluated argument for each static word (null entries
        /// mark dynamic words); this is null exactly when
        /// <paramref name="wordTokenIndexes" /> is null.
        /// </param>
        internal CachedScriptCommand(
            IParseState parseState,
            int[] wordTokenIndexes,
            Argument[] staticWords
            )
        {
            this.parseState = parseState;
            this.wordTokenIndexes = wordTokenIndexes;
            this.staticWords = staticWords;
        }

        /// <summary>
        /// Gets the immutable parser-state snapshot for the command.
        /// </summary>
        internal IParseState ParseState
        {
            get { return parseState; }
        }

        /// <summary>
        /// Gets the token index of each word, or null when unavailable.
        /// </summary>
        internal int[] WordTokenIndexes
        {
            get { return wordTokenIndexes; }
        }

        /// <summary>
        /// Gets the pre-evaluated argument for each static word, with null
        /// entries for dynamic words; null when unavailable.
        /// </summary>
        internal Argument[] StaticWords
        {
            get { return staticWords; }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This class holds the fully parsed (tokenized) commands of one script
    /// text, in execution order.  The engine replays these instead of
    /// re-parsing the script text when the script parse cache is enabled.
    /// When the script could not be fully parsed (i.e. it contains a
    /// deterministic parse error partway through), only the commands before
    /// the error are present; the engine resumes normal (live) parsing
    /// after replaying them, which reproduces the original error semantics
    /// exactly, if and when execution actually reaches that point.
    /// </summary>
    internal sealed class CachedScriptCommands
    {
        /// <summary>
        /// The cached commands, in execution order.  This array (and every
        /// element in it) must be treated as strictly read-only; it may be
        /// replayed concurrently and re-entrantly.
        /// </summary>
        private readonly CachedScriptCommand[] commands;

        /// <summary>
        /// Constructs an instance of this class from the specified cached
        /// commands.
        /// </summary>
        /// <param name="commands">
        /// The cached commands, one per parsed command.
        /// </param>
        internal CachedScriptCommands(
            CachedScriptCommand[] commands
            )
        {
            this.commands = commands;
        }

        /// <summary>
        /// Gets the cached commands, one per parsed command, in execution
        /// order.
        /// </summary>
        internal CachedScriptCommand[] Commands
        {
            get { return commands; }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This structure is the lookup key for the script parse cache.  Two
    /// evaluations may share a cache entry only when every input that can
    /// influence the parse result (or the source locations recorded in it)
    /// is identical: the script text value, the range within it, whether the
    /// parse is bracket-terminated (nested), the substitution flags, and the
    /// originating file name and starting line number.
    /// </summary>
    internal struct ScriptParseCacheKey : IEquatable<ScriptParseCacheKey>
    {
        private readonly string text;
        private readonly int startIndex;
        private readonly int characters;
        private readonly bool nested;
        private readonly SubstitutionFlags substitutionFlags;
        private readonly string fileName;
        private readonly int currentLine;

        /// <summary>
        /// Constructs an instance of this structure from the parse inputs.
        /// </summary>
        /// <param name="text">
        /// The script text being evaluated.
        /// </param>
        /// <param name="startIndex">
        /// The index, within the text, where evaluation starts.
        /// </param>
        /// <param name="characters">
        /// The number of characters available for parsing.
        /// </param>
        /// <param name="nested">
        /// Non-zero if the parse is close-bracket terminated.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect while parsing.
        /// </param>
        /// <param name="fileName">
        /// The name of the file the script originated from, if any.
        /// </param>
        /// <param name="currentLine">
        /// The line number the script starts on.
        /// </param>
        internal ScriptParseCacheKey(
            string text,
            int startIndex,
            int characters,
            bool nested,
            SubstitutionFlags substitutionFlags,
            string fileName,
            int currentLine
            )
        {
            this.text = text;
            this.startIndex = startIndex;
            this.characters = characters;
            this.nested = nested;
            this.substitutionFlags = substitutionFlags;
            this.fileName = fileName;
            this.currentLine = currentLine;
        }

        /// <summary>
        /// Determines whether this key is equal to another key.
        /// </summary>
        /// <param name="other">
        /// The other key to compare against.
        /// </param>
        /// <returns>
        /// True if the keys are equal; otherwise, false.
        /// </returns>
        public bool Equals(
            ScriptParseCacheKey other
            )
        {
            return (startIndex == other.startIndex) &&
                (characters == other.characters) &&
                (nested == other.nested) &&
                (substitutionFlags == other.substitutionFlags) &&
                (currentLine == other.currentLine) &&
                String.Equals(fileName, other.fileName, StringComparison.Ordinal) &&
                String.Equals(text, other.text, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether this key is equal to another object.
        /// </summary>
        /// <param name="obj">
        /// The other object to compare against.
        /// </param>
        /// <returns>
        /// True if the other object is an equal key; otherwise, false.
        /// </returns>
        public override bool Equals(
            object obj
            )
        {
            return (obj is ScriptParseCacheKey) &&
                Equals((ScriptParseCacheKey)obj);
        }

        /// <summary>
        /// Computes the hash code for this key.
        /// </summary>
        /// <returns>
        /// The hash code.
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int result = (text != null) ? text.GetHashCode() : 0;

                result = (result * 31) + startIndex;
                result = (result * 31) + characters;
                result = (result * 31) + (nested ? 1 : 0);
                result = (result * 31) + substitutionFlags.GetHashCode();
                result = (result * 31) +
                    ((fileName != null) ? fileName.GetHashCode() : 0);
                result = (result * 31) + currentLine;

                return result;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This class is the per-interpreter script parse cache.  It maps script
    /// parse inputs to their fully parsed commands so that a script body
    /// evaluated repeatedly (procedure bodies, loop bodies, bracketed
    /// command substitutions, ...) is parsed once and replayed thereafter.
    /// Entries live for the lifetime of the interpreter; they are never
    /// trimmed or expired.  To avoid retaining one-shot dynamically built
    /// scripts, a script is only promoted into the cache on its SECOND
    /// sighting: the first sighting records only the key's hash code.
    /// All members are thread-safe.
    /// </summary>
    internal sealed class ScriptParseCache
    {
        /// <summary>
        /// The bound on the first-sighting hash set.  When reached, the set
        /// is simply cleared (losing pending first sightings, which only
        /// delays promotion of an affected script until it is seen twice
        /// more); this prevents unbounded growth when a workload evaluates
        /// an endless stream of unique scripts.
        /// </summary>
        private const int SeenOnceLimit = 500000;

        private readonly object syncRoot = new object();

        private readonly Dictionary<ScriptParseCacheKey, CachedScriptCommands> entries =
            new Dictionary<ScriptParseCacheKey, CachedScriptCommands>();

        private readonly HashSet<int> seenOnce = new HashSet<int>();

        /// <summary>
        /// Looks up the cached commands for the specified key.  On a miss,
        /// records the sighting and reports (via <paramref name="build" />)
        /// whether the caller should now parse the script and add it to the
        /// cache (i.e. this is at least the second sighting of the key).
        /// </summary>
        /// <param name="key">
        /// The lookup key for the script being evaluated.
        /// </param>
        /// <param name="build">
        /// Upon return, non-zero if the caller should build and add a cache
        /// entry for this key.
        /// </param>
        /// <returns>
        /// The cached commands, or null if not present.
        /// </returns>
        internal CachedScriptCommands GetOrMarkSeen(
            ScriptParseCacheKey key,
            out bool build
            )
        {
            lock (syncRoot)
            {
                CachedScriptCommands cachedScript;

                if (entries.TryGetValue(key, out cachedScript))
                {
                    build = false;
                    return cachedScript;
                }

                int hashCode = key.GetHashCode();

                if (seenOnce.Remove(hashCode))
                {
                    build = true;
                    return null;
                }

                if (seenOnce.Count >= SeenOnceLimit)
                    seenOnce.Clear();

                seenOnce.Add(hashCode);

                build = false;
                return null;
            }
        }

        /// <summary>
        /// Adds (or replaces) the cache entry for the specified key.
        /// </summary>
        /// <param name="key">
        /// The lookup key for the script.
        /// </param>
        /// <param name="cachedScript">
        /// The fully parsed commands for the script.
        /// </param>
        internal void Add(
            ScriptParseCacheKey key,
            CachedScriptCommands cachedScript
            )
        {
            lock (syncRoot)
            {
                entries[key] = cachedScript;
            }
        }
    }
}
