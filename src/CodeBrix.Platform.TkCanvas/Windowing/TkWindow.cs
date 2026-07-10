using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Events;
using CodeBrix.Platform.TkCanvas.Layout;

namespace CodeBrix.Platform.TkCanvas.Windowing;

/// <summary>
/// A node in the Tk window tree: the fundamental rectangle that geometry
/// managers arrange and widgets draw into. Mirrors the classic Tk window
/// model: every window has a dot-separated path name (the root window is
/// <c>.</c>), a requested size (what the window's content wants, set with
/// <see cref="SetRequestedSize"/> like <c>Tk_GeometryRequest</c>), and an
/// allocated geometry (<see cref="X"/>/<see cref="Y"/>/<see cref="Width"/>/
/// <see cref="Height"/>, assigned by the geometry manager that controls the
/// window). Widgets are built ON TOP of windows (a widget owns a window);
/// this class deliberately carries no widget state.
/// </summary>
public sealed class TkWindow
{
    private readonly List<TkWindow> _children = new List<TkWindow>();

    private TkWindow(TkWindow parent, string name)
    {
        Parent = parent;
        Name = name;

        if (parent == null)
        {
            PathName = ".";
        }
        else if (parent.Parent == null)
        {
            PathName = "." + name;
        }
        else
        {
            PathName = parent.PathName + "." + name;
        }
    }

    /// <summary>
    /// Creates the root window of a new window tree. The root window's path
    /// name is <c>.</c>; it corresponds to the Tk main/toplevel window and is
    /// the window whose size the host (or a forced size) determines.
    /// </summary>
    /// <returns>The new root window.</returns>
    public static TkWindow CreateRoot()
    {
        return new TkWindow(null, string.Empty);
    }

    /// <summary>
    /// Creates a child window of this window, appended at the end of the
    /// child list (creation order is preserved; it determines default packing
    /// order and, later, stacking order).
    /// </summary>
    /// <param name="name">
    /// The leaf name of the child (the segment after the final dot of its
    /// path name). Must be non-empty, must not contain a dot, and must be
    /// unique among this window's children.
    /// </param>
    /// <returns>The new child window.</returns>
    public TkWindow CreateChild(string name)
    {
        if (IsDestroyed) { throw new InvalidOperationException("window \"" + PathName + "\" has been destroyed"); }
        if (string.IsNullOrEmpty(name)) { throw new ArgumentException("window name must not be empty", nameof(name)); }
        if (name.IndexOf('.') >= 0) { throw new ArgumentException("window name \"" + name + "\" must not contain \".\"", nameof(name)); }

        foreach (TkWindow existing in _children)
        {
            if (existing.Name == name)
            {
                throw new ArgumentException("window name \"" + name + "\" already exists as a child of \"" + PathName + "\"", nameof(name));
            }
        }

        var child = new TkWindow(this, name);
        _children.Add(child);
        return child;
    }

    private WindowTree _tree;
    private List<string> _bindTags;

    /// <summary>
    /// The event system of the tree this window belongs to (bindings, focus,
    /// pointer routing). One instance per tree, owned by the root window and
    /// created lazily.
    /// </summary>
    public WindowTree Tree
    {
        get
        {
            TkWindow root = this;
            while (root.Parent != null) { root = root.Parent; }
            if (root._tree == null) { root._tree = new WindowTree(root); }
            return root._tree;
        }
    }

    /// <summary>
    /// The widget class name used as this window's class bind tag (Tk's
    /// <c>winfo class</c>), e.g. <c>Frame</c>, <c>Button</c>, <c>Canvas</c>.
    /// Defaults to <c>Frame</c>; widgets set their own class.
    /// </summary>
    public string ClassName { get; set; } = "Frame";

    /// <summary>
    /// Whether keyboard traversal may give this window focus (the essence of
    /// the Tk <c>-takefocus</c> option). Defaults to false; focusable widgets
    /// set it.
    /// </summary>
    public bool Focusable { get; set; }

    /// <summary>
    /// The explicit bind-tag list of this window, or null to use the Tk
    /// default: its path name, its class name, the root path, and <c>all</c>
    /// (the analogue of the <c>bindtags</c> command).
    /// </summary>
    public IList<string> BindTags
    {
        get { return _bindTags; }
        set { _bindTags = (value == null) ? null : new List<string>(value); }
    }

    /// <summary>
    /// The bind tags in effect: the explicit list when one was set, otherwise
    /// the computed Tk default order.
    /// </summary>
    /// <returns>The bind tags, in dispatch order.</returns>
    public IReadOnlyList<string> EffectiveBindTags()
    {
        if (_bindTags != null) { return (List<string>)_bindTags; }

        TkWindow root = this;
        while (root.Parent != null) { root = root.Parent; }
        return new List<string> { PathName, ClassName, root.PathName, "all" };
    }

