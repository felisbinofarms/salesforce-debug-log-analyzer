using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

public class LogParserTests
{
    private readonly LogParserService _parser;
    private readonly string _sampleLogsPath;
    private readonly ITestOutputHelper _output;

    public LogParserTests(ITestOutputHelper output)
    {
        _parser = new LogParserService();
        _output = output;
        // Get the path to SampleLogs relative to test project
        var projectDir = Directory.GetCurrentDirectory();
        _sampleLogsPath = Path.Combine(projectDir, "..", "..", "..", "..", "SampleLogs");
    }

    [Fact]
    public void ParseLog_SimpleAccountInsert_ReturnsValidAnalysis()
    {
        // Arrange
        var logPath = Path.Combine(_sampleLogsPath, "simple_account_insert.log");
        Assert.True(File.Exists(logPath), $"Sample log not found at: {logPath}");
        
        var logContent = File.ReadAllText(logPath);

        // Act
        var analysis = _parser.ParseLog(logContent, "simple_account_insert.log");

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal("simple_account_insert.log", analysis.LogName);
        Assert.True(analysis.LineCount > 0, "Should have parsed lines");
        Assert.NotNull(analysis.RootNode);
        Assert.NotNull(analysis.Summary);
        Assert.False(string.IsNullOrEmpty(analysis.Summary), "Summary should not be empty");
        
        // Check we found database operations
        Assert.NotNull(analysis.DatabaseOperations);
        Assert.True(analysis.DatabaseOperations.Count > 0, "Should have found DML/SOQL operations");
        
        // Check we found governor limits
        Assert.NotNull(analysis.LimitSnapshots);
    }

    [Fact]
    public void ParseLog_ErrorValidationFailure_DetectsErrors()
    {
        // Arrange
        var logPath = Path.Combine(_sampleLogsPath, "error_validation_failure.log");
        Assert.True(File.Exists(logPath), $"Sample log not found at: {logPath}");
        
        var logContent = File.ReadAllText(logPath);

        // Act
        var analysis = _parser.ParseLog(logContent, "error_validation_failure.log");

        // Assert
        Assert.NotNull(analysis);
        // This log has an exception that was caught in a try/catch block,
        // so it should be classified as a handled exception (not an error)
        Assert.True(analysis.HandledExceptions.Count > 0 || analysis.Errors.Count > 0, 
            "Should detect exceptions in error log (either handled or unhandled)");
        // Since the exception was caught, the transaction should NOT be marked as failed
        Assert.False(analysis.TransactionFailed, "Transaction should succeed since exception was caught");
    }

    [Fact]
    public void ParseLog_EmptyContent_ReturnsEmptyAnalysis()
    {
        // Act
        var analysis = _parser.ParseLog("", "empty.log");

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal("Empty log file", analysis.Summary);
    }

    [Fact]
    public void ParseLog_InvalidContent_ReturnsNoLinesFound()
    {
        // Arrange
        var invalidContent = "This is not a valid Salesforce log\nJust some random text";

        // Act
        var analysis = _parser.ParseLog(invalidContent, "invalid.log");

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal("No valid log lines found", analysis.Summary);
    }
    
    [Fact]
    public void LoadLogFromPath_ValidFile_LoadsAndParses()
    {
        // This tests the actual file loading path that the UI uses
        var logPath = Path.Combine(_sampleLogsPath, "simple_account_insert.log");
        Assert.True(File.Exists(logPath), $"Sample log not found at: {logPath}");
        
        // Simulate what the UI does
        var content = File.ReadAllText(logPath);
        var fileName = Path.GetFileName(logPath);
        var analysis = _parser.ParseLog(content, fileName);
        
        Assert.NotNull(analysis);
        Assert.Equal("simple_account_insert.log", analysis.LogName);
        Assert.True(analysis.LineCount > 0);
    }
    
