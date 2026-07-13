#if PERFORMANCE_DIAGNOSIS

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CodeBrix.Platform.TclTk.Diagnostics
{
    /// <summary>
    /// Performance probe, compiled in only when the PERFORMANCE_DIAGNOSIS
    /// symbol is defined (e.g. via
    /// <c>dotnet build -p:TclTkExtraDefines=PERFORMANCE_DIAGNOSIS</c>);
    /// shipped builds contain no trace of it. Accumulates call-count and
    /// elapsed time under named buckets so a profiling harness can see which
    /// native seams (font measurement, canvas item creation, SQLite) and
    /// engine seams (dispatch, parse/replay, argument building, resolution)
    /// dominate an operation. Enabled only when explicitly turned on;
    /// zero-overhead (a single bool check) when off. NOT thread-safe by
    /// design — used only from the single-threaded DIRECT test boot.
    /// </summary>
    public static class PerfProbe
    {
        private static readonly Dictionary<string, long[]> Buckets =
            new Dictionary<string, long[]>(StringComparer.Ordinal);

        /// <summary>When false, every probe call is a no-op after one bool test.</summary>
        public static bool Enabled;

        /// <summary>A timestamp for the caller to pass back to <see cref="Add"/>.</summary>
        public static long Now
        {
            get { return Stopwatch.GetTimestamp(); }
        }

        /// <summary>Record one call to <paramref name="key"/> that started at <paramref name="startTimestamp"/>.</summary>
        public static void Add(string key, long startTimestamp)
        {
            if (!Enabled) { return; }
            long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
            long[] slot;
            if (!Buckets.TryGetValue(key, out slot))
            {
                slot = new long[2];
                Buckets[key] = slot;
            }
            slot[0]++;
            slot[1] += elapsed;
        }

        /// <summary>Clear all buckets and enable (or disable) collection.</summary>
        public static void Reset(bool enable)
        {
            Buckets.Clear();
            Enabled = enable;
        }

        /// <summary>Render the buckets, slowest first, as milliseconds and call counts.</summary>
        public static string Report()
        {
            var rows = new List<KeyValuePair<string, long[]>>(Buckets);
            rows.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            double toMs = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, long[]> row in rows)
            {
                double ms = row.Value[1] * toMs;
                long count = row.Value[0];
                sb.Append("PROBE  ").Append(row.Key.PadRight(28))
                  .Append(ms.ToString("F1").PadLeft(9)).Append(" ms")
                  .Append("   n=").Append(count.ToString().PadLeft(7))
                  .Append("   ").Append((count > 0 ? ms / count : 0).ToString("F3")).Append(" ms/call")
                  .Append('\n');
            }
            return sb.ToString();
        }
    }
}

#endif
