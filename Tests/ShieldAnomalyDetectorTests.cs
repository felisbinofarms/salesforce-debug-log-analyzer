using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Integration tests for ShieldAnomalyDetector.
/// Uses a real SQLite database to verify anomaly detection logic.
/// </summary>
public class ShieldAnomalyDetectorTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testOrgId;
    private readonly MonitoringDatabaseService _db;
    private readonly ShieldAnomalyDetector _detector;
    private readonly List<MonitoringAlert> _generatedAlerts = new();

    public ShieldAnomalyDetectorTests(ITestOutputHelper output)
    {
        _output = output;
        _testOrgId = $"shield_test_{Guid.NewGuid():N}";
        _db = new MonitoringDatabaseService(_testOrgId);
        _detector = new ShieldAnomalyDetector(_db);
        _detector.AlertGenerated += (_, alert) => _generatedAlerts.Add(alert);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try
        {
            var dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BlackWidow", "orgs", _testOrgId);
            if (Directory.Exists(dbDir))
                Directory.Delete(dbDir, true);
        }
        catch { }
    }

    // ================================================================
    //  Login Anomalies — Failed Login Spike
    // ================================================================

    [Fact]
    public async Task DetectsFailedLoginSpike()
    {
        // Insert 6 failed logins in the last hour (threshold = 5)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 6; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                UserId = $"user-{i % 3}",
                ClientIp = $"10.0.0.{i}",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_login_anomaly" &&
            a.MetricName == "failed_logins");
    }

    [Fact]
    public async Task FailedLoginSpike_NoAlert_WhenBelowThreshold()
    {
        // Insert only 3 failed logins (below threshold of 5)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                UserId = $"user-{i}",
                ClientIp = $"10.0.0.{i}",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.MetricName == "failed_logins").Should().BeEmpty();
    }

    [Fact]
    public async Task FailedLoginSpike_Critical_WhenDoubleThreshold()
    {
        // Insert 10 failed logins (double threshold of 5 => critical)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"user-{i}",
                ClientIp = $"10.0.0.{i}",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.MetricName == "failed_logins" && a.Severity == "critical");
    }

    // ================================================================
    //  Login Anomalies — New IP Detection
    // ================================================================

    [Fact]
    public async Task DetectsNewIpLogin()
    {
        // Seed known IPs from historical data (older than 1 hour)
        var historicalEvents = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddDays(-2).ToString("O"),
                UserId = "user-alpha",
                ClientIp = "192.168.1.1",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(historicalEvents);
        await _detector.SeedKnownIpsAsync();

        // Insert a new login from a different IP in the last hour
        var recentEvents = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = "user-alpha",
                ClientIp = "10.99.99.99",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(recentEvents);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_login_anomaly" &&
            a.MetricName == "new_ip_login" &&
            a.Title.Contains("user-alpha"));
    }

    [Fact]
    public async Task NewIp_NoAlert_ForFirstTimeUser()
    {
        // User with no history logs in for the first time
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = "brand-new-user",
                ClientIp = "172.16.0.1",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a =>
            a.MetricName == "new_ip_login" && a.Title.Contains("brand-new-user"))
            .Should().BeEmpty();
    }

    // ================================================================
    //  Login Anomalies — Unusual Hour
    // ================================================================

    [Fact]
    public async Task DetectsUnusualHourLogin()
    {
        // The detector queries events from the last hour, then checks if the parsed
        // EventDate has hour between 0-5 (QuietHourStart..QuietHourEnd).
        // For a reliable test, we insert the event with EventDate = UtcNow - 5 min
        // so it falls within the 1-hour query window, then check if the current
        // UTC hour would trigger unusual_hour detection (0-4).
        var recentTime = DateTime.UtcNow.AddMinutes(-5);
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = recentTime.ToString("O"),
                UserId = "night-owl",
                ClientIp = "10.0.0.1",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        // This alert only fires when the current UTC hour is 0-4 (unusual hours).
        // We verify the detector runs without error and produces the correct behavior.
        if (recentTime.Hour >= 0 && recentTime.Hour < 5)
        {
            _generatedAlerts.Should().Contain(a =>
                a.AlertType == "shield_login_anomaly" &&
                a.MetricName == "unusual_hour_login");
        }
        else
        {
            // Outside unusual hours — no alert expected
            _generatedAlerts.Where(a => a.MetricName == "unusual_hour_login")
                .Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NoAlert_ForNormalHourLogin()
    {
        // Insert login at 10:00 AM UTC (normal hours)
        var normalTime = DateTime.UtcNow.Date.AddHours(10);
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = normalTime.ToString("O"),
                UserId = "day-worker",
                ClientIp = "10.0.0.1",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.MetricName == "unusual_hour_login")
            .Should().BeEmpty();
    }

    // ================================================================
    //  API Spike Detection
    // ================================================================

    [Fact]
    public async Task DetectsApiSpike()
    {
        // Insert 7 days of historical API events (low volume: ~5/hour)
        var historicalEvents = new List<ShieldEvent>();
        for (int day = 7; day >= 1; day--)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                for (int i = 0; i < 5; i++)
                {
                    historicalEvents.Add(new ShieldEvent
                    {
                        OrgId = _testOrgId,
                        EventType = "API",
                        EventDate = DateTime.UtcNow.AddDays(-day).AddHours(hour).AddMinutes(i * 10).ToString("O"),
                        UserId = "integration-user",
                        IsSuccess = true
                    });
                }
            }
        }
        await _db.InsertShieldEventsAsync(historicalEvents);

        // Now spike: insert 50 API events in the last hour
        var recentEvents = new List<ShieldEvent>();
        for (int i = 0; i < 50; i++)
        {
            recentEvents.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-30).ToString("O"),
                UserId = "runaway-integration",
                IsSuccess = true
            });
        }
        await _db.InsertShieldEventsAsync(recentEvents);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_api_spike");
    }

    [Fact]
    public async Task ApiSpike_NoAlert_WhenNormalVolume()
    {
        // Insert consistent 5/hour historical + 5 recent
        var allEvents = new List<ShieldEvent>();
        for (int day = 3; day >= 0; day--)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                for (int i = 0; i < 5; i++)
                {
                    allEvents.Add(new ShieldEvent
                    {
                        OrgId = _testOrgId,
                        EventType = "API",
                        EventDate = DateTime.UtcNow.AddDays(-day).AddHours(hour).AddMinutes(i * 10).ToString("O"),
                        IsSuccess = true
                    });
                }
            }
        }
        await _db.InsertShieldEventsAsync(allEvents);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.AlertType == "shield_api_spike").Should().BeEmpty();
    }

    // ================================================================
    //  Page Performance Degradation
    // ================================================================

    [Fact]
    public async Task DetectsPagePerformanceDegradation()
    {
        // Insert page views with high EPT (>3000ms threshold)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "LightningPageView",
                EventDate = DateTime.UtcNow.AddMinutes(-15).ToString("O"),
                Uri = "/lightning/r/Case/view",
                DurationMs = 4000 + i * 100 // 4000ms+ well above threshold
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_page_slow" &&
            a.EntryPoint == "/lightning/r/Case/view");
    }

    [Fact]
    public async Task PagePerformance_NoAlert_WhenFast()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "LightningPageView",
                EventDate = DateTime.UtcNow.AddMinutes(-15).ToString("O"),
                Uri = "/fast/page",
                DurationMs = 500 // Well under threshold
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.AlertType == "shield_page_slow").Should().BeEmpty();
    }

    [Fact]
    public async Task PagePerformance_NoAlert_WhenTooFewViews()
    {
        // Only 2 views (minimum is 3)
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "LightningPageView",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "/low/traffic",
                DurationMs = 5000
            },
            new()
            {
                OrgId = _testOrgId,
                EventType = "LightningPageView",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                Uri = "/low/traffic",
                DurationMs = 5000
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a =>
            a.AlertType == "shield_page_slow" && a.EntryPoint == "/low/traffic")
            .Should().BeEmpty();
    }

    [Fact]
    public async Task PagePerformance_Critical_WhenDoubleThreshold()
    {
        // EPT > 6000ms (double of 3000ms threshold)
        // Need 5+ events overall AND 3+ per URI group
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "LightningPageView",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "/very/slow/page",
                DurationMs = 7000
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_page_slow" &&
            a.Severity == "critical");
    }

    // ================================================================
    //  Apex Exception Detection
    // ================================================================

    [Fact]
    public async Task DetectsApexExceptionSpike()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-20).ToString("O"),
                Uri = "MyClass.myMethod",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "error_spike" &&
            a.EntryPoint == "MyClass.myMethod");
    }

    [Fact]
    public async Task ApexException_NoAlert_WhenSingleOccurrence()
    {
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-20).ToString("O"),
                Uri = "RareException.method",
                IsSuccess = false
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.EntryPoint == "RareException.method").Should().BeEmpty();
    }

    [Fact]
    public async Task ApexException_Critical_WhenManyExceptions()
    {
        // 5+ exceptions from same source => critical
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 6; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "BrokenClass.badMethod"
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "error_spike" &&
            a.EntryPoint == "BrokenClass.badMethod" &&
            a.Severity == "critical");
    }

    // ================================================================
    //  SeedKnownIps
    // ================================================================

    [Fact]
    public async Task SeedKnownIps_LoadsFromHistory()
    {
        // Insert historical logins
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddDays(-5).ToString("O"),
                UserId = "seed-user",
                ClientIp = "192.168.1.100",
                IsSuccess = true
            },
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddDays(-3).ToString("O"),
                UserId = "seed-user",
                ClientIp = "192.168.1.200",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.SeedKnownIpsAsync();

        // Now a login from 192.168.1.100 should NOT generate new-IP alert
        var recentEvents = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = "seed-user",
                ClientIp = "192.168.1.100",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(recentEvents);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a =>
            a.MetricName == "new_ip_login" && a.Title.Contains("seed-user"))
            .Should().BeEmpty();
    }

    // ================================================================
    //  Alert Deduplication
    // ================================================================

    [Fact]
    public async Task AlertDedup_PreventsDuplicateAlerts()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-20).ToString("O"),
                Uri = "DedupClass.method"
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();
        var firstCount = _generatedAlerts.Count(a => a.EntryPoint == "DedupClass.method");

        // Run again
        await _detector.RunDetectionAsync();
        var secondCount = _generatedAlerts.Count(a => a.EntryPoint == "DedupClass.method");

        secondCount.Should().Be(firstCount, "dedup should prevent duplicate alerts within 24h");
    }

    // ================================================================
    //  Empty DB
    // ================================================================

    [Fact]
    public async Task RunDetection_EmptyDb_DoesNotThrow()
    {
        var act = () => _detector.RunDetectionAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunDetection_EmptyDb_NoAlerts()
    {
        await _detector.RunDetectionAsync();
        _generatedAlerts.Should().BeEmpty();
    }
}
