using System.Collections.Generic;

using CodeBrix.Platform.TkCanvas.Widgets;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas scene-graph item contract (the plan's §3.3): every item type —
/// line, rectangle, polygon, text, and the types still to come — implements
/// this against the owning <see cref="CanvasWidget"/>. Items live in the
/// canvas display list; the canvas routes painting, hit-testing with the
/// <c>-closeenough</c> halo, option configuration, and coordinate access
/// through this interface.
/// </summary>
public interface ICanvasItem
{
    /// <summary>The canvas-unique item id (assigned by <c>create</c>, starting at 1).</summary>
    int Id { get; }

    /// <summary>The item type name (<c>line</c>, <c>rectangle</c>, <c>polygon</c>, <c>text</c>, ...).</summary>
    string TypeName { get; }

    /// <summary>The item's tags, in the order they were added (<c>gettags</c>).</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>The item's option bag (accept-and-store, like widget options).</summary>
    WidgetOptions Options { get; }

    /// <summary>
    /// The item's integer bounding box in canvas coordinates — Tk's item
    /// header box that <c>bbox</c> unions and the quick-reject tests use.
    /// Empty (left ≥ right) means the item currently occupies no area.
    /// </summary>
    SKRectI Bounds { get; }

    /// <summary>
    /// Whether the point (in canvas coordinates) hits the item within the
    /// given halo — the <c>-closeenough</c> click tolerance. Equivalent to
    /// the item's Tk point distance being at most <paramref name="halo"/>.
    /// </summary>
    /// <param name="point">The point in canvas coordinates.</param>
    /// <param name="halo">The tolerance in pixels (0 = exact).</param>
    /// <returns>True when the item is hit.</returns>
    bool HitTest(SKPoint point, double halo);

    /// <summary>
    /// Draws the item onto <paramref name="canvas"/>, which is already
    /// transformed so item coordinates are canvas coordinates.
    /// </summary>
    /// <param name="canvas">The target Skia canvas.</param>
    void Paint(SKCanvas canvas);

    /// <summary>
    /// Applies option changes (<c>itemconfigure</c>): every option is
    /// stored (unknown ones included), known options are interpreted, and
    /// the bounding box is recomputed.
    /// </summary>
    /// <param name="options">The option name/value pairs.</param>
    void Configure(IReadOnlyDictionary<string, string> options);

    /// <summary>Reads the item's coordinates (<c>coords</c> with no arguments).</summary>
    /// <returns>The coordinate list, x/y interleaved.</returns>
    IReadOnlyList<double> GetCoords();

    /// <summary>Replaces the item's coordinates (<c>coords</c> with arguments).</summary>
    /// <param name="coords">The new coordinate list, x/y interleaved.</param>
    void SetCoords(IReadOnlyList<double> coords);
}
