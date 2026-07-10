using System;

using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Layout;

/// <summary>
/// The pack geometry manager engine: a faithful reimplementation of Tk's
/// packer (tkPack.c) cavity model. Pass 1 computes the size a container needs
/// to just satisfy its packing list (geometry propagation); pass 2 walks the
/// packing list slicing frames off the shrinking cavity, growing expandable
/// frames by the leftover space, then places each window inside its frame by
/// fill, internal padding, and anchor. Content whose computed width or height
/// is not positive is undisplayed rather than squeezed, exactly like Tk.
/// </summary>
internal sealed class PackManager : IGeometryManager
{
    /// <summary>The single engine instance (state lives on container windows).</summary>
    public static readonly PackManager Instance = new PackManager();

    private PackManager()
    {
    }

    /// <inheritdoc/>
    public string Name
    {
        get { return "pack"; }
    }

    /// <inheritdoc/>
    public bool TryComputeRequestedSize(TkWindow container, out int width, out int height)
    {
        width = 0;
        height = 0;

        PackContainerState state = container.PackContainer;
        if (state == null || state.Content.Count == 0) { return false; }

        // Pass 1 of ArrangePacking: "width"/"height" accumulate the space
        // consumed by LEFT|RIGHT / TOP|BOTTOM content respectively;
        // maxWidth/maxHeight build up the just-barely-sufficient container
        // size as the list is scanned in packing order.
        int accumulatedWidth = container.InternalBorderLeft + container.InternalBorderRight;
        int accumulatedHeight = container.InternalBorderTop + container.InternalBorderBottom;
        int maxWidth = accumulatedWidth;
        int maxHeight = accumulatedHeight;

        foreach (PackContent content in state.Content)
        {
            TkWindow window = content.Window;
            if (content.Side == Side.Top || content.Side == Side.Bottom)
            {
                int tmp = window.RequestedWidth + content.PadX + content.IPadXTotal + accumulatedWidth;
                if (tmp > maxWidth) { maxWidth = tmp; }
                accumulatedHeight += window.RequestedHeight + content.PadY + content.IPadYTotal;
            }
            else
            {
                int tmp = window.RequestedHeight + content.PadY + content.IPadYTotal + accumulatedHeight;
                if (tmp > maxHeight) { maxHeight = tmp; }
                accumulatedWidth += window.RequestedWidth + content.PadX + content.IPadXTotal;
            }
        }

        if (accumulatedWidth > maxWidth) { maxWidth = accumulatedWidth; }
        if (accumulatedHeight > maxHeight) { maxHeight = accumulatedHeight; }

        if (maxWidth < container.MinimumRequestedWidth) { maxWidth = container.MinimumRequestedWidth; }
        if (maxHeight < container.MinimumRequestedHeight) { maxHeight = container.MinimumRequestedHeight; }

        width = maxWidth;
        height = maxHeight;
        return true;
    }

