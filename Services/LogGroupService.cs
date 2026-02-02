using System.Text.RegularExpressions;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for grouping related debug logs into transactions and detecting execution phases
/// </summary>
public class LogGroupService
{
    private static readonly Regex RecordIdPattern = new(@"\b([a-zA-Z0-9]{15}|[a-zA-Z0-9]{18})\b", RegexOptions.Compiled);
    private static readonly TimeSpan GroupingWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Group debug logs by user context and time proximity
    /// </summary>
    public List<LogGroup> GroupRelatedLogs(List<DebugLogMetadata> allLogs)
    {
        var groups = new List<LogGroup>();
        var ungrouped = new List<DebugLogMetadata>(allLogs);

        // Sort by timestamp
        ungrouped = ungrouped.OrderBy(l => l.Timestamp).ToList();

        while (ungrouped.Any())
        {
            var seed = ungrouped.First();
            var group = new LogGroup
            {
                StartTime = seed.Timestamp,
                UserId = seed.UserId,
                UserName = seed.UserName,
                RecordId = seed.RecordId
            };

            // Find logs within grouping window from same user
            var relatedLogs = ungrouped.Where(l =>
                l.UserId == seed.UserId &&
                (l.Timestamp - seed.Timestamp) <= GroupingWindow
            ).ToList();

            // Further filter by record context if available
            if (!string.IsNullOrEmpty(group.RecordId))
            {
                var contextualLogs = relatedLogs.Where(l =>
                    l.RecordId == group.RecordId
                ).ToList();

                // Only use contextual filtering if we found related logs
                if (contextualLogs.Count > 1)
                {
                    relatedLogs = contextualLogs;
                }
            }

            // If still only one log and no clear context, it's a standalone log
            if (relatedLogs.Count == 1)
            {
                group.IsSingleLog = true;
            }

            group.Logs = relatedLogs;
            group.EndTime = relatedLogs.Max(l => l.Timestamp.Add(TimeSpan.FromMilliseconds(l.DurationMs)));
            group.TotalDuration = (group.EndTime - group.StartTime).TotalMilliseconds;

            // Detect phases and patterns
            DetectPhases(group);
            DetectReentryPatterns(group);
            DetectMixedContexts(group);
            CalculateAggregateMetrics(group);
            GenerateRecommendations(group);

            groups.Add(group);
            ungrouped.RemoveAll(l => relatedLogs.Contains(l));
        }

        return groups;
    }

    /// <summary>
    /// Detect execution phases (Backend, Frontend, Async)
    /// </summary>
    private void DetectPhases(LogGroup group)
    {
        group.Phases = new List<LogPhase>();

        // Phase 1: Backend Processing (triggers, flows, validation)
        var backendLogs = group.Logs.Where(l =>
            l.CodeUnitName.Contains("Trigger", StringComparison.OrdinalIgnoreCase) ||
            l.CodeUnitName.Contains("Flow", StringComparison.OrdinalIgnoreCase) ||
            l.CodeUnitName.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
            l.CodeUnitName.Contains("Validation", StringComparison.OrdinalIgnoreCase) ||
            l.CodeUnitName.Contains("WorkflowRule", StringComparison.OrdinalIgnoreCase) ||
            l.MethodName.Contains("@future") ||
            l.MethodName.Contains("Queueable")
        ).ToList();

        if (backendLogs.Any())
        {
            var backendPhase = new LogPhase
            {
                Name = "Backend Processing",
                Type = PhaseType.Backend,
                Logs = backendLogs,
                StartTime = backendLogs.Min(l => l.Timestamp),
                EndTime = backendLogs.Max(l => l.Timestamp.Add(TimeSpan.FromMilliseconds(l.DurationMs))),
                DurationMs = backendLogs.Sum(l => l.DurationMs)
            };

            // Detect async operations
            backendPhase.HasAsyncOperations = backendLogs.Any(l =>
                l.MethodName.Contains("@future") ||
                l.MethodName.Contains("Queueable")
            );

            group.Phases.Add(backendPhase);
        }

        // Phase 2: Frontend/Component Rehydration (Lightning controllers)
        var frontendLogs = group.Logs.Where(l =>
            l.MethodName.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
            l.MethodName.Contains("@AuraEnabled") ||
            l.CodeUnitName.Contains("Aura", StringComparison.OrdinalIgnoreCase) ||
            l.CodeUnitName.Contains("LWC", StringComparison.OrdinalIgnoreCase) ||
            (l.MethodName.Contains("get") && l.MethodName.Contains("()"))
        ).ToList();

        if (frontendLogs.Any())
        {
            var frontendPhase = new LogPhase
            {
                Name = "Component Rehydration",
                Type = PhaseType.Frontend,
                Logs = frontendLogs,
                StartTime = frontendLogs.Min(l => l.Timestamp),
                EndTime = frontendLogs.Max(l => l.Timestamp.Add(TimeSpan.FromMilliseconds(l.DurationMs))),
                DurationMs = frontendLogs.Sum(l => l.DurationMs)
            };

            // Detect sequential vs parallel loading
            frontendPhase.IsSequentialLoading = DetectSequentialLoading(frontendLogs);
            if (frontendPhase.IsSequentialLoading)
            {
                frontendPhase.ParallelSavings = CalculateParallelSavings(frontendLogs);
            }

            group.Phases.Add(frontendPhase);
        }

        // Calculate gaps between phases
        if (group.Phases.Count > 1)
        {
            for (int i = 1; i < group.Phases.Count; i++)
            {
                var gap = (group.Phases[i].StartTime - group.Phases[i - 1].EndTime).TotalMilliseconds;
                if (gap > 100) // More than 100ms gap
                {
                    group.Phases[i - 1].GapToNextPhase = gap;
                }
            }
        }
    }

