using SourcePorter.App.Theme;

namespace SourcePorter.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Resolve and apply the Source 2 Viewer colour mode before any form is shown.
        Themer.InitializeTheme();

        Application.Run(new MainForm());
    }
}
