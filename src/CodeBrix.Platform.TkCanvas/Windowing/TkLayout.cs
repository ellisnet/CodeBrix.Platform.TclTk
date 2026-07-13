using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;

namespace CodeBrix.Platform.TkCanvas.Windowing;

/// <summary>
/// The synchronous layout driver: recomputes requested sizes bottom-up
/// (geometry propagation) and then arranges content top-down for a whole
/// window tree. In classic Tk both halves run lazily from idle callbacks;
/// TkCanvas runs them synchronously on demand, which is also the mechanism
/// the Tk <c>update</c> command's flush semantics build on.
/// </summary>
public static class TkLayout
{
    /// <summary>
    /// The safety cap on propagate/arrange passes. Layouts converge in one or
    /// two passes; pathological <c>-in</c> chains converge a little slower.
    /// </summary>
    private const int MaxPasses = 16;

    /// <summary>
    /// Recomputes the layout of the whole tree rooted at
    /// <paramref name="root"/>: propagates requested sizes from content to
    /// containers (bottom-up, honoring each container's propagate flag),
    /// sizes the root to its forced size if one is set (otherwise its
    /// requested size), and arranges every container's content (top-down).
    /// Runs to a fixed point, so geometry reads immediately afterwards see
    /// final values — the synchronous-flush guarantee.
    /// </summary>
    /// <param name="root">The root window of the tree to lay out.</param>
    public static void Update(TkWindow root)
    {
        if (root == null) { throw new ArgumentNullException(nameof(root)); }
        if (!root.IsRoot) { throw new ArgumentException("layout must be driven from the root window", nameof(root)); }
        if (root.IsDestroyed) { return; }

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            if (!PropagateRequests(root)) { break; }
        }

        int width = root.ForcedWidth ?? root.RequestedWidth;
        int height = root.ForcedHeight ?? root.RequestedHeight;
        root.Width = (width < 1) ? 1 : width;
        root.Height = (height < 1) ? 1 : height;
        root.X = 0;
        root.Y = 0;
        root.IsDisplayed = true;

        ArrangeOverlays(root);

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            if (!ArrangeTree(root)) { break; }
        }

        FireConfigureEvents(root);
    }

    /// <summary>
    /// Sizes and places overlay toplevels: no geometry manager owns them, so
    /// the layout root sizes each from its <c>wm geometry</c> (or its
    /// requested size), applies its <c>wm</c> position, honors withdrawal,
    /// and clamps frames into the root bounds (§11.4).
    /// </summary>
    private static void ArrangeOverlays(TkWindow root)
    {
        bool any = false;
        foreach (TkWindow child in root.Children)
        {
            Overlay.OverlayState overlay = child.Overlay;
            if (overlay == null) { continue; }
            any = true;

            int width = overlay.GeometryWidth ?? child.RequestedWidth;
            int height = overlay.GeometryHeight ?? child.RequestedHeight;
            child.Width = (width < 1) ? 1 : width;
            child.Height = (height < 1) ? 1 : height;
            if (overlay.GeometryX.HasValue) { child.X = overlay.GeometryX.Value; }
            if (overlay.GeometryY.HasValue) { child.Y = overlay.GeometryY.Value; }
            child.IsDisplayed = !overlay.Withdrawn;
        }

        if (any)
        {
            Events.WindowTree tree = root.Tree;
            Overlay.WindowManager manager = tree.WindowManagerIfCreated;
            if (manager != null) { manager.ClampOverlays(); }
        }
    }

    /// <summary>
    /// Fires a <c>&lt;Configure&gt;</c> event for every displayed window
    /// whose size changed since the last layout pass (a consumer may bind
    /// <c>&lt;Configure&gt;</c> on windows that need to learn their own size).
    /// Nothing fires when the tree has no event system yet.
    /// </summary>
    private static void FireConfigureEvents(TkWindow root)
    {
        var changed = new List<TkWindow>();
        CollectConfigureChanges(root, changed);
        if (changed.Count == 0) { return; }

        WindowTree tree = root.Tree;
        foreach (TkWindow window in changed)
        {
            tree.DispatchEvent(window, new TkEvent
            {
                Type = TkEventType.Configure,
                Width = window.Width,
                Height = window.Height,
                KeySym = string.Empty,
                Character = string.Empty,
            });
        }
    }

    private static void CollectConfigureChanges(TkWindow window, List<TkWindow> changed)
    {
        if (window.IsDisplayed
                && (window.Width != window.LastConfiguredWidth || window.Height != window.LastConfiguredHeight))
        {
            window.LastConfiguredWidth = window.Width;
            window.LastConfiguredHeight = window.Height;
            changed.Add(window);
        }

        foreach (TkWindow child in window.Children)
        {
            CollectConfigureChanges(child, changed);
        }
    }

    /// <summary>
    /// One bottom-up pass computing container requested sizes from their
    /// content. Returns true when any request changed (another pass is then
    /// needed for containers laid out before a late-changing content).
    /// </summary>
    private static bool PropagateRequests(TkWindow window)
    {
        bool changed = false;

        foreach (TkWindow child in window.Children)
        {
            if (PropagateRequests(child)) { changed = true; }
        }

        PackContainerState packState = window.PackContainer;
        if (packState != null && packState.Content.Count > 0 && packState.Propagate)
        {
            int width, height;
            if (PackManager.Instance.TryComputeRequestedSize(window, out width, out height))
            {
                if (width != window.RequestedWidth || height != window.RequestedHeight)
                {
                    window.SetRequestedSize(width, height);
                    changed = true;
                }
            }
        }

        GridContainerState gridState = window.GridContainer;
        if (gridState != null && gridState.Content.Count > 0 && gridState.Propagate)
        {
            int width, height;
            if (GridManager.Instance.TryComputeRequestedSize(window, out width, out height))
            {
                if (width != window.RequestedWidth || height != window.RequestedHeight)
                {
                    window.SetRequestedSize(width, height);
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// One top-down pass arranging every container's content. Returns true
    /// when any geometry changed (content packed <c>-in</c> a container that
    /// is arranged later can shift, needing another pass).
    /// </summary>
    private static bool ArrangeTree(TkWindow window)
    {
        bool changed = false;

        PackContainerState packState = window.PackContainer;
        if (packState != null && packState.Content.Count > 0)
        {
            if (PackManager.Instance.Arrange(window)) { changed = true; }
        }

        GridContainerState gridState = window.GridContainer;
        if (gridState != null && gridState.Content.Count > 0)
        {
            if (GridManager.Instance.Arrange(window)) { changed = true; }
        }

        // Widgets that manage their own children (e.g. panedwindow) arrange
        // them here, after pack/grid content and before descending.
        if (window.ArrangeContent != null)
        {
            if (window.ArrangeContent()) { changed = true; }
        }

        foreach (TkWindow child in window.Children)
        {
            if (ArrangeTree(child)) { changed = true; }
        }

        return changed;
    }
}
