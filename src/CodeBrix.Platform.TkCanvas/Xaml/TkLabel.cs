using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>label</c>: static text, or a photo image when
/// <see cref="Image"/> names one.
/// </summary>
public sealed class TkLabel : TkElement
{
    /// <summary>The label text (<c>-text</c>).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            RegisterOption(nameof(Text), "-text", typeof(TkLabel));

    /// <summary>The photo-image name to display instead of text (<c>-image</c>).</summary>
    public string Image
    {
        get { return (string)GetValue(ImageProperty); }
        set { SetValue(ImageProperty, value); }
    }

    /// <summary>Identifies the <see cref="Image"/> property.</summary>
    public static readonly DependencyProperty ImageProperty =
            RegisterOption(nameof(Image), "-image", typeof(TkLabel));

    /// <summary>Where the content sits in the label (<c>-anchor</c>: n/s/e/w/center/...).</summary>
    public string ContentAnchor
    {
        get { return (string)GetValue(ContentAnchorProperty); }
        set { SetValue(ContentAnchorProperty, value); }
    }

    /// <summary>Identifies the <see cref="ContentAnchor"/> property.</summary>
    public static readonly DependencyProperty ContentAnchorProperty =
            RegisterOption(nameof(ContentAnchor), "-anchor", typeof(TkLabel));

    /// <summary>The multi-line justification (<c>-justify</c>: left/center/right).</summary>
    public string Justify
    {
        get { return (string)GetValue(JustifyProperty); }
        set { SetValue(JustifyProperty, value); }
    }

    /// <summary>Identifies the <see cref="Justify"/> property.</summary>
    public static readonly DependencyProperty JustifyProperty =
            RegisterOption(nameof(Justify), "-justify", typeof(TkLabel));

    /// <summary>The materialized label widget, or null before the host loads.</summary>
    public LabelWidget LabelWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        LabelWidget = new LabelWidget(window);
        return LabelWidget;
    }
}