    /// <summary>
    /// Detect if components are loading sequentially (waterfall) or in parallel
    /// </summary>
    private bool DetectSequentialLoading(List<DebugLogMetadata> logs)
    {
        if (logs.Count < 2) return false;

        var sorted = logs.OrderBy(l => l.Timestamp).ToList();

        // Check if each log starts after the previous one ends
        for (int i = 1; i < sorted.Count; i++)
        {
            var previousEnd = sorted[i - 1].Timestamp.Add(TimeSpan.FromMilliseconds(sorted[i - 1].DurationMs));
            var currentStart = sorted[i].Timestamp;
            var gap = (currentStart - previousEnd).TotalMilliseconds;

            // If logs start more than 50ms apart and after previous ends, it's sequential
            if (gap > 50)
            {
                return true;
            }
        }

        // If all logs start within 50ms of each other, they're parallel
        var firstStart = sorted[0].Timestamp;
        var lastStart = sorted[sorted.Count - 1].Timestamp;
        return (lastStart - firstStart).TotalMilliseconds > 100;
    }

    /// <summary>
    /// Calculate time that could be saved by parallel loading
    /// </summary>
    private double CalculateParallelSavings(List<DebugLogMetadata> logs)
    {
        if (logs.Count < 2) return 0;

        // Sequential: sum of all durations
        var sequentialTotal = logs.Sum(l => l.DurationMs);

        // Parallel: longest single duration
        var parallelTotal = logs.Max(l => l.DurationMs);

        return Math.Max(0, sequentialTotal - parallelTotal);
    }

    /// <summary>
    /// Detect trigger re-entry patterns (recursion)
    /// </summary>
    private void DetectReentryPatterns(LogGroup group)
    {
        var triggerCounts = group.Logs
            .Where(l => l.CodeUnitName.Contains("Trigger", StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.CodeUnitName)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Count());

        if (triggerCounts.Any())
        {
            group.ReentryPatterns = triggerCounts;
            group.TotalReentryCount = triggerCounts.Sum(kvp => kvp.Value - 1); // Subtract first entry
        }
    }

    /// <summary>
    /// Detect if logs have mixed execution contexts (UI + Batch + Integration)
    /// </summary>
    private void DetectMixedContexts(LogGroup group)
    {
        var contexts = group.Logs.Select(l => l.Context).Distinct().ToList();

        // If we have more than one context (and it's not just Unknown), we have mixed contexts
        if (contexts.Count > 1 && contexts.Any(c => c != ExecutionContext.Unknown))
        {
            group.HasMixedContexts = true;

            // Determine primary context (most common)
            var contextCounts = group.Logs
                .Where(l => l.Context != ExecutionContext.Unknown)
                .GroupBy(l => l.Context)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            group.PrimaryContext = contextCounts?.Key ?? ExecutionContext.Unknown;
        }
        else
        {
            group.HasMixedContexts = false;
            group.PrimaryContext = contexts.FirstOrDefault(c => c != ExecutionContext.Unknown);
            if (group.PrimaryContext == ExecutionContext.Unknown && contexts.Any())
            {
                group.PrimaryContext = contexts.First();
            }
        }
    }

    /// <summary>
    /// Calculate aggregate metrics across all logs in the group
    /// </summary>
    private void CalculateAggregateMetrics(LogGroup group)
    {
        group.TotalSoqlQueries = group.Logs.Sum(l => l.SoqlQueries);
        group.TotalQueryRows = group.Logs.Sum(l => l.QueryRows);
        group.TotalDmlStatements = group.Logs.Sum(l => l.DmlStatements);
        group.TotalDmlRows = group.Logs.Sum(l => l.DmlRows);
        group.TotalCpuTime = group.Logs.Sum(l => l.CpuTime);
        group.TotalHeapSize = group.Logs.Max(l => l.HeapSize); // Max heap across all logs
        group.ErrorCount = group.Logs.Count(l => l.HasErrors);
        group.SlowestOperation = group.Logs.OrderByDescending(l => l.DurationMs).FirstOrDefault()?.MethodName ?? "Unknown";
    }

