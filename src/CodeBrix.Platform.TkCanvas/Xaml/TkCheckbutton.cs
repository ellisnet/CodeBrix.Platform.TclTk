using System.Windows.Input;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>checkbutton</c>. <see cref="Command"/> fires with the
/// new checked state (a bool) whenever the user toggles it; read the state
/// through <see cref="CheckbuttonWidget"/>.
/// </summary>
public sealed class TkCheckbutton : TkElement
{
    /// <summary>The checkbutton text (<c>-text</c>).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            RegisterOption(nameof(Text), "-text", typeof(TkCheckbutton));

    /// <summary>Whether the button starts checked.</summary>
    public bool Checked
    {
        get { return (bool)GetValue(CheckedProperty); }
        set { SetValue(CheckedProperty, value); }
    }

    /// <summary>Identifies the <see cref="Checked"/> property.</summary>
    public static readonly DependencyProperty CheckedProperty =
            DependencyProperty.Register(nameof(Checked), typeof(bool), typeof(TkCheckbutton),
                    new PropertyMetadata(false));

    /// <summary>The command fired with the new checked state on toggle.</summary>
    public ICommand Command
    {
        get { return (ICommand)GetValue(CommandProperty); }
        set { SetValue(CommandProperty, value); }
    }

    /// <summary>Identifies the <see cref="Command"/> property.</summary>
    public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TkCheckbutton),
                    new PropertyMetadata(null));

    /// <summary>The materialized checkbutton widget, or null before the host loads.</summary>
    public CheckbuttonWidget CheckbuttonWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        CheckbuttonWidget = new CheckbuttonWidget(window);
        return CheckbuttonWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        if (Checked && !CheckbuttonWidget.IsSelected) { CheckbuttonWidget.Invoke(); }
        CheckbuttonWidget.Command = () => ExecuteCommand(Command, CheckbuttonWidget.IsSelected);
    }
}
