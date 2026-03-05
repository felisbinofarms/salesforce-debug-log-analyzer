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
