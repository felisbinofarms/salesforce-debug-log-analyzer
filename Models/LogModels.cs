namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// Represents a single parsed line from a Salesforce debug log
/// </summary>
public class LogLine
{
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string[] Details { get; set; } = Array.Empty<string>();
    public int LineNumber { get; set; }
    public string RawLine { get; set; } = string.Empty;
}

/// <summary>
/// Represents a node in the execution tree hierarchy
/// </summary>
public class ExecutionNode
{
    public string Name { get; set; } = string.Empty;
    public ExecutionNodeType Type { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long DurationMs => EndTime.HasValue ? (long)(EndTime.Value - StartTime).TotalMilliseconds : 0;
    public List<ExecutionNode> Children { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int StartLineNumber { get; set; }
    public int? EndLineNumber { get; set; }
    
    /// <summary>
    /// For Exception nodes: indicates if this exception was handled (caught) or unhandled
    /// </summary>
    public ExceptionSeverity Severity { get; set; } = ExceptionSeverity.Unhandled;
}

/// <summary>
/// Types of execution nodes
/// </summary>
public enum ExecutionNodeType
{
    Execution,
    CodeUnit,
    Method,
    SystemMethod,
    Soql,
    Dml,
    Exception,
    UserDebug,
    Validation,
    Flow
}

/// <summary>
/// Severity classification for exceptions - distinguishes handled from unhandled
/// </summary>
public enum ExceptionSeverity
{
    /// <summary>Exception was caught by a try/catch - code continued normally</summary>
    Handled,
    /// <summary>Exception was thrown but transaction continued (soft failure)</summary>
    Warning,
    /// <summary>Exception caused transaction to fail</summary>
    Unhandled,
    /// <summary>FATAL_ERROR - complete transaction failure</summary>
    Fatal
}

/// <summary>
/// Represents a database operation (SOQL, SOSL, or DML)
/// </summary>
public class DatabaseOperation
{
    public string OperationType { get; set; } = string.Empty; // SOQL, SOSL, DML
    public string Query { get; set; } = string.Empty;
    public string DmlOperation { get; set; } = string.Empty; // Insert, Update, Delete, Undelete
    public string ObjectType { get; set; } = string.Empty;
    public int RowsAffected { get; set; }
    public int AggregationCount { get; set; }
    public long DurationMs { get; set; }
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Query execution plan from SOQL_EXECUTE_EXPLAIN (index usage, cardinality, cost)
    /// </summary>
    public string? ExecutionPlan { get; set; }
    
    /// <summary>
    /// Relative cost from execution plan (higher = worse, >1.0 often means table scan)
    /// </summary>
    public double RelativeCost { get; set; }
}

/// <summary>
/// Represents an HTTP callout to an external service
/// </summary>
public class CalloutOperation
{
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int LineNumber { get; set; }
    public bool IsError => StatusCode >= 400;
}

/// <summary>
/// Represents a Flow/Process Builder interview execution
/// </summary>
public class FlowExecution
{
    public string FlowName { get; set; } = string.Empty;
    public string InterviewId { get; set; } = string.Empty;
    public int ElementCount { get; set; }
    public bool HasFault { get; set; }
    public string? FaultMessage { get; set; }
    public int LineNumber { get; set; }
}

/// <summary>
/// Represents governor limit usage at a specific checkpoint
/// </summary>
public class GovernorLimitSnapshot
{
    public int SoqlQueries { get; set; }
    public int SoqlQueriesLimit { get; set; }
    public int SoslQueries { get; set; }
    public int SoslQueriesLimit { get; set; }
    public int QueryRows { get; set; }
    public int QueryRowsLimit { get; set; }
    public int CpuTime { get; set; }
    public int CpuTimeLimit { get; set; }
    public int HeapSize { get; set; }
    public int HeapSizeLimit { get; set; }
    public int DmlStatements { get; set; }
    public int DmlStatementsLimit { get; set; }
    public int DmlRows { get; set; }
    public int DmlRowsLimit { get; set; }
    public int FutureCalls { get; set; }
    public int FutureCallsLimit { get; set; }
    public int Callouts { get; set; }
    public int CalloutsLimit { get; set; }
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Namespace this snapshot belongs to (e.g., "(default)", "CloudingoAgent", "et4ae5")
    /// </summary>
    public string Namespace { get; set; } = "(default)";
}

/// <summary>
/// Complete analysis of a parsed debug log
/// </summary>
public class LogAnalysis
{
    public string LogId { get; set; } = string.Empty;
    public string LogName { get; set; } = string.Empty;
    public DateTime ParsedAt { get; set; }
    public double DurationMs { get; set; }
    public int LineCount { get; set; }
    public bool HasErrors { get; set; }
    public ExecutionNode RootNode { get; set; } = new();
    public List<DatabaseOperation> DatabaseOperations { get; set; } = new();
    public List<GovernorLimitSnapshot> LimitSnapshots { get; set; } = new();
    
    /// <summary>
    /// Governor limits broken down by namespace (managed packages).
    /// Excludes the (default) namespace which is in LimitSnapshots.
    /// </summary>
    public List<GovernorLimitSnapshot> NamespaceLimitSnapshots { get; set; } = new();
    
    /// <summary>
    /// Governor limits consumed only by test code (from TESTING_LIMITS section).
    /// Helps distinguish test overhead from actual code performance.
    /// </summary>
    public GovernorLimitSnapshot? TestingLimits { get; set; }
    
    public List<ExecutionNode> Errors { get; set; } = new();
    
    /// <summary>
    /// Entry point that started this transaction (e.g., "CaseTrigger on Case (BeforeInsert)")
    /// </summary>
    public string EntryPoint { get; set; } = string.Empty;
    
    /// <summary>
    /// Exceptions that were caught by try/catch blocks (informational, not errors)
    /// </summary>
    public List<ExecutionNode> HandledExceptions { get; set; } = new();
    
    /// <summary>
    /// True only if there are unhandled exceptions or fatal errors
    /// </summary>
    public bool TransactionFailed { get; set; }
    
    /// <summary>
    /// CPU time from governor limits (actual processing time)
    /// </summary>
    public int CpuTimeMs { get; set; }
    
    /// <summary>
    /// Wall clock duration (includes waiting time, async gaps, etc.)
    /// </summary>
    public double WallClockMs { get; set; }
    public Dictionary<string, MethodStatistics> MethodStats { get; set; } = new();
    public StackDepthAnalysis StackAnalysis { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    
    /// <summary>
    /// Cumulative profiling data (from CUMULATIVE_PROFILING section at end of log)
    /// </summary>
    public CumulativeProfiling? CumulativeProfiling { get; set; }
    
    /// <summary>
    /// Log user name from USER_INFO line
    /// </summary>
    public string LogUser { get; set; } = string.Empty;
    
    /// <summary>
    /// Log file length in lines
    /// </summary>
    public int LogLength { get; set; }
    
    /// <summary>
    /// Is this a test class execution?
    /// </summary>
    public bool IsTestExecution { get; set; }
    
    /// <summary>
    /// Test class name if IsTestExecution is true
    /// </summary>
    public string TestClassName { get; set; } = string.Empty;
    
    /// <summary>
    /// Order of execution timeline showing major phases
    /// </summary>
    public ExecutionTimeline? Timeline { get; set; }
    
    /// <summary>
    /// HTTP callouts to external services (CALLOUT_REQUEST/CALLOUT_RESPONSE events)
    /// </summary>
    public List<CalloutOperation> Callouts { get; set; } = new();
    
    /// <summary>
    /// Flow/Process Builder interviews that executed during this transaction
    /// </summary>
    public List<FlowExecution> Flows { get; set; } = new();
    
    /// <summary>
    /// Health score (0-100) and actionable issues prioritized for fixing
    /// </summary>
    public HealthScore? Health { get; set; }
}

/// <summary>
/// Health score and prioritized actionable issues
/// </summary>
public class HealthScore
{
    public int Score { get; set; } // 0-100
    public string Grade { get; set; } = string.Empty; // A, B, C, D, F
    public string Status { get; set; } = string.Empty; // Excellent, Good, Needs Work, Poor, Critical
    public string StatusIcon { get; set; } = string.Empty; // 🎯, ⚡, ⚠️, 🔥
    public string Reasoning { get; set; } = string.Empty;
    
    public List<ActionableIssue> CriticalIssues { get; set; } = new();
    public List<ActionableIssue> HighPriorityIssues { get; set; } = new();
    public List<ActionableIssue> QuickWins { get; set; } = new(); // Easy fixes
    
    public int TotalIssues => CriticalIssues.Count + HighPriorityIssues.Count + QuickWins.Count;
    public int TotalEstimatedMinutes => CriticalIssues.Sum(i => i.EstimatedFixTimeMinutes) + 
                                          HighPriorityIssues.Sum(i => i.EstimatedFixTimeMinutes) + 
                                          QuickWins.Sum(i => i.EstimatedFixTimeMinutes);
}

/// <summary>
/// An actionable issue with clear steps to fix
/// </summary>
public class ActionableIssue
{
    public string Title { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty; // Plain English description
    public string Impact { get; set; } = string.Empty; // "Saves 2.4 seconds", "Reduces SOQL by 90%"
    public string Location { get; set; } = string.Empty; // "Case_Util.externalEscalationEmail:154"
    public IssueSeverity Severity { get; set; }
    public IssueDifficulty Difficulty { get; set; }
    public string Fix { get; set; } = string.Empty; // Specific recommendation
    public string CodeExample { get; set; } = string.Empty; // Optional code snippet
    public int EstimatedFixTimeMinutes { get; set; }
    public int Priority { get; set; } // 1 = highest
    
    /// <summary>
    /// True if this fix requires a developer (Apex code changes).
    /// False if a Salesforce admin can fix it (Setup changes, validation rules, flows).
    /// </summary>
    public bool RequiresDeveloper { get; set; } = true;
    
    /// <summary>
    /// Human-readable role label: "👩‍💻 Developer Fix" or "🔧 Admin Can Fix"
    /// </summary>
    public string RoleBadge => RequiresDeveloper ? "👩‍💻 Developer Fix" : "🔧 Admin Can Fix";
    
    public string SeverityIcon => Severity switch
    {
        IssueSeverity.Critical => "🔴",
        IssueSeverity.High => "🟠",
        IssueSeverity.Medium => "🟡",
        IssueSeverity.Low => "🟢",
        _ => "⚪"
    };
    
    public string DifficultyBadge => Difficulty switch
    {
        IssueDifficulty.Easy => "✅ Easy",
        IssueDifficulty.Medium => "⚡ Medium",
        IssueDifficulty.Hard => "🔥 Hard",
        _ => "Unknown"
    };
}

public enum IssueSeverity
{
    Critical,
    High,
    Medium,
    Low
}

public enum IssueDifficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>
/// Order of execution timeline showing what executed when
/// </summary>
public class ExecutionTimeline
{
    public List<TimelinePhase> Phases { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public int TotalPhases => Phases.Count;
    public int RecursionCount { get; set; }
}

/// <summary>
/// A phase in the execution timeline (e.g., Trigger, Flow, Validation)
/// </summary>
public class TimelinePhase
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Trigger, Flow, Validation, DML, etc.
    public DateTime StartTime { get; set; } // DateTime, not nanoseconds
    public long DurationMs { get; set; }
    public int LineNumber { get; set; }
    public string Icon { get; set; } = string.Empty; // 🔧, 🌊, ✅, 💾
    public List<TimelinePhase> Children { get; set; } = new(); // Nested calls
    public int Depth { get; set; }
    public bool IsRecursive { get; set; }
}

/// <summary>
/// Cumulative profiling data aggregated across all executions
/// </summary>
public class CumulativeProfiling
{
    public List<CumulativeQuery> TopQueries { get; set; } = new();
    public List<CumulativeDml> TopDmlOperations { get; set; } = new();
    public List<CumulativeMethod> TopMethods { get; set; } = new();
}

/// <summary>
/// Cumulative query statistics (SOQL)
/// </summary>
public class CumulativeQuery
{
    public string ClassName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Query { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public int TotalDurationMs { get; set; }
    
    /// <summary>Display format: "Class.Method:Line"</summary>
    public string Location => $"{ClassName}.{MethodName}:{LineNumber}";
}

/// <summary>
/// Cumulative DML statistics
/// </summary>
public class CumulativeDml
{
    public string ClassName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Operation { get; set; } = string.Empty; // Insert, Update, etc.
    public string ObjectType { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public int TotalDurationMs { get; set; }
    
    /// <summary>Display format: "Operation ObjectType"</summary>
    public string OperationDescription => $"{Operation}: {ObjectType}";
    
    /// <summary>Display format: "Class.Method:Line"</summary>
    public string Location => $"{ClassName}.{MethodName}:{LineNumber}";
}

/// <summary>
/// Cumulative method invocation statistics
/// </summary>
public class CumulativeMethod
{
    public string ClassName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Signature { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public int TotalDurationMs { get; set; }
    public int AverageDurationMs => ExecutionCount > 0 ? TotalDurationMs / ExecutionCount : 0;
    
    /// <summary>Display format: "Class.Method:Line"</summary>
    public string Location => LineNumber > 0 ? $"{ClassName}.{MethodName}:{LineNumber}" : $"{ClassName}.{MethodName}";
}

/// <summary>
/// Performance statistics for a method
/// </summary>
public class MethodStatistics
{
    public string MethodName { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public long TotalDurationMs { get; set; }
    public long AverageDurationMs => CallCount > 0 ? TotalDurationMs / CallCount : 0;
    public long MaxDurationMs { get; set; }
    public long MinDurationMs { get; set; }
}

/// <summary>
/// Metadata for a debug log file (before full parsing)
/// </summary>
public class DebugLogMetadata
{
    public string LogId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double DurationMs { get; set; }
    public string CodeUnitName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public ExecutionContext Context { get; set; }
    public int SoqlQueries { get; set; }
    public int QueryRows { get; set; }
    public int DmlStatements { get; set; }
    public int DmlRows { get; set; }
    public int CpuTime { get; set; }
    public int HeapSize { get; set; }
    public bool HasErrors { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Execution context type (UI vs Batch vs Integration)
/// </summary>
public enum ExecutionContext
{
    Unknown,
    Interactive,    // User clicked button, Lightning page load
    Batch,          // BatchApex, Schedulable
    Integration,    // REST/SOAP API call, Connected App
    Scheduled,      // Scheduled Flow, Time-based workflow
    Async           // @future, Queueable, Platform Event
}

/// <summary>
/// Represents a group of related debug logs (transaction)
/// </summary>
public class LogGroup
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double TotalDuration { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public bool IsSingleLog { get; set; }
    public bool HasMixedContexts { get; set; }
    public ExecutionContext PrimaryContext { get; set; }
    public List<DebugLogMetadata> Logs { get; set; } = new();
    public List<LogPhase> Phases { get; set; } = new();
    public Dictionary<string, int> ReentryPatterns { get; set; } = new();
    public int TotalReentryCount { get; set; }
    public string SlowestOperation { get; set; } = string.Empty;
    
    // Aggregate metrics
    public int TotalSoqlQueries { get; set; }
    public int TotalQueryRows { get; set; }
    public int TotalDmlStatements { get; set; }
    public int TotalDmlRows { get; set; }
    public int TotalCpuTime { get; set; }
    public int TotalHeapSize { get; set; }
    public int ErrorCount { get; set; }
    
    // Recommendations
    public List<string> Recommendations { get; set; } = new();
    
    // UI Properties
    public string DisplayName => IsSingleLog 
        ? $"Single Log - {Logs.FirstOrDefault()?.MethodName ?? "Unknown"}"
        : $"Transaction Group - {Logs.Count} logs";
    
    public string DurationDisplay => TotalDuration > 10000
        ? $"{TotalDuration / 1000:N1}s 🔥🔥🔥"
        : TotalDuration > 5000
            ? $"{TotalDuration / 1000:N1}s ⚠️"
            : $"{TotalDuration:N0}ms ✅";
}

/// <summary>
/// Represents a phase within a transaction (Backend, Frontend, Async)
/// </summary>
public class LogPhase
{
    public string Name { get; set; } = string.Empty;
    public PhaseType Type { get; set; }
    public List<DebugLogMetadata> Logs { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationMs { get; set; }
    public bool IsSequentialLoading { get; set; }
    public double ParallelSavings { get; set; }
    public bool HasAsyncOperations { get; set; }
    public double GapToNextPhase { get; set; }
    
    public string DurationDisplay => DurationMs > 5000
        ? $"{DurationMs / 1000:N1}s"
        : $"{DurationMs:N0}ms";
}

/// <summary>
/// Types of execution phases
/// </summary>
public enum PhaseType
{
    Backend,
    Frontend,
    Async
}

/// <summary>
/// Analysis of call stack depth to detect stack overflow risks
/// </summary>
public class StackDepthAnalysis
{
    /// <summary>
    /// Maximum stack depth observed during execution
    /// </summary>
    public int MaxDepth { get; set; }
    
    /// <summary>
    /// Line number where max depth occurred
    /// </summary>
    public int MaxDepthLine { get; set; }
    
    /// <summary>
    /// Method name at max depth point
    /// </summary>
    public string MaxDepthMethod { get; set; } = string.Empty;
    
    /// <summary>
    /// Estimated total stack frames (including debug overhead)
    /// </summary>
    public int EstimatedTotalFrames { get; set; }
    
    /// <summary>
    /// Whether FINEST logging is enabled (adds massive overhead)
    /// </summary>
    public bool HasFinestLogging { get; set; }
    
    /// <summary>
    /// Debug log level settings detected
    /// </summary>
    public string DebugLevelSettings { get; set; } = string.Empty;
    
    /// <summary>
    /// Estimated frames added by debug logging overhead
    /// </summary>
    public int DebugLoggingOverhead { get; set; }
    
    /// <summary>
    /// Risk level: Safe, Warning, Critical
    /// </summary>
    public StackRiskLevel RiskLevel { get; set; }
    
    /// <summary>
    /// Methods called in a loop pattern (high frequency)
    /// </summary>
    public List<LoopMethodPattern> LoopPatterns { get; set; } = new();
    
    /// <summary>
    /// Deepest call chains detected
    /// </summary>
    public List<CallChain> DeepestCallChains { get; set; } = new();
    
    /// <summary>
    /// Whether stack overflow is imminent (estimated > 800 frames)
    /// </summary>
    public bool IsStackOverflowRisk => EstimatedTotalFrames > 800;
    
    /// <summary>
    /// Human-readable summary
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Stack risk level
/// </summary>
public enum StackRiskLevel
{
    Safe,       // < 300 frames
    Moderate,   // 300-600 frames
    Warning,    // 600-800 frames
    Critical    // > 800 frames (approaching 1000 limit)
}

/// <summary>
/// Pattern of a method called repeatedly in a loop
/// </summary>
public class LoopMethodPattern
{
    public string MethodName { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public int FramesPerCall { get; set; }
    public int TotalFrames => CallCount * FramesPerCall;
    public string ParentContext { get; set; } = string.Empty;
}

/// <summary>
/// A chain of nested method calls
/// </summary>
public class CallChain
{
    public List<string> Methods { get; set; } = new();
    public int Depth => Methods.Count;
    public int LineNumber { get; set; }
    
    public string Display => string.Join(" → ", Methods.Take(5)) + 
        (Methods.Count > 5 ? $" → ... ({Methods.Count - 5} more)" : "");
}

/// <summary>
/// Represents a recorded user interaction - all logs captured from a single user action
/// (e.g., clicking a button to page reload)
/// </summary>
public class Interaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>When the recording started</summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>When the recording ended</summary>
    public DateTime EndTime { get; set; }
    
    /// <summary>Total wait time the user experienced (from action to page reload)</summary>
    public double UserWaitTimeMs => (EndTime - StartTime).TotalMilliseconds;
    
    /// <summary>Raw logs captured during the recording</summary>
    public List<LogAnalysis> CapturedLogs { get; set; } = new();
    
    /// <summary>Logs grouped into transactions</summary>
    public List<LogGroup> LogGroups { get; set; } = new();
    
    /// <summary>User-editable display name</summary>
    public string DisplayName { get; set; } = "";
    
    /// <summary>Auto-generated name from entry point</summary>
    public string AutoName
    {
        get
        {
            if (CapturedLogs.Count == 0) return $"Recording at {StartTime:HH:mm:ss}";
            var firstEntry = CapturedLogs.FirstOrDefault()?.EntryPoint ?? "";
            // Extract meaningful name from entry point
            if (firstEntry.Contains(".")) firstEntry = firstEntry.Split('.').Last();
            if (firstEntry.Contains("(")) firstEntry = firstEntry.Split('(').First();
            return string.IsNullOrEmpty(firstEntry) 
                ? $"Recording at {StartTime:HH:mm:ss}"
                : $"{firstEntry} ({StartTime:HH:mm:ss})";
        }
    }
    
    /// <summary>Name to display (user name or auto-generated)</summary>
    public string Name => string.IsNullOrEmpty(DisplayName) ? AutoName : DisplayName;
    
    /// <summary>Brief summary of the interaction</summary>
    public string Summary
    {
        get
        {
            var logCount = CapturedLogs.Count;
            var duration = UserWaitTimeMs;
            var issueCount = CapturedLogs.Sum(l => l.Issues?.Count ?? 0);
            var durationStr = duration < 1000 ? $"{duration:N0}ms" : $"{duration / 1000.0:N1}s";
            
            if (issueCount > 0)
                return $"{logCount} logs • {durationStr} • {issueCount} issues";
            return $"{logCount} logs • {durationStr}";
        }
    }
    
    /// <summary>Total server-side processing time across all logs</summary>
    public double TotalServerTimeMs => CapturedLogs.Sum(l => l.DurationMs);
    
    /// <summary>Time spent waiting (network, rendering) vs server processing</summary>
    public double OverheadMs => UserWaitTimeMs - TotalServerTimeMs;
    
    /// <summary>Whether any captured log has errors</summary>
    public bool HasErrors => CapturedLogs.Any(l => l.HasErrors);
    
    /// <summary>Total SOQL queries across all logs</summary>
    public int TotalSoqlQueries => CapturedLogs.Sum(l => 
        l.LimitSnapshots?.LastOrDefault()?.SoqlQueries ?? 0);
    
    /// <summary>Total DML statements across all logs</summary>
    public int TotalDmlStatements => CapturedLogs.Sum(l => 
        l.LimitSnapshots?.LastOrDefault()?.DmlStatements ?? 0);
}
