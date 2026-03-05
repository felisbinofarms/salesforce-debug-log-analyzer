using Serilog;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Runs periodic trend analysis: aggregates snapshots, computes baselines,
/// detects statistical deviations, and generates alerts.
/// </summary>
public class TrendAnalysisService
{
    private readonly MonitoringDatabaseService _db;
    private DateTime _lastPruneDate;

    // Minimum samples before statistical alerting kicks in
    private const int MinBaselineSamples = 10;
    private const int BaselineWindowDays = 14;

    // Z-score thresholds
    private const double WarningZScore = 2.0;
    private const double CriticalZScore = 3.0;

    // Absolute governor limit thresholds (percent of limit)
    private const double GovernorWarningPct = 0.80;
    private const double GovernorCriticalPct = 0.90;

    // Performance thresholds (milliseconds)
    private const double DurationWarningMs = 10_000;
    private const double DurationCriticalMs = 20_000;

    // Health score thresholds
    private const double HealthWarningMin = 60;
    private const double HealthCriticalMin = 40;

    // Error rate thresholds (errors per hour)
    private const double ErrorRateWarning = 0.10;
    private const double ErrorRateCritical = 0.25;

    // Linear projection horizon
    private const int ProjectionCriticalDays = 7;
    private const int ProjectionWarningDays = 14;

    /// <summary>Fired when a new alert is generated.</summary>
    public event EventHandler<MonitoringAlert>? AlertGenerated;

