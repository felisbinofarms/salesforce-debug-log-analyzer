using System.Windows;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Views;
using Serilog;
using System.IO;

namespace SalesforceDebugAnalyzer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SystemTrayService? _trayService;
    private SettingsService? _settingsService;

    /// <summary>
    /// Called by XAML Startup="Application_Startup" — replaces StartupUri so we can
    /// create the tray icon before (or instead of) showing the main window.
    /// </summary>
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SalesforceDebugAnalyzer",
            "Logs",
            "app-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Application starting up");

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Load settings
        _settingsService = new SettingsService();
        var settings = _settingsService.Load();

        // Initialize system tray
        _trayService = new SystemTrayService();
        _trayService.Initialize();
        _trayService.ShowWindowRequested += OnShowWindowRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.MonitoringToggled += OnMonitoringToggled;

        // Create and show main window (unless StartMinimized)
        var mainWindow = new MainWindow();
        mainWindow.SetTrayService(_trayService);
        MainWindow = mainWindow;

        if (settings.StartMinimized)
        {
            Log.Information("Starting minimized to tray");
        }
        else
        {
            mainWindow.Show();
        }
    }

    private void OnShowWindowRequested(object? sender, EventArgs e)
    {
        // Events are already marshaled to the WPF dispatcher by SystemTrayService.
        if (MainWindow == null)
        {
            var mainWindow = new MainWindow();
            mainWindow.SetTrayService(_trayService!);
            MainWindow = mainWindow;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        // Events are already marshaled to the WPF dispatcher by SystemTrayService.
        if (MainWindow is MainWindow mw)
            mw.ForceClose = true;

        _trayService?.Dispose();
        _trayService = null;

        Shutdown();
    }

    private void OnMonitoringToggled(object? sender, bool active)
    {
        // Events are already marshaled to the WPF dispatcher by SystemTrayService.
        if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            if (vm.ToggleMonitoringCommand.CanExecute(null))
                vm.ToggleMonitoringCommand.Execute(null);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        // Clean up tray service
        _trayService?.Dispose();

        // Clean up long-lived services
        try
        {
            if (MainWindow?.DataContext is IDisposable disposableVm)
            {
                disposableVm.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during service cleanup");
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Unhandled exception occurred");
        MessageBox.Show($"A critical error occurred: {ex?.Message}\n\nThe application will now close.",
            "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception occurred");
        MessageBox.Show($"An error occurred: {e.Exception.Message}\n\nCheck logs for details.",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception occurred");
        e.SetObserved();
    }
}
