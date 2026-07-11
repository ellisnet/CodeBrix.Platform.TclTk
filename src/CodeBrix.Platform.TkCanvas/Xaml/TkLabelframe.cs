using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>labelframe</c> — a container with a caption in its
/// border.
/// </summary>
public sealed class TkLabelframe : TkElement
{
    /// <summary>The caption text (<c>-text</c>).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            RegisterOption(nameof(Text), "-text", typeof(TkLabelframe));

    /// <summary>The materialized labelframe widget, or null before the host loads.</summary>
    public LabelframeWidget LabelframeWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        LabelframeWidget = new LabelframeWidget(window);
        return LabelframeWidget;
    }
}
