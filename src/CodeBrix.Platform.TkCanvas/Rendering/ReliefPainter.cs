using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Rendering;

/// <summary>The Tk 3D relief styles (<c>-relief</c>).</summary>
public enum Relief
{
    /// <summary>No border drawn.</summary>
    Flat,

    /// <summary>Light top/left, dark bottom/right — the element pops out.</summary>
    Raised,

    /// <summary>Dark top/left, light bottom/right — the element sinks in.</summary>
    Sunken,

    /// <summary>Outer half sunken, inner half raised — a carved groove.</summary>
    Groove,

    /// <summary>Outer half raised, inner half sunken — a raised ridge.</summary>
    Ridge,

    /// <summary>A plain dark border.</summary>
    Solid,
}

/// <summary>
/// THE shared 3D relief/border painting primitive (the plan's §3.8): every
/// widget — frames, buttons, entries, the overlay window chrome — draws its
/// Tk border through this one component, so the classic look stays
/// consistent and a second relief implementation never appears. Shadow
/// colors follow Tk's tkUnix3d.c derivation from the background (dark = 60%
/// brightness, light = the brighter of 140% and halfway-to-white).
/// </summary>
public static class ReliefPainter
{
    /// <summary>Parses a Tk relief name (unknown text is <see cref="Relief.Flat"/>).</summary>
    /// <param name="text">The relief name.</param>
    /// <returns>The relief.</returns>
    public static Relief Parse(string text)
    {
        switch (text)
        {
            case "raised": return Relief.Raised;
            case "sunken": return Relief.Sunken;
            case "groove": return Relief.Groove;
            case "ridge": return Relief.Ridge;
            case "solid": return Relief.Solid;
            default: return Relief.Flat;
        }
    }

    /// <summary>The light (highlight) shadow of a background color — Tk's TkpGetShadows.</summary>
    /// <param name="background">The base background.</param>
    /// <returns>The light shadow color.</returns>
    public static SKColor LightShadow(SKColor background)
    {
        return new SKColor(
                LightComponent(background.Red),
                LightComponent(background.Green),
                LightComponent(background.Blue));
    }

    /// <summary>The dark shadow of a background color — Tk's TkpGetShadows.</summary>
    /// <param name="background">The base background.</param>
    /// <returns>The dark shadow color.</returns>
    public static SKColor DarkShadow(SKColor background)
    {
        return new SKColor(
                (byte)(background.Red * 6 / 10),
                (byte)(background.Green * 6 / 10),
                (byte)(background.Blue * 6 / 10));
    }

    private static byte LightComponent(byte value)
    {
        int brighter = value * 14 / 10;
        int halfway = (value + 255) / 2;
        int light = (brighter > halfway) ? brighter : halfway;
        return (byte)((light > 255) ? 255 : light);
    }

    /// <summary>
    /// Draws a Tk 3D border of the given relief just inside
    /// <paramref name="rect"/> — the analogue of <c>Tk_Draw3DRectangle</c>.
    /// The interior is not filled; callers fill their own background first.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="rect">The outer rectangle of the border.</param>
    /// <param name="borderWidth">The border width in pixels (≤ 0 draws nothing).</param>
    /// <param name="relief">The relief style.</param>
    /// <param name="background">The background the shadows derive from.</param>
    public static void DrawBorder(SKCanvas canvas, SKRect rect, int borderWidth,
            Relief relief, SKColor background)
    {
        if (borderWidth <= 0 || relief == Relief.Flat) { return; }
        if (rect.Width <= 0 || rect.Height <= 0) { return; }

        SKColor light = LightShadow(background);
        SKColor dark = DarkShadow(background);

        switch (relief)
        {
            case Relief.Raised:
            {
                DrawBevel(canvas, rect, borderWidth, light, dark);
                break;
            }
            case Relief.Sunken:
            {
                DrawBevel(canvas, rect, borderWidth, dark, light);
                break;
            }
            case Relief.Ridge:
            {
                int outer = borderWidth / 2;
                int inner = borderWidth - outer;
                DrawBevel(canvas, rect, outer, light, dark);
                DrawBevel(canvas, SKRect.Inflate(rect, -outer, -outer), inner, dark, light);
                break;
            }
            case Relief.Groove:
            {
                int outer = borderWidth / 2;
                int inner = borderWidth - outer;
                DrawBevel(canvas, rect, outer, dark, light);
                DrawBevel(canvas, SKRect.Inflate(rect, -outer, -outer), inner, light, dark);
                break;
            }
            case Relief.Solid:
            {
                using (var paint = new SKPaint())
                {
                    paint.Color = SKColors.Black;
                    paint.Style = SKPaintStyle.Fill;
                    FillFrame(canvas, paint, rect, borderWidth);
                }
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>
    /// Draws one bevel ring: mitered trapezoids with the top/left in
    /// <paramref name="topLeft"/> and the bottom/right in
    /// <paramref name="bottomRight"/> (the classic Tk corner cut).
    /// </summary>
    private static void DrawBevel(SKCanvas canvas, SKRect rect, int width,
            SKColor topLeft, SKColor bottomRight)
    {
        if (width <= 0) { return; }
        float w = width;
        float l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom;

        using (var paint = new SKPaint())
        {
            paint.Style = SKPaintStyle.Fill;
            paint.IsAntialias = false;

            // Top edge and left edge (light for raised).
            paint.Color = topLeft;
            FillQuad(canvas, paint, l, t, r, t, r - w, t + w, l + w, t + w);
            FillQuad(canvas, paint, l, t, l + w, t + w, l + w, b - w, l, b);

            // Bottom edge and right edge (dark for raised).
            paint.Color = bottomRight;
            FillQuad(canvas, paint, l, b, l + w, b - w, r - w, b - w, r, b);
            FillQuad(canvas, paint, r, t, r, b, r - w, b - w, r - w, t + w);
        }
    }

    private static void FillQuad(SKCanvas canvas, SKPaint paint,
            float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        var builder = new SKPathBuilder();
        builder.MoveTo(x0, y0);
        builder.LineTo(x1, y1);
        builder.LineTo(x2, y2);
        builder.LineTo(x3, y3);
        builder.Close();
        using (SKPath path = builder.Detach())
        {
            canvas.DrawPath(path, paint);
        }
    }

    private static void FillFrame(SKCanvas canvas, SKPaint paint, SKRect rect, int width)
    {
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + width), paint);
        canvas.DrawRect(new SKRect(rect.Left, rect.Bottom - width, rect.Right, rect.Bottom), paint);
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + width, rect.Bottom), paint);
        canvas.DrawRect(new SKRect(rect.Right - width, rect.Top, rect.Right, rect.Bottom), paint);
    }
}
