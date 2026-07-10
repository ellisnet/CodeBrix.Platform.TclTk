using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Overlay;

/// <summary>
/// The window-manager state of one overlay toplevel (the plan's §11 Option C
/// model): a Tk <c>toplevel</c> rendered as a Skia overlay window inside the
/// host canvas. The wrapped <see cref="TkWindow"/> rectangle is the CONTENT
/// area (what <c>wm geometry</c> addresses, like a real toplevel); the
/// chrome — title bar, border — is drawn OUTSIDE it by the toolkit renderer
/// and owned by the mini window-manager, exactly as OS decorations belong to
/// the OS window manager and are invisible to Tk bindings.
/// </summary>
public sealed class OverlayState
{
    internal OverlayState(TkWindow window)
    {
        Window = window;
    }

    /// <summary>The toplevel window this state decorates.</summary>
    public TkWindow Window { get; }

    /// <summary>The title text (<c>wm title</c>), drawn in the chrome title bar.</summary>
    public string Title { get; set; } = "";

    /// <summary>Whether the overlay is withdrawn (<c>wm withdraw</c>/<c>deiconify</c>).</summary>
    public bool Withdrawn { get; internal set; }

    /// <summary>
    /// Whether the overlay draws NO chrome (<c>wm overrideredirect</c>) —
    /// tooltips, splash panels, popup menus.
    /// </summary>
    public bool OverrideRedirect { get; set; }

    /// <summary>Whether the user may resize horizontally (<c>wm resizable</c>).</summary>
    public bool ResizableWidth { get; set; } = true;

    /// <summary>Whether the user may resize vertically (<c>wm resizable</c>).</summary>
    public bool ResizableHeight { get; set; } = true;

    /// <summary>
    /// The window this overlay is transient for (<c>wm transient</c>): it
    /// stacks above its master and is destroyed with it. Null when not
    /// transient.
    /// </summary>
    public TkWindow TransientFor { get; internal set; }

    /// <summary>The content width requested via <c>wm geometry</c>, or null for natural sizing.</summary>
    public int? GeometryWidth { get; internal set; }

    /// <summary>The content height requested via <c>wm geometry</c>, or null.</summary>
    public int? GeometryHeight { get; internal set; }

    /// <summary>The content x position requested via <c>wm geometry</c>, or null.</summary>
    public int? GeometryX { get; internal set; }

    /// <summary>The content y position requested via <c>wm geometry</c>, or null.</summary>
    public int? GeometryY { get; internal set; }

    /// <summary>The chrome border width around the content, in pixels.</summary>
    public int BorderWidth
    {
        get { return OverrideRedirect ? 1 : 2; }
    }

    /// <summary>The chrome title-bar height above the content (0 without chrome).</summary>
    public int TitleBarHeight { get; internal set; } = 22;

    /// <summary>The full frame rectangle: the content plus chrome, in root coordinates.</summary>
    public SKRectI FrameRect
    {
        get
        {
            int border = BorderWidth;
            int titleBar = OverrideRedirect ? 0 : TitleBarHeight;
            return new SKRectI(
                    Window.X - border,
                    Window.Y - border - titleBar,
                    Window.X + Window.Width + border,
                    Window.Y + Window.Height + border);
        }
    }

    /// <summary>The title-bar rectangle in root coordinates (empty without chrome).</summary>
    public SKRectI TitleBarRect
    {
        get
        {
            if (OverrideRedirect) { return SKRectI.Empty; }
            SKRectI frame = FrameRect;
            return new SKRectI(
                    frame.Left + BorderWidth,
                    frame.Top + BorderWidth,
                    frame.Right - BorderWidth,
                    frame.Top + BorderWidth + TitleBarHeight);
        }
    }

    /// <summary>The close-affordance rectangle inside the title bar (empty without chrome).</summary>
    public SKRectI CloseBoxRect
    {
        get
        {
            if (OverrideRedirect) { return SKRectI.Empty; }
            SKRectI bar = TitleBarRect;
            int size = bar.Height - 6;
            return new SKRectI(bar.Right - size - 4, bar.Top + 3, bar.Right - 4, bar.Bottom - 3);
        }
    }
}