    [Fact]
    public void ParseLog_LargeRealWorldLog_CompletesWithinTimeout()
    {
        // Test with a real 19MB log file from Downloads if it exists
        var logPath = @"C:\Users\felis\Downloads\apex-07LWH00000OGWxV2AX.log";
        
        if (!File.Exists(logPath))
        {
            _output.WriteLine("Skipping large log test - file not found");
            return; // Skip if the file doesn't exist
        }
        
        _output.WriteLine($"Testing large log: {logPath}");
        
        // Read the file
        var sw = Stopwatch.StartNew();
        var content = File.ReadAllText(logPath);
        var readTime = sw.ElapsedMilliseconds;
        _output.WriteLine($"File read in {readTime}ms - Size: {content.Length:N0} bytes");
        
        // Parse the log
        sw.Restart();
        var analysis = _parser.ParseLog(content, "apex-07LWH00000OGWxV2AX.log");
        var parseTime = sw.ElapsedMilliseconds;
        
        // Output results
        _output.WriteLine($"Parsing completed in {parseTime}ms");
        _output.WriteLine($"Log Name: {analysis.LogName}");
        _output.WriteLine($"Line Count: {analysis.LineCount:N0}");
        _output.WriteLine($"Duration: {analysis.DurationMs}ms");
        _output.WriteLine($"Summary: {analysis.Summary}");
        _output.WriteLine($"Has Errors: {analysis.HasErrors}");
        _output.WriteLine($"Error Count: {analysis.Errors?.Count ?? 0}");
        _output.WriteLine($"DB Operations: {analysis.DatabaseOperations?.Count ?? 0}");
        _output.WriteLine($"Limit Snapshots: {analysis.LimitSnapshots?.Count ?? 0}");
        
        // Assertions
        Assert.NotNull(analysis);
        Assert.Equal("apex-07LWH00000OGWxV2AX.log", analysis.LogName);
        Assert.True(analysis.LineCount > 100000, "Should have parsed 100k+ lines");
        Assert.True(parseTime < 60000, "Parsing should complete within 60 seconds");
        Assert.NotNull(analysis.Summary);
        Assert.False(string.IsNullOrEmpty(analysis.Summary));
    }

    [Fact]
    public void ParseLog_SampleLog_FullAnalysisReport()
    {
        // Test with the sample log in the SampleLogs folder
        var logPath = Path.Combine(_sampleLogsPath, "apex-07LWH00000OOgrV2AT.log");
        
        if (!File.Exists(logPath))
        {
            _output.WriteLine($"SKIP: Sample log not found at: {logPath}");
            return;
        }

        var fileInfo = new FileInfo(logPath);
        _output.WriteLine($"=== PARSING SAMPLE LOG ===");
        _output.WriteLine($"File: {fileInfo.Name}");
        _output.WriteLine($"Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");

        // Phase 1: Read file
        var sw = Stopwatch.StartNew();
        var content = File.ReadAllText(logPath);
        var readMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"File read: {readMs}ms ({content.Length:N0} chars)");

