using DRAKON.Brix.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace DRAKON.Brix.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        //Doing this before InitializeComponent() - in case InitializeComponent()
        //  is the thing that sets the data context.
        DataContextChanged += (sender, args) =>
        {
            //Give the view model access to the Tk widgets it talks to (the
            //  UI itself is declared entirely in MainPage.xaml).
            if (DataContext is ITkWidgetBridge bridge)
            {
                bridge.GetEntryText = () => NameEntry.EntryWidget?.Text ?? string.Empty;
                bridge.AppendOutputLine = line =>
                {
                    Output.TextWidget?.Insert("end - 1 chars", line + "\n");
                    Output.TextWidget?.See("end - 1 chars");
                    TkHost.RequestUpdate();
                };
            }
        };

        InitializeComponent();
    }
}