    /// <summary>
    /// The widget built on this window, or null for a bare window. Set by
    /// the widget's constructor; the toolkit render pass and pointer
    /// refinement reach the widget through it (the analogue of Tk's
    /// per-window instance data — identity only, widget STATE stays in the
    /// widget).
    /// </summary>
    public Widgets.IWidget Widget { get; set; }

    /// <summary>
    /// The window-manager state when this window is an overlay toplevel
    /// (created through the tree's <see cref="Overlay.WindowManager"/>), or
    /// null for an ordinary window. Overlay toplevels are children of the
    /// root window but are excluded from the base layout: the layout pass
    /// sizes them from <c>wm geometry</c> / their requested size, and they
    /// always stack above base content.
    /// </summary>
    internal Overlay.OverlayState Overlay;

    /// <summary>
    /// Moves a child to the end of the child list — the top of the sibling
    /// stacking order (hit-testing and painting treat later siblings as
    /// higher). The window manager uses it to raise overlay toplevels.
    /// </summary>
    /// <param name="child">The child to move.</param>
    internal void MoveChildToEnd(TkWindow child)
    {
        if (_children.Remove(child))
        {
            _children.Add(child);
        }
    }

    /// <summary>
    /// The widget-internal event hook, run BEFORE the window's bind tags
    /// when an event is dispatched — the analogue of a Tk C event handler
    /// (<c>Tk_CreateEventHandler</c>), which fires independently of script
    /// bindings. The canvas uses it for current-item picking and item-level
    /// binding dispatch; its result cannot suppress the bind-tag walk.
    /// </summary>
    internal Events.TkEventHandler ClassEventHandler;

    /// <summary>
    /// The width this window last reported through a <c>&lt;Configure&gt;</c>
    /// event (used by the layout driver to fire Configure only on change).
    /// </summary>
    internal int LastConfiguredWidth = -1;

    /// <summary>The height counterpart of <see cref="LastConfiguredWidth"/>.</summary>
    internal int LastConfiguredHeight = -1;

    /// <summary>The leaf name of this window (empty for the root window).</summary>
    public string Name { get; }

    /// <summary>
    /// The full dot-separated Tk path name of this window (<c>.</c> for the
    /// root, <c>.a.b</c> for nested children).
    /// </summary>
    public string PathName { get; }

    /// <summary>The parent window, or null for the root window.</summary>
    public TkWindow Parent { get; }

    /// <summary>The child windows, in creation order.</summary>
    public IReadOnlyList<TkWindow> Children
    {
        get { return _children; }
    }

    /// <summary>Whether this is the root window of its tree.</summary>
    public bool IsRoot
    {
        get { return Parent == null; }
    }

    /// <summary>Whether <see cref="Destroy"/> has run on this window.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>
    /// The width this window's content wants, in pixels (the analogue of
    /// <c>Tk_ReqWidth</c>). Defaults to 1, like a fresh Tk window.
    /// </summary>
    public int RequestedWidth { get; private set; } = 1;

    /// <summary>
    /// The height this window's content wants, in pixels (the analogue of
    /// <c>Tk_ReqHeight</c>). Defaults to 1, like a fresh Tk window.
    /// </summary>
    public int RequestedHeight { get; private set; } = 1;

    /// <summary>
    /// Sets the size this window wants to be (the analogue of
    /// <c>Tk_GeometryRequest</c>): widgets call this with their natural
    /// content size, and geometry managers call it on container windows when
    /// propagating the size their content needs. The layout pass
    /// (<see cref="TkLayout.Update"/>) reads it; nothing resizes immediately.
    /// </summary>
    /// <param name="width">The wanted width in pixels (negative is clamped to 0).</param>
    /// <param name="height">The wanted height in pixels (negative is clamped to 0).</param>
    public void SetRequestedSize(int width, int height)
    {
        int newWidth = (width < 0) ? 0 : width;
        int newHeight = (height < 0) ? 0 : height;
        bool changed = (newWidth != RequestedWidth) || (newHeight != RequestedHeight);
        RequestedWidth = newWidth;
        RequestedHeight = newHeight;
        if (changed && !IsDestroyed) { Tree.NotifyGeometryChanged(); }
    }

    /// <summary>
    /// The minimum width a geometry manager may shrink this window's request
    /// to when computing a container size from its content (the analogue of
    /// <c>Tk_MinReqWidth</c>). Defaults to 0.
    /// </summary>
    public int MinimumRequestedWidth { get; private set; }

    /// <summary>
    /// The minimum height counterpart of <see cref="MinimumRequestedWidth"/>
    /// (the analogue of <c>Tk_MinReqHeight</c>). Defaults to 0.
    /// </summary>
    public int MinimumRequestedHeight { get; private set; }

