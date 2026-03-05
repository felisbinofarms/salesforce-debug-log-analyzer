using Serilog;
using System.Drawing;
using System.Windows.Forms;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages a Windows system tray icon with context menu for background monitoring.
/// Uses System.Windows.Forms.NotifyIcon for tray functionality.
/// </summary>
public class SystemTrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _monitoringMenuItem;
    private ToolStripMenuItem? _alertsMenuItem;
    private bool _disposed;
    private bool _isMonitoringActive;
    private int _alertCount;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? MonitoringToggled;
    public event EventHandler? AlertCenterRequested;

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Black Widow — Salesforce Monitor",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a simple 16x16 spider-themed icon programmatically.
    /// Falls back to system application icon if GDI+ fails.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        try
        {
            using var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Dark background circle
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(30, 30, 30));
            g.FillEllipse(bgBrush, 0, 0, 15, 15);

            // Spider body (accent red-ish color matching the app)
            var bodyColor = System.Drawing.Color.FromArgb(200, 80, 80);
            using var bodyPen = new Pen(bodyColor, 1.5f);
            using var bodyBrush = new SolidBrush(bodyColor);
            // Body
            g.FillEllipse(bodyBrush, 5, 5, 6, 6);
            // Legs (simplified 4 lines radiating out)
            g.DrawLine(bodyPen, 4, 4, 1, 1);
            g.DrawLine(bodyPen, 12, 4, 15, 1);
            g.DrawLine(bodyPen, 4, 12, 1, 15);
            g.DrawLine(bodyPen, 12, 12, 15, 15);

            var handle = bitmap.GetHicon();
            try
            {
                return Icon.FromHandle(handle).Clone() as Icon ?? SystemIcons.Application;
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create custom tray icon, using system default");
            return SystemIcons.Application;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Show Black Widow");
        showItem.Font = new Font(menu.Font, FontStyle.Bold);
        showItem.Click += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(showItem);

        menu.Items.Add(new ToolStripSeparator());

        _monitoringMenuItem = new ToolStripMenuItem("Monitoring: Paused");
        _monitoringMenuItem.Click += (_, _) =>
        {
            _isMonitoringActive = !_isMonitoringActive;
            UpdateMonitoringDisplay();
            MonitoringToggled?.Invoke(this, _isMonitoringActive);
        };
        menu.Items.Add(_monitoringMenuItem);

        _alertsMenuItem = new ToolStripMenuItem("Alerts (0)");
        _alertsMenuItem.Click += (_, _) => AlertCenterRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_alertsMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Black Widow");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Update the monitoring status in the tray menu and tooltip.
    /// </summary>
    public void SetMonitoringActive(bool active)
    {
        _isMonitoringActive = active;
        UpdateMonitoringDisplay();
    }

    /// <summary>
    /// Update the unread alert count shown in the tray menu.
    /// </summary>
    public void SetAlertCount(int count)
    {
        _alertCount = count;
        if (_alertsMenuItem != null)
        {
            _alertsMenuItem.Text = count > 0 ? $"Alerts ({count})" : "Alerts (0)";
        }
        UpdateTooltip();
    }

    /// <summary>
    /// Show a balloon tip notification from the tray icon.
    /// </summary>
    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 5000)
    {
        _notifyIcon?.ShowBalloonTip(timeoutMs, title, text, icon);
    }

    private void UpdateMonitoringDisplay()
    {
        if (_monitoringMenuItem != null)
        {
            _monitoringMenuItem.Text = _isMonitoringActive
                ? "Monitoring: Active"
                : "Monitoring: Paused";
        }
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (_notifyIcon == null) return;

        var status = _isMonitoringActive ? "Monitoring" : "Paused";
        var alerts = _alertCount > 0 ? $" | {_alertCount} alerts" : "";
        // NotifyIcon.Text max is 63 chars
        var tooltip = $"Black Widow — {status}{alerts}";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