    /// <inheritdoc/>
    public bool Arrange(TkWindow container)
    {
        PackContainerState state = container.PackContainer;
        if (state == null || state.Content.Count == 0) { return false; }

        bool changed = false;

        // Pass 2 of ArrangePacking: the cavity shrinks inward as frames are
        // sliced off its sides for each content window in packing order.
        int cavityX = container.InternalBorderLeft;
        int cavityY = container.InternalBorderTop;
        int cavityWidth = container.Width - container.InternalBorderLeft - container.InternalBorderRight;
        int cavityHeight = container.Height - container.InternalBorderTop - container.InternalBorderBottom;

        for (int index = 0; index < state.Content.Count; index++)
        {
            PackContent content = state.Content[index];
            TkWindow window = content.Window;

            int frameX, frameY, frameWidth, frameHeight;

            if (content.Side == Side.Top || content.Side == Side.Bottom)
            {
                frameWidth = cavityWidth;
                frameHeight = window.RequestedHeight + content.PadY + content.IPadYTotal;
                if (content.Expand) { frameHeight += YExpansion(state, index, cavityHeight); }
                cavityHeight -= frameHeight;
                if (cavityHeight < 0)
                {
                    frameHeight += cavityHeight;
                    cavityHeight = 0;
                }
                frameX = cavityX;
                if (content.Side == Side.Top)
                {
                    frameY = cavityY;
                    cavityY += frameHeight;
                }
                else
                {
                    frameY = cavityY + cavityHeight;
                }
            }
            else
            {
                frameHeight = cavityHeight;
                frameWidth = window.RequestedWidth + content.PadX + content.IPadXTotal;
                if (content.Expand) { frameWidth += XExpansion(state, index, cavityWidth); }
                cavityWidth -= frameWidth;
                if (cavityWidth < 0)
                {
                    frameWidth += cavityWidth;
                    cavityWidth = 0;
                }
                frameY = cavityY;
                if (content.Side == Side.Left)
                {
                    frameX = cavityX;
                    cavityX += frameWidth;
                }
                else
                {
                    frameX = cavityX + cavityWidth;
                }
            }

            // Place the window inside its frame using fill, internal padding,
            // and anchor. borderLeft/borderRight/borderTop/borderBtm are the
            // per-edge external pads (Tk's new-style packing).
            int borderX = content.PadX;
            int borderY = content.PadY;
            int borderLeft = content.PadLeft;
            int borderRight = borderX - borderLeft;
            int borderTop = content.PadTop;
            int borderBtm = borderY - borderTop;

            int width = window.RequestedWidth + content.IPadXTotal;
            if (content.Fill == Fill.X || content.Fill == Fill.Both
                    || width > (frameWidth - borderX))
            {
                width = frameWidth - borderX;
            }

            int height = window.RequestedHeight + content.IPadYTotal;
            if (content.Fill == Fill.Y || content.Fill == Fill.Both
                    || height > (frameHeight - borderY))
            {
                height = frameHeight - borderY;
            }

            int x, y;
            switch (content.Anchor)
            {
                case Anchor.N:
                    x = frameX + (borderLeft + frameWidth - width - borderRight) / 2;
                    y = frameY + borderTop;
                    break;
                case Anchor.NE:
                    x = frameX + frameWidth - width - borderRight;
                    y = frameY + borderTop;
                    break;
                case Anchor.E:
                    x = frameX + frameWidth - width - borderRight;
                    y = frameY + (borderTop + frameHeight - height - borderBtm) / 2;
                    break;
                case Anchor.SE:
                    x = frameX + frameWidth - width - borderRight;
                    y = frameY + frameHeight - height - borderBtm;
                    break;
                case Anchor.S:
                    x = frameX + (borderLeft + frameWidth - width - borderRight) / 2;
                    y = frameY + frameHeight - height - borderBtm;
                    break;
                case Anchor.SW:
                    x = frameX + borderLeft;
                    y = frameY + frameHeight - height - borderBtm;
                    break;
                case Anchor.W:
                    x = frameX + borderLeft;
                    y = frameY + (borderTop + frameHeight - height - borderBtm) / 2;
                    break;
                case Anchor.NW:
                    x = frameX + borderLeft;
                    y = frameY + borderTop;
                    break;
                case Anchor.Center:
                default:
                    x = frameX + (borderLeft + frameWidth - width - borderRight) / 2;
                    y = frameY + (borderTop + frameHeight - height - borderBtm) / 2;
                    break;
            }

            if (width <= 0 || height <= 0)
            {
                // Tk unmaps content that has no room; its geometry is left as-is.
                if (window.IsDisplayed)
                {
                    window.IsDisplayed = false;
                    changed = true;
                }
            }
            else
            {
                // Content packed -in a non-parent container: Tk positions it
                // relative to the container (Tk_MaintainGeometry); translate
                // container-relative coordinates into the window's parent's
                // coordinate space (the container is always the parent or a
                // descendant of the parent).
                if (container != window.Parent)
                {
                    int offsetX, offsetY;
                    OffsetWithinAncestor(container, window.Parent, out offsetX, out offsetY);
                    x += offsetX;
                    y += offsetY;
                }

                if (x != window.X || y != window.Y || width != window.Width || height != window.Height)
                {
                    window.X = x;
                    window.Y = y;
                    window.Width = width;
                    window.Height = height;
                    changed = true;
                }

                if (!window.IsDisplayed)
                {
                    window.IsDisplayed = true;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Computes how many extra horizontal pixels an expandable LEFT|RIGHT
    /// content window gets (Tk's XExpansion). Windows packed top or bottom
    /// can be interspersed among expandable left/right windows, so the list
    /// tail is scanned keeping a running count of unallocated cavity space
    /// and of expandable left/right windows, evaluating a candidate expansion
    /// at each top/bottom window and at the end; the smallest wins.
    /// </summary>
    /// <param name="state">The container state.</param>
    /// <param name="startIndex">The index of the expanding content in the packing list.</param>
    /// <param name="cavityWidth">The horizontal cavity space left for all remaining content.</param>
    /// <returns>The number of additional pixels (never negative).</returns>
    private static int XExpansion(PackContainerState state, int startIndex, int cavityWidth)
    {
        int minExpand = cavityWidth;
        int numExpand = 0;

        for (int index = startIndex; index < state.Content.Count; index++)
        {
            PackContent content = state.Content[index];
            int childWidth = content.Window.RequestedWidth + content.PadX + content.IPadXTotal;

            if (content.Side == Side.Top || content.Side == Side.Bottom)
            {
                if (numExpand != 0)
                {
                    int curExpand = (cavityWidth - childWidth) / numExpand;
                    if (curExpand < minExpand) { minExpand = curExpand; }
                }
            }
            else
            {
                cavityWidth -= childWidth;
                if (content.Expand) { numExpand++; }
            }
        }

        if (numExpand != 0)
        {
            int curExpand = cavityWidth / numExpand;
            if (curExpand < minExpand) { minExpand = curExpand; }
        }
        return (minExpand < 0) ? 0 : minExpand;
    }

    /// <summary>
    /// The vertical counterpart of <see cref="XExpansion"/> (Tk's YExpansion).
    /// </summary>
    /// <param name="state">The container state.</param>
    /// <param name="startIndex">The index of the expanding content in the packing list.</param>
    /// <param name="cavityHeight">The vertical cavity space left for all remaining content.</param>
    /// <returns>The number of additional pixels (never negative).</returns>
    private static int YExpansion(PackContainerState state, int startIndex, int cavityHeight)
    {
        int minExpand = cavityHeight;
        int numExpand = 0;

        for (int index = startIndex; index < state.Content.Count; index++)
        {
            PackContent content = state.Content[index];
            int childHeight = content.Window.RequestedHeight + content.PadY + content.IPadYTotal;

            if (content.Side == Side.Left || content.Side == Side.Right)
            {
                if (numExpand != 0)
                {
                    int curExpand = (cavityHeight - childHeight) / numExpand;
                    if (curExpand < minExpand) { minExpand = curExpand; }
                }
            }
            else
            {
                cavityHeight -= childHeight;
                if (content.Expand) { numExpand++; }
            }
        }

        if (numExpand != 0)
        {
            int curExpand = cavityHeight / numExpand;
            if (curExpand < minExpand) { minExpand = curExpand; }
        }
        return (minExpand < 0) ? 0 : minExpand;
    }

    /// <summary>
    /// Sums the allocated offsets of <paramref name="descendant"/> walking up
    /// to (but excluding) <paramref name="ancestor"/>, yielding the position
    /// of the descendant's origin in the ancestor's coordinate space.
    /// </summary>
    /// <param name="descendant">The lower window (a pack -in container).</param>
    /// <param name="ancestor">The higher window (the content's parent).</param>
    /// <param name="offsetX">The x offset in pixels.</param>
    /// <param name="offsetY">The y offset in pixels.</param>
    private static void OffsetWithinAncestor(TkWindow descendant, TkWindow ancestor, out int offsetX, out int offsetY)
    {
        offsetX = 0;
        offsetY = 0;
        TkWindow current = descendant;
        while (current != null && current != ancestor)
        {
            offsetX += current.X;
            offsetY += current.Y;
            current = current.Parent;
        }
    }

    /// <inheritdoc/>
    public void Forget(TkWindow content)
    {
        Unlink(content);
        content.ManagedBy = null;
        content.Container = null;
        content.IsDisplayed = false;
    }

    /// <inheritdoc/>
    public void ContentDestroyed(TkWindow content)
    {
        Unlink(content);
    }

    /// <inheritdoc/>
    public void ContainerDestroyed(TkWindow container)
    {
        PackContainerState state = container.PackContainer;
        if (state == null) { return; }

        foreach (PackContent content in state.Content)
        {
            content.Window.ManagedBy = null;
            content.Window.Container = null;
            content.Window.IsDisplayed = false;
        }
        state.Content.Clear();
        container.ReleaseContainer("pack");
    }

    /// <summary>Removes a content window's record from its container's packing list.</summary>
    /// <param name="content">The content window.</param>
    private static void Unlink(TkWindow content)
    {
        TkWindow container = content.Container;
        if (container == null) { return; }

        PackContainerState state = container.PackContainer;
        if (state == null) { return; }

        PackContent record = state.Find(content);
        if (record != null) { state.Content.Remove(record); }

        // An emptied container is no longer handled by this manager.
        if (state.Content.Count == 0)
        {
            container.ReleaseContainer("pack");
        }
    }
}
