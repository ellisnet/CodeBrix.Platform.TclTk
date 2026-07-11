using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>frame</c> — the plain container. Nest other elements
/// inside it in XAML to pack/grid them into the frame.
/// </summary>
public sealed class TkFrame : TkElement
{
    /// <summary>The materialized frame widget, or null before the host loads.</summary>
    public FrameWidget FrameWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        FrameWidget = new FrameWidget(window);
        return FrameWidget;
    }
}
