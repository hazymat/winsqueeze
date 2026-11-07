using Velopack;
namespace WinSqueeze;

public static class Program
{
    [STAThread]
    public static void Main(string[] commandLineArgs)
    {
        VelopackApp.Build().Run(); // must run first: handles install, update and uninstall hooks
        var app = new App();
        app.InitializeComponent();
        app.Run();
        Environment.Exit(0); // don't wait on any thread still running after the window closes
    }
}
