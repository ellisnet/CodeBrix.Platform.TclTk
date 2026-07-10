using System;
using System.Collections.Generic;
using System.IO;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tests.Oracle;

/// <summary>
/// Replays a canvas-oracle scenario file against <see cref="CanvasWidget"/>
/// and returns one output line per <c>q</c> query, exactly like the wish
/// capture script (tools/layout-oracle/capture_canvas.tcl). The scenario
/// line format is documented in the capture script; keep the two parsers in
/// sync.
/// </summary>
internal static class CanvasOracleScenario
{
    /// <summary>The directory holding the vendored scenario/fixture pairs.</summary>
    public static string FixtureDirectory
    {
        get { return Path.Combine(AppContext.BaseDirectory, "Assets", "CanvasOracle"); }
    }

    /// <summary>
    /// Builds the scenario's canvas, executes its commands, and returns the
    /// query outputs in order.
    /// </summary>
    /// <param name="scenarioPath">The scenario file path.</param>
    /// <returns>One line per <c>q</c> command.</returns>
    public static IReadOnlyList<string> Run(string scenarioPath)
    {
        TkWindow root = TkWindow.CreateRoot();
        CanvasWidget canvas = null;
        var outputs = new List<string>();

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') { continue; }

            List<string> words = TclString.SplitList(line);
            string verb = words[0];
            var rest = words.GetRange(1, words.Count - 1);

            switch (verb)
            {
                case "canvas":
                {
                    if (canvas != null)
                    {
                        throw new InvalidDataException("scenario has more than one canvas line");
                    }
                    TkWindow window = root.CreateChild("c");
                    canvas = new CanvasWidget(window);

                    var options = new Dictionary<string, string>();
                    for (int i = 0; i + 1 < rest.Count; i += 2)
                    {
                        options[rest[i]] = rest[i + 1];
                    }
                    canvas.Configure(options);

                    // "pack .c; update" — the window takes its requested size.
                    PackLayout.Configure(window, new PackOptions());
                    TkLayout.Update(root);
                    break;
                }
                case "do":
                {
                    RequireCanvas(canvas);
                    canvas.Execute(rest);
                    break;
                }
                case "q":
                {
                    RequireCanvas(canvas);
                    outputs.Add(canvas.Execute(rest));
                    break;
                }
                default:
                {
                    throw new InvalidDataException("unknown scenario verb: " + verb);
                }
            }
        }
        return outputs;
    }

    private static void RequireCanvas(CanvasWidget canvas)
    {
        if (canvas == null)
        {
            throw new InvalidDataException("scenario command before canvas line");
        }
    }
}
