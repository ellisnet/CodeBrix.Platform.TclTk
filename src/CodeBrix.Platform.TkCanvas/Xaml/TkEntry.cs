using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>entry</c> — the single-line text field. <see cref="Text"/>
/// sets the INITIAL content (and pushes on later property changes); read the
/// live text back through <see cref="EntryWidget"/> (e.g. via a view-model
/// bridge interface) — there is no live two-way synchronization.
/// </summary>
public sealed class TkEntry : TkElement
{
    /// <summary>The field width in characters (<c>-width</c>; negative = default).</summary>
    public int WidthChars
    {
        get { return (int)GetValue(WidthCharsProperty); }
        set { SetValue(WidthCharsProperty, value); }
    }

    /// <summary>Identifies the <see cref="WidthChars"/> property.</summary>
    public static readonly DependencyProperty WidthCharsProperty =
            RegisterOption(nameof(WidthChars), "-width", typeof(TkEntry), typeof(int), -1);

    /// <summary>The masking character for secret fields (<c>-show</c>).</summary>
    public string Show
    {
        get { return (string)GetValue(ShowProperty); }
        set { SetValue(ShowProperty, value); }
    }

    /// <summary>Identifies the <see cref="Show"/> property.</summary>
    public static readonly DependencyProperty ShowProperty =
            RegisterOption(nameof(Show), "-show", typeof(TkEntry));

    /// <summary>The initial entry text (declaration-time; later sets replace the content).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(TkEntry),
                    new PropertyMetadata("", (d, e) => ((TkEntry)d).OnTextChanged((string)e.NewValue)));

    /// <summary>The materialized entry widget, or null before the host loads.</summary>
    public EntryWidget EntryWidget { get; private set; }

    private void OnTextChanged(string value)
    {
        if (EntryWidget == null) { return; }
        EntryWidget.SetText(value ?? "");
        Host?.RequestUpdate();
    }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        EntryWidget = new EntryWidget(window);
        return EntryWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string text = Text;
        if (!string.IsNullOrEmpty(text)) { EntryWidget.SetText(text); }
    }
}
