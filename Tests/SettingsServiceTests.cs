using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests for SettingsService: default values, save/load roundtrip,
/// and correct default values for all monitoring-related settings.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bw-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SettingsService CreateIsolatedService() =>
        new(Path.Combine(_tempDir, "settings.json"));

    // ================================================================
    //  Default Values
    // ================================================================

    [Fact]
    public void AppSettings_Defaults_SystemTray()
    {
        var settings = new AppSettings();
        settings.MinimizeToTray.Should().BeTrue();
        settings.StartMinimized.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_Defaults_Monitoring()
    {
        var settings = new AppSettings();
        settings.MonitoringEnabled.Should().BeTrue();
        settings.MonitoringAutoStart.Should().BeTrue();
        settings.MonitoringPollIntervalSeconds.Should().Be(60);
    }

    [Fact]
    public void AppSettings_Defaults_Notifications()
    {
        var settings = new AppSettings();
        settings.ToastNotificationsEnabled.Should().BeTrue();
        settings.CriticalAlertsOnly.Should().BeFalse();
        settings.QuietHoursEnabled.Should().BeTrue();
        settings.QuietHoursStart.Should().Be(22);
        settings.QuietHoursEnd.Should().Be(7);
    }

    [Fact]
    public void AppSettings_Defaults_Shield()
    {
        var settings = new AppSettings();
        settings.ShieldMonitoringEnabled.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Defaults_Thresholds()
    {
        var settings = new AppSettings();
        settings.GovernorWarningPct.Should().Be(80);
        settings.GovernorCriticalPct.Should().Be(90);
        settings.DurationWarningMs.Should().Be(10000);
        settings.DurationCriticalMs.Should().Be(20000);
        settings.HealthScoreWarning.Should().Be(60);
        settings.HealthScoreCritical.Should().Be(40);
    }

    [Fact]
    public void AppSettings_Defaults_General()
    {
        var settings = new AppSettings();
        settings.Theme.Should().Be("Dark");
        settings.AutoCheckUpdates.Should().BeTrue();
        settings.ShowWelcomeOnStartup.Should().BeTrue();
        settings.Language.Should().Be("en-US");
    }

    [Fact]
    public void AppSettings_Defaults_Parser()
    {
        var settings = new AppSettings();
        settings.ShowDebugStatements.Should().BeTrue();
        settings.ShowSystemLogs.Should().BeFalse();
        settings.HighlightErrors.Should().BeTrue();
        settings.GroupTransactions.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Defaults_Privacy()
    {
        var settings = new AppSettings();
        settings.SendAnonymousUsageData.Should().BeFalse();
        settings.EnableCrashReporting.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Defaults_Advanced()
    {
        var settings = new AppSettings();
        settings.EnableEditorBridge.Should().BeTrue();
        settings.EditorBridgePort.Should().Be(7777);
        settings.VerboseLogging.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_Defaults_UIState()
    {
        var settings = new AppSettings();
        settings.WindowWidth.Should().Be(1400);
        settings.WindowHeight.Should().Be(900);
        settings.WindowMaximized.Should().BeFalse();
        settings.SidebarWidth.Should().Be(250);
    }

    [Fact]
    public void AppSettings_Defaults_Export()
    {
        var settings = new AppSettings();
        settings.DefaultExportFormat.Should().Be("PDF");
        settings.LastExportDirectory.Should().BeNull();
    }

    // ================================================================
    //  SettingsService Load/Save Roundtrip
    // ================================================================

    [Fact]
    public void SettingsService_Load_ReturnsDefaults()
    {
        var service = CreateIsolatedService();
        var settings = service.Load();

        settings.Should().NotBeNull();
        settings.Theme.Should().Be("Dark");
    }

    [Fact]
    public void SettingsService_SaveAndLoad_Roundtrip()
    {
        var settingsPath = Path.Combine(_tempDir, "roundtrip.json");
        var service = new SettingsService(settingsPath);
        var settings = service.Load();

        // Modify monitoring settings
        settings.MonitoringPollIntervalSeconds = 120;
        settings.GovernorWarningPct = 75;
        settings.QuietHoursStart = 23;
        settings.ShieldMonitoringEnabled = false;

        service.Save(settings);

        // Load fresh (clear cache by creating new service)
        var freshService = new SettingsService(settingsPath);

        // Force re-read by creating new SettingsService instance
        // (it reads from the same file path)
        var loaded = freshService.Load();
        loaded.MonitoringPollIntervalSeconds.Should().Be(120);
        loaded.GovernorWarningPct.Should().Be(75);
        loaded.QuietHoursStart.Should().Be(23);
        loaded.ShieldMonitoringEnabled.Should().BeFalse();

        // Restore defaults
        service.Reset();
    }

    [Fact]
    public void SettingsService_Reset_RestoresDefaults()
    {
        var service = CreateIsolatedService();
        var settings = service.Load();

        // Modify some values
        settings.MonitoringPollIntervalSeconds = 999;
        settings.Theme = "Light";
        service.Save(settings);

        // Reset
        service.Reset();

        var defaults = service.Load();
        defaults.MonitoringPollIntervalSeconds.Should().Be(60);
        defaults.Theme.Should().Be("Dark");
    }

    [Fact]
    public void SettingsService_GetSettingsPath_NotEmpty()
    {
        var service = CreateIsolatedService();
        service.GetSettingsPath().Should().NotBeNullOrEmpty();
        service.GetSettingsPath().Should().EndWith("settings.json");
    }

    // ================================================================
    //  Threshold Relationship Validation
    // ================================================================

    [Fact]
    public void Thresholds_WarningLessThanCritical()
    {
        var settings = new AppSettings();
        settings.GovernorWarningPct.Should().BeLessThan(settings.GovernorCriticalPct);
        settings.DurationWarningMs.Should().BeLessThan(settings.DurationCriticalMs);
        settings.HealthScoreWarning.Should().BeGreaterThan(settings.HealthScoreCritical);
    }

    // ================================================================
    //  New Shield + Alert Routing Defaults
    // ================================================================

    [Fact]
    public void AppSettings_Defaults_ShieldDetectionThresholds()
    {
        var settings = new AppSettings();
        settings.ShieldFailedLoginThreshold.Should().Be(5);
        settings.ShieldApiSpikeZScore.Should().BeApproximately(2.5, 0.001);
        settings.ShieldEptDegradationMs.Should().Be(3000);
        settings.ShieldApiFailureThreshold.Should().Be(3);
        settings.ShieldApiFailureRate.Should().BeApproximately(0.20, 0.001);
        settings.ShieldReportExportRowThreshold.Should().Be(5000);
    }

    [Fact]
    public void AppSettings_Defaults_EmailAlerts_Disabled()
    {
        var settings = new AppSettings();
        settings.EmailAlertsEnabled.Should().BeFalse();
        settings.AlertEmailTo.Should().BeEmpty();
        settings.SmtpHost.Should().Be("smtp.gmail.com");
        settings.SmtpPort.Should().Be(587);
        settings.SmtpUsername.Should().BeEmpty();
        settings.SmtpPassword.Should().BeEmpty();
    }

    [Fact]
    public void AppSettings_Defaults_SlackAlerts_Disabled()
    {
        var settings = new AppSettings();
        settings.SlackAlertsEnabled.Should().BeFalse();
        settings.SlackWebhookUrl.Should().BeEmpty();
    }

    [Fact]
    public void AppSettings_Defaults_AlertRoutingCriticalOnly()
    {
        var settings = new AppSettings();
        settings.AlertRoutingCriticalOnly.Should().BeTrue();
    }
}