    public TrendAnalysisService(MonitoringDatabaseService db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs a full analysis cycle: aggregate, baseline, detect, alert, prune.
    /// Called periodically (default every 5 minutes) from BackgroundMonitoringService.
    /// </summary>
    public async Task RunAnalysisCycleAsync()
    {
        try
        {
            // 1. Aggregate new snapshots into hourly buckets
            await AggregateSnapshotsAsync();

            // 2. Update baselines per entry point
            await UpdateBaselinesAsync();

            // 3. Run detection checks
            await RunDetectionChecksAsync();

            // 4. Prune old data (once per day)
            if (DateTime.UtcNow.Date > _lastPruneDate)
            {
                await _db.PruneOldDataAsync();
                _lastPruneDate = DateTime.UtcNow.Date;
                Log.Information("Monitoring data pruned");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Trend analysis cycle failed");
        }
    }

    /// <summary>
    /// Aggregates recent snapshots into hourly metric aggregates.
    /// </summary>
    private async Task AggregateSnapshotsAsync()
    {
        var entryPoints = await _db.GetDistinctEntryPointsAsync();
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var periodStart = new DateTime(oneHourAgo.Year, oneHourAgo.Month, oneHourAgo.Day,
            oneHourAgo.Hour, 0, 0, DateTimeKind.Utc);

        foreach (var ep in entryPoints)
        {
            var snapshots = await _db.GetSnapshotsSinceAsync(periodStart, ep);
            if (snapshots.Count == 0) continue;

            var metrics = new Dictionary<string, List<double>>
            {
                ["duration_ms"] = snapshots.Select(s => (double)s.DurationMs).ToList(),
                ["soql_pct"] = snapshots.Where(s => s.SoqlLimit > 0)
                    .Select(s => (double)s.SoqlCount / s.SoqlLimit * 100).ToList(),
                ["dml_pct"] = snapshots.Where(s => s.DmlLimit > 0)
                    .Select(s => (double)s.DmlCount / s.DmlLimit * 100).ToList(),
                ["cpu_time_ms"] = snapshots.Select(s => (double)s.CpuTimeMs).ToList(),
                ["health_score"] = snapshots.Select(s => (double)s.HealthScore).ToList(),
                ["error_count"] = snapshots.Select(s => (double)s.ErrorCount).ToList(),
                ["duplicate_queries"] = snapshots.Select(s => (double)s.DuplicateQueryCount).ToList(),
            };

            foreach (var (metricName, values) in metrics)
            {
                if (values.Count == 0) continue;

                var sorted = values.OrderBy(v => v).ToList();
                var agg = new MetricAggregate
                {
                    OrgId = _db.OrgId,
                    PeriodStart = periodStart,
                    PeriodType = "hourly",
                    EntryPoint = ep,
                    MetricName = metricName,
                    SampleCount = values.Count,
                    AvgValue = values.Average(),
                    MinValue = sorted[0],
                    MaxValue = sorted[^1],
                    P50Value = Percentile(sorted, 0.50),
                    P90Value = Percentile(sorted, 0.90),
                    P99Value = Percentile(sorted, 0.99),
                    StddevValue = StdDev(values)
                };

                await _db.UpsertAggregateAsync(agg);
            }
        }
    }

    /// <summary>
    /// Updates 14-day rolling baselines per entry point per metric.
    /// </summary>
    private async Task UpdateBaselinesAsync()
    {
        var entryPoints = await _db.GetDistinctEntryPointsAsync();
        var windowStart = DateTime.UtcNow.AddDays(-BaselineWindowDays);

        foreach (var ep in entryPoints)
        {
            var snapshots = await _db.GetSnapshotsSinceAsync(windowStart, ep);
            if (snapshots.Count < MinBaselineSamples) continue;

            var metricsToBaseline = new Dictionary<string, List<double>>
            {
                ["duration_ms"] = snapshots.Select(s => (double)s.DurationMs).ToList(),
                ["soql_pct"] = snapshots.Where(s => s.SoqlLimit > 0)
                    .Select(s => (double)s.SoqlCount / s.SoqlLimit * 100).ToList(),
                ["dml_pct"] = snapshots.Where(s => s.DmlLimit > 0)
                    .Select(s => (double)s.DmlCount / s.DmlLimit * 100).ToList(),
                ["cpu_time_ms"] = snapshots.Select(s => (double)s.CpuTimeMs).ToList(),
                ["health_score"] = snapshots.Select(s => (double)s.HealthScore).ToList(),
                ["error_count"] = snapshots.Select(s => (double)s.ErrorCount).ToList(),
            };

            foreach (var (metricName, values) in metricsToBaseline)
            {
                if (values.Count < MinBaselineSamples) continue;

                var baseline = new Baseline
                {
                    OrgId = _db.OrgId,
                    EntryPoint = ep,
                    MetricName = metricName,
                    BaselineValue = values.Average(),
                    Stddev = StdDev(values),
                    SampleCount = values.Count,
                    LastUpdated = DateTime.UtcNow
                };

                await _db.UpsertBaselineAsync(baseline);
            }
        }
    }

    /// <summary>
    /// Runs all detection methods: z-score deviation, absolute thresholds, linear projection.
    /// </summary>
    private async Task RunDetectionChecksAsync()
    {
        var entryPoints = await _db.GetDistinctEntryPointsAsync();
        var recentWindow = DateTime.UtcNow.AddHours(-1);

        foreach (var ep in entryPoints)
        {
            var recentSnapshots = await _db.GetSnapshotsSinceAsync(recentWindow, ep);
            if (recentSnapshots.Count == 0) continue;

            // 1. Statistical deviation checks
            await CheckStatisticalDeviations(ep, recentSnapshots);

            // 2. Absolute threshold checks
            await CheckAbsoluteThresholds(ep, recentSnapshots);

            // 3. Linear projection (limit trending)
            await CheckLinearProjections(ep);
        }
    }

    /// <summary>
    /// Z-score deviation: compares recent averages against stored baselines.
    /// </summary>
    private async Task CheckStatisticalDeviations(string entryPoint, List<LogSnapshot> recent)
    {
        var metricChecks = new (string metric, Func<LogSnapshot, double> extract)[]
        {
            ("duration_ms", s => s.DurationMs),
            ("cpu_time_ms", s => s.CpuTimeMs),
            ("error_count", s => s.ErrorCount),
            ("duplicate_queries", s => s.DuplicateQueryCount),
        };

        foreach (var (metric, extract) in metricChecks)
        {
            var baseline = await _db.GetBaselineAsync(entryPoint, metric);
            if (baseline == null || baseline.SampleCount < MinBaselineSamples || baseline.Stddev < 0.001)
                continue;

            var currentAvg = recent.Average(s => extract(s));
            var z = (currentAvg - baseline.BaselineValue) / baseline.Stddev;

            if (z < WarningZScore) continue;

            var severity = z >= CriticalZScore ? "critical" : "warning";
            var alertType = metric switch
            {
                "duration_ms" => "perf_degradation",
                "error_count" => "error_spike",
                "duplicate_queries" => "n_plus_one_growth",
                _ => "governor_trending"
            };

            var title = metric switch
            {
                "duration_ms" => $"{entryPoint} avg {currentAvg:F0}ms (baseline {baseline.BaselineValue:F0}ms)",
                "error_count" => $"{entryPoint}: {currentAvg:F1} errors/log (baseline {baseline.BaselineValue:F1})",
                "duplicate_queries" => $"{entryPoint}: {currentAvg:F0} duplicate queries (baseline {baseline.BaselineValue:F0})",
                "cpu_time_ms" => $"{entryPoint} CPU {currentAvg:F0}ms (baseline {baseline.BaselineValue:F0}ms)",
                _ => $"{entryPoint} {metric} z-score {z:F1}"
            };

            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = alertType,
                Severity = severity,
                Title = title,
                Description = $"Statistical deviation detected (z-score: {z:F2}). " +
                              $"Current average: {currentAvg:F1}, Baseline: {baseline.BaselineValue:F1} " +
                              $"(stddev: {baseline.Stddev:F1}, samples: {baseline.SampleCount})",
                EntryPoint = entryPoint,
                MetricName = metric,
                CurrentValue = currentAvg,
                BaselineValue = baseline.BaselineValue
            });
        }
    }

