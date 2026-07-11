using DRAKON.Brix.Drakon;

using Microsoft.UI.Xaml.Controls;

namespace DRAKON.Brix.Views;

public sealed partial class MainPage : Page
{
    private readonly DrakonRuntime _runtime = new DrakonRuntime();

    public MainPage()
    {
        this.InitializeComponent();

        //The DRAKON Editor Tcl builds the whole UI; this page just starts
        //  the runtime once the Tk host (and its dispatcher) is live.
        Loaded += (s, e) => _runtime.Start(TkHost);
        Unloaded += (s, e) => _runtime.Dispose();
    }
}
