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

        try
        {
            _settingsService = new SettingsService();
            var settings = _settingsService.Load();

            // Initialize system tray
            _trayService = new SystemTrayService();
            _trayService.Initialize();
            _trayService.ShowWindowRequested += OnShowWindowRequested;
            _trayService.ExitRequested += OnExitRequested;
            _trayService.MonitoringToggled += OnMonitoringToggled;

            // Create and show main window
            var mainWindow = new MainWindow();
            mainWindow.SetTrayService(_trayService);
            MainWindow = mainWindow;
            mainWindow.Show();

            if (settings.StartMinimized)
            {
                Log.Information("Starting minimized to tray");
                mainWindow.Hide();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "FATAL: Exception during Application_Startup");
            Log.CloseAndFlush();
            throw;
        }
    }

    private void OnShowWindowRequested(object? sender, EventArgs e)
    {
        // Re-create the window if it was actually closed (not just hidden).
        if (MainWindow is not MainWindow mw || !mw.IsLoaded)
        {
            var newWindow = new MainWindow();
            newWindow.SetTrayService(_trayService!);
            MainWindow = newWindow;
            mw = newWindow;
        }

        // Make visible and restore from minimized state.
        mw.Show();
        if (mw.WindowState == WindowState.Minimized)
            mw.WindowState = WindowState.Normal;

        // If the window is off-screen (e.g. monitor was disconnected), re-centre it.
        var area = System.Windows.SystemParameters.WorkArea;
        if (mw.Left + mw.Width < 0 || mw.Left > area.Right ||
            mw.Top + mw.Height < 0 || mw.Top > area.Bottom)
        {
            mw.Left = (area.Width - mw.Width) / 2 + area.Left;
            mw.Top  = (area.Height - mw.Height) / 2 + area.Top;
        }

        // Force to front — Topmost trick bypasses Windows focus-stealing prevention.
        mw.Topmost = true;
        mw.Activate();
        mw.Topmost = false;
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
        // Build full exception detail including inner exceptions
        var sb = new System.Text.StringBuilder();
        var ex = e.Exception;
        int depth = 0;
        while (ex != null)
        {
            sb.AppendLine($"[{depth++}] {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            ex = ex.InnerException;
        }
        var detail = sb.ToString();

        // Write to a crash file so it survives even if UI is broken
        try
        {
            var crashPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SalesforceDebugAnalyzer", "crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(crashPath)!);
            File.WriteAllText(crashPath, detail);
        }
        catch { /* best-effort */ }

        Log.Error(e.Exception, "Unhandled UI exception occurred");
        MessageBox.Show($"An error occurred:\n\n{detail}\n\nCheck logs for details.",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception occurred");
        e.SetObserved();
    }
}