    /// <summary>
    /// Sets the minimum request size used when this window is a container
    /// whose requested size is computed from its content (the analogue of
    /// <c>Tk_SetMinimumRequestSize</c>).
    /// </summary>
    /// <param name="width">The minimum wanted width in pixels (negative is clamped to 0).</param>
    /// <param name="height">The minimum wanted height in pixels (negative is clamped to 0).</param>
    public void SetMinimumRequestedSize(int width, int height)
    {
        MinimumRequestedWidth = (width < 0) ? 0 : width;
        MinimumRequestedHeight = (height < 0) ? 0 : height;
    }

    /// <summary>
    /// The allocated x position of this window's top-left corner, in pixels,
    /// relative to its parent (the analogue of <c>winfo x</c>).
    /// </summary>
    public int X { get; internal set; }

    /// <summary>
    /// The allocated y position of this window's top-left corner, in pixels,
    /// relative to its parent (the analogue of <c>winfo y</c>).
    /// </summary>
    public int Y { get; internal set; }

    /// <summary>
    /// The allocated width of this window in pixels (the analogue of
    /// <c>winfo width</c>). Defaults to 1 until a geometry manager (or the
    /// layout root sizing) assigns it, like an unarranged Tk window.
    /// </summary>
    public int Width { get; internal set; } = 1;

    /// <summary>
    /// The allocated height of this window in pixels (the analogue of
    /// <c>winfo height</c>). Defaults to 1 until assigned.
    /// </summary>
    public int Height { get; internal set; } = 1;

    /// <summary>
    /// The internal border reserved on the left edge when this window is a
    /// container: geometry managers keep content inside the internal border
    /// (the analogue of <c>Tk_InternalBorderLeft</c>). For classic widgets
    /// this is borderwidth plus highlightthickness.
    /// </summary>
    public int InternalBorderLeft { get; private set; }

    /// <summary>The right-edge counterpart of <see cref="InternalBorderLeft"/>.</summary>
    public int InternalBorderRight { get; private set; }

    /// <summary>The top-edge counterpart of <see cref="InternalBorderLeft"/>.</summary>
    public int InternalBorderTop { get; private set; }

    /// <summary>The bottom-edge counterpart of <see cref="InternalBorderLeft"/>.</summary>
    public int InternalBorderBottom { get; private set; }

    /// <summary>
    /// Sets the same internal border on all four edges (the common case:
    /// a frame's borderwidth plus highlightthickness).
    /// </summary>
    /// <param name="uniform">The border in pixels for every edge (negative is clamped to 0).</param>
    public void SetInternalBorder(int uniform)
    {
        SetInternalBorders(uniform, uniform, uniform, uniform);
    }

    /// <summary>
    /// Sets the internal border per edge (needed by asymmetric containers
    /// such as labelframe, whose top border includes the label).
    /// </summary>
    /// <param name="left">The left border in pixels (negative is clamped to 0).</param>
    /// <param name="top">The top border in pixels (negative is clamped to 0).</param>
    /// <param name="right">The right border in pixels (negative is clamped to 0).</param>
    /// <param name="bottom">The bottom border in pixels (negative is clamped to 0).</param>
    public void SetInternalBorders(int left, int top, int right, int bottom)
    {
        InternalBorderLeft = (left < 0) ? 0 : left;
        InternalBorderTop = (top < 0) ? 0 : top;
        InternalBorderRight = (right < 0) ? 0 : right;
        InternalBorderBottom = (bottom < 0) ? 0 : bottom;
    }

    /// <summary>
    /// Whether the last layout pass actually placed this window: true for a
    /// window a geometry manager positioned (and that had room), false for an
    /// unmanaged window or one its manager had to unmap because no space was
    /// left (Tk unmaps content whose computed width or height is not
    /// positive). The root window is always displayed.
    /// </summary>
    public bool IsDisplayed { get; internal set; }

    /// <summary>
    /// The width forced on this window from outside the layout system, or
    /// null when the window sizes naturally. Meaningful for the layout root
    /// only (the analogue of a user/window-manager size, e.g.
    /// <c>wm geometry</c>); interior windows are sized by their manager.
    /// </summary>
    public int? ForcedWidth { get; private set; }

    /// <summary>The height counterpart of <see cref="ForcedWidth"/>.</summary>
    public int? ForcedHeight { get; private set; }

    /// <summary>
    /// Forces this window's size from outside the layout system (see
    /// <see cref="ForcedWidth"/>).
    /// </summary>
    /// <param name="width">The forced width in pixels (values below 1 are clamped to 1).</param>
    /// <param name="height">The forced height in pixels (values below 1 are clamped to 1).</param>
    public void SetForcedSize(int width, int height)
    {
        ForcedWidth = (width < 1) ? 1 : width;
        ForcedHeight = (height < 1) ? 1 : height;
        Tree.NotifyGeometryChanged();
    }

