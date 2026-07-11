using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>panedwindow</c>. Its nested elements become panes (in
/// declaration order) separated by draggable sashes — they are managed by
/// the paned window, so their pack/grid layout properties are ignored.
/// </summary>
public sealed class TkPanedwindow : TkElement
{
    /// <summary>The pane orientation (<c>-orient</c>: horizontal/vertical).</summary>
    public string Orient
    {
        get { return (string)GetValue(OrientProperty); }
        set { SetValue(OrientProperty, value); }
    }

    /// <summary>Identifies the <see cref="Orient"/> property.</summary>
    public static readonly DependencyProperty OrientProperty =
            RegisterOption(nameof(Orient), "-orient", typeof(TkPanedwindow));

    /// <summary>The materialized panedwindow widget, or null before the host loads.</summary>
    public PanedWindowWidget PanedWindowWidget { get; private set; }

    private protected override bool ArrangesOwnChildren
    {
        get { return true; }
    }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        PanedWindowWidget = new PanedWindowWidget(window);
        return PanedWindowWidget;
    }

    private protected override void OnChildMaterialized(TkElement child)
    {
        PanedWindowWidget.Add(child.TkWindow);
    }
}
