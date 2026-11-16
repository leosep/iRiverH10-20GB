using System.Windows;
using System.Windows.Threading;
using H10FirmwareManager.Core.Services;

namespace H10FirmwareManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppServices.Log.Error("Unhandled application error.", args.ExceptionObject?.ToString());
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppServices.Log.Error("Unobserved task error.", args.Exception.ToString());
            args.SetObserved();
        };

        AppServices.Initialize();
        AppServices.Log.Info("H10 Firmware Manager started.",
            $"Version {typeof(App).Assembly.GetName().Version}, Windows {Environment.OSVersion.VersionString}");

        string? startPage = ParseStartPage(e.Args);
        var window = new MainWindow(startPage);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Reads <c>--page=&lt;key&gt;</c> or <c>--page &lt;key&gt;</c> from the command line.
    /// Used by the documentation screenshot tooling; harmless in normal use.
    /// </summary>
    private static string? ParseStartPage(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--page=".Length..].Trim().ToLowerInvariant();
            }
            if (arg.Equals("--page", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1].Trim().ToLowerInvariant();
            }
        }
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Log.Info("H10 Firmware Manager closed.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppServices.Log.Error("Unhandled UI error.", e.Exception.ToString());
        MessageBox.Show(
            "An unexpected error occurred:\n\n" + e.Exception.Message +
            "\n\nThe details were written to the activity log.",
            "H10 Firmware Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
