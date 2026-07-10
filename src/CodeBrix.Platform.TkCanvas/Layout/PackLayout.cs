using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The public surface of the pack geometry manager (the Tk <c>pack</c>
/// command): configure a window into a container's packing list, forget it,
/// query its options, list a container's content, and control geometry
/// propagation. The layout math itself runs during
/// <see cref="TkLayout.Update"/>.
/// </summary>
public static class PackLayout
{
    /// <summary>
    /// Packs <paramref name="window"/> (or reconfigures its packing) with the
    /// given options — the analogue of <c>pack configure</c>. The container
    /// is <see cref="PackOptions.In"/> if set, otherwise the container of a
    /// <see cref="PackOptions.Before"/>/<see cref="PackOptions.After"/>
    /// target, otherwise the window's parent. A newly packed window goes to
    /// the end of the packing order unless Before/After places it; a window
    /// already packed in the same container keeps its position.
    /// </summary>
    /// <param name="window">The window to pack.</param>
    /// <param name="options">The pack options (null means all defaults).</param>
    public static void Configure(TkWindow window, PackOptions options)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.IsRoot) { throw new InvalidOperationException("can't pack \".\": it's the root window"); }
        if (options == null) { options = new PackOptions(); }

        // Resolve the target container and the insertion position.
        TkWindow container;
        int insertIndex = -1;

        TkWindow positionTarget = null;
        bool insertBefore = false;
        if (options.Before != null)
        {
            positionTarget = options.Before;
            insertBefore = true;
        }
        else if (options.After != null)
        {
            positionTarget = options.After;
        }

        if (positionTarget != null)
        {
            if (positionTarget.ManagedBy != PackManager.Instance || positionTarget.Container == null)
            {
                throw new InvalidOperationException(
                    "window \"" + positionTarget.PathName + "\" isn't packed");
            }
            container = positionTarget.Container;
            if (options.In != null && options.In != container)
            {
                throw new InvalidOperationException(
                    "can't specify -in \"" + options.In.PathName + "\" together with -before/-after content of \"" + container.PathName + "\"");
            }
        }
        else
        {
            container = (options.In != null) ? options.In : window.Parent;
        }

        ValidateContainer(window, container);

        // Steal the window from another geometry manager FIRST (matching
        // Tk's order): if it was that manager's last content here, the
        // container claim is released and packing into it succeeds.
        if (window.ManagedBy != null && window.ManagedBy != PackManager.Instance)
        {
            window.ManagedBy.Forget(window);
        }

        container.ClaimContainer("pack");

        // Unlink from any current packing (possibly in another container).
        bool samePosition = false;
        PackContainerState state = GetOrCreateState(container);
        if (window.ManagedBy == PackManager.Instance && window.Container != null)
        {
            PackContainerState oldState = window.Container.PackContainer;
            PackContent existing = (oldState != null) ? oldState.Find(window) : null;
            if (existing != null)
            {
                if (window.Container == container && positionTarget == null)
                {
                    samePosition = true; // Reconfigure in place: keep packing order.
                }
                else
                {
                    oldState.Content.Remove(existing);
                    if (oldState.Content.Count == 0) { window.Container.ReleaseContainer("pack"); }
                }
            }
        }

        PackContent record;
        if (samePosition)
        {
            record = state.Find(window);
        }
        else
        {
            record = new PackContent { Window = window };
            if (positionTarget != null)
            {
                PackContent targetRecord = state.Find(positionTarget);
                int targetIndex = state.Content.IndexOf(targetRecord);
                insertIndex = insertBefore ? targetIndex : targetIndex + 1;
            }
            if (insertIndex < 0 || insertIndex > state.Content.Count)
            {
                insertIndex = state.Content.Count;
            }
            state.Content.Insert(insertIndex, record);
        }

        record.Side = options.Side;
        record.Anchor = options.Anchor;
        record.Fill = options.Fill;
        record.Expand = options.Expand;
        record.PadLeft = Clamp(options.PadLeft);
        record.PadRight = Clamp(options.PadRight);
        record.PadTop = Clamp(options.PadTop);
        record.PadBottom = Clamp(options.PadBottom);
        record.IPadX = Clamp(options.IPadX);
        record.IPadY = Clamp(options.IPadY);

        window.ManagedBy = PackManager.Instance;
        window.Container = container;
        window.Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Removes <paramref name="window"/> from its packing list — the analogue
    /// of <c>pack forget</c>. The window becomes unmanaged and undisplayed
    /// but keeps its last geometry. Forgetting an unpacked window is a no-op.
    /// </summary>
    /// <param name="window">The window to forget.</param>
    public static void Forget(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.ManagedBy != PackManager.Instance) { return; }
        PackManager.Instance.Forget(window);
        window.Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// Reports the current pack configuration of <paramref name="window"/> —
    /// the analogue of <c>pack info</c>. <see cref="PackOptions.In"/> is set
    /// to the actual container; the positional Before/After directives are
    /// not part of the persisted state and come back null.
    /// </summary>
    /// <param name="window">The packed window.</param>
    /// <returns>A snapshot of the window's pack options.</returns>
    public static PackOptions Info(TkWindow window)
    {
        if (window == null) { throw new ArgumentNullException(nameof(window)); }
        if (window.ManagedBy != PackManager.Instance || window.Container == null)
        {
            throw new InvalidOperationException("window \"" + window.PathName + "\" isn't packed");
        }

        PackContent record = window.Container.PackContainer.Find(window);
        return new PackOptions
        {
            Side = record.Side,
            Anchor = record.Anchor,
            Fill = record.Fill,
            Expand = record.Expand,
            PadLeft = record.PadLeft,
            PadRight = record.PadRight,
            PadTop = record.PadTop,
            PadBottom = record.PadBottom,
            IPadX = record.IPadX,
            IPadY = record.IPadY,
            In = window.Container,
        };
    }

    /// <summary>
    /// Lists the content packed in <paramref name="container"/>, in packing
    /// order — the analogue of <c>pack content</c> (<c>pack slaves</c>).
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>The packed windows (empty when none).</returns>
    public static IReadOnlyList<TkWindow> Content(TkWindow container)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }

        var result = new List<TkWindow>();
        PackContainerState state = container.PackContainer;
        if (state != null)
        {
            foreach (PackContent content in state.Content)
            {
                result.Add(content.Window);
            }
        }
        return result;
    }

    /// <summary>
    /// Reads the geometry-propagation flag of <paramref name="container"/> —
    /// the analogue of <c>pack propagate</c> with no value. Defaults to true.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <returns>Whether the container's requested size follows its content.</returns>
    public static bool GetPropagate(TkWindow container)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        PackContainerState state = container.PackContainer;
        return (state == null) || state.Propagate;
    }

    /// <summary>
    /// Sets the geometry-propagation flag of <paramref name="container"/> —
    /// the analogue of <c>pack propagate</c> with a boolean value.
    /// </summary>
    /// <param name="container">The container window.</param>
    /// <param name="propagate">Whether the container's requested size follows its content.</param>
    public static void SetPropagate(TkWindow container, bool propagate)
    {
        if (container == null) { throw new ArgumentNullException(nameof(container)); }
        PackContainerState state = GetOrCreateState(container);
        state.Propagate = propagate;

        // Tk releases the container claim when propagation is turned off (a
        // fixed-size container may then host another manager's content), and
        // re-claims when it is turned back on for a container with content.
        if (!propagate)
        {
            container.ReleaseContainer("pack");
        }
        else if (state.Content.Count > 0)
        {
            container.ClaimContainer("pack");
        }
        container.Tree.NotifyGeometryChanged();
    }

    private static PackContainerState GetOrCreateState(TkWindow container)
    {
        if (container.PackContainer == null)
        {
            container.PackContainer = new PackContainerState();
        }
        return container.PackContainer;
    }

    private static int Clamp(int value)
    {
        return (value < 0) ? 0 : value;
    }

    /// <summary>
    /// Enforces Tk's packing constraints: the container must be the window's
    /// parent or a descendant of the parent, and packing a window inside
    /// itself or its own descendant is a management loop.
    /// </summary>
    private static void ValidateContainer(TkWindow window, TkWindow container)
    {
        if (container == null)
        {
            throw new InvalidOperationException("window \"" + window.PathName + "\" has no container to pack in");
        }
        if (container == window)
        {
            throw new InvalidOperationException("can't pack \"" + window.PathName + "\" inside itself");
        }

        // The container must be the parent or a descendant of the parent
        // (this also rules out the container being ABOVE the parent).
        TkWindow ancestor = container;
        while (ancestor != null && ancestor != window.Parent)
        {
            if (ancestor == window)
            {
                throw new InvalidOperationException(
                    "can't put \"" + window.PathName + "\" inside \"" + container.PathName + "\": would cause a management loop");
            }
            ancestor = ancestor.Parent;
        }
        if (ancestor != window.Parent)
        {
            throw new InvalidOperationException(
                "can't pack \"" + window.PathName + "\" inside \"" + container.PathName + "\"");
        }
    }
}
