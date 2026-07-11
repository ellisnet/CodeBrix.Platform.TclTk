using System.Collections;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>listbox</c>. Initial items come from
/// <see cref="Items"/> (a Tcl-style list) or <see cref="ItemsSource"/> (a
/// one-shot read of any enumerable, e.g. a view-model collection at load
/// time); manage the live list through <see cref="ListboxWidget"/>.
/// </summary>
public sealed class TkListbox : TkElement
{
    /// <summary>The listbox height in rows (<c>-height</c>; negative = default).</summary>
    public int HeightRows
    {
        get { return (int)GetValue(HeightRowsProperty); }
        set { SetValue(HeightRowsProperty, value); }
    }

    /// <summary>Identifies the <see cref="HeightRows"/> property.</summary>
    public static readonly DependencyProperty HeightRowsProperty =
            RegisterOption(nameof(HeightRows), "-height", typeof(TkListbox), typeof(int), -1);

    /// <summary>The initial items as a Tcl-style list (e.g. <c>one two {item three}</c>).</summary>
    public string Items
    {
        get { return (string)GetValue(ItemsProperty); }
        set { SetValue(ItemsProperty, value); }
    }

    /// <summary>Identifies the <see cref="Items"/> property.</summary>
    public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(nameof(Items), typeof(string), typeof(TkListbox),
                    new PropertyMetadata(""));

    /// <summary>The initial items from an enumerable (read once at materialization).</summary>
    public IEnumerable ItemsSource
    {
        get { return (IEnumerable)GetValue(ItemsSourceProperty); }
        set { SetValue(ItemsSourceProperty, value); }
    }

    /// <summary>Identifies the <see cref="ItemsSource"/> property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(TkListbox),
                    new PropertyMetadata(null));

    /// <summary>The materialized listbox widget, or null before the host loads.</summary>
    public ListboxWidget ListboxWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        ListboxWidget = new ListboxWidget(window);
        return ListboxWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string items = Items;
        if (!string.IsNullOrEmpty(items))
        {
            foreach (string item in TclString.SplitList(items))
            {
                ListboxWidget.Insert(ListboxWidget.Size, item);
            }
        }
        IEnumerable source = ItemsSource;
        if (source != null)
        {
            foreach (object item in source)
            {
                ListboxWidget.Insert(ListboxWidget.Size, item?.ToString() ?? "");
            }
        }
    }
}
