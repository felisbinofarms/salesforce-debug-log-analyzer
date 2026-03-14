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
        // Use a fixed 2 AM UTC timestamp so the test always exercises the unusual-hour
        // detection path regardless of when CI runs. The anchor becomes 2 AM (the
        // MAX event_date), so GetRecentShieldEventsAsync queries [1 AM, 2 AM] and the
        // event is included. Hour 2 is within QuietHourStart(0)..QuietHourEnd(5).
        var unusualHour = DateTime.UtcNow.Date.AddHours(2); // 02:00 UTC today, always quiet hours
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = unusualHour.ToString("O"),
                UserId = "night-owl",
                ClientIp = "10.0.0.1",
                IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_login_anomaly" &&
            a.MetricName == "unusual_hour_login",
            "a login at 02:00 UTC falls within the quiet hours window (0-5)");
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
    //  AutoTraceFlagRequested event (new — Gap 5)
    // ================================================================

    [Fact]
    public async Task DetectApexExceptions_FiresAutoTraceFlagEvent_WhenExceptionsHaveUserId()
    {
        var autoTraceUserIds = new List<string>();
        _detector.AutoTraceFlagRequested += (_, userId) => autoTraceUserIds.Add(userId);

        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "AutoFlagClass.method",
                UserId = "user-abc",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        autoTraceUserIds.Should().Contain("user-abc");
    }

    [Fact]
    public async Task DetectApexExceptions_DoesNotFireEvent_WhenExceptionsHaveNoUserId()
    {
        var autoTraceUserIds = new List<string>();
        _detector.AutoTraceFlagRequested += (_, userId) => autoTraceUserIds.Add(userId);

        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "AnonymousClass.method",
                UserId = null, // No user ID
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        autoTraceUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectApexExceptions_FiresOneEvent_PerUniqueUser()
    {
        var autoTraceUserIds = new List<string>();
        _detector.AutoTraceFlagRequested += (_, userId) => autoTraceUserIds.Add(userId);

        // 4 exceptions from 2 distinct users
        var events = new List<ShieldEvent>
        {
            new() { OrgId = _testOrgId, EventType = "ApexUnexpectedException", EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"), Uri = "MultiUserClass.method", UserId = "user-001", IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "ApexUnexpectedException", EventDate = DateTime.UtcNow.AddMinutes(-4).ToString("O"), Uri = "MultiUserClass.method", UserId = "user-001", IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "ApexUnexpectedException", EventDate = DateTime.UtcNow.AddMinutes(-3).ToString("O"), Uri = "MultiUserClass.method", UserId = "user-002", IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "ApexUnexpectedException", EventDate = DateTime.UtcNow.AddMinutes(-2).ToString("O"), Uri = "MultiUserClass.method", UserId = "user-002", IsSuccess = false },
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        // One event per unique user — not per exception
        autoTraceUserIds.Should().HaveCount(2);
        autoTraceUserIds.Should().Contain("user-001");
        autoTraceUserIds.Should().Contain("user-002");
    }

    [Fact]
    public async Task DetectApexExceptions_NoEvent_WhenBelowExceptionThreshold()
    {
        var autoTraceUserIds = new List<string>();
        _detector.AutoTraceFlagRequested += (_, userId) => autoTraceUserIds.Add(userId);

        // Only 1 exception — below threshold of 2
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "RareClass.method",
                UserId = "user-xyz",
                IsSuccess = false
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        autoTraceUserIds.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectApexExceptions_AlertDescription_ContainsAutoTraceFlagText_WhenUsersPresent()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "UserDescClass.method",
                UserId = "user-desc",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "UserDescClass.method" &&
            a.Description != null &&
            a.Description.Contains("Auto trace flag set"));
    }

    [Fact]
    public async Task DetectApexExceptions_AlertDescription_DoesNotContainAutoTraceFlagText_WhenNoUsers()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "ApexUnexpectedException",
                EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"),
                Uri = "NoUserClass.method",
                UserId = null,
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "NoUserClass.method" &&
            a.Description != null &&
            !a.Description.Contains("Auto trace flag set"));
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

    // ================================================================
    //  API Failure Correlation — DetectApiFailures
    // ================================================================

    [Fact]
    public async Task ApiFailureSpike_FiresAlert_WhenThreePlusFailuresSameEndpoint()
    {
        // Insert 4 failures on the same endpoint in the last hour
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 4; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"user-{i}",
                Uri = "/services/data/v59.0/query",
                StatusCode = 500,
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a => a.AlertType == "api_failure_spike");
    }

    [Fact]
    public async Task ApiFailureSpike_NoAlert_WhenBelowThreshold()
    {
        // Insert only 2 failures (below threshold of 3)
        var events = new List<ShieldEvent>
        {
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"), UserId = "user-1", Uri = "/services/data/v59.0/query", StatusCode = 500, IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-4).ToString("O"), UserId = "user-2", Uri = "/services/data/v59.0/query", StatusCode = 404, IsSuccess = false },
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.AlertType == "api_failure_spike").Should().BeEmpty();
    }

    [Fact]
    public async Task ApiFailureSpike_SetsAffectedUserCount_Correctly()
    {
        var events = new List<ShieldEvent>
        {
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-10).ToString("O"), UserId = "user-A", Uri = "/services/custom", StatusCode = 400, IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-9).ToString("O"),  UserId = "user-B", Uri = "/services/custom", StatusCode = 503, IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-8).ToString("O"),  UserId = "user-A", Uri = "/services/custom", StatusCode = 500, IsSuccess = false },  // duplicate user
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        var alert = _generatedAlerts.FirstOrDefault(a => a.AlertType == "api_failure_spike");
        alert.Should().NotBeNull();
        // 2 distinct users (user-A appears twice, but only counted once)
        alert!.AffectedUserCount.Should().Be(2);
    }

    [Fact]
    public async Task ApiFailureSpike_IsCritical_WhenTenOrMoreFailures()
    {
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 12; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-3).ToString("O"),
                UserId = $"user-{i}",
                Uri = "/services/data/v59.0/sobjects/Account",
                StatusCode = 500,
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "api_failure_spike" && a.Severity == "critical");
    }

    [Fact]
    public async Task ApiFailureRate_FiresAlert_WhenHighFailureRateWithEnoughCalls()
    {
        // 10 calls, 4 failures = 40% > 20% threshold
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"user-{i}",
                Uri = $"/services/data/v59.0/endpoint-{i}",  // different endpoints to avoid spike alert
                StatusCode = i < 4 ? 500 : 200,
                IsSuccess = i >= 4
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a => a.AlertType == "api_failure_rate");
    }

    [Fact]
    public async Task ApiFailureRate_NoAlert_WhenBelowThreshold()
    {
        // 10 calls, only 1 failure = 10% < 20% threshold
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"user-{i}",
                Uri = $"/services/data/v59.0/endpoint-{i}",
                StatusCode = i == 0 ? 500 : 200,
                IsSuccess = i > 0
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.AlertType == "api_failure_rate").Should().BeEmpty();
    }

    [Fact]
    public async Task ApiFailureRate_NoAlert_WhenLessThanTenCalls()
    {
        // 5 calls all failing = 100% but not enough data volume (< 10 calls)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 5; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId,
                EventType = "API",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"user-{i}",
                Uri = $"/services/data/v59.0/endpoint-{i}",
                StatusCode = 500,
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Where(a => a.AlertType == "api_failure_rate").Should().BeEmpty();
    }

    [Fact]
    public async Task ApiFailureSpike_AffectedUserCount_IsNull_WhenNoUserIds()
    {
        // Failures with no user IDs
        var events = new List<ShieldEvent>
        {
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"), UserId = null, Uri = "/anonymous", StatusCode = 500, IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-4).ToString("O"), UserId = null, Uri = "/anonymous", StatusCode = 500, IsSuccess = false },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.AddMinutes(-3).ToString("O"), UserId = null, Uri = "/anonymous", StatusCode = 500, IsSuccess = false },
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        var alert = _generatedAlerts.FirstOrDefault(a => a.AlertType == "api_failure_spike");
        alert.Should().NotBeNull();
        alert!.AffectedUserCount.Should().BeNull();
    }

    // ================================================================
    //  Data Exfiltration — ReportExport
    // ================================================================

    [Fact]
    public async Task DetectsLargeReportExport_WhenRowsExceedThreshold()
    {
        // 8000 rows exported — above default threshold of 5000
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId, EventType = "ReportExport",
                EventDate = DateTime.UtcNow.AddMinutes(-30).ToString("O"),
                UserId = "user-exporter", Uri = "00O123",
                RowCount = 8000, IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_data_exfiltration" &&
            a.MetricName == "report_export_rows" &&
            a.EntryPoint == "user-exporter");
    }

    [Fact]
    public async Task NoAlertForSmallReportExport_WhenBelowThreshold()
    {
        // Only 1000 rows — well below default threshold of 5000
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId, EventType = "ReportExport",
                EventDate = DateTime.UtcNow.AddMinutes(-30).ToString("O"),
                UserId = "normal-user", Uri = "00O456",
                RowCount = 1000, IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().NotContain(a =>
            a.AlertType == "shield_data_exfiltration" &&
            a.MetricName == "report_export_rows");
    }

    [Fact]
    public async Task LargeReportExport_Critical_WhenRowsExceedFourTimesThreshold()
    {
        // 25000 rows = 5× default threshold → critical
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId, EventType = "ReportExport",
                EventDate = DateTime.UtcNow.AddMinutes(-15).ToString("O"),
                UserId = "bulk-exporter", Uri = "00O789",
                RowCount = 25000, IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        var alert = _generatedAlerts.FirstOrDefault(a =>
            a.AlertType == "shield_data_exfiltration" && a.MetricName == "report_export_rows");
        alert.Should().NotBeNull();
        alert!.Severity.Should().Be("critical");
    }

    // ================================================================
    //  Data Exfiltration — BulkApi
    // ================================================================

    [Fact]
    public async Task DetectsHighVolumeBulkApiOperation()
    {
        // 12000 rows via Bulk API — above 2× threshold (2 × 5000 = 10000)
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId, EventType = "BulkApi",
                EventDate = DateTime.UtcNow.AddMinutes(-20).ToString("O"),
                UserId = "etl-user", Uri = "Contact",
                RowCount = 12000, IsSuccess = true
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_data_exfiltration" &&
            a.MetricName == "bulk_api_rows" &&
            a.EntryPoint == "etl-user");
    }

    // ================================================================
    //  Permission Changes — SetupAuditTrail
    // ================================================================

    [Fact]
    public async Task DetectsPermissionSetChange_InHighRiskSection()
    {
        // Insert 2 PermissionSets changes + 1 Profiles change (3 total — triggers warning threshold)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 2; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId, EventType = "SetupAuditTrail",
                EventDate = DateTime.UtcNow.AddHours(-2).ToString("O"),
                UserId = "admin-001", Uri = "PermissionSets",
                ExtraJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "PermissionSetAssigned",
                    section = "PermissionSets",
                    display = $"Assigned perm set {i}"
                })
            });
        }
        events.Add(new ShieldEvent
        {
            OrgId = _testOrgId, EventType = "SetupAuditTrail",
            EventDate = DateTime.UtcNow.AddHours(-1).ToString("O"),
            UserId = "admin-002", Uri = "Profiles",
            ExtraJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                action = "ProfileUpdated",
                section = "Profiles",
                display = "Modified Admin profile"
            })
        });

        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "shield_permission_change" &&
            a.MetricName == "permission_changes");
    }

    [Fact]
    public async Task NoPermissionChangeAlert_ForNonSensitiveSection()
    {
        // A setup change in a non-sensitive section (e.g., "Dashboards")
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId, EventType = "SetupAuditTrail",
                EventDate = DateTime.UtcNow.AddHours(-1).ToString("O"),
                UserId = "admin-003", Uri = "Dashboards",
                ExtraJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    action = "DashboardCreated",
                    section = "Dashboards",
                    display = "New sales dashboard"
                })
            }
        };
        await _db.InsertShieldEventsAsync(events);

        await _detector.RunDetectionAsync();

        _generatedAlerts.Should().NotContain(a =>
            a.AlertType == "shield_permission_change" &&
            a.MetricName == "permission_changes");
    }

    // ================================================================
    //  Configurable Thresholds
    // ================================================================

    [Fact]
    public async Task ConfigurableThreshold_LowerFailedLoginThreshold_TriggersEarlier()
    {
        // Default threshold = 5 — but override to 2 via settings
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.ShieldFailedLoginThreshold = 2;
        settingsService.Save(settings);

        var detector = new ShieldAnomalyDetector(_db, settingsService);
        var alerts = new List<MonitoringAlert>();
        detector.AlertGenerated += (_, a) => alerts.Add(a);

        // Insert only 3 failed logins (between 2 and 5)
        var events = new List<ShieldEvent>();
        for (int i = 0; i < 3; i++)
        {
            events.Add(new ShieldEvent
            {
                OrgId = _testOrgId, EventType = "Login",
                EventDate = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
                UserId = $"victim-{i}", ClientIp = $"192.168.1.{i}",
                IsSuccess = false
            });
        }
        await _db.InsertShieldEventsAsync(events);

        await detector.RunDetectionAsync();

        alerts.Should().Contain(a =>
            a.AlertType == "shield_login_anomaly" &&
            a.MetricName == "failed_logins");
    }
}
