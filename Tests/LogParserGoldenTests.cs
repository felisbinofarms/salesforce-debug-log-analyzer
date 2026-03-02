using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Models;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Golden-file regression tests. Each test asserts EXACT metric values for a known log.
/// These tests act as a regression fence: if a parser change shifts any metric, the
/// relevant test will fail immediately, telling you exactly what changed and by how much.
///
/// DO NOT loosen assertions here to make a failing test pass. Instead, investigate
/// whether the change is correct and update the expected value with a comment explaining why.
/// </summary>
public class LogParserGoldenTests
{
    private readonly LogParserService _parser;
    private readonly string _sampleLogsPath;
    private readonly ITestOutputHelper _output;

    public LogParserGoldenTests(ITestOutputHelper output)
    {
        _parser = new LogParserService();
        _output = output;
        var projectDir = Directory.GetCurrentDirectory();
        _sampleLogsPath = Path.Combine(projectDir, "..", "..", "..", "..", "SampleLogs");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-1: Simple Account Insert
    // A clean, minimal log. No errors, 1 SOQL (record type lookup), 1 DML.
    // Tests: basic metric extraction, nanosecond duration, governor limit parsing.
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_SimpleAccountInsert_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "simple_account_insert.log"));
        var a = _parser.ParseLog(content, "simple_account_insert.log");

        _output.WriteLine($"Duration: {a.DurationMs}ms");
        _output.WriteLine($"SOQL (governor): {a.LimitSnapshots?.LastOrDefault()?.SoqlQueries}");

        // Duration from nanosecond counters: (16600000 - 1000000) / 1,000,000 = 15.6ms
        Assert.Equal(15.6, a.DurationMs, precision: 1);

        // Transaction outcome
        Assert.False(a.TransactionFailed, "Transaction should succeed");
        Assert.False(a.HasErrors, "No unhandled errors");
        Assert.False(a.IsLogTruncated, "Log is complete");
        Assert.False(a.IsTestExecution, "Not a test execution");

        // Governor limits (from CUMULATIVE_LIMIT_USAGE - authoritative)
        Assert.NotNull(a.LimitSnapshots);
        Assert.True(a.LimitSnapshots.Count >= 1, "Should have at least 1 limit snapshot");
        var lim = a.LimitSnapshots.Last();
        Assert.Equal(1, lim.SoqlQueries);
        Assert.Equal(100, lim.SoqlQueriesLimit);
        Assert.Equal(1, lim.DmlStatements);
        Assert.Equal(150, lim.DmlStatementsLimit);
        Assert.Equal(156, lim.CpuTime);
        Assert.Equal(10000, lim.CpuTimeLimit);
        Assert.Equal(3, lim.QueryRows);
        Assert.Equal(50000, lim.QueryRowsLimit);
        Assert.Equal(15, lim.HeapSize);
        Assert.Equal(6000000, lim.HeapSizeLimit);
        Assert.Equal(0, lim.Callouts);

        // Parsed DB events (from SOQL_EXECUTE_BEGIN/DML_BEGIN events)
        Assert.NotNull(a.DatabaseOperations);
        Assert.Equal(1, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
        Assert.Equal(1, a.DatabaseOperations.Count(d => d.OperationType == "DML"));

        // No managed package overhead (governor matches parsed)
        Assert.Equal(0, (a.LimitSnapshots.Last().SoqlQueries - a.DatabaseOperations.Count(d => d.OperationType == "SOQL")));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-2: Validation Failure (Handled Exception)
    // Tests: exception classification (thrown + caught = handled, not a failure),
    // DML that triggers validation rule, zero SOQL.
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_ValidationFailure_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "error_validation_failure.log"));
        var a = _parser.ParseLog(content, "error_validation_failure.log");

        _output.WriteLine($"Duration: {a.DurationMs}ms");
        _output.WriteLine($"TransactionFailed: {a.TransactionFailed}");
        _output.WriteLine($"HandledExceptions: {a.HandledExceptions?.Count}");

        // Duration: (14600000 - 1000000) / 1,000,000 = 13.6ms
        Assert.Equal(13.6, a.DurationMs, precision: 1);

        // Exception was caught in try/catch - transaction should be SUCCESS
        Assert.False(a.TransactionFailed, "Exception was caught - transaction succeeded");
        Assert.False(a.HasErrors, "No unhandled errors");
        Assert.False(a.IsLogTruncated, "Log is complete");

        // The DmlException should be classified as a HANDLED exception
        Assert.NotNull(a.HandledExceptions);
        Assert.True(a.HandledExceptions.Count >= 1, "Should detect 1 handled exception");

        // Governor limits
        var lim = a.LimitSnapshots!.Last();
        Assert.Equal(0, lim.SoqlQueries);        // No SOQL in this log
        Assert.Equal(1, lim.DmlStatements);       // 1 DML attempt (even though validation failed)
        Assert.Equal(45, lim.CpuTime);

        // Parsed events: no SOQL, 1 DML attempt
        Assert.Equal(0, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
        Assert.Equal(1, a.DatabaseOperations.Count(d => d.OperationType == "DML"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-3: N+1 Query Pattern
    // Contact trigger with 6 identical SOQL queries (one per record in loop).
    // Tests: duplicate query detection, per-query grouping, N+1 flagging.
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_N1QueryPattern_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "golden_n1_trigger.log"));
        var a = _parser.ParseLog(content, "golden_n1_trigger.log");

        _output.WriteLine($"Duration: {a.DurationMs}ms");
        _output.WriteLine($"SOQL (governor): {a.LimitSnapshots?.LastOrDefault()?.SoqlQueries}");
        _output.WriteLine($"SOQL (parsed events): {a.DatabaseOperations?.Count(d => d.OperationType == "SOQL")}");
        _output.WriteLine($"Duplicate query groups: {a.DuplicateQueries?.Count}");

        // Duration: (22000000 - 1000000) / 1,000,000 = 21.0ms
        Assert.Equal(21.0, a.DurationMs, precision: 1);

        // Transaction outcome
        Assert.False(a.TransactionFailed);
        Assert.False(a.HasErrors);
        Assert.False(a.IsLogTruncated);

        // Governor limits
        var lim = a.LimitSnapshots!.Last();
        Assert.Equal(6, lim.SoqlQueries);
        Assert.Equal(100, lim.SoqlQueriesLimit);
        Assert.Equal(0, lim.DmlStatements);
        Assert.Equal(320, lim.CpuTime);
        Assert.Equal(6, lim.QueryRows);

        // Parsed events: 6 SOQL, 0 DML (no discrepancy → no hidden queries)
        Assert.Equal(6, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
        Assert.Equal(0, a.DatabaseOperations.Count(d => d.OperationType == "DML"));

        // N+1 detection: 6 identical queries should be grouped as duplicates
        Assert.NotNull(a.DuplicateQueries);
        Assert.True(a.DuplicateQueries.Count >= 1, "Should detect at least 1 duplicate query group");
        var topDupe = a.DuplicateQueries.OrderByDescending(d => d.ExecutionCount).First();
        Assert.True(topDupe.ExecutionCount >= 6, $"Top duplicate should have 6+ executions, got {topDupe.ExecutionCount}");
        Assert.Contains("Account", topDupe.ExampleQuery ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-4: Managed Package Namespace Limits
    // Log with a 'crm' managed package consuming SOQL alongside user code.
    // Governor total = 11 SOQL (default ns), but user only wrote 1 SOQL visible.
    // Tests: namespace limit isolation, managed package overhead calculation.
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_ManagedPackageLimits_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "golden_managed_packages.log"));
        var a = _parser.ParseLog(content, "golden_managed_packages.log");

        _output.WriteLine($"Default NS SOQL: {a.LimitSnapshots?.LastOrDefault()?.SoqlQueries}");
        _output.WriteLine($"Namespace snapshots: {a.NamespaceLimitSnapshots?.Count}");
        _output.WriteLine($"Parsed SOQL events: {a.DatabaseOperations?.Count(d => d.OperationType == "SOQL")}");

        // Duration: (10000000 - 1000000) / 1,000,000 = 9.0ms
        Assert.Equal(9.0, a.DurationMs, precision: 1);
        Assert.False(a.TransactionFailed);

        // Default namespace governor limits
        var lim = a.LimitSnapshots!.Last();
        Assert.Equal(11, lim.SoqlQueries);   // Total org SOQL (your code + managed package)
        Assert.Equal(3, lim.DmlStatements);
        Assert.Equal(875, lim.CpuTime);

        // Managed package namespace limits must be parsed separately
        Assert.NotNull(a.NamespaceLimitSnapshots);
        Assert.True(a.NamespaceLimitSnapshots.Count >= 1, "Should detect at least 1 managed package namespace");
        var crmNs = a.NamespaceLimitSnapshots.FirstOrDefault(n => n.Namespace == "crm");
        Assert.NotNull(crmNs);
        Assert.Equal(10, crmNs!.SoqlQueries);
        Assert.Equal(2, crmNs.DmlStatements);

        // Parsed events: 1 SOQL visible in log (the OpportunityTrigger query)
        // Governor (11) > parsed (1) → 10 queries hidden in managed package
        Assert.Equal(1, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
        var hiddenSoql = lim.SoqlQueries - a.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        Assert.Equal(10, hiddenSoql); // 10 queries hidden (from crm package)
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-5: HTTP Callout
    // Two callouts: POST 200 (success) and GET 404 (error).
    // Tests: callout parsing, status code detection, duration calculation with
    //        large nanosecond values (callout adds 1 second of real time).
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_Callout_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "golden_callout.log"));
        var a = _parser.ParseLog(content, "golden_callout.log");

        _output.WriteLine($"Duration: {a.DurationMs}ms");
        _output.WriteLine($"Callouts parsed: {a.Callouts?.Count}");
        _output.WriteLine($"Callout 1 status: {a.Callouts?.FirstOrDefault()?.StatusCode}");

        // Duration: (1315000000 - 1000000) / 1,000,000 = 1314.0ms (includes callout wait time)
        Assert.Equal(1314.0, a.DurationMs, precision: 1);
        Assert.False(a.TransactionFailed);

        // Governor limits
        var lim = a.LimitSnapshots!.Last();
        Assert.Equal(0, lim.SoqlQueries);
        Assert.Equal(2, lim.Callouts);
        Assert.Equal(100, lim.CalloutsLimit);
        Assert.Equal(450, lim.CpuTime);

        // Parsed callout events
        Assert.NotNull(a.Callouts);
        Assert.Equal(2, a.Callouts.Count);

        var firstCallout = a.Callouts.FirstOrDefault(c => c.StatusCode == 200);
        Assert.NotNull(firstCallout);
        Assert.Contains("contacts", firstCallout!.Endpoint ?? "");
        Assert.False(firstCallout.IsError);

        var secondCallout = a.Callouts.FirstOrDefault(c => c.StatusCode == 404);
        Assert.NotNull(secondCallout);
        Assert.Contains("accounts", secondCallout!.Endpoint ?? "");
        Assert.True(secondCallout.IsError, "404 response should be flagged as error");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-6: Truncated Log
    // Log ends mid-execution without CUMULATIVE_LIMIT_USAGE section.
    // Tests: truncation detection, graceful handling of incomplete data.
    //
    // The parser PRESERVES partial data visible before the truncation point:
    // - SOQL with a matching SOQL_EXECUTE_END pair IS captured (count = 1)
    // - DML with only DML_BEGIN and no DML_END is NOT captured (count = 0)
    // - Governor limits are unavailable (CUMULATIVE_LIMIT_USAGE never reached)
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_TruncatedLog_DetectedCorrectly()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "golden_truncated.log"));
        var a = _parser.ParseLog(content, "golden_truncated.log");

        _output.WriteLine($"IsLogTruncated: {a.IsLogTruncated}");
        _output.WriteLine($"LimitSnapshots: {a.LimitSnapshots?.Count}");
        _output.WriteLine($"DatabaseOperations: {a.DatabaseOperations?.Count}");

        // The most critical assertion: truncation must be detected
        Assert.True(a.IsLogTruncated, "Log ends without CUMULATIVE_LIMIT_USAGE - must be flagged as truncated");

        // Parser returns gracefully — no exceptions
        Assert.NotNull(a);
        Assert.NotNull(a.DatabaseOperations);
        Assert.NotNull(a.LimitSnapshots);

        // No governor limit data (CUMULATIVE_LIMIT_USAGE never reached in this log)
        Assert.Empty(a.LimitSnapshots);

        // Partial data IS preserved: SOQL with complete BEGIN/END pair = 1
        Assert.Equal(1, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
        // DML without DML_END is NOT added to operations (stack never flushed)
        Assert.Equal(0, a.DatabaseOperations.Count(d => d.OperationType == "DML"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GOLDEN-7: Custom Metadata (CMDT) Query Split
    // 3 SOQL events total: 2 from __mdt objects (free, don't count against limits)
    // and 1 regular SOQL. Governor shows 1 because CMDT is free.
    // Tests: CMDT detection, RegularSoqlCount vs CustomMetadataQueryCount split.
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Golden_CmdtSplit_ExactMetrics()
    {
        var content = File.ReadAllText(Path.Combine(_sampleLogsPath, "golden_cmdt_queries.log"));
        var a = _parser.ParseLog(content, "golden_cmdt_queries.log");

        _output.WriteLine($"Total SOQL events parsed: {a.DatabaseOperations?.Count(d => d.OperationType == "SOQL")}");
        _output.WriteLine($"Regular SOQL count: {a.RegularSoqlCount}");
        _output.WriteLine($"CMDT query count: {a.CustomMetadataQueryCount}");
        _output.WriteLine($"Governor SOQL: {a.LimitSnapshots?.LastOrDefault()?.SoqlQueries}");

        // Duration: (14000000 - 1000000) / 1,000,000 = 13.0ms
        Assert.Equal(13.0, a.DurationMs, precision: 1);
        Assert.False(a.TransactionFailed);

        // Governor says 1 SOQL (CMDT queries are free, not counted)
        var lim = a.LimitSnapshots!.Last();
        Assert.Equal(1, lim.SoqlQueries); // Only the Account query counts against governor

        // Parser should identify 3 total SOQL events
        Assert.Equal(3, a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));

        // CMDT split: 2 __mdt queries are free, 1 regular
        Assert.Equal(1, a.RegularSoqlCount);
        Assert.Equal(2, a.CustomMetadataQueryCount);

        // Consistency check: regular + cmdt = total parsed events
        Assert.Equal(a.RegularSoqlCount + a.CustomMetadataQueryCount,
            a.DatabaseOperations.Count(d => d.OperationType == "SOQL"));
    }
}
