using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for managing application settings and preferences
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _cachedSettings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackWidow"
        );

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
    }

    /// <summary>
    /// Load settings from disk (or create default if none exist)
    /// </summary>
    public AppSettings Load()
    {
        if (_cachedSettings != null)
            return _cachedSettings;

        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _cachedSettings = JsonConvert.DeserializeObject<AppSettings>(json);
                
                if (_cachedSettings != null)
                    return _cachedSettings;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings from {Path}", _settingsPath);
            }
        }

        // Return defaults
        _cachedSettings = new AppSettings();
        Save(_cachedSettings);
        return _cachedSettings;
    }

    /// <summary>
    /// Save settings to disk
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
            _cachedSettings = settings;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save settings to {Path}", _settingsPath);
            throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reset to default settings
    /// </summary>
    public void Reset()
    {
        _cachedSettings = new AppSettings();
        Save(_cachedSettings);
    }

    /// <summary>
    /// Get settings file path
    /// </summary>
    public string GetSettingsPath() => _settingsPath;
}

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    // General Settings
    public string Theme { get; set; } = "Dark"; // "Light", "Dark", "Auto"
    public bool AutoCheckUpdates { get; set; } = true;
    public bool ShowWelcomeOnStartup { get; set; } = true;
    public string Language { get; set; } = "en-US";

    // Connection Settings
    public string? DefaultOrgUsername { get; set; }
    public bool SaveConnectionHistory { get; set; } = true;
    public int MaxConnectionHistory { get; set; } = 10;

    // Parser Settings
    public bool ShowDebugStatements { get; set; } = true;
    public bool ShowSystemLogs { get; set; } = false;
    public bool HighlightErrors { get; set; } = true;
    public bool GroupTransactions { get; set; } = true;

    // Performance Settings
    public int MaxLogSizeMB { get; set; } = 100;
    public bool EnableCaching { get; set; } = true;
    public int CacheRetentionDays { get; set; } = 7;

    // Export Settings
    public string DefaultExportFormat { get; set; } = "PDF"; // "PDF", "JSON", "TXT"
    public string? LastExportDirectory { get; set; }

    // Privacy Settings
    public bool SendAnonymousUsageData { get; set; } = false;
    public bool EnableCrashReporting { get; set; } = true;

    // Advanced Settings
    public bool EnableEditorBridge { get; set; } = true;
    public int EditorBridgePort { get; set; } = 7777;
    public bool VerboseLogging { get; set; } = false;

    // Onboarding
    public bool OnboardingShown { get; set; } = false;

    // Developer / Power-User Settings
    public bool IsDebugMode { get; set; } = false;

    // UI State
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; } = false;
    public double SidebarWidth { get; set; } = 250;

    // System Tray & Monitoring
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool MonitoringEnabled { get; set; } = true;
    public bool MonitoringAutoStart { get; set; } = true;
    public int MonitoringPollIntervalSeconds { get; set; } = 60;

    // Notifications
    public bool ToastNotificationsEnabled { get; set; } = true;
    public bool CriticalAlertsOnly { get; set; } = false;
    public bool QuietHoursEnabled { get; set; } = true;
    public int QuietHoursStart { get; set; } = 22;
    public int QuietHoursEnd { get; set; } = 7;

    // Shield
    public bool ShieldMonitoringEnabled { get; set; } = true;

    // Threshold overrides (percentages 0-100)
    public int GovernorWarningPct { get; set; } = 80;
    public int GovernorCriticalPct { get; set; } = 90;
    public int DurationWarningMs { get; set; } = 10000;
    public int DurationCriticalMs { get; set; } = 20000;
    public int HealthScoreWarning { get; set; } = 60;
    public int HealthScoreCritical { get; set; } = 40;
}
