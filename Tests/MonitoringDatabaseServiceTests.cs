using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests MonitoringDatabaseService: schema creation, CRUD operations, retention, and Shield methods.
/// Each test gets a fresh in-memory-like temp DB to avoid cross-test interference.
/// </summary>
public class MonitoringDatabaseServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private MonitoringDatabaseService _db;
    private readonly string _testOrgId;

    public MonitoringDatabaseServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _testOrgId = $"test_org_{Guid.NewGuid():N}";
        _db = new MonitoringDatabaseService(_testOrgId);
    }

    public void Dispose()
    {
        _db?.Dispose();
        // Clean up the test database file
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
    //  Schema & Initialization
    // ================================================================

    [Fact]
    public void Constructor_CreatesDatabase_WithCorrectOrgId()
    {
        _db.OrgId.Should().Be(_testOrgId);
    }

    [Fact]
    public async Task GetRecentLogIds_EmptyDb_ReturnsEmptyList()
    {
        var ids = await _db.GetRecentLogIdsAsync(100);
        ids.Should().BeEmpty();
    }

    // ================================================================
    //  Snapshot CRUD
    // ================================================================

    [Fact]
    public async Task InsertSnapshot_And_IsLogPersisted_ReturnsTrue()
    {
        var snapshot = CreateTestSnapshot("log-001");
        await _db.InsertSnapshotAsync(snapshot);

        var persisted = await _db.IsLogPersistedAsync("log-001");
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task IsLogPersisted_NonexistentLog_ReturnsFalse()
    {
        var persisted = await _db.IsLogPersistedAsync("nonexistent-log");
        persisted.Should().BeFalse();
    }

    [Fact]
    public async Task GetRecentLogIds_ReturnsInsertedIds()
    {
        await _db.InsertSnapshotAsync(CreateTestSnapshot("log-A"));
        await _db.InsertSnapshotAsync(CreateTestSnapshot("log-B"));
        await _db.InsertSnapshotAsync(CreateTestSnapshot("log-C"));

        var ids = await _db.GetRecentLogIdsAsync(100);
        ids.Should().HaveCount(3);
        ids.Should().Contain(new[] { "log-A", "log-B", "log-C" });
    }

    [Fact]
    public async Task GetRecentLogIds_RespectsLimit()
    {
        for (int i = 0; i < 10; i++)
            await _db.InsertSnapshotAsync(CreateTestSnapshot($"log-{i}"));

        var ids = await _db.GetRecentLogIdsAsync(5);
        ids.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetSnapshotsSince_FiltersCorrectly()
    {
        var oldSnapshot = CreateTestSnapshot("old");
        oldSnapshot.CapturedAt = DateTime.UtcNow.AddHours(-5);
        await _db.InsertSnapshotAsync(oldSnapshot);

        var newSnapshot = CreateTestSnapshot("new");
        newSnapshot.CapturedAt = DateTime.UtcNow;
        await _db.InsertSnapshotAsync(newSnapshot);

        var since = DateTime.UtcNow.AddHours(-1);
        var results = await _db.GetSnapshotsSinceAsync(since, null);
        results.Should().HaveCount(1);
        results[0].LogId.Should().Be("new");
    }

    [Fact]
    public async Task GetSnapshotsSince_FiltersByEntryPoint()
    {
        var s1 = CreateTestSnapshot("log-1");
        s1.EntryPoint = "TriggerA";
        await _db.InsertSnapshotAsync(s1);

        var s2 = CreateTestSnapshot("log-2");
        s2.EntryPoint = "TriggerB";
        await _db.InsertSnapshotAsync(s2);

        var results = await _db.GetSnapshotsSinceAsync(DateTime.UtcNow.AddHours(-1), "TriggerA");
        results.Should().HaveCount(1);
        results[0].EntryPoint.Should().Be("TriggerA");
    }

    [Fact]
    public async Task GetDistinctEntryPoints_ReturnsUniqueValues()
    {
        var s1 = CreateTestSnapshot("log-1");
        s1.EntryPoint = "TriggerA";
        await _db.InsertSnapshotAsync(s1);

        var s2 = CreateTestSnapshot("log-2");
        s2.EntryPoint = "TriggerB";
        await _db.InsertSnapshotAsync(s2);

        var s3 = CreateTestSnapshot("log-3");
        s3.EntryPoint = "TriggerA"; // Duplicate
        await _db.InsertSnapshotAsync(s3);

        var entryPoints = await _db.GetDistinctEntryPointsAsync();
        entryPoints.Should().HaveCount(2);
        entryPoints.Should().Contain(new[] { "TriggerA", "TriggerB" });
    }

    // ================================================================
    //  Aggregate CRUD
    // ================================================================

    [Fact]
    public async Task UpsertAggregate_InsertsAndUpdates()
    {
        var agg = new MetricAggregate
        {
            OrgId = _testOrgId,
            PeriodStart = DateTime.UtcNow.AddHours(-1),
            PeriodType = "hourly",
            EntryPoint = "TestTrigger",
            MetricName = "duration_ms",
            SampleCount = 5,
            AvgValue = 100,
            MinValue = 50,
            MaxValue = 200,
            P50Value = 90,
            P90Value = 180,
            P99Value = 195,
            StddevValue = 30
        };

        await _db.UpsertAggregateAsync(agg);

        var results = await _db.GetAggregatesAsync("duration_ms", "hourly", DateTime.UtcNow.AddHours(-2), "TestTrigger");
        results.Should().HaveCount(1);
        results[0].AvgValue.Should().Be(100);

        // Update
        agg.AvgValue = 200;
        agg.SampleCount = 10;
        await _db.UpsertAggregateAsync(agg);

        results = await _db.GetAggregatesAsync("duration_ms", "hourly", DateTime.UtcNow.AddHours(-2), "TestTrigger");
        results.Should().HaveCount(1);
        results[0].AvgValue.Should().Be(200);
    }

    // ================================================================
    //  Baseline CRUD
    // ================================================================

    [Fact]
    public async Task UpsertBaseline_And_GetBaseline()
    {
        var baseline = new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "CaseTrigger",
            MetricName = "duration_ms",
            BaselineValue = 500,
            Stddev = 100,
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        };

        await _db.UpsertBaselineAsync(baseline);

        var result = await _db.GetBaselineAsync("CaseTrigger", "duration_ms");
        result.Should().NotBeNull();
        result!.BaselineValue.Should().Be(500);
        result.Stddev.Should().Be(100);
        result.SampleCount.Should().Be(50);
    }

    [Fact]
    public async Task GetBaseline_NonExistent_ReturnsNull()
    {
        var result = await _db.GetBaselineAsync("NonExistent", "duration_ms");
        result.Should().BeNull();
    }

    // ================================================================
    //  Alert CRUD
    // ================================================================

    [Fact]
    public async Task InsertAlert_And_GetAlerts()
    {
        var alert = CreateTestAlert("Test alert", "warning");
        await _db.InsertAlertAsync(alert);

        var alerts = await _db.GetAlertsAsync();
        alerts.Should().NotBeEmpty();
        alerts.First().Title.Should().Be("Test alert");
    }

    [Fact]
    public async Task GetRecentAlert_FindsDuplicate()
    {
        var alert = CreateTestAlert("Duplicate test", "critical");
        alert.AlertType = "governor_trending";
        alert.EntryPoint = "TestTrigger";
        alert.MetricName = "soql_pct";
        await _db.InsertAlertAsync(alert);

        var existing = await _db.GetRecentAlertAsync("governor_trending", "TestTrigger", "soql_pct");
        existing.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRecentAlert_NoDuplicate_ReturnsNull()
    {
        var existing = await _db.GetRecentAlertAsync("nonexistent_type", "NonExistent", "none");
        existing.Should().BeNull();
    }

    [Fact]
    public async Task MarkAllRead_SetsIsRead()
    {
        await _db.InsertAlertAsync(CreateTestAlert("Alert 1", "warning"));
        await _db.InsertAlertAsync(CreateTestAlert("Alert 2", "critical"));

        await _db.MarkAllReadAsync();

        var alerts = await _db.GetAlertsAsync();
        alerts.Should().AllSatisfy(a => a.IsRead.Should().BeTrue());
    }

    [Fact]
    public async Task DismissAlert_SetsIsDismissed()
    {
        var alert = CreateTestAlert("Alert to dismiss", "warning");
        await _db.InsertAlertAsync(alert);

        var alerts = await _db.GetAlertsAsync();
        var alertId = alerts.First().Id;

        await _db.DismissAlertAsync(alertId);

        alerts = await _db.GetAlertsAsync();
        alerts.Should().BeEmpty(); // Dismissed alerts are excluded from GetAlertsAsync
    }

    // ================================================================
    //  Shield CRUD
    // ================================================================

    [Fact]
    public async Task IsLogFileProcessed_NewFile_ReturnsFalse()
    {
        var result = await _db.IsLogFileProcessedAsync("new-file-id");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task InsertShieldLogFileRecord_And_IsLogFileProcessed()
    {
        var record = new ShieldLogFileRecord
        {
            OrgId = _testOrgId,
            EventType = "Login",
            LogDate = "2024-01-15",
            LogFileId = "file-001",
            IntervalType = "Hourly",
            ProcessedAt = DateTime.UtcNow,
            RecordCount = 50,
            FileSize = 1024
        };

        await _db.InsertShieldLogFileRecordAsync(record);

        var processed = await _db.IsLogFileProcessedAsync("file-001");
        processed.Should().BeTrue();
    }

    [Fact]
    public async Task InsertShieldEvents_And_GetRecentEvents()
    {
        var events = new List<ShieldEvent>
        {
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.ToString("O"),
                UserId = "user-001",
                ClientIp = "192.168.1.1",
                IsSuccess = true
            },
            new()
            {
                OrgId = _testOrgId,
                EventType = "Login",
                EventDate = DateTime.UtcNow.ToString("O"),
                UserId = "user-002",
                ClientIp = "10.0.0.1",
                IsSuccess = false
            }
        };

        await _db.InsertShieldEventsAsync(events);

        var result = await _db.GetRecentShieldEventsAsync("Login", DateTime.UtcNow.AddHours(-1));
        result.Should().HaveCount(2);
        result.Should().Contain(e => e.UserId == "user-001" && e.IsSuccess);
        result.Should().Contain(e => e.UserId == "user-002" && !e.IsSuccess);
    }

    [Fact]
    public async Task GetRecentShieldEvents_FiltersEventType()
    {
        var events = new List<ShieldEvent>
        {
            new() { OrgId = _testOrgId, EventType = "Login", EventDate = DateTime.UtcNow.ToString("O") },
            new() { OrgId = _testOrgId, EventType = "API", EventDate = DateTime.UtcNow.ToString("O") }
        };
        await _db.InsertShieldEventsAsync(events);

        var loginEvents = await _db.GetRecentShieldEventsAsync("Login", DateTime.UtcNow.AddHours(-1));
        loginEvents.Should().HaveCount(1);
        loginEvents[0].EventType.Should().Be("Login");
    }

    // ================================================================
    //  Pruning
    // ================================================================

    [Fact]
    public async Task PruneOldData_DoesNotThrow()
    {
        // Insert some data
        await _db.InsertSnapshotAsync(CreateTestSnapshot("prune-test"));
        await _db.InsertAlertAsync(CreateTestAlert("Prune test alert", "info"));

        // Prune should not throw even with recent data
        var act = () => _db.PruneOldDataAsync();
        await act.Should().NotThrowAsync();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private LogSnapshot CreateTestSnapshot(string logId)
    {
        return new LogSnapshot
        {
            LogId = logId,
            OrgId = _testOrgId,
            CapturedAt = DateTime.UtcNow,
            EntryPoint = "TestTrigger",
            OperationType = "TRIGGERS",
            LogUser = "testuser@test.com",
            DurationMs = 1500,
            CpuTimeMs = 500,
            SoqlCount = 10,
            SoqlLimit = 100,
            DmlCount = 5,
            DmlLimit = 150,
            QueryRows = 100,
            QueryRowsLimit = 50000,
            HeapSize = 50000,
            HeapLimit = 6000000,
            CalloutCount = 0,
            CalloutLimit = 100,
            HealthScore = 75,
            HealthGrade = "C",
            BulkSafetyGrade = "B",
            HasErrors = false,
            TransactionFailed = false,
            ErrorCount = 0,
            HandledExceptionCount = 0,
            DuplicateQueryCount = 2,
            NPlusOneWorst = 5,
            StackDepthMax = 3,
            IsAsync = false,
            IsTruncated = false,
            Source = "debug_log"
        };
    }

    private MonitoringAlert CreateTestAlert(string title, string severity)
    {
        return new MonitoringAlert
        {
            OrgId = _testOrgId,
            CreatedAt = DateTime.UtcNow,
            AlertType = "test_alert",
            Severity = severity,
            Title = title,
            Description = $"Test alert: {title}",
            EntryPoint = "TestTrigger",
            MetricName = "test_metric",
            CurrentValue = 50,
            BaselineValue = 25,
            ThresholdValue = 40
        };
    }
}