        // Phase 2: Parse
        sw.Restart();
        var a = _parser.ParseLog(content, fileInfo.Name);
        var parseMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"Parse time: {parseMs}ms");

        // === RESULTS ===
        _output.WriteLine($"\n=== SUMMARY ===");
        _output.WriteLine($"Summary: {a.Summary}");
        _output.WriteLine($"Line Count: {a.LineCount:N0}");
        _output.WriteLine($"Duration: {a.DurationMs:N0}ms ({a.DurationMs / 1000.0:F1}s)");
        _output.WriteLine($"Transaction Failed: {a.TransactionFailed}");
        _output.WriteLine($"Has Errors: {a.HasErrors}");
        _output.WriteLine($"Entry Point: {a.EntryPoint}");
        _output.WriteLine($"Is Test Execution: {a.IsTestExecution}");
        _output.WriteLine($"Log User: {a.LogUser}");
        _output.WriteLine($"CPU Time: {a.CpuTimeMs}ms");
        _output.WriteLine($"Wall Clock: {a.WallClockMs:F0}ms");

        _output.WriteLine($"\n=== ERRORS & EXCEPTIONS ===");
        _output.WriteLine($"Errors: {a.Errors?.Count ?? 0}");
        if (a.Errors != null)
            foreach (var e in a.Errors.Take(5))
                _output.WriteLine($"  ❌ {e}");
        _output.WriteLine($"Handled Exceptions: {a.HandledExceptions?.Count ?? 0}");
        if (a.HandledExceptions != null)
            foreach (var e in a.HandledExceptions.Take(5))
                _output.WriteLine($"  ⚠️ {e}");

        _output.WriteLine($"\n=== DATABASE OPERATIONS ===");
        _output.WriteLine($"Total DB Operations: {a.DatabaseOperations?.Count ?? 0}");
        if (a.DatabaseOperations != null)
        {
            var soql = a.DatabaseOperations.Where(d => d.OperationType == "SOQL").ToList();
            var sosl = a.DatabaseOperations.Where(d => d.OperationType == "SOSL").ToList();
            var dml = a.DatabaseOperations.Where(d => d.OperationType == "DML").ToList();
            _output.WriteLine($"  SOQL queries: {soql.Count}");
            _output.WriteLine($"  SOSL searches: {sosl.Count}");
            _output.WriteLine($"  DML operations: {dml.Count}");
            _output.WriteLine($"  Top 5 SOQL by rows:");
            foreach (var q in soql.OrderByDescending(q => q.RowsAffected).Take(5))
                _output.WriteLine($"    [{q.RowsAffected} rows] {q.Query?.Substring(0, Math.Min(q.Query.Length, 80))}");
            if (sosl.Count > 0)
            {
                _output.WriteLine($"  SOSL searches:");
                foreach (var q in sosl.Take(5))
                    _output.WriteLine($"    [{q.RowsAffected} rows] {q.Query?.Substring(0, Math.Min(q.Query.Length, 80))}");
            }
            _output.WriteLine($"  Top 5 DML:");
            foreach (var d in dml.Take(5))
                _output.WriteLine($"    [{d.DmlOperation}] {d.ObjectType} ({d.RowsAffected} rows)");
        }
        
        // Callouts
        if (a.Callouts != null && a.Callouts.Count > 0)
        {
            _output.WriteLine($"\n=== CALLOUTS ({a.Callouts.Count}) ===");
            foreach (var c in a.Callouts)
                _output.WriteLine($"  [{c.HttpMethod}] {c.Endpoint} → {c.StatusCode} {c.StatusMessage} ({c.DurationMs}ms){(c.IsError ? " ⚠️ ERROR" : "")}");
        }
        
        // Flows
        if (a.Flows != null && a.Flows.Count > 0)
        {
            _output.WriteLine($"\n=== FLOWS ({a.Flows.Count}) ===");
            foreach (var f in a.Flows)
                _output.WriteLine($"  🔄 {f.FlowName} ({f.ElementCount} elements){(f.HasFault ? $" ⚠️ FAULT: {f.FaultMessage}" : "")}");
        }

        _output.WriteLine($"\n=== GOVERNOR LIMITS ===");
        _output.WriteLine($"Limit Snapshots: {a.LimitSnapshots?.Count ?? 0}");
        if (a.LimitSnapshots != null && a.LimitSnapshots.Count > 0)
        {
            var last = a.LimitSnapshots.Last();
            _output.WriteLine($"  SOQL Queries: {last.SoqlQueries}/{last.SoqlQueriesLimit} ({(last.SoqlQueriesLimit > 0 ? last.SoqlQueries * 100.0 / last.SoqlQueriesLimit : 0):F0}%)  {(last.SoqlQueriesLimit > 0 && last.SoqlQueries * 100.0 / last.SoqlQueriesLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  SOSL Queries: {last.SoslQueries}/{last.SoslQueriesLimit}");
            _output.WriteLine($"  Query Rows: {last.QueryRows}/{last.QueryRowsLimit} ({(last.QueryRowsLimit > 0 ? last.QueryRows * 100.0 / last.QueryRowsLimit : 0):F0}%)  {(last.QueryRowsLimit > 0 && last.QueryRows * 100.0 / last.QueryRowsLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  CPU Time: {last.CpuTime}/{last.CpuTimeLimit}ms ({(last.CpuTimeLimit > 0 ? last.CpuTime * 100.0 / last.CpuTimeLimit : 0):F0}%)  {(last.CpuTimeLimit > 0 && last.CpuTime * 100.0 / last.CpuTimeLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  Heap Size: {last.HeapSize}/{last.HeapSizeLimit} ({(last.HeapSizeLimit > 0 ? last.HeapSize * 100.0 / last.HeapSizeLimit : 0):F0}%)  {(last.HeapSizeLimit > 0 && last.HeapSize * 100.0 / last.HeapSizeLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  DML Statements: {last.DmlStatements}/{last.DmlStatementsLimit} ({(last.DmlStatementsLimit > 0 ? last.DmlStatements * 100.0 / last.DmlStatementsLimit : 0):F0}%)  {(last.DmlStatementsLimit > 0 && last.DmlStatements * 100.0 / last.DmlStatementsLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  DML Rows: {last.DmlRows}/{last.DmlRowsLimit} ({(last.DmlRowsLimit > 0 ? last.DmlRows * 100.0 / last.DmlRowsLimit : 0):F0}%)  {(last.DmlRowsLimit > 0 && last.DmlRows * 100.0 / last.DmlRowsLimit > 80 ? "⚠️ HIGH" : "✅")}");
            _output.WriteLine($"  Future Calls: {last.FutureCalls}/{last.FutureCallsLimit}");
            _output.WriteLine($"  Callouts: {last.Callouts}/{last.CalloutsLimit}");
        }
        
        // Namespace limits
        if (a.NamespaceLimitSnapshots != null && a.NamespaceLimitSnapshots.Count > 0)
        {
            _output.WriteLine($"\n=== NAMESPACE LIMITS ({a.NamespaceLimitSnapshots.Count} packages) ===");
            foreach (var ns in a.NamespaceLimitSnapshots)
                _output.WriteLine($"  [{ns.Namespace}] SOQL: {ns.SoqlQueries}/{ns.SoqlQueriesLimit}, Query Rows: {ns.QueryRows}, DML: {ns.DmlStatements}, CPU: {ns.CpuTime}ms");
        }
        
        // Testing limits
        if (a.TestingLimits != null)
        {
            var tl = a.TestingLimits;
            _output.WriteLine($"\n=== TESTING LIMITS (test code only) ===");
            _output.WriteLine($"  SOQL: {tl.SoqlQueries}/{tl.SoqlQueriesLimit}, Query Rows: {tl.QueryRows}/{tl.QueryRowsLimit}, DML: {tl.DmlStatements}/{tl.DmlStatementsLimit}, CPU: {tl.CpuTime}/{tl.CpuTimeLimit}ms");
        }

        // Execution plans
        if (a.DatabaseOperations != null)
        {
            var opsWithPlans = a.DatabaseOperations.Where(d => d.ExecutionPlan != null).ToList();
            if (opsWithPlans.Count > 0)
            {
                _output.WriteLine($"\n=== QUERY EXECUTION PLANS ({opsWithPlans.Count} queries have plans) ===");
                var tableScans = opsWithPlans.Where(d => d.ExecutionPlan!.Contains("TableScan")).ToList();
                if (tableScans.Count > 0)
                    _output.WriteLine($"  ⚠️ Table Scans: {tableScans.Count} (expensive full-table scans detected)");
                foreach (var op in opsWithPlans.OrderByDescending(d => d.RelativeCost).Take(5))
                    _output.WriteLine($"  [cost: {op.RelativeCost:F2}] {op.ExecutionPlan}");
            }
        }

        _output.WriteLine($"\n=== EXECUTION TREE ===");
        if (a.RootNode != null)
        {
            _output.WriteLine($"Root children: {a.RootNode.Children?.Count ?? 0}");
            if (a.RootNode.Children != null)
                foreach (var child in a.RootNode.Children.Take(10))
                    _output.WriteLine($"  [{child.DurationMs:F0}ms] {child.Name}");
        }

        _output.WriteLine($"\n=== ISSUES ===");
        if (a.Issues != null)
        {
            _output.WriteLine($"Total issues: {a.Issues.Count}");
            foreach (var issue in a.Issues.Take(10))
                _output.WriteLine($"  ⚠️ {issue}");
        }

        _output.WriteLine($"\n=== CUMULATIVE PROFILING ===");
        if (a.CumulativeProfiling != null)
        {
            _output.WriteLine($"  Top Queries ({a.CumulativeProfiling.TopQueries.Count}):");
            foreach (var q in a.CumulativeProfiling.TopQueries.Take(10))
                _output.WriteLine($"    [{q.ExecutionCount}x, {q.TotalDurationMs}ms] {q.Location}: {q.Query?.Substring(0, Math.Min(q.Query.Length, 80))}");
            _output.WriteLine($"  Top DML ({a.CumulativeProfiling.TopDmlOperations.Count}):");
            foreach (var d in a.CumulativeProfiling.TopDmlOperations.Take(10))
                _output.WriteLine($"    [{d.ExecutionCount}x, {d.TotalDurationMs}ms] {d.Location}: {d.OperationDescription}");
            _output.WriteLine($"  Top Methods ({a.CumulativeProfiling.TopMethods.Count}):");
            foreach (var m in a.CumulativeProfiling.TopMethods.Take(10))
                _output.WriteLine($"    [{m.ExecutionCount}x, {m.TotalDurationMs}ms, avg {m.AverageDurationMs}ms] {m.Location}");
        }
        else
        {
            _output.WriteLine($"  (No cumulative profiling data found)");
        }

        _output.WriteLine($"\n=== METHOD STATS ===");
        if (a.MethodStats != null && a.MethodStats.Count > 0)
        {
            _output.WriteLine($"Total tracked methods: {a.MethodStats.Count}");
            foreach (var m in a.MethodStats.OrderByDescending(m => m.Value.TotalDurationMs).Take(10))
                _output.WriteLine($"    [{m.Value.CallCount}x, {m.Value.TotalDurationMs}ms total, avg {m.Value.AverageDurationMs}ms] {m.Key}");
        }

        _output.WriteLine($"\n=== RECOMMENDATIONS ===");
        if (a.Recommendations != null)
        {
            _output.WriteLine($"Total recommendations: {a.Recommendations.Count}");
            foreach (var r in a.Recommendations.Take(10))
                _output.WriteLine($"  💡 {r}");
        }

        // Assertions
        Assert.NotNull(a);
        Assert.True(a.LineCount > 100000, $"Expected 100k+ lines, got {a.LineCount}");
        Assert.True(parseMs < 120000, $"Parse took {parseMs}ms - too slow for 18MB log");
        Assert.NotNull(a.Summary);
        Assert.True(a.DatabaseOperations?.Count > 0, "Should detect DB operations");
    }

    [Fact]
    public void ParseLog_AllSampleLogs_BatchReport()
    {
        // Parse ALL .log files in SampleLogs folder and produce a comparison report
        var logFiles = Directory.GetFiles(_sampleLogsPath, "*.log")
            .Where(f => !Path.GetFileName(f).StartsWith("simple_") && !Path.GetFileName(f).StartsWith("error_"))
            .OrderBy(f => new FileInfo(f).Length)
            .ToList();

        _output.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        _output.WriteLine($"║  BLACK WIDOW BATCH LOG ANALYSIS - {logFiles.Count} LOGS                    ║");
        _output.WriteLine($"╚══════════════════════════════════════════════════════════════╝\n");

        var totalSw = Stopwatch.StartNew();
        int passed = 0, failed = 0;
        var results = new System.Collections.Generic.List<(string name, long parseMs, string summary)>();

        foreach (var logFile in logFiles)
        {
            var fi = new FileInfo(logFile);
            var name = fi.Name;
            var sizeMb = fi.Length / 1024.0 / 1024.0;

            _output.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _output.WriteLine($"📄 {name} ({sizeMb:F2} MB)");
            _output.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            try
            {
                var content = File.ReadAllText(logFile);
                var sw = Stopwatch.StartNew();
                var a = _parser.ParseLog(content, name);
                var parseMs = sw.ElapsedMilliseconds;

                // Core info
                _output.WriteLine($"  ⏱️  Parse: {parseMs}ms | Lines: {a.LineCount:N0} | Duration: {a.DurationMs:N0}ms ({a.DurationMs / 1000.0:F1}s)");
                _output.WriteLine($"  👤 User: {a.LogUser ?? "N/A"}");
                _output.WriteLine($"  🎯 Entry: {a.EntryPoint ?? "N/A"}");
                _output.WriteLine($"  {(a.TransactionFailed ? "❌ FAILED" : "✅ Success")} | Test: {(a.IsTestExecution ? "Yes" : "No")}");

                // Errors
                if (a.Errors != null && a.Errors.Count > 0)
                {
                    _output.WriteLine($"  🚨 Errors ({a.Errors.Count}):");
                    foreach (var e in a.Errors.Take(3))
                        _output.WriteLine($"     {e.Name.Substring(0, Math.Min(e.Name.Length, 120))}");
                }
                if (a.HandledExceptions != null && a.HandledExceptions.Count > 0)
                    _output.WriteLine($"  ⚠️  Handled exceptions: {a.HandledExceptions.Count}");

                // DB operations
                if (a.DatabaseOperations != null && a.DatabaseOperations.Count > 0)
                {
                    var soql = a.DatabaseOperations.Count(d => d.OperationType == "SOQL");
                    var sosl = a.DatabaseOperations.Count(d => d.OperationType == "SOSL");
                    var dml = a.DatabaseOperations.Count(d => d.OperationType == "DML");
                    var plans = a.DatabaseOperations.Count(d => d.ExecutionPlan != null);
                    var scans = a.DatabaseOperations.Count(d => d.ExecutionPlan != null && d.ExecutionPlan.Contains("TableScan"));
                    _output.WriteLine($"  💾 DB: {soql} SOQL, {sosl} SOSL, {dml} DML" +
                        (plans > 0 ? $" | Plans: {plans}" : "") +
                        (scans > 0 ? $" | ⚠️ {scans} table scans" : ""));
                    
                    // Top expensive query
                    var topQuery = a.DatabaseOperations
                        .Where(d => d.OperationType == "SOQL" && d.RowsAffected > 0)
                        .OrderByDescending(d => d.RowsAffected)
                        .FirstOrDefault();
                    if (topQuery != null)
                        _output.WriteLine($"     Top query: [{topQuery.RowsAffected} rows] {topQuery.Query?.Substring(0, Math.Min(topQuery.Query.Length, 80))}");
                }
                else
                {
                    _output.WriteLine($"  💾 DB: No operations detected");
                }

                // Callouts
                if (a.Callouts != null && a.Callouts.Count > 0)
                {
                    _output.WriteLine($"  📡 Callouts: {a.Callouts.Count}");
                    foreach (var c in a.Callouts.Take(3))
                        _output.WriteLine($"     [{c.HttpMethod}] {c.Endpoint?.Substring(0, Math.Min(c.Endpoint.Length, 60))} → {c.StatusCode}{(c.IsError ? " ⚠️" : "")}");
                }

                // Flows
                if (a.Flows != null && a.Flows.Count > 0)
                {
                    _output.WriteLine($"  🔄 Flows: {a.Flows.Count}");
                    foreach (var f in a.Flows.Take(3))
                        _output.WriteLine($"     {f.FlowName} ({f.ElementCount} elements){(f.HasFault ? " ⚠️ FAULT" : "")}");
                    if (a.Flows.Count > 3)
                        _output.WriteLine($"     ...and {a.Flows.Count - 3} more");
                }

                // Governor limits
                if (a.LimitSnapshots != null && a.LimitSnapshots.Count > 0)
                {
                    var last = a.LimitSnapshots.Last();
                    var soqlPct = last.SoqlQueriesLimit > 0 ? last.SoqlQueries * 100.0 / last.SoqlQueriesLimit : 0;
                    var rowsPct = last.QueryRowsLimit > 0 ? last.QueryRows * 100.0 / last.QueryRowsLimit : 0;
                    var cpuPct = last.CpuTimeLimit > 0 ? last.CpuTime * 100.0 / last.CpuTimeLimit : 0;
                    var dmlPct = last.DmlStatementsLimit > 0 ? last.DmlStatements * 100.0 / last.DmlStatementsLimit : 0;
                    
                    var alerts = new System.Collections.Generic.List<string>();
                    if (soqlPct > 80) alerts.Add($"SOQL {soqlPct:F0}%");
                    if (rowsPct > 80) alerts.Add($"Rows {rowsPct:F0}%");
                    if (cpuPct > 80) alerts.Add($"CPU {cpuPct:F0}%");
                    if (dmlPct > 80) alerts.Add($"DML {dmlPct:F0}%");
                    
                    _output.WriteLine($"  📊 Limits: SOQL {last.SoqlQueries}/{last.SoqlQueriesLimit} ({soqlPct:F0}%), Rows {last.QueryRows:N0}/{last.QueryRowsLimit:N0} ({rowsPct:F0}%), CPU {last.CpuTime}/{last.CpuTimeLimit}ms ({cpuPct:F0}%), DML {last.DmlStatements}/{last.DmlStatementsLimit} ({dmlPct:F0}%)");
                    if (alerts.Count > 0)
                        _output.WriteLine($"     🚨 HIGH USAGE: {string.Join(", ", alerts)}");
                }

                // Namespace limits
                if (a.NamespaceLimitSnapshots != null && a.NamespaceLimitSnapshots.Count > 0)
                    _output.WriteLine($"  📦 Packages: {string.Join(", ", a.NamespaceLimitSnapshots.Select(ns => $"{ns.Namespace}({ns.SoqlQueries}q)"))}");

                // Execution tree
                if (a.RootNode?.Children != null && a.RootNode.Children.Count > 0)
                {
                    _output.WriteLine($"  🌳 Tree: {a.RootNode.Children.Count} root nodes");
                    foreach (var child in a.RootNode.Children.Take(5))
                        _output.WriteLine($"     [{child.DurationMs:F0}ms] {child.Name?.Substring(0, Math.Min(child.Name.Length, 80))}");
                }

                // Issues & recommendations
                _output.WriteLine($"  🔍 Issues: {a.Issues?.Count ?? 0} | Recommendations: {a.Recommendations?.Count ?? 0}");
                if (a.Issues != null)
                    foreach (var issue in a.Issues.Take(3))
                        _output.WriteLine($"     {issue.Substring(0, Math.Min(issue.Length, 120))}");

                // Method stats summary
                if (a.MethodStats != null && a.MethodStats.Count > 0)
                    _output.WriteLine($"  📈 Methods tracked: {a.MethodStats.Count}");

                // Cumulative profiling
                if (a.CumulativeProfiling != null)
                    _output.WriteLine($"  📊 Profiling: {a.CumulativeProfiling.TopQueries.Count} queries, {a.CumulativeProfiling.TopDmlOperations.Count} DML, {a.CumulativeProfiling.TopMethods.Count} methods");

                // Summary text (first 200 chars)
                _output.WriteLine($"  📋 Summary: {a.Summary?.Substring(0, Math.Min(a.Summary.Length, 200))}...");

                results.Add((name, parseMs, a.TransactionFailed ? "FAILED" : "OK"));
                passed++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  💥 PARSE ERROR: {ex.Message}");
                _output.WriteLine($"     {ex.StackTrace?.Split('\n').FirstOrDefault()}");
                results.Add((name, -1, $"ERROR: {ex.Message}"));
                failed++;
            }
            _output.WriteLine("");
        }

        totalSw.Stop();

        // Summary table
        _output.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        _output.WriteLine($"║  BATCH RESULTS: {passed} passed, {failed} failed, {totalSw.ElapsedMilliseconds}ms total       ║");
        _output.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
        _output.WriteLine($"{"File",-45} {"Parse",-8} {"Status",-10}");
        _output.WriteLine(new string('─', 65));
        foreach (var r in results)
            _output.WriteLine($"{r.name,-45} {(r.parseMs >= 0 ? $"{r.parseMs}ms" : "ERR"),-8} {r.summary,-10}");

        Assert.Equal(0, failed);
        Assert.True(passed > 0, "Should have parsed at least one log");
    }

    [Fact]
    public async Task ViewModel_LoadLogFromPath_UpdatesSelectedLog()
    {
        // This test verifies the ViewModel correctly processes a log file
        // without needing to launch the actual UI
        var logPath = @"C:\Users\felis\Downloads\apex-07LWH00000OGWxV2AX.log";
        
        if (!File.Exists(logPath))
        {
            _output.WriteLine($"SKIP: Test log file not found at: {logPath}");
            return;
        }

        // Create ViewModel with real services (no mocks needed for this test)
        var parserService = new LogParserService();
        var apiService = new SalesforceApiService();
        var oauthService = new OAuthService();
        
        var viewModel = new MainViewModel(apiService, parserService, oauthService);
        
        _output.WriteLine($"Loading log file: {logPath}");
        var stopwatch = Stopwatch.StartNew();
        
        // Act - call LoadLogFromPath (this is what drag-drop calls)
        await viewModel.LoadLogFromPath(logPath);
        
        stopwatch.Stop();
        _output.WriteLine($"ViewModel load completed in {stopwatch.ElapsedMilliseconds}ms");
        
        // Assert - verify the log was loaded correctly
        Assert.NotNull(viewModel.SelectedLog);
        Assert.Single(viewModel.Logs);
        Assert.Equal("apex-07LWH00000OGWxV2AX.log", viewModel.SelectedLog?.LogName);
        Assert.True(viewModel.SelectedLog?.LineCount > 100000, "Should have parsed 100k+ lines");
        Assert.NotEmpty(viewModel.SummaryText);
        
        _output.WriteLine($"Summary Text: {viewModel.SummaryText}");
        _output.WriteLine($"Status Message: {viewModel.StatusMessage}");
        _output.WriteLine($"Selected Log Lines: {viewModel.SelectedLog?.LineCount:N0}");
    }
}