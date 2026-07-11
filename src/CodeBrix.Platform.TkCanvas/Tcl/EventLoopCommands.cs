using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

using CodeBrix.Platform.TkCanvas.Events;

namespace CodeBrix.Platform.TkCanvas.Tcl;

/// <summary>
/// The event-loop commands: <c>update</c>/<c>update idletasks</c> (the R1
/// synchronous flush, marshaled to the UI thread), the <c>after</c> timer
/// bridge, and the <c>tkwait</c> forms DRAKON-era code uses
/// (<c>tkwait visibility</c> flushes and returns — overlays are visible
/// as soon as layout runs).
/// </summary>
internal static class EventLoopCommands
{
    internal static void Register(BridgeContext ctx)
    {
        var handles = new Dictionary<string, AfterHandle>(StringComparer.Ordinal);
        int serial = 0;

        BridgeRegistrar.Add(ctx, "update", words => ctx.Ui(() =>
        {
            if (words.Length >= 2 && words[1] == "idletasks")
            {
                ctx.Tree.Scheduler.UpdateIdleTasks();
            }
            else
            {
                ctx.Tree.Scheduler.Update();
            }
            return "";
        }));

        BridgeRegistrar.Add(ctx, "after", words =>
        {
            if (words.Length < 2)
            {
                throw BridgeRegistrar.WrongArgs("after option ?arg ...?");
            }

            string first = words[1];

            if (first == "cancel")
            {
                if (words.Length >= 3)
                {
                    string id = words[2];
                    ctx.Ui(() =>
                    {
                        AfterHandle handle;
                        if (handles.TryGetValue(id, out handle))
                        {
                            handles.Remove(id);
                            ctx.Tree.Scheduler.CancelAfter(handle);
                        }
                    });
                }
                return "";
            }

            if (first == "info")
            {
                return "";
            }

            if (first == "idle")
            {
                string script = JoinScript(words, 2);
                string id = "after#" + (++serial).ToString(CultureInfo.InvariantCulture);
                ctx.Ui(() =>
                {
                    AfterHandle handle = ctx.Tree.Scheduler.AfterIdle(() =>
                    {
                        handles.Remove(id);
                        ctx.EvalCallbackScript(script);
                    });
                    handles[id] = handle;
                });
                return id;
            }

            int milliseconds;
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out milliseconds))
            {
                throw new TkTclError("bad argument \"" + first +
                    "\": must be cancel, idle, info, or an integer");
            }

            if (words.Length == 2)
            {
                // "after ms" sleeps the script (the Tcl thread), not the UI.
                Thread.Sleep(Math.Max(0, milliseconds));
                return "";
            }

            string timerScript = JoinScript(words, 2);
            string timerId = "after#" + (++serial).ToString(CultureInfo.InvariantCulture);
            ctx.Ui(() =>
            {
                AfterHandle handle = ctx.Tree.Scheduler.After(milliseconds, () =>
                {
                    handles.Remove(timerId);
                    ctx.EvalCallbackScript(timerScript);
                });
                handles[timerId] = handle;
            });
            return timerId;
        });

        BridgeRegistrar.Add(ctx, "tkwait", words =>
        {
            if (words.Length < 3)
            {
                throw BridgeRegistrar.WrongArgs("tkwait variable|visibility|window name");
            }

            switch (words[1])
            {
                case "visibility":
                    // Overlays and windows are "visible" once layout has
                    // run — flush synchronously and return.
                    return ctx.Ui(() =>
                    {
                        ctx.Tree.Scheduler.UpdateIdleTasks();
                        return "";
                    });

                case "window":
                case "variable":
                default:
                    // Not used by the reference consumer; accept-and-no-op
                    // per the deferral discipline.
                    return "";
            }
        });

        BridgeRegistrar.Add(ctx, "bell", words => "");
    }

    private static string JoinScript(string[] words, int start)
    {
        // "after ms cmd arg arg" concatenates the words like Tcl does.
        if (start >= words.Length) { return ""; }
        if (start == words.Length - 1) { return words[start]; }
        return string.Join(" ", words, start, words.Length - start);
    }
}
