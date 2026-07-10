using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Widgets;

/// <summary>
/// The toolkit-wide widget contract. A widget OWNS a <see cref="TkWindow"/>
/// (composition, mirroring Tk's window/widget-record split): the window
/// carries the tree position, geometry-manager participation, and allocated
/// geometry; the widget adds appearance and behavior. Concrete widgets set
/// the window's class name (their class bind tag), register class bindings
/// on the tree's binding table, request their natural size via
/// <see cref="Measure"/>, and draw themselves in <see cref="Paint"/>.
/// </summary>
public interface IWidget
{
    /// <summary>The window this widget owns.</summary>
    TkWindow Window { get; }

    /// <summary>The widget class name (<c>Button</c>, <c>Label</c>, ...).</summary>
    string ClassName { get; }

    /// <summary>The widget's option bag (accept-and-store, see <see cref="WidgetOptions"/>).</summary>
    WidgetOptions Options { get; }

    /// <summary>
    /// Computes the widget's natural size from its current options (text,
    /// font, image, borders, ...) and requests it on the window
    /// (<see cref="TkWindow.SetRequestedSize"/>), like a Tk widget calling
    /// <c>Tk_GeometryRequest</c> after reconfiguration.
    /// </summary>
    void Measure();

    /// <summary>
    /// Draws the widget onto <paramref name="canvas"/>. The canvas is
    /// already translated so (0,0) is the widget window's top-left corner,
    /// and clipped to the window's allocated width/height.
    /// </summary>
    /// <param name="canvas">The target Skia canvas.</param>
    void Paint(SKCanvas canvas);

    /// <summary>
    /// Refines pointer hit-testing within the window's rectangle (the
    /// window-tree hit test already guarantees the point is inside the
    /// window). Rectangular widgets simply return true.
    /// </summary>
    /// <param name="point">The point in window-relative coordinates.</param>
    /// <returns>True when the point hits the widget.</returns>
    bool HitTest(SKPoint point);

    /// <summary>
    /// Applies option changes — the analogue of <c>$w configure ...</c>.
    /// Implementations store every option (unknown ones included), interpret
    /// the ones they understand, and re-<see cref="Measure"/> when a change
    /// affects the natural size.
    /// </summary>
    /// <param name="options">The option name/value pairs to apply.</param>
    void Configure(System.Collections.Generic.IReadOnlyDictionary<string, string> options);
}
