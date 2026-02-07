using System.Text.RegularExpressions;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for parsing Salesforce debug logs into structured data
/// </summary>
public class LogParserService
{
    private static readonly Regex LogLinePattern = new(@"^(\d{2}:\d{2}:\d{2}\.\d+)\s+\((\d+)\)\|([A-Z_]+)\|(.*)$", RegexOptions.Compiled);
    private static readonly Regex LimitPattern = new(@"Number of (\w+(?:\s+\w+)*?):\s*(\d+)\s+out of\s+(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Parse a complete debug log into structured analysis
    /// </summary>
    public LogAnalysis ParseLog(string logContent, string logId)
    {
        var analysis = new LogAnalysis
        {
            LogId = logId,
            LogName = logId,
            ParsedAt = DateTime.UtcNow
        };

        if (string.IsNullOrWhiteSpace(logContent))
        {
            analysis.Summary = "Empty log file";
            return analysis;
        }

        var lines = logContent.Split('\n');
        analysis.LineCount = lines.Length;
        var logLines = new List<LogLine>();
        
        // Phase 1: Tokenize all lines
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var match = LogLinePattern.Match(line);
            if (match.Success)
            {
                logLines.Add(new LogLine
                {
                    Timestamp = ParseTimestamp(match.Groups[1].Value),
                    EventType = match.Groups[3].Value,
                    Details = match.Groups[4].Value.Split('|'),
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
        }

        if (logLines.Count == 0)
        {
            analysis.Summary = "No valid log lines found";
            return analysis;
        }

        // Extract debug level settings from first line
        var debugLevelSettings = ExtractDebugLevelSettings(lines);

        // Phase 2: Build execution tree
        analysis.RootNode = BuildExecutionTree(logLines);

        // Phase 3: Extract database operations
        analysis.DatabaseOperations = ExtractDatabaseOperations(logLines);

        // Phase 4: Extract governor limits
        analysis.LimitSnapshots = ExtractGovernorLimits(logLines);

        // Phase 5: Extract entry point (trigger/flow that started this transaction)
        analysis.EntryPoint = ExtractEntryPoint(logLines);

        // Phase 5b: Find and classify exceptions (handled vs unhandled)
        ClassifyExceptions(logLines, analysis);
        analysis.HasErrors = analysis.Errors.Count > 0;
        analysis.TransactionFailed = analysis.Errors.Any(e => 
            e.Severity == ExceptionSeverity.Fatal || e.Severity == ExceptionSeverity.Unhandled);

        // Phase 6: Calculate method statistics
        analysis.MethodStats = CalculateMethodStatistics(analysis.RootNode);

        // Phase 7: Analyze stack depth (NEW - Stack Overflow Detection)
        analysis.StackAnalysis = AnalyzeStackDepth(logLines, debugLevelSettings);

        // Calculate duration from root node
        analysis.DurationMs = analysis.RootNode.DurationMs;
        analysis.WallClockMs = analysis.RootNode.DurationMs;
        
        // Extract CPU time from governor limits
        var lastSnapshot = analysis.LimitSnapshots.LastOrDefault();
        if (lastSnapshot != null)
        {
            analysis.CpuTimeMs = lastSnapshot.CpuTime;
        }

        // Phase 8: Generate summary and recommendations
        analysis.Summary = GenerateSummary(analysis);
        analysis.Issues = DetectIssues(analysis);
        analysis.Recommendations = GenerateRecommendations(analysis);
        
        // Phase 9: Parse cumulative profiling
        analysis.CumulativeProfiling = ParseCumulativeProfiling(lines);
        
        // Phase 10: Parse CUMULATIVE_LIMIT_USAGE for accurate CPU/Heap (fixes 0ms bug)
        ParseCumulativeLimitUsage(lines, analysis);
        
        // Phase 11: Detect test class execution
        DetectTestExecution(logLines, analysis);
        
        // Phase 12: Build order of execution timeline
        analysis.Timeline = BuildExecutionTimeline(logLines, analysis);
        
        // Phase 13: Calculate health score and generate actionable issues
        analysis.Health = CalculateHealthScore(analysis);
        
        // Extract user info
        var userInfoLine = logLines.FirstOrDefault(l => l.EventType == "USER_INFO");
        if (userInfoLine != null && userInfoLine.Details.Length > 1)
        {
            analysis.LogUser = userInfoLine.Details[1];
        }
        analysis.LogLength = lines.Length;

        return analysis;
    }

    private DateTime ParseTimestamp(string timestamp)
    {
        if (TimeSpan.TryParse(timestamp, out var timeSpan))
        {
            return DateTime.Today.Add(timeSpan);
        }
        return DateTime.MinValue;
    }

    private ExecutionNode BuildExecutionTree(List<LogLine> logLines)
    {
        var root = new ExecutionNode
        {
            Name = "Execution Root",
            Type = ExecutionNodeType.Execution,
            StartTime = logLines.FirstOrDefault()?.Timestamp ?? DateTime.MinValue
        };

        var stack = new Stack<ExecutionNode>();
        stack.Push(root);

        foreach (var line in logLines)
        {
            try
            {
                switch (line.EventType)
                {
                    case "CODE_UNIT_STARTED":
                        var codeUnitNode = new ExecutionNode
                        {
                            Name = line.Details.Length > 0 ? CleanCodeUnitName(line.Details[0]) : "Unknown",
                            Type = ExecutionNodeType.CodeUnit,
                            StartTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        stack.Peek().Children.Add(codeUnitNode);
                        stack.Push(codeUnitNode);
                        break;

                    case "CODE_UNIT_FINISHED":
                        if (stack.Count > 1 && stack.Peek().Type == ExecutionNodeType.CodeUnit)
                        {
                            var node = stack.Pop();
                            node.EndTime = line.Timestamp;
                            node.EndLineNumber = line.LineNumber;
                        }
                        break;

                    case "METHOD_ENTRY":
                        var methodNode = new ExecutionNode
                        {
                            Name = line.Details.Length > 1 ? line.Details[1] : "Unknown Method",
                            Type = ExecutionNodeType.Method,
                            StartTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        stack.Peek().Children.Add(methodNode);
                        stack.Push(methodNode);
                        break;

                    case "METHOD_EXIT":
                        if (stack.Count > 1 && stack.Peek().Type == ExecutionNodeType.Method)
                        {
                            var node = stack.Pop();
                            node.EndTime = line.Timestamp;
                            node.EndLineNumber = line.LineNumber;
                        }
                        break;

                    case "SYSTEM_METHOD_ENTRY":
                        var sysMethodNode = new ExecutionNode
                        {
                            Name = line.Details.Length > 1 ? line.Details[1] : "System Method",
                            Type = ExecutionNodeType.SystemMethod,
                            StartTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        stack.Peek().Children.Add(sysMethodNode);
                        stack.Push(sysMethodNode);
                        break;

                    case "SYSTEM_METHOD_EXIT":
                        if (stack.Count > 1 && stack.Peek().Type == ExecutionNodeType.SystemMethod)
                        {
                            var node = stack.Pop();
                            node.EndTime = line.Timestamp;
                            node.EndLineNumber = line.LineNumber;
                        }
                        break;

                    case "USER_DEBUG":
                        var debugNode = new ExecutionNode
                        {
                            Name = line.Details.Length > 2 ? $"Debug: {line.Details[2]}" : "Debug Statement",
                            Type = ExecutionNodeType.UserDebug,
                            StartTime = line.Timestamp,
                            EndTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        if (line.Details.Length > 2)
                        {
                            debugNode.Metadata["Message"] = line.Details[2];
                        }
                        stack.Peek().Children.Add(debugNode);
                        break;

                    case "EXCEPTION_THROWN":
                        var errorNode = new ExecutionNode
                        {
                            Name = line.Details.Length > 1 ? $"Exception: {line.Details[1]}" : "Exception",
                            Type = ExecutionNodeType.Exception,
                            StartTime = line.Timestamp,
                            EndTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        if (line.Details.Length > 2)
                        {
                            errorNode.Metadata["Message"] = line.Details[2];
                        }
                        if (line.Details.Length > 0 && line.Details[0].Contains('['))
                        {
                            errorNode.Metadata["LineNumber"] = line.Details[0];
                        }
                        stack.Peek().Children.Add(errorNode);
                        break;

                    case "FATAL_ERROR":
                        var fatalNode = new ExecutionNode
                        {
                            Name = "Fatal Error: " + (line.Details.Length > 0 ? line.Details[0] : "Unknown"),
                            Type = ExecutionNodeType.Exception,
                            StartTime = line.Timestamp,
                            EndTime = line.Timestamp,
                            StartLineNumber = line.LineNumber
                        };
                        if (line.Details.Length > 0)
                        {
                            fatalNode.Metadata["Message"] = string.Join("|", line.Details);
                        }
                        stack.Peek().Children.Add(fatalNode);
                        break;
                }
            }
            catch
            {
                // Continue parsing even if one line fails
            }
        }

        root.EndTime = logLines.LastOrDefault()?.Timestamp ?? DateTime.MinValue;
        return root;
    }

    private string CleanCodeUnitName(string name)
    {
        // Remove [EXTERNAL] prefix if present
        if (name.StartsWith("[EXTERNAL]"))
        {
            name = name.Substring("[EXTERNAL]".Length);
        }
        return name.Trim('|', ' ');
    }

    private List<DatabaseOperation> ExtractDatabaseOperations(List<LogLine> logLines)
    {
        var operations = new List<DatabaseOperation>();
        DatabaseOperation? currentOp = null;
        DateTime? startTime = null;

        foreach (var line in logLines)
        {
            if (line.EventType == "SOQL_EXECUTE_BEGIN")
            {
                startTime = line.Timestamp;
                currentOp = new DatabaseOperation
                {
                    OperationType = "SOQL",
                    LineNumber = line.LineNumber,
                    Query = line.Details.Length > 2 ? line.Details[2] : ""
                };
                
                if (line.Details.Length > 1 && line.Details[1].StartsWith("Aggregations:"))
                {
                    var parts = line.Details[1].Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1], out var aggCount))
                    {
                        currentOp.AggregationCount = aggCount;
                    }
                }
            }
            else if (line.EventType == "SOQL_EXECUTE_END" && currentOp != null)
            {
                if (line.Details.Length > 1 && line.Details[1].StartsWith("Rows:"))
                {
                    var parts = line.Details[1].Split(':');
                    if (parts.Length > 1 && int.TryParse(parts[1], out var rows))
                    {
                        currentOp.RowsAffected = rows;
                    }
                }
                if (startTime.HasValue)
                {
                    currentOp.DurationMs = (long)(line.Timestamp - startTime.Value).TotalMilliseconds;
                }
                operations.Add(currentOp);
                currentOp = null;
                startTime = null;
            }
            else if (line.EventType == "DML_BEGIN")
            {
                startTime = line.Timestamp;
                currentOp = new DatabaseOperation
                {
                    OperationType = "DML",
                    LineNumber = line.LineNumber
                };

                foreach (var detail in line.Details)
                {
                    if (detail.StartsWith("Op:"))
                    {
                        var parts = detail.Split(':');
                        if (parts.Length > 1) currentOp.DmlOperation = parts[1];
                    }
                    else if (detail.StartsWith("Type:"))
                    {
                        var parts = detail.Split(':');
                        if (parts.Length > 1) currentOp.ObjectType = parts[1];
                    }
                    else if (detail.StartsWith("Rows:"))
                    {
                        var parts = detail.Split(':');
                        if (parts.Length > 1 && int.TryParse(parts[1], out var rows))
                        {
                            currentOp.RowsAffected = rows;
                        }
                    }
                }
            }
            else if (line.EventType == "DML_END" && currentOp != null)
            {
                if (startTime.HasValue)
                {
                    currentOp.DurationMs = (long)(line.Timestamp - startTime.Value).TotalMilliseconds;
                }
                operations.Add(currentOp);
                currentOp = null;
                startTime = null;
            }
        }

        return operations;
    }

    private List<GovernorLimitSnapshot> ExtractGovernorLimits(List<LogLine> logLines)
    {
        var snapshots = new List<GovernorLimitSnapshot>();
        GovernorLimitSnapshot? currentSnapshot = null;

        for (int i = 0; i < logLines.Count; i++)
        {
            var line = logLines[i];
            
            if (line.EventType == "CUMULATIVE_LIMIT_USAGE" || line.EventType == "CUMULATIVE_LIMIT_USAGE_END")
            {
                currentSnapshot = new GovernorLimitSnapshot
                {
                    LineNumber = line.LineNumber,
                    SoqlQueriesLimit = 100,
                    QueryRowsLimit = 50000,
                    CpuTimeLimit = 10000,
                    HeapSizeLimit = 6000000,
                    DmlStatementsLimit = 150,
                    DmlRowsLimit = 10000
                };

                // Parse the next several lines for limit values
                for (int j = i + 1; j < Math.Min(i + 20, logLines.Count); j++)
                {
                    var limitLine = logLines[j];
                    if (limitLine.EventType != "CUMULATIVE_LIMIT_USAGE" && limitLine.EventType != "CUMULATIVE_LIMIT_USAGE_END")
                        break;

                    var limitText = limitLine.Details.Length > 0 ? limitLine.Details[0] : "";
                    var match = LimitPattern.Match(limitText);
                    
                    if (match.Success)
                    {
                        var limitName = match.Groups[1].Value.ToLower();
                        var current = int.Parse(match.Groups[2].Value);
                        var max = int.Parse(match.Groups[3].Value);

                        if (limitName.Contains("soql") && limitName.Contains("queries"))
                        {
                            currentSnapshot.SoqlQueries = current;
                            currentSnapshot.SoqlQueriesLimit = max;
                        }
                        else if (limitName.Contains("query") && limitName.Contains("rows"))
                        {
                            currentSnapshot.QueryRows = current;
                            currentSnapshot.QueryRowsLimit = max;
                        }
                        else if (limitName.Contains("cpu"))
                        {
                            currentSnapshot.CpuTime = current;
                            currentSnapshot.CpuTimeLimit = max;
                        }
                        else if (limitName.Contains("heap"))
                        {
                            currentSnapshot.HeapSize = current;
                            currentSnapshot.HeapSizeLimit = max;
                        }
                        else if (limitName.Contains("dml") && limitName.Contains("statements"))
                        {
                            currentSnapshot.DmlStatements = current;
                            currentSnapshot.DmlStatementsLimit = max;
                        }
                        else if (limitName.Contains("dml") && limitName.Contains("rows"))
                        {
                            currentSnapshot.DmlRows = current;
                            currentSnapshot.DmlRowsLimit = max;
                        }
                    }
                }

                if (currentSnapshot != null)
                {
                    snapshots.Add(currentSnapshot);
                }
            }
        }

        return snapshots;
    }

    private List<ExecutionNode> FindErrors(ExecutionNode root)
    {
        var errors = new List<ExecutionNode>();
        FindErrorsRecursive(root, errors);
        return errors;
    }

    private void FindErrorsRecursive(ExecutionNode node, List<ExecutionNode> errors)
    {
        if (node.Type == ExecutionNodeType.Exception)
        {
            errors.Add(node);
        }

        foreach (var child in node.Children)
        {
            FindErrorsRecursive(child, errors);
        }
    }

    /// <summary>
    /// Extract the entry point (trigger, flow, VF page, etc.) that started this transaction
    /// </summary>
    private string ExtractEntryPoint(List<LogLine> logLines)
    {
        foreach (var line in logLines.Take(100))
        {
            if (line.EventType == "CODE_UNIT_STARTED")
            {
                var details = string.Join("|", line.Details);
                
                // Parse trigger format: "__sfdc_trigger/CaseTrigger" or "CaseTrigger on Case trigger event BeforeInsert"
                if (details.Contains("trigger", StringComparison.OrdinalIgnoreCase))
                {
                    // Look for pattern like "CaseTrigger on Case trigger event BeforeInsert"
                    var match = Regex.Match(details, @"(\w+)\s+on\s+(\w+)\s+trigger\s+event\s+(\w+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return $"{match.Groups[1].Value} on {match.Groups[2].Value} ({match.Groups[3].Value})";
                    }
                    
                    // Fallback: extract trigger name from __sfdc_trigger/TriggerName
                    match = Regex.Match(details, @"__sfdc_trigger/(\w+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
                
                // Parse flow format
                if (details.Contains("Flow", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(details, @"Flow[:|](\w+)");
                    if (match.Success)
                    {
                        return $"Flow: {match.Groups[1].Value}";
                    }
                }
                
                // Parse VF/Lightning Controller
                if (details.Contains("VF") || details.Contains("Aura") || details.Contains("LWC"))
                {
                    return CleanCodeUnitName(line.Details.LastOrDefault() ?? "Unknown Controller");
                }
                
                // Generic code unit
                var cleanName = CleanCodeUnitName(line.Details.LastOrDefault() ?? "");
                if (!string.IsNullOrEmpty(cleanName) && !cleanName.Contains("[EXTERNAL]"))
                {
                    return cleanName;
                }
            }
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Classify all exceptions as handled, warning, unhandled, or fatal
    /// </summary>
    private void ClassifyExceptions(List<LogLine> logLines, LogAnalysis analysis)
    {
        var allExceptions = new List<ExecutionNode>();
        FindErrorsRecursive(analysis.RootNode, allExceptions);
        
        // Check for FATAL_ERROR anywhere in the log - this means transaction failed
        bool hasFatalError = logLines.Any(l => l.EventType == "FATAL_ERROR");
        
        // Find all EXCEPTION_THROWN lines to analyze context
        var exceptionLines = logLines
            .Select((line, index) => new { Line = line, Index = index })
            .Where(x => x.Line.EventType == "EXCEPTION_THROWN")
            .ToList();
        
        foreach (var exception in allExceptions)
        {
            // FATAL_ERROR is always Fatal severity
            if (exception.Name.StartsWith("Fatal Error:"))
            {
                exception.Severity = ExceptionSeverity.Fatal;
                analysis.Errors.Add(exception);
                continue;
            }
            
            // Find this exception in the log lines
            var exceptionLine = exceptionLines.FirstOrDefault(x => 
                x.Line.LineNumber == exception.StartLineNumber);
            
            if (exceptionLine != null)
            {
                // Look ahead to see what happens after the exception
                bool isHandled = false;
                bool causedFatal = false;
                
                // Check the next 20 lines to see if code continues or fails
                for (int i = exceptionLine.Index + 1; i < Math.Min(exceptionLine.Index + 20, logLines.Count); i++)
                {
                    var nextLine = logLines[i];
                    
                    // If we see FATAL_ERROR immediately after, it's unhandled
                    if (nextLine.EventType == "FATAL_ERROR")
                    {
                        causedFatal = true;
                        break;
                    }
                    
                    // If we see normal code execution continuing, it was handled
                    if (nextLine.EventType == "METHOD_ENTRY" || 
                        nextLine.EventType == "METHOD_EXIT" ||
                        nextLine.EventType == "STATEMENT_EXECUTE" ||
                        nextLine.EventType == "USER_DEBUG" ||
                        nextLine.EventType == "VARIABLE_ASSIGNMENT")
                    {
                        isHandled = true;
                        break;
                    }
                    
                    // If we see another EXCEPTION_THROWN (re-throw), check if it's the same one
                    if (nextLine.EventType == "EXCEPTION_THROWN")
                    {
                        // Exception was re-thrown, not handled yet - keep looking
                        continue;
                    }
                }
                
                if (causedFatal)
                {
                    exception.Severity = ExceptionSeverity.Unhandled;
                    analysis.Errors.Add(exception);
                }
                else if (isHandled)
                {
                    exception.Severity = ExceptionSeverity.Handled;
                    analysis.HandledExceptions.Add(exception);
                }
                else if (hasFatalError)
                {
                    // If there's a fatal error somewhere, and we can't determine if this was handled,
                    // assume it's related to the failure
                    exception.Severity = ExceptionSeverity.Unhandled;
                    analysis.Errors.Add(exception);
                }
                else
                {
                    // No fatal error in the log, and code continued - treat as warning
                    exception.Severity = ExceptionSeverity.Warning;
                    analysis.HandledExceptions.Add(exception);
                }
            }
            else
            {
                // Can't find the original line - default based on fatal error presence
                if (hasFatalError)
                {
                    exception.Severity = ExceptionSeverity.Unhandled;
                    analysis.Errors.Add(exception);
                }
                else
                {
                    exception.Severity = ExceptionSeverity.Warning;
                    analysis.HandledExceptions.Add(exception);
                }
            }
        }
    }

    private Dictionary<string, MethodStatistics> CalculateMethodStatistics(ExecutionNode root)
    {
        var stats = new Dictionary<string, MethodStatistics>();
        CalculateStatsRecursive(root, stats);
        return stats;
    }

    private void CalculateStatsRecursive(ExecutionNode node, Dictionary<string, MethodStatistics> stats)
    {
        if ((node.Type == ExecutionNodeType.Method || node.Type == ExecutionNodeType.CodeUnit) 
            && node.EndTime.HasValue)
        {
            if (!stats.ContainsKey(node.Name))
            {
                stats[node.Name] = new MethodStatistics
                {
                    MethodName = node.Name,
                    MinDurationMs = long.MaxValue
                };
            }

            var stat = stats[node.Name];
            stat.CallCount++;
            stat.TotalDurationMs += node.DurationMs;
            stat.MaxDurationMs = Math.Max(stat.MaxDurationMs, node.DurationMs);
            stat.MinDurationMs = Math.Min(stat.MinDurationMs, node.DurationMs);
        }

        foreach (var child in node.Children)
        {
            CalculateStatsRecursive(child, stats);
        }
    }

    /// <summary>
    /// Extract debug level settings from the first line of the log
    /// </summary>
    private string ExtractDebugLevelSettings(string[] lines)
    {
        foreach (var line in lines.Take(5))
        {
            // Look for pattern like: 66.0 APEX_CODE,FINEST;APEX_PROFILING,FINEST;...
            if (line.Contains("APEX_CODE,") || line.Contains("DB,") || line.Contains("SYSTEM,"))
            {
                return line.Trim();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Analyze stack depth to detect stack overflow risks
    /// This is critical for detecting issues that governor limits miss!
    /// </summary>
    private StackDepthAnalysis AnalyzeStackDepth(List<LogLine> logLines, string debugLevelSettings)
    {
        var analysis = new StackDepthAnalysis
        {
            DebugLevelSettings = debugLevelSettings
        };

        // Detect FINEST logging (adds massive overhead)
        analysis.HasFinestLogging = debugLevelSettings.Contains("FINEST");
        
        // Calculate debug overhead frames
        if (analysis.HasFinestLogging)
        {
            // FINEST logging adds METHOD_ENTRY/EXIT for every method call
            // This can add 200-400+ extra stack frames
            var methodEntryCount = logLines.Count(l => l.EventType == "METHOD_ENTRY");
            analysis.DebugLoggingOverhead = Math.Min(methodEntryCount / 10, 400); // Estimate
        }

        // Track current stack depth as we process the log
        int currentDepth = 0;
        int maxDepth = 0;
        int maxDepthLine = 0;
        string maxDepthMethod = "";
        var currentStack = new Stack<string>();
        var methodCallCounts = new Dictionary<string, int>();
        var methodCallChains = new Dictionary<string, List<string>>();

        foreach (var line in logLines)
        {
            switch (line.EventType)
            {
                case "METHOD_ENTRY":
                case "SYSTEM_METHOD_ENTRY":
                case "CODE_UNIT_STARTED":
                    currentDepth++;
                    // Extract human-readable name (field 3) instead of ID (field 2)
                    // Format: CODE_UNIT_STARTED|[EXTERNAL]|{ID}|{ClassName.MethodName}
                    var methodName = line.Details.Length >= 3 && !string.IsNullOrEmpty(line.Details[2]) 
                        ? line.Details[2]  // Use ClassName.MethodName
                        : line.Details.Length > 1 ? line.Details[1] 
                        : (line.Details.Length > 0 ? line.Details[0] : "Unknown");
                    currentStack.Push(methodName);
                    
                    // Track method call counts
                    if (!methodCallCounts.ContainsKey(methodName))
                        methodCallCounts[methodName] = 0;
                    methodCallCounts[methodName]++;
                    
                    if (currentDepth > maxDepth)
                    {
                        maxDepth = currentDepth;
                        maxDepthLine = line.LineNumber;
                        maxDepthMethod = methodName;
                    }
                    break;

                case "METHOD_EXIT":
                case "SYSTEM_METHOD_EXIT":
                case "CODE_UNIT_FINISHED":
                    if (currentDepth > 0)
                    {
                        currentDepth--;
                        if (currentStack.Count > 0)
                            currentStack.Pop();
                    }
                    break;
            }
        }

        analysis.MaxDepth = maxDepth;
        analysis.MaxDepthLine = maxDepthLine;
        analysis.MaxDepthMethod = maxDepthMethod;

        // Detect loop patterns (methods called many times)
        var loopPatterns = methodCallCounts
            .Where(kvp => kvp.Value > 10) // Methods called more than 10 times
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .Select(kvp => new LoopMethodPattern
            {
                MethodName = kvp.Key,
                CallCount = kvp.Value,
                FramesPerCall = EstimateFramesPerCall(kvp.Key), // Estimate based on method name
                ParentContext = "Loop iteration"
            })
            .ToList();
        
        analysis.LoopPatterns = loopPatterns;

        // Calculate estimated total frames
        // Formula: (max depth) + (loop patterns contribution) + (debug overhead)
        int loopFrameContribution = loopPatterns.Sum(p => p.TotalFrames);
        analysis.EstimatedTotalFrames = maxDepth + loopFrameContribution + analysis.DebugLoggingOverhead;

        // Determine risk level
        analysis.RiskLevel = analysis.EstimatedTotalFrames switch
        {
            > 800 => StackRiskLevel.Critical,
            > 600 => StackRiskLevel.Warning,
            > 300 => StackRiskLevel.Moderate,
            _ => StackRiskLevel.Safe
        };

        // Generate summary
        analysis.Summary = GenerateStackSummary(analysis);

        return analysis;
    }

    /// <summary>
    /// Estimate how many stack frames a method call typically adds
    /// </summary>
    private int EstimateFramesPerCall(string methodName)
    {
        // Methods that call other methods add more frames
        if (methodName.Contains("getRecordType", StringComparison.OrdinalIgnoreCase))
            return 3; // getRecordType → getRecordTypeFromMap → putRecordTypeInMap
        if (methodName.Contains("handle", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (methodName.Contains("TriggerHandler", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 1;
    }

    /// <summary>
    /// Generate a human-readable summary of stack depth analysis
    /// </summary>
    private string GenerateStackSummary(StackDepthAnalysis analysis)
    {
        var sb = new System.Text.StringBuilder();
        
        if (analysis.RiskLevel == StackRiskLevel.Critical)
        {
            sb.AppendLine($"🚨 **CRITICAL: Stack Overflow Risk Detected!**");
            sb.AppendLine($"Estimated stack frames: {analysis.EstimatedTotalFrames} (Salesforce limit: 1,000)");
            sb.AppendLine();
        }
        else if (analysis.RiskLevel == StackRiskLevel.Warning)
        {
            sb.AppendLine($"⚠️ **Warning: High Stack Depth**");
            sb.AppendLine($"Estimated stack frames: {analysis.EstimatedTotalFrames} (approaching 1,000 limit)");
            sb.AppendLine();
        }

        if (analysis.HasFinestLogging)
        {
            sb.AppendLine($"📊 **FINEST Debug Logging Active**");
            sb.AppendLine($"This adds ~{analysis.DebugLoggingOverhead} extra stack frames for method tracking.");
            sb.AppendLine($"Production without logging may have fewer issues, but the underlying code is still risky.");
            sb.AppendLine();
        }

        if (analysis.LoopPatterns.Any())
        {
            sb.AppendLine("🔁 **Loop Patterns Detected:**");
            foreach (var pattern in analysis.LoopPatterns.Take(5))
            {
                sb.AppendLine($"  • {pattern.MethodName}: {pattern.CallCount} calls × {pattern.FramesPerCall} frames = {pattern.TotalFrames} total frames");
            }
        }

        return sb.ToString();
    }

    private string GenerateSummary(LogAnalysis analysis)
    {
        var totalDuration = analysis.RootNode.DurationMs;
        var methodCount = analysis.MethodStats.Count;
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        var dmlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "DML");
        var unhandledErrorCount = analysis.Errors.Count;
        var handledExceptionCount = analysis.HandledExceptions.Count;

        var summary = "📋 **What Happened:**\n\n";

        // Entry point - what started this transaction
        if (!string.IsNullOrEmpty(analysis.EntryPoint))
        {
            summary += $"**Trigger:** {analysis.EntryPoint}\n\n";
        }

        // Opening statement - now uses TransactionFailed instead of just error count
        if (analysis.TransactionFailed)
        {
            summary += $"❌ This transaction **failed** and took {FormatDuration(totalDuration)} total.\n\n";
        }
        else if (handledExceptionCount > 0)
        {
            summary += $"⚠️ This transaction **completed with {handledExceptionCount} caught exception{(handledExceptionCount > 1 ? "s" : "")}** in {FormatDuration(totalDuration)}.\n";
            summary += "The exceptions were handled gracefully - your code continued running.\n\n";
        }
        else if (totalDuration > 5000)
        {
            summary += $"⚠️ This transaction completed successfully but was slow, taking {FormatDuration(totalDuration)}.\n\n";
        }
        else
        {
            summary += $"✅ This transaction completed successfully in {FormatDuration(totalDuration)}.\n\n";
        }

        // What was executed
        summary += "**What Your Code Did:**\n";
        if (methodCount > 0)
        {
            summary += $"• Called {methodCount} different method{(methodCount > 1 ? "s" : "")} (pieces of code)\n";
        }
        
        // Database interactions
        if (soqlCount > 0 || dmlCount > 0)
        {
            summary += $"• Talked to the database {soqlCount + dmlCount} time{(soqlCount + dmlCount > 1 ? "s" : "")}\n";
            if (soqlCount > 0)
                summary += $"  - Read data {soqlCount} time{(soqlCount > 1 ? "s" : "")} (queries)\n";
            if (dmlCount > 0)
                summary += $"  - Wrote/updated data {dmlCount} time{(dmlCount > 1 ? "s" : "")} (inserts/updates)\n";
        }

        summary += "\n";

        // Performance assessment with wall clock vs CPU time distinction
        summary += "**Performance:**\n";
        var lastLimitSnapshot = analysis.LimitSnapshots.LastOrDefault();
        if (lastLimitSnapshot != null)
        {
            var soqlPercent = (lastLimitSnapshot.SoqlQueries * 100.0) / lastLimitSnapshot.SoqlQueriesLimit;
            var cpuPercent = (lastLimitSnapshot.CpuTime * 100.0) / lastLimitSnapshot.CpuTimeLimit;
            
            // Show wall clock vs CPU time breakdown
            var wallClockMs = analysis.WallClockMs > 0 ? analysis.WallClockMs : totalDuration;
            var cpuTimeMs = lastLimitSnapshot.CpuTime;
            var overheadMs = wallClockMs - cpuTimeMs;
            
            if (overheadMs > 1000 && wallClockMs > 2000)
            {
                summary += $"⏱️ **Timing breakdown:**\n";
                summary += $"  - Total wall clock: {FormatDuration((long)wallClockMs)}\n";
                summary += $"  - Your code (CPU): {FormatDuration(cpuTimeMs)}\n";
                summary += $"  - Salesforce overhead: {FormatDuration((long)overheadMs)} (database I/O, async processing)\n\n";
            }
            
            if (soqlPercent < 30 && cpuPercent < 30)
            {
                summary += "✓ Your code is using resources efficiently - plenty of room to spare!\n";
            }
            else if (soqlPercent < 70 && cpuPercent < 70)
            {
                summary += "⚡ Your code is using a moderate amount of resources - this is normal for most operations.\n";
            }
            else
            {
                summary += "⚠️ Your code is pushing Salesforce's limits - you might want to optimize it.\n";
            }

            summary += $"• Database query budget: Used {lastLimitSnapshot.SoqlQueries} out of {lastLimitSnapshot.SoqlQueriesLimit} allowed ({soqlPercent:F0}%)\n";
            summary += $"• Processing time: Used {lastLimitSnapshot.CpuTime}ms out of {lastLimitSnapshot.CpuTimeLimit}ms allowed ({cpuPercent:F0}%)\n";
        }
        else
        {
            summary += "✓ No resource limits were recorded (likely a simple operation).\n";
        }

        summary += "\n";

        // Overall verdict - now using TransactionFailed and distinguishing handled exceptions
        if (analysis.TransactionFailed)
        {
            summary += $"**Result:** ❌ Failed with {unhandledErrorCount} unhandled error{(unhandledErrorCount > 1 ? "s" : "")}. Check the 'Issues' tab for details.\n";
        }
        else if (handledExceptionCount > 0)
        {
            summary += $"**Result:** ⚠️ Completed successfully with {handledExceptionCount} caught exception{(handledExceptionCount > 1 ? "s" : "")} (handled gracefully).\n";
        }
        else if (soqlCount > 100 || (lastLimitSnapshot != null && 
                 ((lastLimitSnapshot.SoqlQueries * 100.0 / lastLimitSnapshot.SoqlQueriesLimit) > 80 ||
                  (lastLimitSnapshot.CpuTime * 100.0 / lastLimitSnapshot.CpuTimeLimit) > 80)))
        {
            summary += "**Result:** ⚠️ Completed successfully, but you should review the recommendations to prevent future issues.\n";
        }
        else
        {
            summary += "**Result:** ✅ Everything looks good! Your code executed as expected.\n";
        }

        return summary;
    }

    private string FormatDuration(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        if (ms < 60000) return $"{ms / 1000.0:F1} seconds";
        return $"{ms / 60000.0:F1} minutes";
    }

    private List<string> DetectIssues(LogAnalysis analysis)
    {
        var issues = new List<string>();
        
        // 🚨 NEW: Use cumulative profiling for specific N+1 patterns FIRST
        if (analysis.CumulativeProfiling != null)
        {
            var excessiveQueries = analysis.CumulativeProfiling.TopQueries
                .Where(q => q.ExecutionCount > 1000)
                .OrderByDescending(q => q.ExecutionCount)
                .ToList();
            
            foreach (var query in excessiveQueries.Take(3))
            {
                issues.Add($"🚨 **N+1 QUERY PATTERN**: {query.Location} - Query executed {query.ExecutionCount:N0} times in {query.TotalDurationMs}ms");
                issues.Add($"   📍 Location: {query.ClassName}.{query.MethodName} line {query.LineNumber}");
                issues.Add($"   🔍 Query: {(query.Query.Length > 80 ? query.Query.Substring(0, 80) + "..." : query.Query)}");
                issues.Add($"   💡 Fix: Move query outside loop, cache results, or batch using Map/Set");
                issues.Add("");
            }
            
            // Check for slow DML operations
            var slowDml = analysis.CumulativeProfiling.TopDmlOperations
                .Where(d => d.TotalDurationMs > 2000)
                .OrderByDescending(d => d.TotalDurationMs)
                .ToList();
            
            foreach (var dml in slowDml.Take(2))
            {
                issues.Add($"🐌 **SLOW DML OPERATION**: {dml.OperationDescription} taking {dml.TotalDurationMs}ms");
                issues.Add($"   📍 Location: {dml.Location}");
                issues.Add($"   💡 Fix: Check validation rules, workflow rules, or bulk operations");
                issues.Add("");
            }
            
            // Check for slow methods
            var slowMethods = analysis.CumulativeProfiling.TopMethods
                .Where(m => m.TotalDurationMs > 3000)
                .OrderByDescending(m => m.TotalDurationMs)
                .ToList();
            
            foreach (var method in slowMethods.Take(2))
            {
                issues.Add($"⏱️ **SLOW METHOD**: {method.Location} taking {method.TotalDurationMs}ms total ({method.ExecutionCount}x calls, avg {method.AverageDurationMs}ms)");
                issues.Add($"   💡 Fix: Profile this method for optimization opportunities");
                issues.Add("");
            }
        }

        // 🚨 Check for stack overflow risk
        if (analysis.StackAnalysis.RiskLevel == StackRiskLevel.Critical)
        {
            issues.Add($"🚨 **CRITICAL: Stack Overflow Risk!** Your code is using an estimated {analysis.StackAnalysis.EstimatedTotalFrames} stack frames " +
                $"(Salesforce limit: 1,000). This WILL cause a 'System.LimitException: Maximum stack depth reached' error. " +
                $"The problem is usually a method called inside a loop that itself calls other methods. " +
                $"Look for: {analysis.StackAnalysis.MaxDepthMethod} at line {analysis.StackAnalysis.MaxDepthLine}.");
        }
        else if (analysis.StackAnalysis.RiskLevel == StackRiskLevel.Warning)
        {
            issues.Add($"⚠️ **High Stack Depth Warning**: Your code is using ~{analysis.StackAnalysis.EstimatedTotalFrames} stack frames " +
                $"(approaching the 1,000 limit). This may fail in certain conditions, especially with debug logging enabled. " +
                $"Consider refactoring loops that call nested methods.");
        }

        // Check for FINEST logging overhead
        if (analysis.StackAnalysis.HasFinestLogging)
        {
            issues.Add($"📊 **FINEST Debug Logging Active**: This log was captured with maximum debug verbosity, " +
                $"which adds ~{analysis.StackAnalysis.DebugLoggingOverhead} extra stack frames. " +
                $"Production may work where QA fails, but the underlying code is still risky!");
        }
        
        // Check for trigger recursion from timeline
        if (analysis.Timeline != null && analysis.Timeline.RecursionCount > 0)
        {
            var recursiveTriggers = analysis.Timeline.Phases
                .Where(p => p.IsRecursive && p.Type == "Trigger")
                .Select(p => p.Name.Split('.')[0])
                .Distinct()
                .ToList();
            
            if (recursiveTriggers.Any())
            {
                var triggerList = string.Join(", ", recursiveTriggers.Take(3));
                issues.Add($"🔄 **Trigger Recursion Detected**: {triggerList} fired multiple times in same transaction. " +
                    $"Total recursive calls: {analysis.Timeline.RecursionCount}. " +
                    $"This causes performance degradation and may indicate missing recursion control.");
            }
        }

        // Check for loop patterns contributing to stack depth
        var dangerousLoopPatterns = analysis.StackAnalysis.LoopPatterns
            .Where(p => p.TotalFrames > 100)
            .ToList();
        
        if (dangerousLoopPatterns.Any())
        {
            var topPattern = dangerousLoopPatterns.First();
            issues.Add($"🔄 **Method Called Excessively in Loop**: '{topPattern.MethodName}' was called {topPattern.CallCount} times, " +
                $"adding ~{topPattern.TotalFrames} stack frames. If this method calls other methods, " +
                $"it's multiplying the problem. Cache the result or move the call outside the loop.");
        }

        // Check for excessive SOQL queries in plain English
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        if (soqlCount > 50)
        {
            issues.Add($"⚠️ **Too Many Database Queries**: Your code asked the database for data {soqlCount} times. " +
                "Salesforce recommends keeping this under 100, but ideally under 20. " +
                "High query counts can make your code slow and may cause failures if you hit the limit.");
        }
        else if (soqlCount > 20)
        {
            issues.Add($"⚡ **Moderate Query Usage**: You're running {soqlCount} database queries. " +
                "This isn't critical, but consider optimizing - fewer queries = faster code.");
        }

        // Check for slow queries
        var slowQueries = analysis.DatabaseOperations.Where(d => d.DurationMs > 1000).ToList();
        if (slowQueries.Any())
        {
            if (slowQueries.Count == 1)
            {
                issues.Add($"🐌 **Slow Query Detected**: One of your database queries took over 1 second. " +
                    "This is like waiting on hold - it wastes time. Speed it up by using filters (WHERE) or indexes.");
            }
            else
            {
                issues.Add($"🐌 **Multiple Slow Queries**: {slowQueries.Count} of your queries took over 1 second each. " +
                    "These are performance bottlenecks - focus optimization here first.");
            }
        }

        // Check for N+1 query pattern
        if (soqlCount > 10)
        {
            var queryCounts = analysis.DatabaseOperations
                .Where(d => d.OperationType == "SOQL")
                .GroupBy(d => SimplifyQuery(d.Query))
                .Where(g => g.Count() > 5)
                .ToList();
            
            if (queryCounts.Any())
            {
                issues.Add($"🔁 **Repetitive Query Pattern (N+1)**: You're asking the database the same question multiple times. " +
                    "This classic mistake happens when you query inside a loop. " +
                    "Example: Instead of asking 'Who is customer #1? Who is customer #2? Who is customer #3?' 100 times, " +
                    "ask once: 'Who are customers #1-100?'");
            }
        }

        // Check governor limits
        var lastLimitSnapshot = analysis.LimitSnapshots.LastOrDefault();
        if (lastLimitSnapshot != null)
        {
            var soqlPercent = (lastLimitSnapshot.SoqlQueries * 100.0) / lastLimitSnapshot.SoqlQueriesLimit;
            var cpuPercent = (lastLimitSnapshot.CpuTime * 100.0) / lastLimitSnapshot.CpuTimeLimit;
            var heapPercent = (lastLimitSnapshot.HeapSize * 100.0) / lastLimitSnapshot.HeapSizeLimit;

            if (soqlPercent > 80)
                issues.Add($"⚠️ **Query Limit Warning**: You've used {lastLimitSnapshot.SoqlQueries} out of {lastLimitSnapshot.SoqlQueriesLimit} allowed queries ({soqlPercent:F0}%). " +
                    "You're dangerously close to the limit! If you hit 100%, your code will stop with an error.");
            
            if (cpuPercent > 80)
                issues.Add($"⏱️ **Processing Time Warning**: Your code used {lastLimitSnapshot.CpuTime}ms out of {lastLimitSnapshot.CpuTimeLimit}ms allowed ({cpuPercent:F0}%). " +
                    "You're running out of processing time! This means your code is doing too much work. " +
                    "Simplify logic or move heavy processing to background jobs.");
            
            if (heapPercent > 80)
                issues.Add($"💾 **Memory Warning**: You're using {lastLimitSnapshot.HeapSize} bytes out of {lastLimitSnapshot.HeapSizeLimit} allowed ({heapPercent:F0}%). " +
                    "Your code is holding too much data in memory at once. " +
                    "Process data in smaller batches to avoid running out of memory.");
        }

        // Check for recursive triggers
        var triggerMethods = analysis.MethodStats
            .Where(m => m.Value.CallCount > 2 && m.Key.Contains("trigger", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        if (triggerMethods.Any())
        {
            var triggerNames = string.Join(", ", triggerMethods.Select(t => t.Key));
            issues.Add($"🔄 **Recursive Trigger Detected**: The trigger '{triggerNames}' is calling itself multiple times. " +
                "This is like a hall of mirrors - the trigger fires, which changes data, which fires the trigger again, and so on. " +
                "This can cause infinite loops and performance problems. Add logic to prevent re-entry.");
        }

        // Check for errors - now distinguishes handled from unhandled
        if (analysis.Errors.Any())
        {
            var fatalErrors = analysis.Errors.Where(e => e.Severity == ExceptionSeverity.Fatal).ToList();
            var unhandledErrors = analysis.Errors.Where(e => e.Severity == ExceptionSeverity.Unhandled).ToList();
            
            if (fatalErrors.Any())
            {
                issues.Add($"💀 **Fatal Error - Transaction Failed**: The execution crashed with a fatal error. " +
                    "This is a complete failure - the transaction was rolled back and no changes were saved.");
            }
            
            if (unhandledErrors.Any())
            {
                if (unhandledErrors.Count == 1)
                {
                    issues.Add($"❌ **Unhandled Exception**: Your code threw an exception that wasn't caught by a try/catch block. " +
                        "Check the error details below to see what went wrong and where.");
                }
                else
                {
                    issues.Add($"❌ **Multiple Unhandled Exceptions**: Your code encountered {unhandledErrors.Count} exceptions that weren't caught. " +
                        "Review each one below to understand what went wrong.");
                }
            }
        }
        
        // Report handled exceptions as informational (not errors)
        if (analysis.HandledExceptions.Any())
        {
            var handledCount = analysis.HandledExceptions.Count;
            issues.Add($"ℹ️ **{handledCount} Caught Exception{(handledCount > 1 ? "s" : "")}**: Your code caught {handledCount} exception{(handledCount > 1 ? "s" : "")} " +
                "with try/catch blocks. These are **not failures** - your code handled them gracefully and continued. " +
                "This is actually good defensive programming, though you may want to review why they occurred.");
        }

        // All clear!
        if (!issues.Any())
        {
            issues.Add("✅ **No Issues Found**: Your code ran cleanly with no warnings or errors!");
        }

        return issues;
    }

    private string SimplifyQuery(string query)
    {
        // Remove specific values to detect similar queries
        query = Regex.Replace(query, @"'[^']*'", "'?'");
        query = Regex.Replace(query, @"\d+", "?");
        return query;
    }

    private List<string> GenerateRecommendations(LogAnalysis analysis)
    {
        var recommendations = new List<string>();
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        var dmlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "DML");

        // 🚨 NEW: Stack depth recommendations FIRST - this is often the critical issue!
        if (analysis.StackAnalysis.RiskLevel == StackRiskLevel.Critical || 
            analysis.StackAnalysis.RiskLevel == StackRiskLevel.Warning)
        {
            recommendations.Add($"🚨 **Fix Stack Overflow Risk**: Your code is at risk of exceeding Salesforce's 1,000 stack frame limit. " +
                $"The main culprit appears to be '{analysis.StackAnalysis.MaxDepthMethod}'. " +
                "**How to fix:**\n" +
                "  1. **Cache record types BEFORE the loop**: Instead of calling getRecordType() 281 times inside a loop, " +
                "query all needed record types once and store them in a Map.\n" +
                "  2. **Flatten method chains**: If method A calls B calls C, consider combining them.\n" +
                "  3. **Process in batches**: Instead of handling all 281 trigger configs at once, chunk them into groups of 50.");
        }

        // Specific recommendations for loop patterns
        var topLoopPatterns = analysis.StackAnalysis.LoopPatterns
            .Where(p => p.TotalFrames > 50)
            .Take(3)
            .ToList();
        
        foreach (var pattern in topLoopPatterns)
        {
            if (pattern.MethodName.Contains("getRecordType", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add($"🔧 **Optimize {pattern.MethodName}**: This method was called {pattern.CallCount} times. " +
                    "**Solution**: Before entering the loop, query ALL record types you need:\n" +
                    "```apex\n" +
                    "// BEFORE: Called in loop\n" +
                    "for (Trigger_Detail__c td : triggerDetails) {\n" +
                    "    RecordType rt = Global_Util.getRecordType(td.Object__c, td.Record_Type_Name__c); // ❌ 281 calls!\n" +
                    "}\n\n" +
                    "// AFTER: Cache before loop\n" +
                    "Set<String> recordTypeNames = new Set<String>();\n" +
                    "for (Trigger_Detail__c td : triggerDetails) {\n" +
                    "    recordTypeNames.add(td.Object__c + '.' + td.Record_Type_Name__c);\n" +
                    "}\n" +
                    "Map<String, RecordType> rtMap = Global_Util.getRecordTypes(recordTypeNames); // ✅ 1 call!\n" +
                    "```");
            }
        }

        // FINEST logging warning
        if (analysis.StackAnalysis.HasFinestLogging)
        {
            recommendations.Add("📊 **Disable FINEST Logging in Production**: Your log shows APEX_CODE,FINEST which adds " +
                $"~{analysis.StackAnalysis.DebugLoggingOverhead} extra stack frames. " +
                "While this helps with debugging, it can push borderline code over the stack limit. " +
                "The code will likely work in Production without trace flags, but you should still fix the underlying issue.");
        }

        // SOQL query recommendations in plain English
        if (soqlCount > 50)
        {
            recommendations.Add($"💡 **Too Many Database Queries**: You're asking the database for information too many times ({soqlCount} times). " +
                "Think of it like making 50+ separate phone calls instead of one call with a list of questions. " +
                "Try to combine multiple queries into one where possible.");
        }
        else if (soqlCount > 20)
        {
            recommendations.Add($"📊 **Moderate Database Usage**: You're querying the database {soqlCount} times. " +
                "This works fine now, but if you're looping through records, consider using 'bulkification' - " +
                "which means processing multiple records at once instead of one at a time.");
        }

        // Slow query recommendations
        var slowQueries = analysis.DatabaseOperations.Where(d => d.DurationMs > 1000).ToList();
        if (slowQueries.Any())
        {
            recommendations.Add($"🐌 **Slow Database Queries Detected**: {slowQueries.Count} of your database queries took over 1 second each. " +
                "This usually means either: (1) You're searching through too much data, or (2) The database needs better 'indexes' " +
                "(think of indexes like a book's table of contents - they help find things faster). " +
                "Consider adding filters (WHERE clauses) to narrow down your search.");
        }

        // N+1 pattern detection in plain English
        if (soqlCount > 10)
        {
            var repeatedQueries = analysis.DatabaseOperations
                .Where(d => d.OperationType == "SOQL")
                .GroupBy(d => SimplifyQuery(d.Query))
                .Where(g => g.Count() > 5)
                .ToList();
            
            if (repeatedQueries.Any())
            {
                recommendations.Add("🔁 **Repetitive Queries (N+1 Pattern)**: Your code is asking the same question over and over. " +
                    "This is like asking 'What's the weather?' 100 times instead of asking once and remembering the answer. " +
                    "Solution: Move your queries outside of loops, or better yet, query for all the data you need at once.");
            }
        }

        // Governor limit warnings in plain English
        var lastLimitSnapshot = analysis.LimitSnapshots.LastOrDefault();
        if (lastLimitSnapshot != null)
        {
            var soqlPercent = (lastLimitSnapshot.SoqlQueries * 100.0) / lastLimitSnapshot.SoqlQueriesLimit;
            var cpuPercent = (lastLimitSnapshot.CpuTime * 100.0) / lastLimitSnapshot.CpuTimeLimit;
            var heapPercent = (lastLimitSnapshot.HeapSize * 100.0) / lastLimitSnapshot.HeapSizeLimit;

            if (soqlPercent > 80)
            {
                recommendations.Add($"⚠️ **Running Out of Query Allowance**: You've used {soqlPercent:F0}% of your allowed database queries. " +
                    "Salesforce limits how many times you can query the database to keep things running smoothly for everyone. " +
                    "If you hit 100%, your code will fail. Reduce the number of queries by combining them or processing in batches.");
            }
            
            if (cpuPercent > 80)
            {
                recommendations.Add($"⏱️ **Running Out of Processing Time**: Your code used {cpuPercent:F0}% of allowed processing time. " +
                    "This means your code is doing a lot of work. Consider: (1) Doing fewer calculations, " +
                    "(2) Processing fewer records at once, or (3) Moving heavy processing to asynchronous jobs that run in the background.");
            }
            
            if (heapPercent > 80)
            {
                recommendations.Add($"💾 **Using Too Much Memory**: Your code is using {heapPercent:F0}% of available memory. " +
                    "This happens when you're holding onto too much data at once. " +
                    "Try processing data in smaller chunks instead of loading everything into memory at once.");
            }
        }

        // DML recommendations
        if (dmlCount > 100)
        {
            recommendations.Add($"📝 **Too Many Database Updates**: You're saving/updating data {dmlCount} times. " +
                "Instead of saving one record at a time in a loop, collect all your changes and save them all at once. " +
                "Think of it like making one trip to the store with a shopping list instead of 100 separate trips.");
        }

        // Method frequency recommendations
        var frequentMethods = analysis.MethodStats.Values
            .Where(m => m.CallCount > 20)
            .OrderByDescending(m => m.CallCount)
            .Take(3)
            .ToList();
        
        if (frequentMethods.Any())
        {
            foreach (var method in frequentMethods)
            {
                recommendations.Add($"🔄 **Method Called Many Times**: '{method.MethodName}' was called {method.CallCount} times. " +
                    "If this method does database queries or complex calculations, it could slow things down. " +
                    "Consider storing the result (caching) and reusing it instead of recalculating every time.");
            }
        }

        // Database time percentage
        var totalDbTime = analysis.DatabaseOperations.Sum(d => d.DurationMs);
        var totalExecution = analysis.RootNode.DurationMs;
        
        if (totalDbTime > 0 && totalExecution > 0)
        {
            var dbPercent = (totalDbTime * 100.0) / totalExecution;
            if (dbPercent > 70)
            {
                recommendations.Add($"⏳ **Most Time Spent on Database**: {dbPercent:F0}% of your execution time is spent talking to the database. " +
                    "This is where you should focus optimization efforts - make your queries more efficient by adding filters or using indexes.");
            }
        }

        // LIMIT clause check
        var queriesWithoutLimit = analysis.DatabaseOperations
            .Where(d => d.OperationType == "SOQL" && !d.Query.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
            .ToList();
            
        if (queriesWithoutLimit.Any())
        {
            recommendations.Add($"🎯 **Add Safety Limits**: {queriesWithoutLimit.Count} of your queries don't have LIMIT clauses. " +
                "Without limits, your query could accidentally try to load thousands of records at once. " +
                "Always add 'LIMIT 200' (or whatever number makes sense) to protect against unexpected data growth.");
        }

        // Error-specific recommendations
        if (analysis.Errors.Any())
        {
            var firstError = analysis.Errors.First().Name;
            if (firstError.Contains("UNABLE_TO_LOCK_ROW", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("🔒 **Record Locking Issue**: Someone else was trying to update the same record at the same time as you. " +
                    "This is like two people trying to edit the same document simultaneously. " +
                    "The system protected the data by blocking one of the updates. Your code should retry the operation or handle this gracefully.");
            }
            else if (firstError.Contains("REQUIRED_FIELD_MISSING", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("📋 **Missing Required Information**: You tried to create/update a record without filling in all required fields. " +
                    "It's like trying to submit a form without filling in the mandatory fields marked with an asterisk (*).");
            }
            else if (firstError.Contains("FIELD_CUSTOM_VALIDATION_EXCEPTION", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("✋ **Validation Rule Blocked Your Change**: A validation rule (a business rule set up in Salesforce) prevented your update. " +
                    "Think of it like a bouncer at a club - the rule is checking if your data meets certain criteria before allowing it in. " +
                    "Check what validation rules exist on this object and make sure your data passes them.");
            }
        }

        // All good!
        if (!recommendations.Any() && !analysis.Errors.Any() && soqlCount < 20 && totalExecution < 5000)
        {
            recommendations.Add("✅ **Everything Looks Great!**: Your code is running efficiently and following best practices. No optimization needed right now.");
        }

        return recommendations;
    }
    
    /// <summary>
    /// Parse CUMULATIVE_PROFILING section at end of log
    /// </summary>
    private CumulativeProfiling? ParseCumulativeProfiling(string[] lines)
    {
        var profiling = new CumulativeProfiling();
        bool inProfilingSection = false;
        bool inSoqlSection = false;
        bool inDmlSection = false;
        bool inMethodSection = false;
        
        var soqlPattern = new Regex(@"^(.+?):\s+line\s+(\d+),\s+column\s+\d+:\s+\[(.+?)\]:\s+executed\s+(\d+)\s+times?\s+in\s+(\d+)\s+ms", RegexOptions.Compiled);
        var dmlPattern = new Regex(@"^(.+?):\s+line\s+(\d+),\s+column\s+\d+:\s+(\w+):\s+(.+?):\s+executed\s+(\d+)\s+times?\s+in\s+(\d+)\s+ms", RegexOptions.Compiled);
        var methodPattern = new Regex(@"^(.+?):\s+line\s+(\d+),\s+column\s+\d+:\s+(.+?):\s+executed\s+(\d+)\s+times?\s+in\s+(\d+)\s+ms", RegexOptions.Compiled);
        
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            
            if (line.Contains("CUMULATIVE_PROFILING_BEGIN"))
            {
                inProfilingSection = true;
                continue;
            }
            
            if (line.Contains("CUMULATIVE_PROFILING_END"))
            {
                break;
            }
            
            if (!inProfilingSection) continue;
            
            // Section headers
            if (line.Contains("CUMULATIVE_PROFILING|SOQL operations|"))
            {
                inSoqlSection = true;
                inDmlSection = false;
                inMethodSection = false;
                continue;
            }
            else if (line.Contains("CUMULATIVE_PROFILING|DML operations|"))
            {
                inSoqlSection = false;
                inDmlSection = true;
                inMethodSection = false;
                continue;
            }
            else if (line.Contains("CUMULATIVE_PROFILING|method invocations|"))
            {
                inSoqlSection = false;
                inDmlSection = false;
                inMethodSection = true;
                continue;
            }
            
            // Parse SOQL operations
            if (inSoqlSection)
            {
                var match = soqlPattern.Match(line);
                if (match.Success)
                {
                    var location = match.Groups[1].Value;
                    var parts = ParseLocation(location);
                    
                    profiling.TopQueries.Add(new CumulativeQuery
                    {
                        ClassName = parts.className,
                        MethodName = parts.methodName,
                        LineNumber = int.Parse(match.Groups[2].Value),
                        Query = match.Groups[3].Value,
                        ExecutionCount = int.Parse(match.Groups[4].Value),
                        TotalDurationMs = int.Parse(match.Groups[5].Value)
                    });
                }
            }
            
            // Parse DML operations
            if (inDmlSection)
            {
                var match = dmlPattern.Match(line);
                if (match.Success)
                {
                    var location = match.Groups[1].Value;
                    var parts = ParseLocation(location);
                    
                    profiling.TopDmlOperations.Add(new CumulativeDml
                    {
                        ClassName = parts.className,
                        MethodName = parts.methodName,
                        LineNumber = int.Parse(match.Groups[2].Value),
                        Operation = match.Groups[3].Value,
                        ObjectType = match.Groups[4].Value,
                        ExecutionCount = int.Parse(match.Groups[5].Value),
                        TotalDurationMs = int.Parse(match.Groups[6].Value)
                    });
                }
            }
            
            // Parse method invocations
            if (inMethodSection)
            {
                var match = methodPattern.Match(line);
                if (match.Success)
                {
                    var location = match.Groups[1].Value;
                    var parts = ParseLocation(location);
                    
                    profiling.TopMethods.Add(new CumulativeMethod
                    {
                        ClassName = parts.className,
                        MethodName = parts.methodName,
                        LineNumber = int.Parse(match.Groups[2].Value),
                        Signature = match.Groups[3].Value,
                        ExecutionCount = int.Parse(match.Groups[4].Value),
                        TotalDurationMs = int.Parse(match.Groups[5].Value)
                    });
                }
            }
        }
        
        // Sort by execution count (descending)
        profiling.TopQueries = profiling.TopQueries.OrderByDescending(q => q.ExecutionCount).Take(10).ToList();
        profiling.TopDmlOperations = profiling.TopDmlOperations.OrderByDescending(d => d.TotalDurationMs).Take(10).ToList();
        profiling.TopMethods = profiling.TopMethods.OrderByDescending(m => m.TotalDurationMs).Take(10).ToList();
        
        return profiling.TopQueries.Any() || profiling.TopDmlOperations.Any() || profiling.TopMethods.Any() 
            ? profiling 
            : null;
    }
    
    private (string className, string methodName) ParseLocation(string location)
    {
        // Handles: "Class.MyClass.myMethod", "Trigger.MyTrigger", "(System Code)"
        if (location.StartsWith("Class."))
        {
            var parts = location.Substring(6).Split('.');
            if (parts.Length >= 2)
                return (parts[0], parts[1]);
            return (parts[0], "unknown");
        }
        else if (location.StartsWith("Trigger."))
        {
            var triggerName = location.Substring(8).Split(':')[0];
            return (triggerName, "trigger");
        }
        else if (location.StartsWith("("))
        {
            return ("System", location.Trim('(', ')'));
        }
        
        return ("Unknown", location);
    }
    
    /// <summary>
    /// Parse CUMULATIVE_LIMIT_USAGE section for accurate final CPU/Heap values
    /// Fixes the "0ms CPU time" bug by reading the actual totals at end of log
    /// </summary>
    private void ParseCumulativeLimitUsage(string[] lines, LogAnalysis analysis)
    {
        // Search backwards from end of file for CUMULATIVE_LIMIT_USAGE section
        for (int i = lines.Length - 1; i >= Math.Max(0, lines.Length - 200); i--)
        {
            var line = lines[i];
            
            // Example: "Number of SOQL queries: 91 out of 100"
            if (line.Contains("|CUMULATIVE_LIMIT_USAGE|"))
            {
                if (line.Contains("Maximum CPU time:"))
                {
                    // Format: "Maximum CPU time: 9272 out of 10000"
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"Maximum CPU time:\s+(\d+)\s+out of\s+(\d+)");
                    if (match.Success)
                    {
                        var cpuMs = int.Parse(match.Groups[1].Value);
                        analysis.CpuTimeMs = cpuMs;
                        
                        // Update the last limit snapshot too
                        if (analysis.LimitSnapshots.Any())
                        {
                            var lastSnapshot = analysis.LimitSnapshots.Last();
                            lastSnapshot.CpuTime = cpuMs;
                        }
                    }
                }
                else if (line.Contains("Maximum heap size:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"Maximum heap size:\s+(\d+)\s+out of\s+(\d+)");
                    if (match.Success && analysis.LimitSnapshots.Any())
                    {
                        var heapSize = int.Parse(match.Groups[1].Value);
                        var lastSnapshot = analysis.LimitSnapshots.Last();
                        lastSnapshot.HeapSize = heapSize;
                    }
                }
                else if (line.Contains("Number of SOQL queries:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"Number of SOQL queries:\s+(\d+)\s+out of\s+(\d+)");
                    if (match.Success && analysis.LimitSnapshots.Any())
                    {
                        var soqlCount = int.Parse(match.Groups[1].Value);
                        var lastSnapshot = analysis.LimitSnapshots.Last();
                        lastSnapshot.SoqlQueries = soqlCount;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Detect if this is a test class execution (vs production)
    /// </summary>
    private void DetectTestExecution(List<LogLine> logLines, LogAnalysis analysis)
    {
        // Look for CODE_UNIT_STARTED with class names ending in "_Test" or "Test"
        var testUnits = logLines
            .Where(l => l.EventType == "CODE_UNIT_STARTED" && l.Details.Length >= 3)
            .Where(l => 
            {
                var className = l.Details.Length >= 3 ? l.Details[2] : "";
                return className.EndsWith("_Test") || 
                       (className.EndsWith("Test") && !className.Contains("."));
            })
            .ToList();
        
        if (testUnits.Any())
        {
            analysis.IsTestExecution = true;
            var firstTest = testUnits.First();
            if (firstTest.Details.Length >= 3)
            {
                // Extract class name (remove method signature)
                var fullName = firstTest.Details[2];
                var className = fullName.Split('.')[0];
                analysis.TestClassName = className;
                
                // Update entry point to show test method
                analysis.EntryPoint = $"Test: {fullName}";
            }
        }
    }
    
    /// <summary>
    /// Build order of execution timeline showing major phases
    /// </summary>
    private ExecutionTimeline BuildExecutionTimeline(List<LogLine> logLines, LogAnalysis analysis)
    {
        var timeline = new ExecutionTimeline();
        var phaseStack = new Stack<TimelinePhase>();
        var allPhases = new List<TimelinePhase>();
        var triggerCounts = new Dictionary<string, int>();
        
        foreach (var line in logLines)
        {
            if (line.EventType == "CODE_UNIT_STARTED")
            {
                var phase = new TimelinePhase
                {
                    StartTime = line.Timestamp,
                    LineNumber = line.LineNumber,
                    Depth = phaseStack.Count
                };
                
                // Parse the name and type from details
                // Details format: [EXTERNAL]|{ID}|{ClassName.MethodName}|{OptionalPath}
                if (line.Details.Length >= 3 && !string.IsNullOrEmpty(line.Details[2]))
                {
                    var fullName = line.Details[2];  // Human-readable name, not ID
                    phase.Name = fullName;
                    
                    // Determine type and icon
                    if (fullName.Contains("Trigger"))
                    {
                        phase.Type = "Trigger";
                        phase.Icon = "🔧";
                        
                        // Track trigger recursion
                        var triggerName = fullName.Split('.')[0];
                        if (!triggerCounts.ContainsKey(triggerName))
                            triggerCounts[triggerName] = 0;
                        triggerCounts[triggerName]++;
                        
                        if (triggerCounts[triggerName] > 1)
                        {
                            phase.IsRecursive = true;
                            timeline.RecursionCount++;
                        }
                    }
                    else if (fullName.Contains("Flow:"))
                    {
                        phase.Type = "Flow";
                        phase.Icon = "🌊";
                    }
                    else if (fullName.Contains("Validation:"))
                    {
                        phase.Type = "Validation";
                        phase.Icon = "✅";
                    }
                    else if (fullName.Contains("Workflow:"))
                    {
                        phase.Type = "Workflow";
                        phase.Icon = "⚙️";
                    }
                    else if (fullName.EndsWith("_Test") || fullName.EndsWith("Test"))
                    {
                        phase.Type = "Test";
                        phase.Icon = "🧪";
                    }
                    else
                    {
                        phase.Type = "Class";
                        phase.Icon = "📦";
                    }
                }
                
                // Add as child of current phase or root
                if (phaseStack.Any())
                {
                    phaseStack.Peek().Children.Add(phase);
                }
                else
                {
                    timeline.Phases.Add(phase);
                }
                
                phaseStack.Push(phase);
                allPhases.Add(phase);
            }
            else if (line.EventType == "CODE_UNIT_FINISHED")
            {
                if (phaseStack.Any())
                {
                    var phase = phaseStack.Pop();
                    // Calculate duration in milliseconds
                    phase.DurationMs = (long)(line.Timestamp - phase.StartTime).TotalMilliseconds;
                }
            }
            else if (line.EventType == "DML_BEGIN")
            {
                var phase = new TimelinePhase
                {
                    StartTime = line.Timestamp,
                    LineNumber = line.LineNumber,
                    Type = "DML",
                    Icon = "💾",
                    Depth = phaseStack.Count
                };
                
                if (line.Details.Length >= 3)
                {
                    phase.Name = $"{line.Details[1]}: {line.Details[2]}";
                }
                
                if (phaseStack.Any())
                {
                    phaseStack.Peek().Children.Add(phase);
                }
                
                allPhases.Add(phase);
            }
        }
        
        // Generate summary
        var triggerCount = allPhases.Count(p => p.Type == "Trigger");
        var flowCount = allPhases.Count(p => p.Type == "Flow");
        var validationCount = allPhases.Count(p => p.Type == "Validation");
        var dmlCount = allPhases.Count(p => p.Type == "DML");
        
        timeline.Summary = $"{triggerCount} triggers, {flowCount} flows, {validationCount} validations, {dmlCount} DML operations";
        
        if (timeline.RecursionCount > 0)
        {
            timeline.Summary += $" ⚠️ {timeline.RecursionCount} recursive calls";
        }
        
        return timeline;
    }
    
    /// <summary>
    /// Calculate health score (0-100) and generate prioritized actionable issues
    /// </summary>
    private HealthScore CalculateHealthScore(LogAnalysis analysis)
    {
        var health = new HealthScore();
        var score = 100.0;
        var reasons = new List<string>();
        
        // Factor 1: Governor Limits (40 points)
        var lastSnapshot = analysis.LimitSnapshots?.LastOrDefault();
        if (lastSnapshot != null)
        {
            var soqlPct = (lastSnapshot.SoqlQueries * 100.0) / lastSnapshot.SoqlQueriesLimit;
            var cpuPct = (lastSnapshot.CpuTime * 100.0) / lastSnapshot.CpuTimeLimit;
            var heapPct = (lastSnapshot.HeapSize * 100.0) / lastSnapshot.HeapSizeLimit;
            var rowsPct = (lastSnapshot.QueryRows * 100.0) / lastSnapshot.QueryRowsLimit;
            
            // Deduct points based on usage
            if (soqlPct > 90) { score -= 15; reasons.Add("SOQL usage critical (>90%)"); }
            else if (soqlPct > 80) { score -= 10; reasons.Add("SOQL usage high (>80%)"); }
            else if (soqlPct > 50) { score -= 5; }
            
            if (cpuPct > 90) { score -= 15; reasons.Add("CPU time critical (>90%)"); }
            else if (cpuPct > 80) { score -= 10; reasons.Add("CPU time high (>80%)"); }
            else if (cpuPct > 50) { score -= 5; }
            
            if (rowsPct > 90) { score -= 10; reasons.Add("Query rows critical (>90%)"); }
            else if (rowsPct > 80) { score -= 5; }
        }
        
        // Factor 2: Performance (30 points)
        var durationSec = analysis.DurationMs / 1000.0;
        if (durationSec > 20) { score -= 15; reasons.Add("Execution time >20 seconds"); }
        else if (durationSec > 10) { score -= 10; reasons.Add("Execution time >10 seconds"); }
        else if (durationSec > 5) { score -= 5; }
        
        // Factor 3: Code Quality (30 points)
        if (analysis.Timeline?.RecursionCount > 0)
        {
            score -= Math.Min(15, analysis.Timeline.RecursionCount * 3);
            reasons.Add($"{analysis.Timeline.RecursionCount} recursive trigger calls");
        }
        
        if (analysis.CumulativeProfiling != null)
        {
            var excessiveQueries = analysis.CumulativeProfiling.TopQueries.Count(q => q.ExecutionCount > 1000);
            if (excessiveQueries > 0)
            {
                score -= Math.Min(15, excessiveQueries * 5);
                reasons.Add($"{excessiveQueries} queries executed >1000 times (N+1 pattern)");
            }
        }
        
        health.Score = Math.Max(0, (int)score);
        health.Reasoning = reasons.Any() ? string.Join(", ", reasons) : "No major issues detected";
        
        // Assign grade and status
        health.Grade = health.Score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };
        
        health.Status = health.Score switch
        {
            >= 90 => "Excellent",
            >= 80 => "Good",
            >= 60 => "Needs Work",
            >= 40 => "Poor",
            _ => "Critical"
        };
        
        health.StatusIcon = health.Score switch
        {
            >= 80 => "🎯",
            >= 60 => "⚡",
            >= 40 => "⚠️",
            _ => "🔥"
        };
        
        // Generate actionable issues
        GenerateActionableIssues(analysis, health, lastSnapshot);
        
        return health;
    }
    
    /// <summary>
    /// Generate prioritized, actionable issues with specific fixes
    /// </summary>
    private void GenerateActionableIssues(LogAnalysis analysis, HealthScore health, GovernorLimitSnapshot? limits)
    {
        var priority = 1;
        
        // CRITICAL: N+1 Query Patterns
        if (analysis.CumulativeProfiling != null)
        {
            var worstQuery = analysis.CumulativeProfiling.TopQueries
                .Where(q => q.ExecutionCount > 1000)
                .OrderByDescending(q => q.ExecutionCount)
                .FirstOrDefault();
            
            if (worstQuery != null)
            {
                var objType = ExtractObjectType(worstQuery.Query);
                health.CriticalIssues.Add(new ActionableIssue
                {
                    Title = "N+1 Query Pattern Detected",
                    Problem = $"Query executed {worstQuery.ExecutionCount:N0} times in a loop",
                    Impact = $"Wastes {worstQuery.TotalDurationMs}ms and {worstQuery.ExecutionCount:N0} query rows",
                    Location = worstQuery.Location,
                    Severity = IssueSeverity.Critical,
                    Difficulty = IssueDifficulty.Easy,
                    Fix = "Move query OUTSIDE the loop. Query once, store in a Map, then reference inside loop.",
                    CodeExample = $"Map<String,{objType}> cache = new Map<String,{objType}>();\n" +
                                  $"// Query once before loop\nfor({objType} obj : cache.values()) {{\n    // Use obj here\n}}",
                    EstimatedFixTimeMinutes = 30,
                    Priority = priority++
                });
            }
        }
        
        // CRITICAL: Trigger Recursion
        if (analysis.Timeline?.RecursionCount > 0)
        {
            var recursiveTriggers = analysis.Timeline.Phases
                .Where(p => p.IsRecursive && p.Type == "Trigger")
                .OrderByDescending(p => p.DurationMs)
                .Take(2)
                .ToList();
            
            foreach (var trigger in recursiveTriggers)
            {
                health.CriticalIssues.Add(new ActionableIssue
                {
                    Title = "Trigger Recursion Detected",
                    Problem = $"{trigger.Name} fired multiple times in same transaction",
                    Impact = $"Wastes {trigger.DurationMs}ms and risks governor limits",
                    Location = trigger.Name,
                    Severity = IssueSeverity.Critical,
                    Difficulty = IssueDifficulty.Medium,
                    Fix = "Add static Set<Id> to prevent re-processing same records",
                    CodeExample = "private static Set<Id> processedIds = new Set<Id>();\nif(processedIds.contains(record.Id)) continue;\nprocessedIds.add(record.Id);",
                    EstimatedFixTimeMinutes = 60,
                    Priority = priority++
                });
            }
        }
        
        // HIGH PRIORITY: Governor Limit Warnings
        if (limits != null)
        {
            var soqlPct = (limits.SoqlQueries * 100.0) / limits.SoqlQueriesLimit;
            if (soqlPct > 80)
            {
                health.HighPriorityIssues.Add(new ActionableIssue
                {
                    Title = "SOQL Queries Near Limit",
                    Problem = $"Used {limits.SoqlQueries} of {limits.SoqlQueriesLimit} SOQL queries ({soqlPct:F0}%)",
                    Impact = "Risk of hitting governor limit and transaction failure",
                    Location = "Multiple locations",
                    Severity = IssueSeverity.High,
                    Difficulty = IssueDifficulty.Medium,
                    Fix = "Bulkify queries by combining multiple queries into one with WHERE IN clause",
                    EstimatedFixTimeMinutes = 45,
                    Priority = priority++
                });
            }
        }
        
        // QUICK WINS: Slow DML operations
        if (analysis.CumulativeProfiling != null)
        {
            var slowDml = analysis.CumulativeProfiling.TopDmlOperations
                .Where(d => d.TotalDurationMs > 2000)
                .OrderByDescending(d => d.TotalDurationMs)
                .Take(2)
                .ToList();
            
            foreach (var dml in slowDml)
            {
                health.QuickWins.Add(new ActionableIssue
                {
                    Title = "Slow DML Operation",
                    Problem = $"{dml.OperationDescription} taking {dml.TotalDurationMs}ms",
                    Impact = $"Reduces transaction time by {dml.TotalDurationMs}ms",
                    Location = dml.Location,
                    Severity = IssueSeverity.Medium,
                    Difficulty = IssueDifficulty.Easy,
                    Fix = "Check for validation rules, workflow rules, or process builders slowing this down",
                    EstimatedFixTimeMinutes = 15,
                    Priority = priority++
                });
            }
        }
    }
    
    /// <summary>
    /// Extract object type from SOQL query (e.g., "SELECT Id FROM Account" => "Account")
    /// </summary>
    private string ExtractObjectType(string query)
    {
        var match = System.Text.RegularExpressions.Regex.Match(query, @"FROM\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "SObject";
    }
}
