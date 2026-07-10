using SkiaSharp;

namespace CodeBrix.Platform.TkCanvas.Canvas;

/// <summary>
/// The canvas <c>window</c> item (Tk 8.6.16 tkCanvWind.c): an embedded child
/// widget positioned by <c>-anchor</c> at a single point, optionally forced
/// to <c>-width</c>/<c>-height</c>. Embedding a live control is its own module
/// (§3.20), so v1 registers the type, accepts and stores every option, and
/// sizes its bounding box from an explicit <c>-width</c>/<c>-height</c> when
/// given (so geometry-driven Tk code still lays out correctly); painting the
/// embedded control is deferred and no-ops. Never throws.
/// </summary>
public sealed class WindowItem : AnchoredCanvasItem
{
    private int _width;
    private int _height;

    /// <inheritdoc/>
    public override string TypeName
    {
        get { return "window"; }
    }

    private protected override int ContentWidth
    {
        get { return _width; }
    }

    private protected override int ContentHeight
    {
        get { return _height; }
    }

    private protected override void OnConfigured()
    {
        base.OnConfigured();

        int value;
        _width = TclString.TryParsePixels(Options.Get("-width", "0"), out value) ? value : 0;
        if (_width < 0) { _width = 0; }
        _height = TclString.TryParsePixels(Options.Get("-height", "0"), out value) ? value : 0;
        if (_height < 0) { _height = 0; }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas)
    {
        // Deferred: embedding a live control is its own module (§3.20).
    }
}
