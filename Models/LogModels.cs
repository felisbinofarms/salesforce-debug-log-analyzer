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
/// Represents a database operation (SOQL or DML)
/// </summary>
public class DatabaseOperation
{
    public string OperationType { get; set; } = string.Empty; // SOQL, DML
    public string Query { get; set; } = string.Empty;
    public string DmlOperation { get; set; } = string.Empty; // Insert, Update, Delete, Undelete
    public string ObjectType { get; set; } = string.Empty;
    public int RowsAffected { get; set; }
    public int AggregationCount { get; set; }
    public long DurationMs { get; set; }
    public int LineNumber { get; set; }
}

/// <summary>
/// Represents governor limit usage at a specific checkpoint
/// </summary>
public class GovernorLimitSnapshot
{
    public int SoqlQueries { get; set; }
    public int SoqlQueriesLimit { get; set; }
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
    public int LineNumber { get; set; }
}

/// <summary>
/// Complete analysis of a parsed debug log
/// </summary>
public class LogAnalysis
{
    public string LogId { get; set; } = string.Empty;
    public DateTime ParsedAt { get; set; }
    public ExecutionNode RootNode { get; set; } = new();
    public List<DatabaseOperation> DatabaseOperations { get; set; } = new();
    public List<GovernorLimitSnapshot> LimitSnapshots { get; set; } = new();
    public List<ExecutionNode> Errors { get; set; } = new();
    public Dictionary<string, MethodStatistics> MethodStats { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
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
