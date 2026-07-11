using System.Globalization;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>scrollbar</c>. Set <see cref="For"/> to the
/// <c>Name</c> of a listbox, treeview, or text element and the classic
/// two-way scroll protocol (<c>-yscrollcommand</c> / <c>yview</c>) is wired
/// automatically; other pairings wire in code through
/// <see cref="ScrollbarWidget"/>.
/// </summary>
public sealed class TkScrollbar : TkElement
{
    /// <summary>The scrollbar orientation (<c>-orient</c>: vertical/horizontal).</summary>
    public string Orient
    {
        get { return (string)GetValue(OrientProperty); }
        set { SetValue(OrientProperty, value); }
    }

    /// <summary>Identifies the <see cref="Orient"/> property.</summary>
    public static readonly DependencyProperty OrientProperty =
            RegisterOption(nameof(Orient), "-orient", typeof(TkScrollbar));

    /// <summary>The <c>Name</c> of the scrollable element this scrollbar drives.</summary>
    public string For
    {
        get { return (string)GetValue(ForProperty); }
        set { SetValue(ForProperty, value); }
    }

    /// <summary>Identifies the <see cref="For"/> property.</summary>
    public static readonly DependencyProperty ForProperty =
            DependencyProperty.Register(nameof(For), typeof(string), typeof(TkScrollbar),
                    new PropertyMetadata(""));

    /// <summary>The materialized scrollbar widget, or null before the host loads.</summary>
    public ScrollbarWidget ScrollbarWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        ScrollbarWidget = new ScrollbarWidget(window);
        return ScrollbarWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string target = For;
        if (string.IsNullOrEmpty(target)) { return; }

        TkElement element = host.FindTkElement(target);
        if (element == null) { return; }

        switch (element.TkWidget)
        {
            case ListboxWidget listbox:
                listbox.YScrollChanged += ScrollbarWidget.Set;
                ScrollbarWidget.Command += words => Drive(words, listbox.YViewMoveTo, listbox.YViewScroll);
                break;
            case TreeviewWidget treeview:
                treeview.YScrollChanged += ScrollbarWidget.Set;
                ScrollbarWidget.Command += words => Drive(words, treeview.YViewMoveTo, treeview.YViewScroll);
                break;
            case Text.TextWidget text:
                text.YScrollChanged += ScrollbarWidget.Set;
                ScrollbarWidget.Command += words => Drive(words, text.YViewMoveTo, text.YViewScroll);
                break;
            default:
                break; // unsupported pairings wire in code
        }
    }

    private static void Drive(string[] words,
            System.Action<double> moveTo, System.Action<int, bool> scroll)
    {
        if (words == null || words.Length == 0) { return; }
        if (words[0] == "moveto" && words.Length >= 2)
        {
            double fraction;
            if (double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out fraction))
            {
                moveTo(fraction);
            }
        }
        else if (words[0] == "scroll" && words.Length >= 3)
        {
            int count;
            if (int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                scroll(count, words[2] == "pages");
            }
        }
    }
}
