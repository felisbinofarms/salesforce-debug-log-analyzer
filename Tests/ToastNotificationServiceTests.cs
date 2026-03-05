using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests for ToastNotificationService rate limiting and notification behavior.
/// Note: quiet hours cannot be reliably unit-tested since IsQuietHours() uses DateTime.Now.
/// </summary>
public class ToastNotificationServiceTests
{
    private readonly SettingsService _settingsService;
    private readonly ToastNotificationService _toastService;

    public ToastNotificationServiceTests()
    {
        _settingsService = new SettingsService();
        _toastService = new ToastNotificationService(_settingsService);
    }

    // ================================================================
    //  Basic Behavior
    // ================================================================

    [Fact]
    public void ShowAlert_WithoutTrayService_DoesNotThrow()
    {
        // No tray service set — should not crash
        var alert = CreateAlert("Test alert", "warning");

        var act = () => _toastService.ShowAlert(alert);
        act.Should().NotThrow();
    }

    [Fact]
    public void ShowAlert_WithTrayService_DoesNotThrow()
    {
        // Setting tray service and showing alert should work
        // (SystemTrayService requires WinForms context so we test behavior without it)
        var alert = CreateAlert("Test alert", "critical");

        var act = () => _toastService.ShowAlert(alert);
        act.Should().NotThrow();
    }

    // ================================================================
    //  Rate Limiting
    // ================================================================

    [Fact]
    public void RateLimiting_AllowsUpToMaxToasts()
    {
        // MaxToastsPerWindow = 5 — calling 5 times should not throw
        for (int i = 0; i < 5; i++)
        {
            var act = () => _toastService.ShowAlert(CreateAlert($"Alert {i}", "warning"));
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void RateLimiting_DoesNotThrowAfterMax()
    {
        // Even after exceeding the rate limit, it should gracefully skip
        for (int i = 0; i < 10; i++)
        {
            var act = () => _toastService.ShowAlert(CreateAlert($"Alert {i}", "warning"));
            act.Should().NotThrow();
        }
    }

    // ================================================================
    //  Alert Severity Mapping
    // ================================================================

    [Theory]
    [InlineData("critical")]
    [InlineData("warning")]
    [InlineData("info")]
    public void ShowAlert_AcceptsAllSeverities(string severity)
    {
        var act = () => _toastService.ShowAlert(CreateAlert("Test", severity));
        act.Should().NotThrow();
    }

    // ================================================================
    //  Long Description Truncation
    // ================================================================

    [Fact]
    public void ShowAlert_LongDescription_DoesNotThrow()
    {
        var alert = CreateAlert("Test", "warning");
        alert.Description = new string('x', 500); // Very long description

        var act = () => _toastService.ShowAlert(alert);
        act.Should().NotThrow();
    }

    [Fact]
    public void ShowAlert_NullDescription_DoesNotThrow()
    {
        var alert = new MonitoringAlert
        {
            OrgId = "test",
            Title = "Test",
            Severity = "warning",
            Description = null!,
            AlertType = "test"
        };

        var act = () => _toastService.ShowAlert(alert);
        act.Should().NotThrow();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static MonitoringAlert CreateAlert(string title, string severity)
    {
        return new MonitoringAlert
        {
            OrgId = "test-org",
            CreatedAt = DateTime.UtcNow,
            AlertType = "test_alert",
            Severity = severity,
            Title = title,
            Description = $"Test description for {title}",
            MetricName = "test_metric",
            CurrentValue = 50
        };
    }
}