    /// <summary>
    /// Generate performance recommendations based on group patterns
    /// </summary>
    private void GenerateRecommendations(LogGroup group)
    {
        var recommendations = new List<string>();

        // Single log - no grouping recommendations
        if (group.IsSingleLog)
        {
            return;
        }

        // Mixed execution contexts - CRITICAL governance issue
        if (group.HasMixedContexts)
        {
            var contextTypes = group.Logs.Select(l => l.Context).Distinct().Where(c => c != ExecutionContext.Unknown).ToList();
            var contextNames = string.Join(", ", contextTypes.Select(c => c.ToString()));

            recommendations.Add($"🚨 GOVERNANCE ISSUE: Mixed execution contexts detected ({contextNames})");
            recommendations.Add("💡 This user account is being used for multiple purposes:");

            if (contextTypes.Contains(ExecutionContext.Interactive) && contextTypes.Contains(ExecutionContext.Batch))
            {
                recommendations.Add("   • Interactive UI actions AND Batch processing");
                recommendations.Add("   • Recommendation: Create dedicated 'BatchUser' for batch jobs");
            }

            if (contextTypes.Contains(ExecutionContext.Interactive) && contextTypes.Contains(ExecutionContext.Integration))
            {
                recommendations.Add("   • Interactive UI actions AND API integrations");
                recommendations.Add("   • Recommendation: Create dedicated integration user per external system");
            }

            if (contextTypes.Contains(ExecutionContext.Integration) && contextTypes.Contains(ExecutionContext.Batch))
            {
                recommendations.Add("   • API integrations AND Batch processing");
                recommendations.Add("   • Recommendation: Separate integration users from batch users");
            }

            recommendations.Add("⚠️ Impact: Hard to debug issues, logs are mixed, difficult to trace user actions");
            recommendations.Add("📊 Best Practice: One user per execution context (UI, Batch, Integration, Scheduled)");
        }

        // Re-entry detection
        if (group.TotalReentryCount > 0)
        {
            foreach (var pattern in group.ReentryPatterns)
            {
                recommendations.Add($"🔥 {pattern.Key} fired {pattern.Value} times - add recursion control using static boolean or framework");
            }
        }

        // Sequential component loading
        var frontendPhase = group.Phases.FirstOrDefault(p => p.Type == PhaseType.Frontend);
        if (frontendPhase?.IsSequentialLoading == true)
        {
            recommendations.Add($"⚡ Components loading sequentially - optimize for parallel loading to save {frontendPhase.ParallelSavings:N0}ms");
            recommendations.Add("💡 Use @wire with cacheable=true or load data in connectedCallback() simultaneously");
        }

        // Async operations blocking
        var backendPhase = group.Phases.FirstOrDefault(p => p.Type == PhaseType.Backend);
        if (backendPhase?.HasAsyncOperations == true)
        {
            var asyncLog = backendPhase.Logs.FirstOrDefault(l =>
                l.MethodName.Contains("@future") || l.MethodName.Contains("Queueable"));
            if (asyncLog != null && asyncLog.DurationMs > 1000)
            {
                recommendations.Add($"🐌 @future method took {asyncLog.DurationMs:N0}ms - consider true async pattern or queueable with continuation");
            }
        }

        // Multiple phases gap
        if (group.Phases.Count > 1)
        {
            var gapPhase = group.Phases.FirstOrDefault(p => p.GapToNextPhase > 500);
            if (gapPhase != null)
            {
                recommendations.Add($"⏱️ {gapPhase.GapToNextPhase:N0}ms gap between {gapPhase.Name} and next phase - investigate page refresh delay");
            }
        }

        // High SOQL query count
        if (group.TotalSoqlQueries > 20)
        {
            recommendations.Add($"📊 {group.TotalSoqlQueries} total SOQL queries across transaction - look for N+1 patterns and consolidate queries");
        }

        // High CPU time
        if (group.TotalCpuTime > 5000)
        {
            recommendations.Add($"⚠️ {group.TotalCpuTime}ms total CPU time - approaching limit, optimize loops and calculations");
        }

        // Long total duration
        if (group.TotalDuration > 10000)
        {
            recommendations.Add($"🔥 Total user wait time: {group.TotalDuration / 1000:N1} seconds - CRITICAL: User experience severely impacted");
            recommendations.Add("💡 Priority fixes: recursion control, parallel component loading, query optimization");
        }
        else if (group.TotalDuration > 5000)
        {
            recommendations.Add($"⚠️ Total user wait time: {group.TotalDuration / 1000:N1} seconds - Users will notice this delay");
        }

        group.Recommendations = recommendations;
    }

    /// <summary>
    /// Extract Salesforce record ID from log content or metadata
    /// </summary>
    public string ExtractRecordId(string logContent)
    {
        if (string.IsNullOrWhiteSpace(logContent)) return string.Empty;

        // Look for common Salesforce ID patterns (001, 003, 005, 500, etc.)
        var match = RecordIdPattern.Match(logContent);
        if (match.Success)
        {
            var id = match.Value;
            // Validate it looks like a Salesforce ID (starts with common prefixes)
            if (id.StartsWith("001") || id.StartsWith("003") || id.StartsWith("005") ||
                id.StartsWith("500") || id.StartsWith("006") || id.StartsWith("00Q") ||
                id.StartsWith("01t") || id.StartsWith("a0"))
            {
                return id;
            }
        }

        return string.Empty;
    }
}
