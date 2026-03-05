using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Integration tests for TrendAnalysisService.
/// Uses a real SQLite database (per-test temp org) to verify aggregation,
/// baseline computation, deviation detection, and alert generation.
/// </summary>
public class TrendAnalysisServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testOrgId;
    private readonly MonitoringDatabaseService _db;
    private readonly TrendAnalysisService _trendService;
    private readonly List<MonitoringAlert> _generatedAlerts = new();

    public TrendAnalysisServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _testOrgId = $"trend_test_{Guid.NewGuid():N}";
        _db = new MonitoringDatabaseService(_testOrgId);
        _trendService = new TrendAnalysisService(_db);
        _trendService.AlertGenerated += (_, alert) => _generatedAlerts.Add(alert);
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
    //  Basic Lifecycle
    // ================================================================

    [Fact]
    public async Task RunAnalysisCycle_EmptyDb_DoesNotThrow()
    {
        var act = () => _trendService.RunAnalysisCycleAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAnalysisCycle_EmptyDb_GeneratesNoAlerts()
    {
        await _trendService.RunAnalysisCycleAsync();
        _generatedAlerts.Should().BeEmpty();
    }

    // ================================================================
    //  Aggregation
    // ================================================================

    [Fact]
    public async Task Aggregation_CreatesHourlyAggregates()
    {
        // Insert snapshots for a single entry point in the last hour
        for (int i = 0; i < 5; i++)
        {
            await _db.InsertSnapshotAsync(CreateSnapshot($"agg-{i}", "TriggerA",
                durationMs: 1000 + i * 100, soqlCount: 10 + i, soqlLimit: 100));
        }

        await _trendService.RunAnalysisCycleAsync();

        // Verify aggregates were created
        var aggs = await _db.GetAggregatesAsync("duration_ms", "hourly",
            DateTime.UtcNow.AddHours(-2), "TriggerA");
        aggs.Should().NotBeEmpty();
        aggs[0].SampleCount.Should().Be(5);
    }

    [Fact]
    public async Task Aggregation_ComputesCorrectAverage()
    {
        var durations = new[] { 100.0, 200.0, 300.0, 400.0, 500.0 };
        for (int i = 0; i < durations.Length; i++)
        {
            await _db.InsertSnapshotAsync(CreateSnapshot($"avg-{i}", "TriggerB",
                durationMs: durations[i]));
        }

        await _trendService.RunAnalysisCycleAsync();

        var aggs = await _db.GetAggregatesAsync("duration_ms", "hourly",
            DateTime.UtcNow.AddHours(-2), "TriggerB");
        aggs.Should().NotBeEmpty();
        aggs[0].AvgValue.Should().Be(300); // (100+200+300+400+500)/5
        aggs[0].MinValue.Should().Be(100);
        aggs[0].MaxValue.Should().Be(500);
    }

    // ================================================================
    //  Baseline Computation
    // ================================================================

    [Fact]
    public async Task Baseline_NotCreated_WhenLessThan10Samples()
    {
        // Insert only 5 snapshots (below MinBaselineSamples = 10)
        for (int i = 0; i < 5; i++)
        {
            await _db.InsertSnapshotAsync(CreateSnapshot($"base-{i}", "SmallTrigger", durationMs: 1000));
        }

        await _trendService.RunAnalysisCycleAsync();

        var baseline = await _db.GetBaselineAsync("SmallTrigger", "duration_ms");
        baseline.Should().BeNull();
    }

    [Fact]
    public async Task Baseline_Created_WhenSufficientSamples()
    {
        // Insert 15 snapshots (above MinBaselineSamples = 10)
        for (int i = 0; i < 15; i++)
        {
            await _db.InsertSnapshotAsync(CreateSnapshot($"base15-{i}", "BigTrigger",
                durationMs: 1000 + i * 10));
        }

        await _trendService.RunAnalysisCycleAsync();

        var baseline = await _db.GetBaselineAsync("BigTrigger", "duration_ms");
        baseline.Should().NotBeNull();
        baseline!.SampleCount.Should().Be(15);
        baseline.BaselineValue.Should().BeGreaterThan(0);
        baseline.Stddev.Should().BeGreaterThan(0);
    }

    // ================================================================
    //  Statistical Deviation Detection
    // ================================================================

    [Fact]
    public async Task StatisticalDeviation_DetectsDurationSpike()
    {
        // Create a baseline with known values
        await _db.UpsertBaselineAsync(new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "SpikeTrigger",
            MetricName = "duration_ms",
            BaselineValue = 100,
            Stddev = 10,
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        });

        // Insert recent snapshot with huge deviation (z-score > 3)
        await _db.InsertSnapshotAsync(CreateSnapshot("spike-1", "SpikeTrigger", durationMs: 200));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "perf_degradation" &&
            a.EntryPoint == "SpikeTrigger" &&
            a.Severity == "critical");
    }

    [Fact]
    public async Task StatisticalDeviation_WarningForModerateDeviation()
    {
        await _db.UpsertBaselineAsync(new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "ModTrigger",
            MetricName = "duration_ms",
            BaselineValue = 100,
            Stddev = 10,
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        });

        // z-score = (125-100)/10 = 2.5 => warning
        await _db.InsertSnapshotAsync(CreateSnapshot("mod-1", "ModTrigger", durationMs: 125));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "perf_degradation" &&
            a.EntryPoint == "ModTrigger" &&
            a.Severity == "warning");
    }

    [Fact]
    public async Task StatisticalDeviation_NoAlert_WhenWithinNormal()
    {
        await _db.UpsertBaselineAsync(new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "NormalTrigger",
            MetricName = "duration_ms",
            BaselineValue = 100,
            Stddev = 10,
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        });

        // z-score = (110-100)/10 = 1.0 => no alert
        await _db.InsertSnapshotAsync(CreateSnapshot("normal-1", "NormalTrigger", durationMs: 110));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().NotContain(a =>
            a.EntryPoint == "NormalTrigger" && a.MetricName == "duration_ms");
    }

    [Fact]
    public async Task StatisticalDeviation_SkipsLowStddev()
    {
        await _db.UpsertBaselineAsync(new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "FlatTrigger",
            MetricName = "duration_ms",
            BaselineValue = 100,
            Stddev = 0.0001, // Too low
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        });

        await _db.InsertSnapshotAsync(CreateSnapshot("flat-1", "FlatTrigger", durationMs: 200));

        await _trendService.RunAnalysisCycleAsync();

        // Alert for duration_ms via statistical deviation should not be generated
        // (but absolute threshold might trigger for different metrics)
        _generatedAlerts.Where(a => a.MetricName == "duration_ms" && a.EntryPoint == "FlatTrigger"
            && a.Description.Contains("z-score")).Should().BeEmpty();
    }

    [Fact]
    public async Task StatisticalDeviation_DetectsErrorSpike()
    {
        await _db.UpsertBaselineAsync(new Baseline
        {
            OrgId = _testOrgId,
            EntryPoint = "ErrorTrigger",
            MetricName = "error_count",
            BaselineValue = 0.5,
            Stddev = 0.1,
            SampleCount = 50,
            LastUpdated = DateTime.UtcNow
        });

        await _db.InsertSnapshotAsync(CreateSnapshot("err-1", "ErrorTrigger",
            durationMs: 100, errorCount: 5, hasErrors: true));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "error_spike" &&
            a.EntryPoint == "ErrorTrigger");
    }

    // ================================================================
    //  Absolute Threshold Detection
    // ================================================================

    [Fact]
    public async Task AbsoluteThreshold_SoqlWarning()
    {
        // SOQL at 85% of limit
        await _db.InsertSnapshotAsync(CreateSnapshot("soql-warn", "SoqlTrigger",
            soqlCount: 85, soqlLimit: 100));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "governor_trending" &&
            a.EntryPoint == "SoqlTrigger" &&
            a.MetricName == "soql_pct" &&
            a.Severity == "warning");
    }

    [Fact]
    public async Task AbsoluteThreshold_SoqlCritical()
    {
        // SOQL at 95% of limit
        await _db.InsertSnapshotAsync(CreateSnapshot("soql-crit", "SoqlCritTrigger",
            soqlCount: 95, soqlLimit: 100));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "governor_trending" &&
            a.EntryPoint == "SoqlCritTrigger" &&
            a.MetricName == "soql_pct" &&
            a.Severity == "critical");
    }

    [Fact]
    public async Task AbsoluteThreshold_DmlWarning()
    {
        // DML at 85% of limit
        await _db.InsertSnapshotAsync(CreateSnapshot("dml-warn", "DmlTrigger",
            dmlCount: 128, dmlLimit: 150));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "governor_trending" &&
            a.MetricName == "dml_pct");
    }

    [Fact]
    public async Task AbsoluteThreshold_DurationWarning()
    {
        // Duration at 15000ms (above 10000ms warning threshold)
        await _db.InsertSnapshotAsync(CreateSnapshot("dur-warn", "SlowTrigger",
            durationMs: 15000));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "perf_degradation" &&
            a.EntryPoint == "SlowTrigger" &&
            a.MetricName == "duration_ms" &&
            a.Severity == "warning");
    }

    [Fact]
    public async Task AbsoluteThreshold_DurationCritical()
    {
        // Duration at 25000ms (above 20000ms critical threshold)
        await _db.InsertSnapshotAsync(CreateSnapshot("dur-crit", "VerySlow",
            durationMs: 25000));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.AlertType == "perf_degradation" &&
            a.EntryPoint == "VerySlow" &&
            a.MetricName == "duration_ms" &&
            a.Severity == "critical");
    }

    [Fact]
    public async Task AbsoluteThreshold_LowHealthScore_Warning()
    {
        // Health score 50 (below 60 = warning)
        await _db.InsertSnapshotAsync(CreateSnapshot("health-warn", "UnhealthyTrigger",
            healthScore: 50));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "UnhealthyTrigger" &&
            a.MetricName == "health_score" &&
            a.Severity == "warning");
    }

    [Fact]
    public async Task AbsoluteThreshold_VeryLowHealthScore_Critical()
    {
        // Health score 30 (below 40 = critical)
        await _db.InsertSnapshotAsync(CreateSnapshot("health-crit", "TerribleTrigger",
            healthScore: 30));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "TerribleTrigger" &&
            a.MetricName == "health_score" &&
            a.Severity == "critical");
    }

    [Fact]
    public async Task AbsoluteThreshold_ErrorRate_Warning()
    {
        // 2 out of 5 logs have errors (40% > 10% warning threshold)
        for (int i = 0; i < 5; i++)
        {
            await _db.InsertSnapshotAsync(CreateSnapshot($"errrate-{i}", "ErrorRateTrigger",
                hasErrors: i < 2, errorCount: i < 2 ? 1 : 0));
        }

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "ErrorRateTrigger" &&
            a.MetricName == "error_rate");
    }

    [Fact]
    public async Task AbsoluteThreshold_NoAlert_WhenBelowThresholds()
    {
        // Everything normal
        await _db.InsertSnapshotAsync(CreateSnapshot("ok-1", "HealthyTrigger",
            durationMs: 500, soqlCount: 10, soqlLimit: 100, healthScore: 90));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Where(a => a.EntryPoint == "HealthyTrigger").Should().BeEmpty();
    }

    // ================================================================
    //  Linear Projection
    // ================================================================

    [Fact]
    public async Task LinearProjection_DetectsTrendingToLimit()
    {
        // Insert daily aggregates trending upward: 50%, 55%, 60%, 65%, 70%, 75%, 80%
        for (int i = 0; i < 7; i++)
        {
            var agg = new MetricAggregate
            {
                OrgId = _testOrgId,
                PeriodStart = DateTime.UtcNow.AddDays(-7 + i),
                PeriodType = "daily",
                EntryPoint = "ProjectionTrigger",
                MetricName = "soql_pct",
                SampleCount = 20,
                AvgValue = 50 + i * 5,
                MinValue = 40 + i * 5,
                MaxValue = 60 + i * 5,
                P50Value = 50 + i * 5,
                P90Value = 55 + i * 5,
                P99Value = 58 + i * 5,
                StddevValue = 5
            };
            await _db.UpsertAggregateAsync(agg);
        }

        // Insert a snapshot with SOQL below the absolute warning threshold (80%)
        // so the absolute threshold check doesn't fire and dedup the projection alert.
        await _db.InsertSnapshotAsync(CreateSnapshot("proj-1", "ProjectionTrigger",
            soqlCount: 70, soqlLimit: 100));

        await _trendService.RunAnalysisCycleAsync();

        // slope ≈ 5%/day, current ≈ 80% (last agg value), days to 100% ≈ 4 days → critical (< 7)
        _generatedAlerts.Should().Contain(a =>
            a.EntryPoint == "ProjectionTrigger" &&
            a.MetricName == "soql_pct" &&
            a.Title.Contains("projected"));
    }

    [Fact]
    public async Task LinearProjection_NoAlert_WhenNotTrending()
    {
        // Flat daily aggregates at 30%
        for (int i = 0; i < 5; i++)
        {
            var agg = new MetricAggregate
            {
                OrgId = _testOrgId,
                PeriodStart = DateTime.UtcNow.AddDays(-5 + i),
                PeriodType = "daily",
                EntryPoint = "FlatTrigger",
                MetricName = "soql_pct",
                SampleCount = 20,
                AvgValue = 30,
                MinValue = 25,
                MaxValue = 35,
                P50Value = 30,
                P90Value = 33,
                P99Value = 34,
                StddevValue = 3
            };
            await _db.UpsertAggregateAsync(agg);
        }

        await _db.InsertSnapshotAsync(CreateSnapshot("flat-p-1", "FlatTrigger",
            soqlCount: 30, soqlLimit: 100));

        await _trendService.RunAnalysisCycleAsync();

        _generatedAlerts.Where(a =>
            a.EntryPoint == "FlatTrigger" && a.Title.Contains("projected"))
            .Should().BeEmpty();
    }

    // ================================================================
    //  Alert Deduplication
    // ================================================================

    [Fact]
    public async Task AlertDedup_DoesNotCreateDuplicate()
    {
        // SOQL at 85%
        await _db.InsertSnapshotAsync(CreateSnapshot("dedup-1", "DedupTrigger",
            soqlCount: 85, soqlLimit: 100));

        await _trendService.RunAnalysisCycleAsync();
        var firstCount = _generatedAlerts.Count(a => a.EntryPoint == "DedupTrigger");

        // Run again with a new high snapshot
        await _db.InsertSnapshotAsync(CreateSnapshot("dedup-2", "DedupTrigger",
            soqlCount: 87, soqlLimit: 100));
        await _trendService.RunAnalysisCycleAsync();

        var secondCount = _generatedAlerts.Count(a => a.EntryPoint == "DedupTrigger");
        secondCount.Should().Be(firstCount, "24h dedup should prevent duplicate alerts");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private LogSnapshot CreateSnapshot(string logId, string entryPoint,
        double durationMs = 500, int soqlCount = 5, int soqlLimit = 100,
        int dmlCount = 2, int dmlLimit = 150, int healthScore = 80,
        int errorCount = 0, bool hasErrors = false)
    {
        return new LogSnapshot
        {
            LogId = logId,
            OrgId = _testOrgId,
            CapturedAt = DateTime.UtcNow,
            EntryPoint = entryPoint,
            OperationType = "TRIGGERS",
            LogUser = "test@test.com",
            DurationMs = durationMs,
            CpuTimeMs = (int)(durationMs * 0.3),
            SoqlCount = soqlCount,
            SoqlLimit = soqlLimit,
            DmlCount = dmlCount,
            DmlLimit = dmlLimit,
            QueryRows = 100,
            QueryRowsLimit = 50000,
            HeapSize = 50000,
            HeapLimit = 6000000,
            CalloutCount = 0,
            CalloutLimit = 100,
            HealthScore = healthScore,
            HealthGrade = healthScore >= 80 ? "A" : healthScore >= 60 ? "B" : "C",
            BulkSafetyGrade = "A",
            HasErrors = hasErrors,
            TransactionFailed = false,
            ErrorCount = errorCount,
            HandledExceptionCount = 0,
            DuplicateQueryCount = 0,
            NPlusOneWorst = 0,
            StackDepthMax = 2,
            IsAsync = false,
            IsTruncated = false,
            Source = "debug_log"
        };
    }
}
