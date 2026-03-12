using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SalesforceDebugAnalyzer.Views;
using Serilog;

namespace SalesforceDebugAnalyzer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SalesforceDebugAnalyzer", "Logs", "app-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Application starting up (Avalonia)");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) =>
            {
                Log.Information("Application shutting down");
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
