using System.Windows.Input;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>radiobutton</c>. Buttons sharing the same
/// <see cref="Group"/> name share one toggle variable (mutual exclusion);
/// <see cref="Value"/> is this button's value in the group.
/// <see cref="Command"/> fires with that value when the button is selected.
/// </summary>
public sealed class TkRadiobutton : TkElement
{
    /// <summary>The radiobutton text (<c>-text</c>).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            RegisterOption(nameof(Text), "-text", typeof(TkRadiobutton));

    /// <summary>This button's value within the group (<c>-value</c>).</summary>
    public string Value
    {
        get { return (string)GetValue(ValueProperty); }
        set { SetValue(ValueProperty, value); }
    }

    /// <summary>Identifies the <see cref="Value"/> property.</summary>
    public static readonly DependencyProperty ValueProperty =
            RegisterOption(nameof(Value), "-value", typeof(TkRadiobutton));

    /// <summary>The group name; buttons with the same group are mutually exclusive.</summary>
    public string Group
    {
        get { return (string)GetValue(GroupProperty); }
        set { SetValue(GroupProperty, value); }
    }

    /// <summary>Identifies the <see cref="Group"/> property.</summary>
    public static readonly DependencyProperty GroupProperty =
            DependencyProperty.Register(nameof(Group), typeof(string), typeof(TkRadiobutton),
                    new PropertyMetadata(""));

    /// <summary>Whether the button starts selected.</summary>
    public bool Checked
    {
        get { return (bool)GetValue(CheckedProperty); }
        set { SetValue(CheckedProperty, value); }
    }

    /// <summary>Identifies the <see cref="Checked"/> property.</summary>
    public static readonly DependencyProperty CheckedProperty =
            DependencyProperty.Register(nameof(Checked), typeof(bool), typeof(TkRadiobutton),
                    new PropertyMetadata(false));

    /// <summary>The command fired with <see cref="Value"/> when the button is selected.</summary>
    public ICommand Command
    {
        get { return (ICommand)GetValue(CommandProperty); }
        set { SetValue(CommandProperty, value); }
    }

    /// <summary>Identifies the <see cref="Command"/> property.</summary>
    public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TkRadiobutton),
                    new PropertyMetadata(null));

    /// <summary>The materialized radiobutton widget, or null before the host loads.</summary>
    public RadiobuttonWidget RadiobuttonWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        RadiobuttonWidget = new RadiobuttonWidget(window);
        return RadiobuttonWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        string group = Group;
        if (!string.IsNullOrEmpty(group))
        {
            RadiobuttonWidget.Variable = host.GetGroupVariable(group);
        }
        if (Checked) { RadiobuttonWidget.Select(); }
        RadiobuttonWidget.Command = () => ExecuteCommand(Command, Value);
    }
}
