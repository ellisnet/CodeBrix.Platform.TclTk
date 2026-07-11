using CodeBrix.Platform.TkCanvas.Fonts;
using CodeBrix.Platform.TkCanvas.Overlay;
using CodeBrix.Platform.TkCanvas.Theming;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Rendering;

/// <summary>
/// The toolkit render pass: paints a whole window tree onto one Skia canvas
/// — the base layout first, then overlay toplevels (with their
/// TkCanvas-drawn chrome) in stacking order. Every displayed window paints
/// its widget (when it has one) clipped and translated to its allocated
/// rectangle, then its children bottom-to-top. The host calls
/// <see cref="Render"/> from its Skia paint handler; headless tests can
/// render into an offscreen surface.
/// </summary>
public static class TkRenderer
{
    /// <summary>
    /// Paints the whole tree rooted at <paramref name="root"/> onto
    /// <paramref name="canvas"/>, with (0,0) at the root window's top-left
    /// corner. The stage clears to the tree theme's stage background.
    /// </summary>
    /// <param name="root">The root window of the tree.</param>
    /// <param name="canvas">The target Skia canvas.</param>
    public static void Render(TkWindow root, SKCanvas canvas)
    {
        canvas.Clear(TkTheme.Color(root.Tree.Theme.StageBackground));
        PaintWindow(root, canvas);
    }

    /// <summary>
    /// Paints one window's subtree onto <paramref name="canvas"/> with (0,0)
    /// at that window's own top-left corner — the engine of the
    /// <c>image create photo -format window</c> widget snapshot. The stage
    /// background shows through wherever the subtree paints nothing.
    /// </summary>
    /// <param name="window">The window to paint.</param>
    /// <param name="canvas">The target Skia canvas.</param>
    public static void RenderWindow(TkWindow window, SKCanvas canvas)
    {
        canvas.Clear(TkTheme.Color(window.Tree.Theme.StageBackground));
        PaintWindow(window, canvas);
    }

    private static void PaintWindow(TkWindow window, SKCanvas canvas)
    {
        if (window.IsDestroyed || !window.IsDisplayed) { return; }

        IWidget widget = window.Widget;
        if (widget != null)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, window.Width, window.Height));
            widget.Paint(canvas);
            canvas.Restore();
        }

        foreach (TkWindow child in window.Children)
        {
            if (child.IsDestroyed || !child.IsDisplayed) { continue; }

            OverlayState overlay = child.Overlay;
            if (overlay != null)
            {
                PaintOverlayChrome(child, overlay, canvas);
            }

            canvas.Save();
            canvas.Translate(child.X, child.Y);
            PaintWindow(child, canvas);
            canvas.Restore();
        }
    }

    /// <summary>
    /// Paints an overlay toplevel's chrome (border, title bar, title text,
    /// close affordance) around its content rectangle, in the parent's
    /// coordinates. Chromeless (<c>wm overrideredirect</c>) overlays get a
    /// plain 1-pixel solid border only.
    /// </summary>
    private static void PaintOverlayChrome(TkWindow window, OverlayState overlay, SKCanvas canvas)
    {
        SKRectI frameI = overlay.FrameRect;
        var frame = new SKRect(frameI.Left, frameI.Top, frameI.Right, frameI.Bottom);

        TkTheme theme = window.Tree.Theme;
        SKColor chrome = TkTheme.Color(theme.Background);

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = false;
            paint.Style = SKPaintStyle.Fill;

            if (overlay.OverrideRedirect)
            {
                ReliefPainter.DrawBorder(canvas, frame, overlay.BorderWidth,
                        Relief.Solid, chrome);
                return;
            }

            // Frame background (fills the chrome band) + raised border.
            paint.Color = chrome;
            canvas.DrawRect(frame, paint);
            ReliefPainter.DrawBorder(canvas, frame, overlay.BorderWidth,
                    Relief.Raised, chrome);

            // Title bar.
            SKRectI barI = overlay.TitleBarRect;
            var bar = new SKRect(barI.Left, barI.Top, barI.Right, barI.Bottom);
            paint.Color = TkTheme.Color(theme.TitleBarBackground);
            canvas.DrawRect(bar, paint);

            // Title text through the font seam.
            FontManager fonts = window.Tree.Fonts;
            TkFont font = fonts.GetNamed("TkCaptionFont") ?? fonts.Parse("");
            if (!string.IsNullOrEmpty(overlay.Title))
            {
                FontMetrics metrics = fonts.Metrics(font);
                float baseline = bar.Top + (bar.Height - metrics.LineSpace) / 2f + metrics.Ascent;
                using (SKFont skFont = fonts.GetSkFont(font))
                {
                    paint.Color = TkTheme.Color(theme.TitleBarForeground);
                    paint.IsAntialias = true;
                    canvas.DrawText(overlay.Title, bar.Left + 6, baseline,
                            SKTextAlign.Left, skFont, paint);
                    paint.IsAntialias = false;
                }
            }

            // Close affordance: a raised box with an X.
            SKRectI closeI = overlay.CloseBoxRect;
            var close = new SKRect(closeI.Left, closeI.Top, closeI.Right, closeI.Bottom);
            paint.Color = chrome;
            canvas.DrawRect(close, paint);
            ReliefPainter.DrawBorder(canvas, close, 1, Relief.Raised, chrome);

            paint.Color = TkTheme.Color(theme.Foreground);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.5f;
            paint.IsAntialias = true;
            canvas.DrawLine(close.Left + 4, close.Top + 4, close.Right - 4, close.Bottom - 4, paint);
            canvas.DrawLine(close.Left + 4, close.Bottom - 4, close.Right - 4, close.Top + 4, paint);
        }
    }
}
