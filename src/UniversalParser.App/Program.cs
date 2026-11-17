namespace UniversalParser.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var initialPath = args.Length > 0 ? args[0] : null;
        Application.Run(new MainForm(initialPath));
    }
}