    /// <summary>
    /// Absolute threshold checks: governor limits, duration, health score.
    /// </summary>
    private async Task CheckAbsoluteThresholds(string entryPoint, List<LogSnapshot> recent)
    {
        // Governor limit checks (SOQL %)
        var soqlPcts = recent.Where(s => s.SoqlLimit > 0)
            .Select(s => (double)s.SoqlCount / s.SoqlLimit).ToList();
        if (soqlPcts.Count > 0)
        {
            var avgSoqlPct = soqlPcts.Average();
            if (avgSoqlPct >= GovernorWarningPct)
            {
                var severity = avgSoqlPct >= GovernorCriticalPct ? "critical" : "warning";
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "governor_trending",
                    Severity = severity,
                    Title = $"SOQL at {avgSoqlPct * 100:F0}% for {entryPoint}",
                    Description = $"Average SOQL usage is {avgSoqlPct * 100:F0}% of the governor limit. " +
                                  $"Based on {soqlPcts.Count} recent logs.",
                    EntryPoint = entryPoint,
                    MetricName = "soql_pct",
                    CurrentValue = avgSoqlPct * 100,
                    ThresholdValue = GovernorWarningPct * 100
                });
            }
        }

        // Governor limit checks (DML %)
        var dmlPcts = recent.Where(s => s.DmlLimit > 0)
            .Select(s => (double)s.DmlCount / s.DmlLimit).ToList();
        if (dmlPcts.Count > 0)
        {
            var avgDmlPct = dmlPcts.Average();
            if (avgDmlPct >= GovernorWarningPct)
            {
                var severity = avgDmlPct >= GovernorCriticalPct ? "critical" : "warning";
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "governor_trending",
                    Severity = severity,
                    Title = $"DML at {avgDmlPct * 100:F0}% for {entryPoint}",
                    Description = $"Average DML usage is {avgDmlPct * 100:F0}% of the governor limit.",
                    EntryPoint = entryPoint,
                    MetricName = "dml_pct",
                    CurrentValue = avgDmlPct * 100,
                    ThresholdValue = GovernorWarningPct * 100
                });
            }
        }

        // Duration checks
        var avgDuration = recent.Average(s => (double)s.DurationMs);
        if (avgDuration >= DurationWarningMs)
        {
            var severity = avgDuration >= DurationCriticalMs ? "critical" : "warning";
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "perf_degradation",
                Severity = severity,
                Title = $"{entryPoint} avg {avgDuration / 1000:F1}s execution time",
                Description = $"Average duration is {avgDuration:F0}ms across {recent.Count} recent logs.",
                EntryPoint = entryPoint,
                MetricName = "duration_ms",
                CurrentValue = avgDuration,
                ThresholdValue = DurationWarningMs
            });
        }

        // Health score checks
        var avgHealth = recent.Average(s => (double)s.HealthScore);
        if (avgHealth < HealthWarningMin)
        {
            var severity = avgHealth < HealthCriticalMin ? "critical" : "warning";
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "perf_degradation",
                Severity = severity,
                Title = $"{entryPoint} health score dropped to {avgHealth:F0}",
                Description = $"Average health score is {avgHealth:F0}/100 across {recent.Count} recent logs.",
                EntryPoint = entryPoint,
                MetricName = "health_score",
                CurrentValue = avgHealth,
                ThresholdValue = HealthWarningMin
            });
        }

        // Error rate check
        var errorRate = recent.Count(s => s.HasErrors) / (double)recent.Count;
        if (errorRate >= ErrorRateWarning)
        {
            var severity = errorRate >= ErrorRateCritical ? "critical" : "warning";
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "error_spike",
                Severity = severity,
                Title = $"{entryPoint}: {errorRate * 100:F0}% error rate",
                Description = $"{recent.Count(s => s.HasErrors)} of {recent.Count} recent logs had errors.",
                EntryPoint = entryPoint,
                MetricName = "error_rate",
                CurrentValue = errorRate * 100,
                ThresholdValue = ErrorRateWarning * 100
            });
        }
    }

    /// <summary>
    /// Linear projection: fits regression on daily aggregates and projects when a governor limit will be hit.
    /// </summary>
    private async Task CheckLinearProjections(string entryPoint)
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var metricsToProject = new[] { "soql_pct", "dml_pct", "cpu_pct" };

        foreach (var metricName in metricsToProject)
        {
            var dailyAggs = await _db.GetAggregatesAsync(metricName, "daily", weekAgo, entryPoint);

            if (dailyAggs.Count < 3) continue; // Need at least 3 data points

            var xValues = dailyAggs.Select((a, i) => (double)i).ToArray();
            var yValues = dailyAggs.Select(a => a.AvgValue).ToArray();

            var (slope, intercept) = LinearRegression(xValues, yValues);

            // Only alert if trending upward
            if (slope <= 0) continue;

            // Project days until 100% (governor limit)
            var currentValue = yValues[^1];
            if (currentValue >= 100) continue; // Already at limit

            var daysToLimit = (100 - currentValue) / slope;

            if (daysToLimit <= ProjectionWarningDays)
            {
                var friendlyName = metricName switch
                {
                    "soql_pct" => "SOQL",
                    "dml_pct" => "DML",
                    "cpu_pct" => "CPU",
                    _ => metricName
                };
                var severity = daysToLimit <= ProjectionCriticalDays ? "critical" : "warning";
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "governor_trending",
                    Severity = severity,
                    Title = $"{entryPoint} {friendlyName} projected to hit limit in {daysToLimit:F0} days",
                    Description = $"Current {friendlyName} usage: {currentValue:F0}%, trending up {slope:F1}%/day. " +
                                  $"At this rate, the governor limit will be reached in ~{daysToLimit:F0} days.",
                    EntryPoint = entryPoint,
                    MetricName = metricName,
                    CurrentValue = currentValue,
                    ThresholdValue = 100
                });
            }
        }
    }

    /// <summary>
    /// Creates an alert if one hasn't been created for the same type/entry/metric in the last 24 hours.
    /// </summary>
    private async Task TryCreateAlert(MonitoringAlert alert)
    {
        alert.CreatedAt = DateTime.UtcNow;

        // 24-hour dedup check
        var existing = await _db.GetRecentAlertAsync(
            alert.AlertType, alert.EntryPoint, alert.MetricName);

        if (existing != null) return;

        await _db.InsertAlertAsync(alert);
        AlertGenerated?.Invoke(this, alert);

        Log.Information("Alert generated: [{Severity}] {Title}", alert.Severity, alert.Title);
    }

    // ── Math helpers ──────────────────────────────────────────────────────

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (index - lower) * (sorted[upper] - sorted[lower]);
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSqDiff = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSqDiff / (values.Count - 1));
    }

    private static (double slope, double intercept) LinearRegression(double[] x, double[] y)
    {
        var n = x.Length;
        var sumX = x.Sum();
        var sumY = y.Sum();
        var sumXY = x.Zip(y, (xi, yi) => xi * yi).Sum();
        var sumX2 = x.Sum(xi => xi * xi);

        var denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10) return (0, y.Average());

        var slope = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;
        return (slope, intercept);
    }
}
