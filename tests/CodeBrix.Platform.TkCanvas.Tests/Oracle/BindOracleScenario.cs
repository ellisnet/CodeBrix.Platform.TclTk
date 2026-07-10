using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Tests.Oracle;

/// <summary>
/// Replays a bind-dispatch scenario file against the TkCanvas event system
/// and produces the same firing log the wish capture script
/// (tools/layout-oracle/capture_bind.tcl) produces: one line per fired
/// binding, fields typed by the binding's pattern. Events are dispatched
/// directly to their target window, mirroring <c>event generate</c>.
/// </summary>
internal static class BindOracleScenario
{
    /// <summary>The directory holding the vendored scenario/fixture pairs.</summary>
    public static string FixtureDirectory
    {
        get { return Path.Combine(AppContext.BaseDirectory, "Assets", "BindOracle"); }
    }

    /// <summary>
    /// Runs the scenario and returns the firing log.
    /// </summary>
    /// <param name="scenarioPath">The scenario file path.</param>
    /// <returns>The log lines, in firing order.</returns>
    public static IReadOnlyList<string> Run(string scenarioPath)
    {
        TkWindow root = TkWindow.CreateRoot();
        WindowTree tree = root.Tree;
        var log = new List<string>();

        foreach (string rawLine in File.ReadAllLines(scenarioPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') { continue; }

            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "window":
                {
                    CreateWindow(root, words[1]);
                    break;
                }
                case "bind":
                {
                    string tag = words[1];
                    string pattern = words[2];
                    string label = words[3];
                    bool breakAfter = (words.Length > 4 && words[4] == "break");
                    EventPattern parsed = EventPattern.Parse(pattern);
                    tree.Bindings.Bind(tag, pattern, tkEvent =>
                    {
                        log.Add(FormatLog(label, parsed, tkEvent));
                        return breakAfter ? DispatchResult.Break : DispatchResult.Continue;
                    });
                    break;
                }
                case "unbind":
                {
                    tree.Bindings.Unbind(words[1], words[2]);
                    break;
                }
                case "bindtags":
                {
                    var tags = new List<string>();
                    for (int i = 2; i < words.Length; i++) { tags.Add(words[i]); }
                    Find(root, words[1]).BindTags = tags;
                    break;
                }
                case "event":
                {
                    DispatchScenarioEvent(tree, Find(root, words[1]), words);
                    break;
                }
                case "destroywin":
                {
                    Find(root, words[1]).Destroy();
                    break;
                }
                default:
                {
                    throw new InvalidDataException("unknown scenario command: " + words[0]);
                }
            }
        }

        return log;
    }

    private static void DispatchScenarioEvent(WindowTree tree, TkWindow window, string[] words)
    {
        string kind = words[2];
        switch (kind)
        {
            case "buttonpress":
            case "buttonrelease":
            {
                var tkEvent = new TkEvent
                {
                    Type = (kind == "buttonpress") ? TkEventType.ButtonPress : TkEventType.ButtonRelease,
                    Button = ParseInt(words[3]),
                    X = ParseInt(words[4]),
                    Y = ParseInt(words[5]),
                    State = (words.Length > 6) ? ParseMods(words[6]) : EventModifiers.None,
                    KeySym = string.Empty,
                    Character = string.Empty,
                };
                tree.DispatchEvent(window, tkEvent);
                break;
            }
            case "motion":
            {
                var tkEvent = new TkEvent
                {
                    Type = TkEventType.Motion,
                    X = ParseInt(words[3]),
                    Y = ParseInt(words[4]),
                    State = (words.Length > 5) ? ParseMods(words[5]) : EventModifiers.None,
                    KeySym = string.Empty,
                    Character = string.Empty,
                };
                tree.DispatchEvent(window, tkEvent);
                break;
            }
            case "keypress":
            case "keyrelease":
            {
                var tkEvent = new TkEvent
                {
                    Type = (kind == "keypress") ? TkEventType.KeyPress : TkEventType.KeyRelease,
                    KeySym = words[3],
                    Character = string.Empty,
                    State = (words.Length > 4) ? ParseMods(words[4]) : EventModifiers.None,
                };
                tree.DispatchEvent(window, tkEvent);
                break;
            }
            case "enter":
            case "leave":
            {
                tree.DispatchEvent(window, new TkEvent
                {
                    Type = (kind == "enter") ? TkEventType.Enter : TkEventType.Leave,
                    KeySym = string.Empty,
                    Character = string.Empty,
                });
                break;
            }
            case "wheel":
            {
                tree.DispatchEvent(window, new TkEvent
                {
                    Type = TkEventType.MouseWheel,
                    Delta = ParseInt(words[3]),
                    KeySym = string.Empty,
                    Character = string.Empty,
                });
                break;
            }
            case "virtual":
            {
                tree.VirtualEvent(window, words[3]);
                break;
            }
            case "focusin":
            case "focusout":
            {
                tree.DispatchEvent(window, new TkEvent
                {
                    Type = (kind == "focusin") ? TkEventType.FocusIn : TkEventType.FocusOut,
                    KeySym = string.Empty,
                    Character = string.Empty,
                });
                break;
            }
            default:
            {
                throw new InvalidDataException("unknown event kind: " + kind);
            }
        }
    }

    /// <summary>
    /// Formats a log line with the same pattern-typed field template the
    /// wish capture script uses.
    /// </summary>
    private static string FormatLog(string label, EventPattern pattern, TkEvent tkEvent)
    {
        string window = tkEvent.Window.PathName;
        switch (pattern.Type)
        {
            case TkEventType.KeyPress:
            case TkEventType.KeyRelease:
                return label + " " + window + " " + tkEvent.KeySym;
            case TkEventType.ButtonPress:
            case TkEventType.ButtonRelease:
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4}",
                        label, window, tkEvent.X, tkEvent.Y, tkEvent.Button);
            case TkEventType.Motion:
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}",
                        label, window, tkEvent.X, tkEvent.Y);
            case TkEventType.MouseWheel:
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}",
                        label, window, tkEvent.Delta);
            default:
                return label + " " + window;
        }
    }

    private static TkWindow CreateWindow(TkWindow root, string pathName)
    {
        int lastDot = pathName.LastIndexOf('.');
        string parentPath = (lastDot == 0) ? "." : pathName.Substring(0, lastDot);
        string leafName = pathName.Substring(lastDot + 1);
        return Find(root, parentPath).CreateChild(leafName);
    }

    private static TkWindow Find(TkWindow root, string pathName)
    {
        TkWindow window = root.FindDescendant(pathName);
        if (window == null) { throw new InvalidDataException("scenario references unknown window: " + pathName); }
        return window;
    }

    private static EventModifiers ParseMods(string mods)
    {
        EventModifiers state = EventModifiers.None;
        foreach (string mod in mods.Split(','))
        {
            switch (mod)
            {
                case "": break;
                case "shift": state |= EventModifiers.Shift; break;
                case "lock": state |= EventModifiers.Lock; break;
                case "control": state |= EventModifiers.Control; break;
                case "mod1": state |= EventModifiers.Alt; break;
                case "meta": state |= EventModifiers.Meta; break;
                case "b1": state |= EventModifiers.Button1; break;
                case "b2": state |= EventModifiers.Button2; break;
                case "b3": state |= EventModifiers.Button3; break;
                case "b4": state |= EventModifiers.Button4; break;
                case "b5": state |= EventModifiers.Button5; break;
                default: throw new InvalidDataException("unknown modifier: " + mod);
            }
        }
        return state;
    }

    private static int ParseInt(string value)
    {
        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
