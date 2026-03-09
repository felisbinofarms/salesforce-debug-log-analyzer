namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// A persisted snapshot of a single log analysis — one row per analyzed log.
/// Used for trend analysis, baseline calculation, and proactive alerting.
/// </summary>
public class LogSnapshot
{
    public long Id { get; set; }
    public string LogId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public string EntryPoint { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string LogUser { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public int CpuTimeMs { get; set; }
    public int SoqlCount { get; set; }
    public int SoqlLimit { get; set; }
    public int DmlCount { get; set; }
    public int DmlLimit { get; set; }
    public int QueryRows { get; set; }
    public int QueryRowsLimit { get; set; }
    public int HeapSize { get; set; }
    public int HeapLimit { get; set; }
    public int CalloutCount { get; set; }
    public int CalloutLimit { get; set; }
    public int HealthScore { get; set; }
    public string HealthGrade { get; set; } = string.Empty;
    public string BulkSafetyGrade { get; set; } = string.Empty;
    public bool HasErrors { get; set; }
    public bool TransactionFailed { get; set; }
    public int ErrorCount { get; set; }
    public int HandledExceptionCount { get; set; }
    public int DuplicateQueryCount { get; set; }
    public int NPlusOneWorst { get; set; }
    public int StackDepthMax { get; set; }
    public bool IsAsync { get; set; }
    public bool IsTruncated { get; set; }
    public string Source { get; set; } = "debug_log";

    /// <summary>
    /// Create a snapshot from an existing LogAnalysis.
    /// </summary>
    public static LogSnapshot FromAnalysis(LogAnalysis analysis, string orgId)
    {
        var limits = analysis.LimitSnapshots.FirstOrDefault();

        return new LogSnapshot
        {
            LogId = analysis.LogId,
            OrgId = orgId,
            CapturedAt = DateTime.UtcNow,
            EntryPoint = analysis.EntryPoint,
            OperationType = analysis.OperationType,
            LogUser = analysis.LogUser,
            DurationMs = analysis.DurationMs,
            CpuTimeMs = analysis.CpuTimeMs,
            SoqlCount = limits?.SoqlQueries ?? 0,
            SoqlLimit = limits?.SoqlQueriesLimit ?? 100,
            DmlCount = limits?.DmlStatements ?? 0,
            DmlLimit = limits?.DmlStatementsLimit ?? 150,
            QueryRows = limits?.QueryRows ?? 0,
            QueryRowsLimit = limits?.QueryRowsLimit ?? 50000,
            HeapSize = limits?.HeapSize ?? 0,
            HeapLimit = limits?.HeapSizeLimit ?? 6000000,
            CalloutCount = limits?.Callouts ?? 0,
            CalloutLimit = limits?.CalloutsLimit ?? 100,
            HealthScore = analysis.Health?.Score ?? 0,
            HealthGrade = analysis.Health?.Grade ?? "",
            BulkSafetyGrade = analysis.BulkSafetyGrade,
            HasErrors = analysis.HasErrors,
            TransactionFailed = analysis.TransactionFailed,
            ErrorCount = analysis.Errors.Count,
            HandledExceptionCount = analysis.HandledExceptions.Count,
            DuplicateQueryCount = analysis.DuplicateQueries.Count,
            NPlusOneWorst = analysis.DuplicateQueries.Count > 0
                ? analysis.DuplicateQueries.Max(d => d.ExecutionCount)
                : 0,
            StackDepthMax = analysis.StackAnalysis?.MaxDepth ?? 0,
            IsAsync = analysis.IsAsyncExecution,
            IsTruncated = analysis.IsLogTruncated,
            Source = "debug_log"
        };
    }
}

/// <summary>
/// Aggregated metric over a time period for a specific entry point.
/// </summary>
public class MetricAggregate
{
    public long Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public string PeriodType { get; set; } = "hourly"; // hourly, daily, weekly
    public string? EntryPoint { get; set; } // null = org-wide
    public string MetricName { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public double AvgValue { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double P50Value { get; set; }
    public double P90Value { get; set; }
    public double P99Value { get; set; }
    public double StddevValue { get; set; }
}

/// <summary>
/// What is "normal" for a specific entry point and metric (14-day rolling window).
/// </summary>
public class Baseline
{
    public long Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double BaselineValue { get; set; }
    public double Stddev { get; set; }
    public int SampleCount { get; set; }
    public DateTime LastUpdated { get; set; }
    public int WindowDays { get; set; } = 14;
}

/// <summary>
/// A monitoring alert — detected deviation, threshold breach, or anomaly.
/// </summary>
public class MonitoringAlert
{
    public long Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info"; // critical, warning, info
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EntryPoint { get; set; }
    public string? MetricName { get; set; }
    public double? CurrentValue { get; set; }
    public double? BaselineValue { get; set; }
    public double? ThresholdValue { get; set; }
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime? DismissedAt { get; set; }
    public string? ActionTaken { get; set; }
    public string? RelatedLogId { get; set; }
    public string? NotifiedVia { get; set; }
    public string? UserFeedback { get; set; } // "accurate" or "false_alarm"
    public DateTime? FeedbackAt { get; set; }
    /// <summary>Number of unique Salesforce users directly affected by this anomaly.</summary>
    public int? AffectedUserCount { get; set; }
    public double Opacity => IsRead ? 0.7 : 1.0;
    public string AffectedUsersDisplay => AffectedUserCount.HasValue && AffectedUserCount > 0
        ? $"{AffectedUserCount} user{(AffectedUserCount > 1 ? "s" : "")}"
        : string.Empty;
    public bool HasAffectedUsers => AffectedUserCount.HasValue && AffectedUserCount > 0;

    /// <summary>
    /// Severity color indicator for UI.
    /// </summary>
    public string SeverityColor => Severity switch
    {
        "critical" => "#F85149",
        "warning" => "#D29922",
        "info" => "#4493F8",
        _ => "#7D8590"
    };

    /// <summary>
    /// Relative time display.
    /// </summary>
    public string TimeAgo
    {
        get
        {
            var elapsed = DateTime.UtcNow - CreatedAt;
            if (elapsed.TotalMinutes < 1) return "Just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
            return CreatedAt.ToString("MMM dd");
        }
    }

    /// <summary>
    /// Change description showing current vs baseline.
    /// </summary>
    public string ChangeDescription => BaselineValue.HasValue && CurrentValue.HasValue
        ? $"{CurrentValue:F0} (was {BaselineValue:F0})"
        : CurrentValue.HasValue
            ? $"{CurrentValue:F0}"
            : "";
}

/// <summary>
/// Tracks which Shield EventLogFiles have been downloaded and processed.
/// </summary>
public class ShieldLogFileRecord
{
    public long Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string LogDate { get; set; } = string.Empty;
    public string LogFileId { get; set; } = string.Empty;
    public string IntervalType { get; set; } = "Hourly";
    public DateTime ProcessedAt { get; set; }
    public int RecordCount { get; set; }
    public long FileSize { get; set; }
}

/// <summary>
/// A single parsed event from a Shield EventLogFile CSV row.
/// </summary>
public class ShieldEvent
{
    public long Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventDate { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Uri { get; set; }
    public double? DurationMs { get; set; }
    public double? CpuTimeMs { get; set; }
    public int? RowCount { get; set; }
    public int? StatusCode { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ClientIp { get; set; }
    public string? ExtraJson { get; set; }
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
}

/// <summary>
/// Represents a Salesforce EventLogFile record from the REST API.
/// </summary>
public class EventLogFileRecord
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string LogDate { get; set; } = string.Empty;
    public long LogFileLength { get; set; }
    public string Interval { get; set; } = "Hourly";
}

// ================================================================
//  SHIELD DASHBOARD MODELS
// ================================================================

/// <summary>
/// A row in the Shield dashboard — one aggregation bucket (e.g. top API endpoint, top exception).
/// </summary>
public class ShieldDashboardRow
{
    public string Label { get; set; } = string.Empty;
    public string? SubLabel { get; set; }
    public long Count { get; set; }
    public double? AvgDurationMs { get; set; }
    public double? MaxDurationMs { get; set; }
    public int UniqueUsers { get; set; }
    public string? SeverityColor { get; set; }

    public string CountDisplay => Count >= 1000 ? $"{Count / 1000.0:F1}k" : Count.ToString("N0");
    public string AvgDurationDisplay => AvgDurationMs.HasValue ? $"{AvgDurationMs:F0}ms" : "—";
    public string MaxDurationDisplay => MaxDurationMs.HasValue ? $"{MaxDurationMs:F0}ms" : "—";
}

/// <summary>
/// An actionable insight card for the Shield dashboard.
/// Unlike raw data tables, insights tell you what's wrong, who's affected, and what to fix.
/// </summary>
public class ShieldInsight
{
    public string Severity { get; set; } = "info"; // critical, warning, info
    public string Category { get; set; } = "";      // exception, security, performance
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Detail { get; set; }              // stack trace, IPs, etc.
    public string? Recommendation { get; set; }
    public long Count { get; set; }
    public int AffectedUsers { get; set; }
    public string? TrendText { get; set; }           // "↑ 312% vs yesterday"
    public double ImpactScore { get; set; }          // for priority sorting

    // Display helpers
    public System.Windows.Media.SolidColorBrush SeverityBrush => new(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            Severity switch { "critical" => "#F85149", "warning" => "#D29922", _ => "#58A6FF" }));

    public System.Windows.Media.SolidColorBrush SeverityBgBrush => new(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            Severity switch { "critical" => "#2D1418", "warning" => "#2D2714", _ => "#14202D" }));

    public string SeverityLabel => Severity.ToUpperInvariant();
    public string CountDisplay => Count >= 1000 ? $"{Count / 1000.0:F1}k" : Count.ToString("N0");
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool HasRecommendation => !string.IsNullOrEmpty(Recommendation);
    public bool HasTrend => !string.IsNullOrEmpty(TrendText);
}

/// <summary>
/// Rich per-IP login failure analysis for the Shield dashboard.
/// </summary>
public class LoginFailureDetail
{
    public string IpAddress { get; set; } = "";
    public long Attempts { get; set; }
    public int UniqueTargets { get; set; }
    public List<string> UserIds { get; set; } = new();
    public List<string> UserNames { get; set; } = new();  // resolved from Salesforce
    public string PrimaryReason { get; set; } = "";
    public string PrimaryReasonFriendly { get; set; } = "";
    public string LoginTypeDecoded { get; set; } = "";
    public string BrowserOrApp { get; set; } = "";
    public Dictionary<string, long> ReasonBreakdown { get; set; } = new();
    public string Severity { get; set; } = "info";

    public System.Windows.Media.SolidColorBrush SeverityBrush => new(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            Severity switch { "critical" => "#F85149", "warning" => "#D29922", _ => "#58A6FF" }));

    public string UserNamesDisplay => UserNames.Count > 0
        ? string.Join(", ", UserNames.Take(5)) + (UserNames.Count > 5 ? $" +{UserNames.Count - 5} more" : "")
        : string.Join(", ", UserIds.Take(3)) + (UserIds.Count > 3 ? $" +{UserIds.Count - 3} more" : "");

    public string ReasonsDisplay => string.Join(", ", ReasonBreakdown.Select(r => $"{r.Key}: {r.Value}"));

    public string AttemptsDisplay => Attempts >= 1000 ? $"{Attempts / 1000.0:F1}k" : Attempts.ToString("N0");

    public bool HasMultipleReasons => ReasonBreakdown.Count > 1;
}

/// <summary>
/// A single hourly data point for sparkline charts in the Shield dashboard.
/// </summary>
public record SparklinePoint(DateTime Hour, double Value);

/// <summary>
/// Complete data for the Shield monitoring dashboard tab.
/// </summary>
public class ShieldDashboardData
{
    // Summary
    public DateTime DataFrom { get; set; }
    public DateTime DataTo { get; set; }
    public long TotalEvents { get; set; }
    public int UniqueUsers { get; set; }

    // Actionable insights (priority-sorted)
    public List<ShieldInsight> Insights { get; set; } = new();

    // Reference tables (detail behind the insights)
    public List<ShieldDashboardRow> TopApiEndpoints { get; set; } = new();
    public List<ShieldDashboardRow> SlowestApiEndpoints { get; set; } = new();
    public List<ShieldDashboardRow> ApexExceptions { get; set; } = new();
    public List<ShieldDashboardRow> FailedLogins { get; set; } = new();
    public List<LoginFailureDetail> LoginDetails { get; set; } = new();
    public List<ShieldDashboardRow> TopPages { get; set; } = new();
    public List<ShieldDashboardRow> RecentAlerts { get; set; } = new();

    // Quick stats
    public int ExceptionTotal { get; set; }
    public int FailedLoginTotal { get; set; }
    public int SlowPageCount { get; set; }
    public bool HasInsights => Insights.Count > 0;

    /// <summary>Distinct users who triggered at least one Shield anomaly in the window.</summary>
    public int AnomalyAffectedUsers { get; set; }
    public bool HasAnomalyAffectedUsers => AnomalyAffectedUsers > 0;
    public string AnomalyAffectedUsersDisplay => AnomalyAffectedUsers > 0
        ? $"{AnomalyAffectedUsers} user{(AnomalyAffectedUsers > 1 ? "s" : "")} affected"
        : "No users affected";

    // ── Sparkline time-series data ──
    /// <summary>Hourly total event count for the last 24 hours.</summary>
    public List<SparklinePoint> ActivitySparkline { get; set; } = new();

    /// <summary>True when there is enough sparkline data to render a meaningful trend line.</summary>
    public bool HasActivityTrend => ActivitySparkline.Count >= 3;

    /// <summary>
    /// Pre-computed normalized PointCollection for WPF Polyline binding.
    /// Normalizes to a 240 × 40 coordinate space.
    /// </summary>
    public System.Windows.Media.PointCollection ActivitySparklinePoints =>
        ComputeSparklinePoints(ActivitySparkline, 240, 40);

    private static System.Windows.Media.PointCollection ComputeSparklinePoints(
        List<SparklinePoint> points, double width, double height)
    {
        var col = new System.Windows.Media.PointCollection();
        if (points.Count < 2) return col;

        var maxVal = points.Max(p => p.Value);
        if (maxVal <= 0) maxVal = 1;
        var steps = points.Count - 1;

        for (int i = 0; i < points.Count; i++)
        {
            var x = (i / (double)steps) * width;
            var y = height - (points[i].Value / maxVal) * (height - 2); // 2px top margin
            col.Add(new System.Windows.Point(x, y));
        }
        return col;
    }
}

// ================================================================
//  GOVERNOR ARCHAEOLOGY MODELS
// ================================================================

/// <summary>
/// One row in the Governor Archaeology report — aggregated governor limit stats
/// for a single entry point across all analysed executions in the selected timeframe.
/// </summary>
public class GovernorArchaeologyRow
{
    public string EntryPoint { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public double AvgSoqlCount { get; set; }
    public int MaxSoqlCount { get; set; }
    public int SoqlLimit { get; set; }
    public double AvgQueryRows { get; set; }
    public int MaxQueryRows { get; set; }
    public double AvgCpuMs { get; set; }
    public int MaxCpuMs { get; set; }
    public double AvgDurationMs { get; set; }
    public double AvgDuplicateQueryCount { get; set; }
    public int ErrorCount { get; set; }

    // Display helpers
    public double SoqlLimitPct => SoqlLimit > 0 ? (AvgSoqlCount / SoqlLimit) * 100 : 0;
    public string SoqlRiskColor => SoqlLimitPct >= 80 ? "#F85149" : SoqlLimitPct >= 50 ? "#D29922" : "#3FB950";
    public System.Windows.Media.SolidColorBrush SoqlRiskBrush => new(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(SoqlRiskColor));
    public string AvgSoqlDisplay => $"{AvgSoqlCount:F0} / {SoqlLimit}";
    public string MaxSoqlDisplay => MaxSoqlCount.ToString();
    public string AvgCpuDisplay => AvgCpuMs >= 1000 ? $"{AvgCpuMs / 1000:F1}s" : $"{AvgCpuMs:F0}ms";
    public string MaxCpuDisplay => MaxCpuMs >= 1000 ? $"{MaxCpuMs / 1000.0:F1}s" : $"{MaxCpuMs}ms";
    public string AvgDurationDisplay => AvgDurationMs >= 1000 ? $"{AvgDurationMs / 1000:F1}s" : $"{AvgDurationMs:F0}ms";
    public string AvgDupDisplay => AvgDuplicateQueryCount > 0 ? $"{AvgDuplicateQueryCount:F1}" : "—";
    public string ShortEntryPoint => EntryPoint.Length > 60 ? "\u2026" + EntryPoint[^57..] : EntryPoint;
    public bool HasNPlusOne => AvgDuplicateQueryCount >= 1;
}

/// <summary>
/// Aggregated Governor Archaeology report across all analysed debug logs for this org.
/// Answers: "Which entry points are consistently expensive?"
/// </summary>
public class GovernorArchaeologyData
{
    public int TotalExecutions { get; set; }
    public int DaysAnalyzed { get; set; }
    public DateTime Since { get; set; }
    public List<GovernorArchaeologyRow> TopBySoql { get; set; } = new();
    public List<GovernorArchaeologyRow> TopByCpu { get; set; } = new();
    public List<GovernorArchaeologyRow> TopByNPlusOne { get; set; } = new();
    public bool HasSoqlData => TopBySoql.Count > 0;
    public bool HasCpuData => TopByCpu.Count > 0;
    public bool HasNPlusOneData => TopByNPlusOne.Count > 0;
    public bool HasAnyData => HasSoqlData || HasCpuData || HasNPlusOneData;
}
