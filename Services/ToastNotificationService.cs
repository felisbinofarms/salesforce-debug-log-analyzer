using Serilog;
using SalesforceDebugAnalyzer.Models;
using System.Windows.Forms;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Sends desktop notifications for monitoring alerts via the system tray balloon tip.
/// Rate-limited and respects quiet hours.
/// </summary>
public class ToastNotificationService
{
    private readonly SettingsService _settingsService;
    private SystemTrayService? _trayService;
    private readonly Queue<DateTime> _recentToastTimes = new();
    private const int MaxToastsPerWindow = 5;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(5);

    public ToastNotificationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Set the tray service reference (called after tray is initialized).
    /// </summary>
    public void SetTrayService(SystemTrayService trayService)
    {
        _trayService = trayService;
    }

    /// <summary>
    /// Show a notification for the given monitoring alert.
    /// Respects rate limiting and quiet hours.
    /// </summary>
    public void ShowAlert(MonitoringAlert alert)
    {
        // Quiet hours check (critical alerts bypass quiet hours)
        if (IsQuietHours() && alert.Severity != "critical")
            return;

        // Rate limiting
        CleanupOldToasts();
        if (_recentToastTimes.Count >= MaxToastsPerWindow)
        {
            Log.Debug("Notification rate limit reached, skipping for {AlertTitle}", alert.Title);
            return;
        }

        try
        {
            var icon = alert.Severity switch
            {
                "critical" => ToolTipIcon.Error,
                "warning" => ToolTipIcon.Warning,
                _ => ToolTipIcon.Info
            };

            var title = FormatTitle(alert);
            var body = alert.Description?.Length > 200
                ? alert.Description[..200] + "..."
                : alert.Description ?? "";

            _trayService?.ShowBalloonTip(title, body, icon);
            _recentToastTimes.Enqueue(DateTime.UtcNow);

            Log.Information("Notification shown: [{Severity}] {Title}", alert.Severity, alert.Title);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show notification");
        }
    }

    private static string FormatTitle(MonitoringAlert alert)
    {
        var prefix = alert.Severity switch
        {
            "critical" => "CRITICAL",
            "warning" => "Warning",
            _ => "Info"
        };
        return $"Black Widow — {prefix}";
    }

    private bool IsQuietHours()
    {
        var settings = _settingsService.Load();
        if (!settings.QuietHoursEnabled)
            return false;

        var now = DateTime.Now.TimeOfDay;
        var quietStart = new TimeSpan(settings.QuietHoursStart, 0, 0);
        var quietEnd = new TimeSpan(settings.QuietHoursEnd, 0, 0);

        if (quietStart > quietEnd)
        {
            // Wraps midnight
            return now >= quietStart || now < quietEnd;
        }
        return now >= quietStart && now < quietEnd;
    }

    private void CleanupOldToasts()
    {
        var cutoff = DateTime.UtcNow - RateWindow;
        while (_recentToastTimes.Count > 0 && _recentToastTimes.Peek() < cutoff)
        {
            _recentToastTimes.Dequeue();
        }
    }
}