    /// <summary>Clears a forced size so the window sizes naturally again.</summary>
    public void ClearForcedSize()
    {
        ForcedWidth = null;
        ForcedHeight = null;
        Tree.NotifyGeometryChanged();
    }

    /// <summary>
    /// The geometry manager currently managing this window as content, or
    /// null when unmanaged.
    /// </summary>
    internal IGeometryManager ManagedBy;

    /// <summary>
    /// The container window this window is managed inside (its parent by
    /// default, or the <c>-in</c> target), or null when unmanaged.
    /// </summary>
    internal TkWindow Container;

    /// <summary>
    /// The packer state of this window when it is a pack container (created
    /// lazily by the pack engine), or null.
    /// </summary>
    internal PackContainerState PackContainer;

    /// <summary>
    /// The grid state of this window when it is a grid container (created
    /// lazily by the grid engine), or null.
    /// </summary>
    internal GridContainerState GridContainer;

    /// <summary>
    /// The name of the geometry manager that currently claims this window AS
    /// A CONTAINER ("pack" or "grid"), or null. Tk forbids two managers
    /// fighting over one container (error code TK GEOMETRY FIGHT); the claim
    /// is taken when content arrives and released when the container empties.
    /// </summary>
    internal string GeometryContainerName;

    /// <summary>
    /// Claims this window as a container for the named geometry manager,
    /// throwing the Tk "geometry fight" error when another manager already
    /// manages content inside it.
    /// </summary>
    /// <param name="managerName">The manager name ("pack" or "grid").</param>
    internal void ClaimContainer(string managerName)
    {
        if (GeometryContainerName == null)
        {
            GeometryContainerName = managerName;
        }
        else if (GeometryContainerName != managerName)
        {
            throw new InvalidOperationException(
                "cannot use geometry manager " + managerName + " inside " + PathName +
                " which already has slaves managed by " + GeometryContainerName);
        }
    }

    /// <summary>Releases a container claim taken by <see cref="ClaimContainer"/>.</summary>
    /// <param name="managerName">The manager name that held the claim.</param>
    internal void ReleaseContainer(string managerName)
    {
        if (GeometryContainerName == managerName)
        {
            GeometryContainerName = null;
        }
    }

    /// <summary>
    /// Destroys this window and its whole subtree: children are destroyed
    /// first, the window is forgotten by its geometry manager, any content
    /// managed inside it is released, and it is removed from its parent.
    /// </summary>
    public void Destroy()
    {
        if (IsDestroyed) { return; }

        // Children first (copy: Destroy mutates the list).
        TkWindow[] children = _children.ToArray();
        foreach (TkWindow child in children)
        {
            child.Destroy();
        }

        // Fire <Destroy> and release event-system references while the
        // window is still intact. The root's tree may not exist yet; do not
        // create one just to tear it down.
        TkWindow treeRoot = this;
        while (treeRoot.Parent != null) { treeRoot = treeRoot.Parent; }
        if (treeRoot._tree != null)
        {
            treeRoot._tree.WindowDestroyed(this);
        }

        // Release content managed INSIDE this window (content windows stay
        // alive if they are not descendants; they just become unmanaged).
        if (ManagedBy != null)
        {
            ManagedBy.ContentDestroyed(this);
            ManagedBy = null;
            Container = null;
        }

        if (PackContainer != null)
        {
            PackManager.Instance.ContainerDestroyed(this);
            PackContainer = null;
        }

        if (GridContainer != null)
        {
            GridManager.Instance.ContainerDestroyed(this);
            GridContainer = null;
        }

        if (Parent != null)
        {
            Parent._children.Remove(this);
        }

        IsDestroyed = true;
        IsDisplayed = false;
    }

    /// <summary>
    /// Finds a window in this window's tree by absolute Tk path name
    /// (e.g. <c>.a.b</c>). The lookup always starts from the tree's root,
    /// whichever window it is called on.
    /// </summary>
    /// <param name="pathName">The absolute path name; <c>.</c> is the root.</param>
    /// <returns>The window, or null when no window has that path.</returns>
    public TkWindow FindDescendant(string pathName)
    {
        if (string.IsNullOrEmpty(pathName) || pathName[0] != '.') { return null; }

        TkWindow current = this;
        while (current.Parent != null)
        {
            current = current.Parent;
        }

        if (pathName == ".") { return current; }

        string[] segments = pathName.Substring(1).Split('.');
        foreach (string segment in segments)
        {
            TkWindow next = null;
            foreach (TkWindow child in current._children)
            {
                if (child.Name == segment)
                {
                    next = child;
                    break;
                }
            }
            if (next == null) { return null; }
            current = next;
        }
        return current;
    }

    /// <summary>Returns the Tk path name of this window.</summary>
    /// <returns>The value of <see cref="PathName"/>.</returns>
    public override string ToString()
    {
        return PathName;
    }
}
