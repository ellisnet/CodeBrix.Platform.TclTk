using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;
using CodeBrix.Platform.TkCanvas.Windowing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.TkCanvas.Tests;

/// <summary>
/// Tests for the scheduler: the synchronous <c>update</c>/<c>update
/// idletasks</c> flush semantics (R1 — geometry reads immediately after the
/// flush see final values), coalesced relayout, the <c>after</c> timer
/// bridge with a deterministic clock, and host-dispatcher integration
/// through the <see cref="ITkDispatcher"/> seam.
/// </summary>
public class TkSchedulerTests
{
    private sealed class FakeTime : ITkTimeSource
    {
        public long NowMilliseconds { get; set; }
    }

    private sealed class FakeDispatcher : ITkDispatcher
    {
        public readonly List<Action> Posted = new List<Action>();
        public readonly List<(int Milliseconds, Action Callback)> Timers = new List<(int, Action)>();
        public int PumpCalls;

        public void Post(Action action)
        {
            Posted.Add(action);
        }

        public object StartTimer(int milliseconds, Action callback)
        {
            Timers.Add((milliseconds, callback));
            return Timers.Count - 1;
        }

        public void CancelTimer(object handle)
        {
        }

        public void PumpPendingWork()
        {
            PumpCalls++;
        }

        public void RunPosted()
        {
            Action[] batch = Posted.ToArray();
            Posted.Clear();
            foreach (Action action in batch) { action(); }
        }
    }

    [Fact]
    public void UpdateIdleTasks_flushes_pending_relayout_synchronously()
    {
        //Arrange (R1: configure, then read geometry right after the flush)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        a.SetRequestedSize(80, 60);
        PackLayout.Configure(a, null);

        //Act
        root.Tree.Scheduler.IsRelayoutPending.Should().BeTrue();
        root.Tree.Scheduler.UpdateIdleTasks();

        //Assert
        root.Tree.Scheduler.IsRelayoutPending.Should().BeFalse();
        a.Width.Should().Be(80);
        a.Height.Should().Be(60);
        root.RequestedWidth.Should().Be(80);
    }

    [Fact]
    public void Geometry_changes_coalesce_into_one_layout_pass()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        var log = new List<string>();
        root.Tree.Bindings.Bind(".a", "<Configure>", e => { log.Add("conf " + e.Width + "x" + e.Height); return DispatchResult.Continue; });

        //Act (three mutations, one drain)
        a.SetRequestedSize(30, 30);
        PackLayout.Configure(a, null);
        a.SetRequestedSize(70, 50);
        root.Tree.Scheduler.UpdateIdleTasks();

        //Assert (exactly one Configure with the final size)
        log.Should().Equal("conf 70x50");
    }

    [Fact]
    public void After_fires_only_when_due_and_in_due_order()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        var time = new FakeTime();
        TkScheduler scheduler = root.Tree.Scheduler;
        scheduler.TimeSource = time;
        var log = new List<string>();
        scheduler.After(200, () => log.Add("late"));
        scheduler.After(100, () => log.Add("early"));

        //Act / Assert
        scheduler.Update();
        log.Should().BeEmpty();

        time.NowMilliseconds = 150;
        scheduler.Update();
        log.Should().Equal("early");

        time.NowMilliseconds = 500;
        scheduler.Update();
        log.Should().Equal("early", "late");
    }

    [Fact]
    public void UpdateIdleTasks_does_not_run_due_timers()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        var time = new FakeTime();
        TkScheduler scheduler = root.Tree.Scheduler;
        scheduler.TimeSource = time;
        bool fired = false;
        scheduler.After(0, () => fired = true);
        time.NowMilliseconds = 10;

        //Act
        scheduler.UpdateIdleTasks();

        //Assert (idletasks flushes idle work only; update runs timers too)
        fired.Should().BeFalse();
        scheduler.Update();
        fired.Should().BeTrue();
    }

    [Fact]
    public void AfterIdle_runs_with_the_idle_queue_and_cancel_prevents_firing()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkScheduler scheduler = root.Tree.Scheduler;
        var log = new List<string>();
        scheduler.AfterIdle(() => log.Add("one"));
        AfterHandle cancelled = scheduler.AfterIdle(() => log.Add("two"));
        scheduler.CancelAfter(cancelled);

        //Act
        scheduler.UpdateIdleTasks();

        //Assert
        log.Should().Equal("one");
    }

    [Fact]
    public void CancelAfter_stops_a_timer_and_is_idempotent()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        var time = new FakeTime();
        TkScheduler scheduler = root.Tree.Scheduler;
        scheduler.TimeSource = time;
        bool fired = false;
        AfterHandle handle = scheduler.After(50, () => fired = true);

        //Act
        scheduler.CancelAfter(handle);
        scheduler.CancelAfter(handle);
        time.NowMilliseconds = 100;
        scheduler.Update();

        //Assert
        fired.Should().BeFalse();
    }

    [Fact]
    public void Timer_callbacks_may_schedule_more_work_which_the_same_update_drains()
    {
        //Arrange (a timer that triggers a relayout: the same Update flushes it)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        PackLayout.Configure(a, null);
        var time = new FakeTime();
        TkScheduler scheduler = root.Tree.Scheduler;
        scheduler.TimeSource = time;
        scheduler.UpdateIdleTasks();
        scheduler.After(10, () => a.SetRequestedSize(90, 90));
        time.NowMilliseconds = 20;

        //Act
        scheduler.Update();

        //Assert (the resize requested BY the timer is already laid out)
        a.Width.Should().Be(90);
    }

    [Fact]
    public void RepaintRequested_is_raised_once_per_drain_after_relayout()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        int repaints = 0;
        root.Tree.Scheduler.RepaintRequested += () => repaints++;

        //Act
        a.SetRequestedSize(40, 40);
        PackLayout.Configure(a, null);
        root.Tree.Scheduler.UpdateIdleTasks();
        root.Tree.Scheduler.UpdateIdleTasks(); // nothing pending: no repaint

        //Assert
        repaints.Should().Be(1);
    }

    [Fact]
    public void Host_is_woken_by_scheduled_work_and_its_post_drains_it()
    {
        //Arrange (simulates the Uno adapter: posted work runs on the UI thread)
        TkWindow root = TkWindow.CreateRoot();
        TkWindow a = root.CreateChild("a");
        var host = new FakeDispatcher();
        root.Tree.Scheduler.Host = host;

        //Act
        a.SetRequestedSize(60, 45);
        PackLayout.Configure(a, null);
        host.Posted.Count.Should().BeGreaterThan(0);
        host.RunPosted();

        //Assert (layout ran without anyone calling Update)
        a.Width.Should().Be(60);
    }

    [Fact]
    public void Host_timers_fire_after_callbacks()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        var host = new FakeDispatcher();
        TkScheduler scheduler = root.Tree.Scheduler;
        scheduler.Host = host;
        bool fired = false;

        //Act
        scheduler.After(25, () => fired = true);
        host.Timers.Count.Should().Be(1);
        host.Timers[0].Milliseconds.Should().Be(25);
        host.Timers[0].Callback();

        //Assert
        fired.Should().BeTrue();
    }

    [Fact]
    public void Full_update_pumps_the_host()
    {
        //Arrange
        TkWindow root = TkWindow.CreateRoot();
        var host = new FakeDispatcher();
        root.Tree.Scheduler.Host = host;

        //Act
        root.Tree.Scheduler.Update();
        root.Tree.Scheduler.UpdateIdleTasks();

        //Assert (only the full update pumps host work)
        host.PumpCalls.Should().Be(1);
    }
}
