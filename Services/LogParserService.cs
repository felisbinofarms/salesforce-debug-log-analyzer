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
            ParsedAt = DateTime.UtcNow
        };

        if (string.IsNullOrWhiteSpace(logContent))
        {
            analysis.Summary = "Empty log file";
            return analysis;
        }

        var lines = logContent.Split('\n');
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

        // Phase 2: Build execution tree
        analysis.RootNode = BuildExecutionTree(logLines);

        // Phase 3: Extract database operations
        analysis.DatabaseOperations = ExtractDatabaseOperations(logLines);

        // Phase 4: Extract governor limits
        analysis.LimitSnapshots = ExtractGovernorLimits(logLines);

        // Phase 5: Find errors
        analysis.Errors = FindErrors(analysis.RootNode);

        // Phase 6: Calculate method statistics
        analysis.MethodStats = CalculateMethodStatistics(analysis.RootNode);

        // Phase 7: Generate summary and recommendations
        analysis.Summary = GenerateSummary(analysis);
        analysis.Issues = DetectIssues(analysis);
        analysis.Recommendations = GenerateRecommendations(analysis);

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

    private string GenerateSummary(LogAnalysis analysis)
    {
        var totalDuration = analysis.RootNode.DurationMs;
        var methodCount = analysis.MethodStats.Count;
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        var dmlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "DML");
        var errorCount = analysis.Errors.Count;

        var summary = $"Execution completed in {totalDuration}ms. ";
        
        if (methodCount > 0)
            summary += $"Executed {methodCount} unique method(s). ";
        
        if (soqlCount > 0)
            summary += $"Performed {soqlCount} SOQL query(ies)";
        
        if (dmlCount > 0)
            summary += soqlCount > 0 ? $" and {dmlCount} DML operation(s). " : $"Performed {dmlCount} DML operation(s). ";
        else if (soqlCount > 0)
            summary += ". ";
        
        if (errorCount > 0)
            summary += $"⚠️ Encountered {errorCount} error(s).";
        else
            summary += "✓ No errors detected.";

        return summary;
    }

    private List<string> DetectIssues(LogAnalysis analysis)
    {
        var issues = new List<string>();

        // Check for excessive SOQL queries
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        if (soqlCount > 50)
        {
            issues.Add($"⚠️ High number of SOQL queries: {soqlCount} (Consider bulkification)");
        }
        else if (soqlCount > 20)
        {
            issues.Add($"⚡ Moderate SOQL usage: {soqlCount} queries (Review for optimization opportunities)");
        }

        // Check for slow queries
        var slowQueries = analysis.DatabaseOperations.Where(d => d.DurationMs > 1000).ToList();
        if (slowQueries.Any())
        {
            issues.Add($"🐌 Found {slowQueries.Count} slow database operation(s) (>1000ms) - Review indexes and selectivity");
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
                issues.Add($"🔁 Possible N+1 query pattern detected - {queryCounts.Count} query type(s) executed multiple times");
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
                issues.Add($"⚠️ SOQL queries near limit: {lastLimitSnapshot.SoqlQueries}/{lastLimitSnapshot.SoqlQueriesLimit} ({soqlPercent:F0}%)");
            
            if (cpuPercent > 80)
                issues.Add($"⚠️ CPU time near limit: {lastLimitSnapshot.CpuTime}/{lastLimitSnapshot.CpuTimeLimit}ms ({cpuPercent:F0}%)");
            
            if (heapPercent > 80)
                issues.Add($"⚠️ Heap size near limit: {lastLimitSnapshot.HeapSize}/{lastLimitSnapshot.HeapSizeLimit} bytes ({heapPercent:F0}%)");
        }

        // Check for recursive triggers
        var triggerMethods = analysis.MethodStats
            .Where(m => m.Value.CallCount > 2 && m.Key.Contains("trigger", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        if (triggerMethods.Any())
        {
            issues.Add($"🔄 Possible recursive trigger: {string.Join(", ", triggerMethods.Select(t => t.Key))} called multiple times");
        }

        // Check for errors
        if (analysis.Errors.Any())
        {
            issues.Add($"❌ Execution failed with {analysis.Errors.Count} error(s) - Review exception details below");
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
        
        if (soqlCount > 20 && soqlCount <= 50)
        {
            recommendations.Add("💡 Consider using bulkified patterns to reduce SOQL queries");
        }

        // Check for methods called many times
        var frequentMethods = analysis.MethodStats.Values
            .Where(m => m.CallCount > 10)
            .OrderByDescending(m => m.CallCount)
            .Take(3)
            .ToList();
        
        if (frequentMethods.Any())
        {
            foreach (var method in frequentMethods)
            {
                recommendations.Add($"💡 Method '{method.MethodName}' called {method.CallCount} times - Consider caching or refactoring");
            }
        }

        // Check for slow operations
        var totalDbTime = analysis.DatabaseOperations.Sum(d => d.DurationMs);
        var totalExecution = analysis.RootNode.DurationMs;
        
        if (totalDbTime > 0 && totalExecution > 0)
        {
            var dbPercent = (totalDbTime * 100.0) / totalExecution;
            if (dbPercent > 70)
            {
                recommendations.Add($"💡 Database operations account for {dbPercent:F0}% of execution time - Focus optimization here");
            }
        }

        // General best practices
        if (analysis.DatabaseOperations.Any(d => !d.Query.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) && d.OperationType == "SOQL"))
        {
            recommendations.Add("💡 Some SOQL queries don't have LIMIT clauses - Consider adding limits for safety");
        }

        if (!analysis.Errors.Any() && soqlCount < 20 && totalExecution < 5000)
        {
            recommendations.Add("✅ Good performance! Execution is efficient and within best practices");
        }

        return recommendations;
    }
}
