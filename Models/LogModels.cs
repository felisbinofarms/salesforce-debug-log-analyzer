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
