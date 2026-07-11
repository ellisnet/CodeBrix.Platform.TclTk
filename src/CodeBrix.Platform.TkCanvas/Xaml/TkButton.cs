using System.Windows.Input;

using CodeBrix.Platform.TkCanvas.Hosting;
using CodeBrix.Platform.TkCanvas.Widgets;
using CodeBrix.Platform.TkCanvas.Windowing;

using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.TkCanvas.Xaml;

/// <summary>
/// Declares a Tk <c>button</c>. Bind <see cref="Command"/> to a view-model
/// command (e.g. a <c>SimpleCommand</c>) — it fires when the button is
/// invoked.
/// </summary>
public sealed class TkButton : TkElement
{
    /// <summary>The button text (<c>-text</c>).</summary>
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty =
            RegisterOption(nameof(Text), "-text", typeof(TkButton));

    /// <summary>The photo-image name to display instead of text (<c>-image</c>).</summary>
    public string Image
    {
        get { return (string)GetValue(ImageProperty); }
        set { SetValue(ImageProperty, value); }
    }

    /// <summary>Identifies the <see cref="Image"/> property.</summary>
    public static readonly DependencyProperty ImageProperty =
            RegisterOption(nameof(Image), "-image", typeof(TkButton));

    /// <summary>The command invoked when the button fires.</summary>
    public ICommand Command
    {
        get { return (ICommand)GetValue(CommandProperty); }
        set { SetValue(CommandProperty, value); }
    }

    /// <summary>Identifies the <see cref="Command"/> property.</summary>
    public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TkButton),
                    new PropertyMetadata(null));

    /// <summary>The parameter passed to <see cref="Command"/>.</summary>
    public object CommandParameter
    {
        get { return GetValue(CommandParameterProperty); }
        set { SetValue(CommandParameterProperty, value); }
    }

    /// <summary>Identifies the <see cref="CommandParameter"/> property.</summary>
    public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(TkButton),
                    new PropertyMetadata(null));

    /// <summary>The materialized button widget, or null before the host loads.</summary>
    public ButtonWidget ButtonWidget { get; private set; }

    private protected override IWidget CreateWidget(TkWindow window)
    {
        ButtonWidget = new ButtonWidget(window);
        return ButtonWidget;
    }

    private protected override void OnMaterialized(TkHostView host)
    {
        ButtonWidget.Invoked += () => ExecuteCommand(Command, CommandParameter);
    }
}
