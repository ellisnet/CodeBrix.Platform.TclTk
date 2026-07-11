using System.Windows.Input;

using CodeBrix.Platform.TkCanvas.Canvas;
using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>ttk::combobox</c>. <see cref="SelectionCommand"/>
/// fires with the newly selected value when the user picks from the
/// drop-down.
/// </summary>
public sealed class TkCombobox : TkElement
{
    /// <summary>The drop-down values as a Tcl-style list.</summary>
    public string Items
    {
        get { return (string)GetValue(ItemsProperty); }
        set { SetValue(ItemsProperty, value); }
    }

    /// <summary>Identifies the <see cref="Items"/> property.</summary>
    public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(nameof(Items), typeof(string), typeof(TkCombobox),
                    new PropertyMetadata(""));

    /// <summary>The initially selected value.</summary>
    public string Value
    {
        get { return (string)GetValue(ValueProperty); }
        set { SetValue(ValueProperty, value); }
    }

    /// <summary>Identifies the <see cref="Value"/> property.</summary>
    public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(TkCombobox),
                    new PropertyMetadata(""));

    /// <summary>The command fired with the selected value on <c>&lt;&lt;ComboboxSelected&gt;&gt;</c>.</summary>
    public ICommand SelectionCommand
    {
        get { return (ICommand)GetValue(SelectionCommandProperty); }
        set { SetValue(SelectionCommandProperty, value); }
    }

    /// <summary>Identifies the <see cref="SelectionCommand"/> property.</summary>
    public static readonly DependencyProperty SelectionCommandProperty =
            DependencyProperty.Register(nameof(SelectionCommand), typeof(ICommand), typeof(TkCombobox),
                    new PropertyMetadata(null));

    /// <summary>The materialized combobox widget, or null before the host loads.</summary>
    public ComboboxWidget ComboboxWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        ComboboxWidget = new ComboboxWidget(window);
        return ComboboxWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string items = Items;
        if (!string.IsNullOrEmpty(items))
        {
            ComboboxWidget.SetValues(TclString.SplitList(items).ToArray());
        }
        string value = Value;
        if (!string.IsNullOrEmpty(value)) { ComboboxWidget.SetValue(value); }
        ComboboxWidget.Selected += () => ExecuteCommand(SelectionCommand, ComboboxWidget.Value);
    }
}
