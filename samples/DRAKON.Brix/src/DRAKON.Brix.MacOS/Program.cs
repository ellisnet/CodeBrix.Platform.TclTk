using CodeBrix.Platform.UI.Hosting;
using System;

namespace DRAKON.Brix;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            .UseMacOS()
            .Build();

        host.Run();
    }
}
