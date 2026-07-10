using System;
using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Fonts;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The shared text-block measuring and drawing used by the text-bearing
/// classic widgets (label, button, and the toggles): it measures a possibly
/// multi-line label through the toolkit font seam, positions the block inside
/// a content rectangle by Tk <c>-anchor</c>, and draws each line with the
/// requested <c>-justify</c>. Because all measurement goes through the same
/// <see cref="FontManager"/> the painter draws with, the placement always
/// matches what is rendered.
/// </summary>
internal static class WidgetText
{
    /// <summary>Measures the block size (max line advance × line count) of a label.</summary>
    /// <param name="fonts">The font seam.</param>
    /// <param name="font">The font.</param>
    /// <param name="text">The (possibly multi-line) text.</param>
    /// <param name="width">Receives the block width in pixels.</param>
    /// <param name="height">Receives the block height in pixels.</param>
    public static void MeasureBlock(FontManager fonts, TkFont font, string text,
            out int width, out int height)
    {
        string[] lines = SplitLines(text);
        int lineHeight = fonts.Metrics(font).LineSpace;
        int w = 0;
        foreach (string line in lines)
        {
            int lw = fonts.Measure(font, line);
            if (lw > w) { w = lw; }
        }
        width = w;
        height = lines.Length * lineHeight;
    }

    /// <summary>
    /// Draws a text block inside <paramref name="content"/>, anchored by
    /// <paramref name="anchor"/> and per-line aligned by
    /// <paramref name="justify"/>, in <paramref name="color"/>.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="fonts">The font seam.</param>
    /// <param name="font">The font.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="content">The content rectangle (inside padding/border).</param>
    /// <param name="anchor">The Tk anchor of the block within the content box.</param>
    /// <param name="justify">The per-line justification (<c>left</c>/<c>center</c>/<c>right</c>).</param>
    /// <param name="color">The text color.</param>
    public static void DrawBlock(SKCanvas canvas, FontManager fonts, TkFont font, string text,
            SKRect content, Canvas.CanvasAnchor anchor, string justify, SKColor color)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        string[] lines = SplitLines(text);
        FontMetrics metrics = fonts.Metrics(font);
        int lineHeight = metrics.LineSpace;
        int blockWidth;
        int blockHeight;
        MeasureBlock(fonts, font, text, out blockWidth, out blockHeight);

        float blockLeft;
        switch (anchor)
        {
            case Canvas.CanvasAnchor.W:
            case Canvas.CanvasAnchor.NW:
            case Canvas.CanvasAnchor.SW:
                blockLeft = content.Left; break;
            case Canvas.CanvasAnchor.E:
            case Canvas.CanvasAnchor.NE:
            case Canvas.CanvasAnchor.SE:
                blockLeft = content.Right - blockWidth; break;
            default:
                blockLeft = content.MidX - blockWidth / 2f; break;
        }

        float blockTop;
        switch (anchor)
        {
            case Canvas.CanvasAnchor.N:
            case Canvas.CanvasAnchor.NW:
            case Canvas.CanvasAnchor.NE:
                blockTop = content.Top; break;
            case Canvas.CanvasAnchor.S:
            case Canvas.CanvasAnchor.SW:
            case Canvas.CanvasAnchor.SE:
                blockTop = content.Bottom - blockHeight; break;
            default:
                blockTop = content.MidY - blockHeight / 2f; break;
        }

        using (SKFont skFont = fonts.GetSkFont(font))
        using (var paint = new SKPaint())
        {
            paint.Color = color;
            paint.IsAntialias = true;
            for (int i = 0; i < lines.Length; i++)
            {
                int lineWidth = fonts.Measure(font, lines[i]);
                float x;
                switch (justify)
                {
                    case "right": x = blockLeft + (blockWidth - lineWidth); break;
                    case "center": x = blockLeft + (blockWidth - lineWidth) / 2f; break;
                    default: x = blockLeft; break;
                }
                float baseline = blockTop + i * lineHeight + metrics.Ascent;
                canvas.DrawText(lines[i], x, baseline, SKTextAlign.Left, skFont, paint);
            }
        }
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) { return new string[] { "" }; }
        return text.Replace("\r\n", "\n").Split('\n');
    }
}
