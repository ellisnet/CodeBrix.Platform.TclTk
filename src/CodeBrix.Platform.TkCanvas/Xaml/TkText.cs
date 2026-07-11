using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>text</c> — the multi-line editor widget.
/// <see cref="Text"/> sets the INITIAL content (and replaces it on later
/// property changes); interact with the live buffer through
/// <see cref="TextWidget"/> (e.g. via a view-model bridge interface).
/// </summary>
public sealed class TkText : TkElement
{
    /// <summary>The widget width in characters (<c>-width</c>; negative = default 80).</summary>
    public int WidthChars
    {
        get { return (int)GetValue(WidthCharsProperty); }
        set { SetValue(WidthCharsProperty, value); }
    }

    /// <summary>Identifies the <see cref="WidthChars"/> property.</summary>
    public static readonly DependencyProperty WidthCharsProperty =
            RegisterOption(nameof(WidthChars), "-width", typeof(TkText), typeof(int), -1);

    /// <summary>The widget height in lines (<c>-height</c>; negative = default 24).</summary>
    public int HeightLines
    {
        get { return (int)GetValue(HeightLinesProperty); }
        set { SetValue(HeightLinesProperty, value); }
    }

    /// <summary>Identifies the <see cref="HeightLines"/> property.</summary>
    public static readonly DependencyProperty HeightLinesProperty =
            RegisterOption(nameof(HeightLines), "-height", typeof(TkText), typeof(int), -1);

    /// <summary>The wrap mode (<c>-wrap</c>: char/word/none).</summary>
    public string Wrap
    {
        get { return (string)GetValue(WrapProperty); }
        set { SetValue(WrapProperty, value); }
    }

    /// <summary>Identifies the <see cref="Wrap"/> property.</summary>
    public static readonly DependencyProperty WrapProperty =
            RegisterOption(nameof(Wrap), "-wrap", typeof(TkText));

    /// <summary>The initial buffer content (declaration-time; later sets replace the content).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(TkText),
                    new PropertyMetadata("", (d, e) => ((TkText)d).OnTextChanged((string)e.NewValue)));

    /// <summary>The materialized text widget, or null before the host loads.</summary>
    public Text.TextWidget TextWidget { get; private set; }

    private void OnTextChanged(string value)
    {
        if (TextWidget == null) { return; }
        TextWidget.Delete("1.0", "end - 1 chars");
        if (!string.IsNullOrEmpty(value)) { TextWidget.Insert("1.0", value); }
        Host?.RequestUpdate();
    }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        TextWidget = new Text.TextWidget(window);
        return TextWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string text = Text;
        if (!string.IsNullOrEmpty(text)) { TextWidget.Insert("1.0", text); }
    }
}
