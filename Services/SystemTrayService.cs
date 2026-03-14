using Serilog;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages a system tray icon for background monitoring.
/// Currently a stub — platform-specific tray support will be added per-platform.
/// </summary>
public class SystemTrayService : IDisposable
{
    private bool _disposed;
    private bool _isMonitoringActive;
    private int _alertCount;

    // These events are part of the planned tray API and will be wired up when
    // platform-specific tray support is implemented (GH#TODO).
#pragma warning disable CS0067
    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? MonitoringToggled;
    public event EventHandler? AlertCenterRequested;
#pragma warning restore CS0067

    public void Initialize()
    {
        Log.Information("SystemTrayService initialized (stub — no platform tray icon)");
    }

    public void SetMonitoringActive(bool active)
    {
        _isMonitoringActive = active;
    }

    public void SetAlertCount(int count)
    {
        _alertCount = count;
    }

    public enum TrayIcon { Info, Warning, Error }

    public void ShowBalloonTip(string title, string text, TrayIcon icon = TrayIcon.Info, int timeoutMs = 5000)
    {
        Log.Information("Tray notification: [{Icon}] {Title} — {Text}", icon, title, text);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
