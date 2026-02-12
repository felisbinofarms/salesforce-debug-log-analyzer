using System.Configuration;
using System.Data;
using System.Windows;
using Serilog;
using System.IO;

namespace SalesforceDebugAnalyzer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
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
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

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

