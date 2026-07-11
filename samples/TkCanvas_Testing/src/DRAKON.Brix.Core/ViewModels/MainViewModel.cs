using System;

using CodeBrix.Platform.Simple;

namespace DRAKON.Brix.ViewModels;

/// <summary>
/// Lets the hosting page hand the view model access to the Tk widgets it
/// needs to talk to (the same bridge pattern the CodeBrix.Samples apps use
/// for WebView/clipboard access): reading the name entry's live text and
/// appending lines to the output text widget.
/// </summary>
public interface ITkWidgetBridge
{
    /// <summary>Reads the live text of the name entry (wired by the page).</summary>
    Func<string> GetEntryText { get; set; }

    /// <summary>Appends one line to the output text widget (wired by the page).</summary>
    Action<string> AppendOutputLine { get; set; }
}

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, ITkWidgetBridge
{
    private int _clicks;

    /// <inheritdoc/>
    public Func<string> GetEntryText { get; set; }

    /// <inheritdoc/>
    public Action<string> AppendOutputLine { get; set; }

    private string _statusText = "Ready. The whole Tk UI above is declared in MainPage.xaml.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value ?? string.Empty);
    }

    private SimpleCommand _greetCommand;
    public SimpleCommand GreetCommand =>
        (_greetCommand ??= new SimpleCommand(() => true, DoGreet));

    private void DoGreet()
    {
        string name = GetEntryText?.Invoke();
        if (string.IsNullOrWhiteSpace(name)) { name = "stranger"; }
        AppendOutputLine?.Invoke($"Hello, {name}!");
        StatusText = $"Greeted {name}.";
    }

    private SimpleCommand _backCommand;
    public SimpleCommand BackCommand =>
        (_backCommand ??= new SimpleCommand(() => true, DoBack));

    private void DoBack()
    {
        _clicks++;
        StatusText = $"Toolbar image button clicked ({_clicks}x).";
    }

    private SimpleCommand _newCommand;
    public SimpleCommand NewCommand =>
        (_newCommand ??= new SimpleCommand(() => true, DoNew));

    private void DoNew()
    {
        AppendOutputLine?.Invoke("--- File > New ---");
        StatusText = "File > New chosen from the Tk menubar.";
    }

    private SimpleCommand _aboutCommand;
    public SimpleCommand AboutCommand =>
        (_aboutCommand ??= new SimpleCommand(() => true, DoAbout));

    private void DoAbout()
    {
        StatusText = "DRAKON.Brix - a TkCanvas XAML/MVVM sample.";
    }

    private SimpleCommand _verboseCommand;
    public SimpleCommand VerboseCommand =>
        (_verboseCommand ??= new SimpleCommand(() => true, DoVerbose));

    private void DoVerbose(object parameter)
    {
        StatusText = $"Verbose checkbutton is now {((parameter as bool?) == true ? "ON" : "OFF")}.";
    }

    private SimpleCommand _modeCommand;
    public SimpleCommand ModeCommand =>
        (_modeCommand ??= new SimpleCommand(() => true, DoMode));

    private void DoMode(object parameter)
    {
        StatusText = $"Mode radiobutton selected: {parameter}.";
    }
}
