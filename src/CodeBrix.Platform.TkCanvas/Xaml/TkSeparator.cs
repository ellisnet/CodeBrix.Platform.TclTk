using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>Declares a Tk <c>ttk::separator</c> — a thin divider line.</summary>
public sealed class TkSeparator : TkElement
{
    /// <summary>The separator orientation (<c>-orient</c>: horizontal/vertical).</summary>
    public string Orient
    {
        get { return (string)GetValue(OrientProperty); }
        set { SetValue(OrientProperty, value); }
    }

    /// <summary>Identifies the <see cref="Orient"/> property.</summary>
    public static readonly DependencyProperty OrientProperty =
            RegisterOption(nameof(Orient), "-orient", typeof(TkSeparator));

    /// <summary>The materialized separator widget, or null before the host loads.</summary>
    public SeparatorWidget SeparatorWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        SeparatorWidget = new SeparatorWidget(window);
        return SeparatorWidget;
    }
}
