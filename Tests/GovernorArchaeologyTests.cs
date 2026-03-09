using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

// ======================================================================
//  UNIT TESTS — GovernorArchaeologyRow display helpers
// ======================================================================

/// <summary>
/// Pure-unit tests for GovernorArchaeologyRow computed/display properties.
/// No database or WPF thread required.
/// </summary>
public class GovernorArchaeologyRowDisplayTests
{
    // ----------------------------------------------------------------
    //  SoqlLimitPct
    // ----------------------------------------------------------------

    [Fact]
    public void SoqlLimitPct_CalculatesCorrectly()
    {
        var row = new GovernorArchaeologyRow { AvgSoqlCount = 50, SoqlLimit = 100 };
        row.SoqlLimitPct.Should().BeApproximately(50.0, 0.01);
    }

    [Fact]
    public void SoqlLimitPct_ZeroLimit_ReturnsZero()
    {
        var row = new GovernorArchaeologyRow { AvgSoqlCount = 50, SoqlLimit = 0 };
        row.SoqlLimitPct.Should().Be(0);
    }

    // ----------------------------------------------------------------
    //  SoqlRiskColor
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(85, 100, "#F85149")]  // 85% — red
    [InlineData(80, 100, "#F85149")]  // exactly 80% — red threshold
    [InlineData(60, 100, "#D29922")]  // 60% — yellow
    [InlineData(50, 100, "#D29922")]  // exactly 50% — yellow threshold
    [InlineData(49, 100, "#3FB950")]  // 49% — green
    [InlineData(0,  100, "#3FB950")]  // 0% — green
    public void SoqlRiskColor_ReturnsCorrectColorForPercentage(
        double avgSoql, int limit, string expectedColor)
    {
        var row = new GovernorArchaeologyRow { AvgSoqlCount = avgSoql, SoqlLimit = limit };
        row.SoqlRiskColor.Should().Be(expectedColor);
    }

    [Fact]
    public void SoqlRiskColor_ZeroLimit_ReturnsGreen()
    {
        // Guard against divide-by-zero — 0 pct means green
        var row = new GovernorArchaeologyRow { AvgSoqlCount = 10, SoqlLimit = 0 };
        row.SoqlRiskColor.Should().Be("#3FB950");
    }

    // ----------------------------------------------------------------
    //  AvgCpuDisplay
    // ----------------------------------------------------------------

    [Fact]
    public void AvgCpuDisplay_Shows_Ms_WhenUnder1000()
    {
        var row = new GovernorArchaeologyRow { AvgCpuMs = 450 };
        row.AvgCpuDisplay.Should().Be("450ms");
    }

    [Fact]
    public void AvgCpuDisplay_Shows_Seconds_WhenOver1000()
    {
        var row = new GovernorArchaeologyRow { AvgCpuMs = 2500 };
        row.AvgCpuDisplay.Should().Be("2.5s");
    }

    [Fact]
    public void AvgCpuDisplay_At_Exactly1000_Shows_Seconds()
    {
        var row = new GovernorArchaeologyRow { AvgCpuMs = 1000 };
        row.AvgCpuDisplay.Should().Be("1.0s");
    }

    // ----------------------------------------------------------------
    //  MaxCpuDisplay
    // ----------------------------------------------------------------

    [Fact]
    public void MaxCpuDisplay_Shows_Ms_WhenUnder1000()
    {
        var row = new GovernorArchaeologyRow { MaxCpuMs = 300 };
        row.MaxCpuDisplay.Should().Be("300ms");
    }

    [Fact]
    public void MaxCpuDisplay_Shows_Seconds_WhenOver1000()
    {
        var row = new GovernorArchaeologyRow { MaxCpuMs = 3000 };
        row.MaxCpuDisplay.Should().Be("3.0s");
    }

    // ----------------------------------------------------------------
    //  AvgDurationDisplay
    // ----------------------------------------------------------------

    [Fact]
    public void AvgDurationDisplay_Shows_Ms_WhenUnder1000()
    {
        var row = new GovernorArchaeologyRow { AvgDurationMs = 800 };
        row.AvgDurationDisplay.Should().Be("800ms");
    }

    [Fact]
    public void AvgDurationDisplay_Shows_Seconds_WhenOver1000()
    {
        var row = new GovernorArchaeologyRow { AvgDurationMs = 1500 };
        row.AvgDurationDisplay.Should().Be("1.5s");
    }

    // ----------------------------------------------------------------
    //  AvgDupDisplay
    // ----------------------------------------------------------------

    [Fact]
    public void AvgDupDisplay_Returns_Dash_WhenZero()
    {
        var row = new GovernorArchaeologyRow { AvgDuplicateQueryCount = 0 };
        row.AvgDupDisplay.Should().Be("—");
    }

    [Fact]
    public void AvgDupDisplay_Returns_FormattedValue_WhenPositive()
    {
        var row = new GovernorArchaeologyRow { AvgDuplicateQueryCount = 3.7 };
        row.AvgDupDisplay.Should().Be("3.7");
    }

    // ----------------------------------------------------------------
    //  HasNPlusOne
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(2.5, true)]
    [InlineData(0.9, false)]
    [InlineData(0.0, false)]
    public void HasNPlusOne_ReflectsAvgDuplicateQueryCount(double avgDup, bool expected)
    {
        var row = new GovernorArchaeologyRow { AvgDuplicateQueryCount = avgDup };
        row.HasNPlusOne.Should().Be(expected);
    }

    // ----------------------------------------------------------------
    //  ShortEntryPoint
    // ----------------------------------------------------------------

    [Fact]
    public void ShortEntryPoint_DoesNotTruncate_ShortStrings()
    {
        var row = new GovernorArchaeologyRow { EntryPoint = "CaseTrigger" };
        row.ShortEntryPoint.Should().Be("CaseTrigger");
    }

    [Fact]
    public void ShortEntryPoint_DoesNotTruncate_Exactly60Chars()
    {
        var ep = new string('A', 60);
        var row = new GovernorArchaeologyRow { EntryPoint = ep };
        row.ShortEntryPoint.Should().Be(ep);
    }

    [Fact]
    public void ShortEntryPoint_Truncates_61CharString_WithLeadingEllipsis()
    {
        var ep = new string('A', 61);
        var row = new GovernorArchaeologyRow { EntryPoint = ep };
        row.ShortEntryPoint.Should().StartWith("…");
        row.ShortEntryPoint.Length.Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public void ShortEntryPoint_Truncates_LongStrings_WithLeadingEllipsis()
    {
        var ep = new string('X', 100);
        var row = new GovernorArchaeologyRow { EntryPoint = ep };
        row.ShortEntryPoint.Should().StartWith("…");
        row.ShortEntryPoint.Length.Should().BeLessThanOrEqualTo(60);
    }

    // ----------------------------------------------------------------
    //  AvgSoqlDisplay
    // ----------------------------------------------------------------

    [Fact]
    public void AvgSoqlDisplay_FormatsAsAvgSlashLimit()
    {
        var row = new GovernorArchaeologyRow { AvgSoqlCount = 12.0, SoqlLimit = 100 };
        row.AvgSoqlDisplay.Should().Be("12 / 100");
    }
}

// ======================================================================
//  UNIT TESTS — GovernorArchaeologyData aggregate helpers
// ======================================================================

public class GovernorArchaeologyDataTests
{
    [Fact]
    public void HasSoqlData_False_WhenTopBySoqlIsEmpty()
    {
        var data = new GovernorArchaeologyData();
        data.HasSoqlData.Should().BeFalse();
    }

    [Fact]
    public void HasSoqlData_True_WhenTopBySoqlHasItems()
    {
        var data = new GovernorArchaeologyData();
        data.TopBySoql.Add(new GovernorArchaeologyRow());
        data.HasSoqlData.Should().BeTrue();
    }

    [Fact]
    public void HasCpuData_False_WhenTopByCpuIsEmpty()
    {
        var data = new GovernorArchaeologyData();
        data.HasCpuData.Should().BeFalse();
    }

    [Fact]
    public void HasCpuData_True_WhenTopByCpuHasItems()
    {
        var data = new GovernorArchaeologyData();
        data.TopByCpu.Add(new GovernorArchaeologyRow());
        data.HasCpuData.Should().BeTrue();
    }

    [Fact]
    public void HasNPlusOneData_False_WhenTopByNPlusOneIsEmpty()
    {
        var data = new GovernorArchaeologyData();
        data.HasNPlusOneData.Should().BeFalse();
    }

    [Fact]
    public void HasNPlusOneData_True_WhenTopByNPlusOneHasItems()
    {
        var data = new GovernorArchaeologyData();
        data.TopByNPlusOne.Add(new GovernorArchaeologyRow());
        data.HasNPlusOneData.Should().BeTrue();
    }

    [Fact]
    public void HasAnyData_False_WhenAllListsEmpty()
    {
        var data = new GovernorArchaeologyData();
        data.HasAnyData.Should().BeFalse();
    }

    [Theory]
    [InlineData("soql")]
    [InlineData("cpu")]
    [InlineData("nplus")]
    public void HasAnyData_True_WhenAtLeastOneListHasItems(string which)
    {
        var data = new GovernorArchaeologyData();
        var row = new GovernorArchaeologyRow();
        if (which == "soql") data.TopBySoql.Add(row);
        else if (which == "cpu") data.TopByCpu.Add(row);
        else data.TopByNPlusOne.Add(row);
        data.HasAnyData.Should().BeTrue();
    }
}

// ======================================================================
//  INTEGRATION TESTS — GetGovernorArchaeologyAsync (real SQLite)
// ======================================================================

/// <summary>
/// Integration tests for MonitoringDatabaseService.GetGovernorArchaeologyAsync.
/// Each test gets its own SQLite database in a temp directory.
/// </summary>
public class GovernorArchaeologyDatabaseTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testOrgId;
    private readonly MonitoringDatabaseService _db;

    public GovernorArchaeologyDatabaseTests(ITestOutputHelper output)
    {
        _output = output;
        _testOrgId = $"arch_test_{Guid.NewGuid():N}";
        _db = new MonitoringDatabaseService(_testOrgId);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try
        {
            var dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BlackWidow", "orgs", _testOrgId);
            if (Directory.Exists(dbDir))
                Directory.Delete(dbDir, true);
        }
        catch { }
    }

    // ----------------------------------------------------------------
    //  Empty database
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyDb_Returns_ZeroTotalExecutions()
    {
        var result = await _db.GetGovernorArchaeologyAsync();
        result.TotalExecutions.Should().Be(0);
    }

    [Fact]
    public async Task EmptyDb_Returns_EmptyAllLists()
    {
        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopBySoql.Should().BeEmpty();
        result.TopByCpu.Should().BeEmpty();
        result.TopByNPlusOne.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyDb_HasAnyData_IsFalse()
    {
        var result = await _db.GetGovernorArchaeologyAsync();
        result.HasAnyData.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyDb_DoesNotThrow()
    {
        var act = () => _db.GetGovernorArchaeologyAsync();
        await act.Should().NotThrowAsync();
    }

    // ----------------------------------------------------------------
    //  Single snapshot
    // ----------------------------------------------------------------

    [Fact]
    public async Task SingleSnapshot_TotalExecutions_IsOne()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "CaseTrigger", soql: 20));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TotalExecutions.Should().Be(1);
    }

    [Fact]
    public async Task SingleSnapshot_AppearsInTopBySoql()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "CaseTrigger", soql: 20));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopBySoql.Should().ContainSingle(r => r.EntryPoint == "CaseTrigger");
    }

    [Fact]
    public async Task SingleSnapshot_AppearsInTopByCpu()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "CaseTrigger", cpu: 3000));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByCpu.Should().ContainSingle(r => r.EntryPoint == "CaseTrigger");
    }

    // ----------------------------------------------------------------
    //  Ranking
    // ----------------------------------------------------------------

    [Fact]
    public async Task HigherSoql_RankedFirst_InTopBySoql()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "LowSoqlTrigger", soql: 5));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "HighSoqlTrigger", soql: 80));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopBySoql[0].EntryPoint.Should().Be("HighSoqlTrigger");
    }

    [Fact]
    public async Task HigherCpu_RankedFirst_InTopByCpu()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "FastTrigger", cpu: 100));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "SlowTrigger", cpu: 9000));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByCpu[0].EntryPoint.Should().Be("SlowTrigger");
    }

    // ----------------------------------------------------------------
    //  N+1 filtering
    // ----------------------------------------------------------------

    [Fact]
    public async Task NPlusOne_AppearsInNPlusOneList_WhenAvgDupIsOne()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "BadTrigger", duplicateQueries: 3));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByNPlusOne.Should().ContainSingle(r => r.EntryPoint == "BadTrigger");
    }

    [Fact]
    public async Task ZeroDuplicates_ExcludedFromNPlusOneList()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "CleanTrigger", duplicateQueries: 0));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByNPlusOne.Should().BeEmpty();
    }

    [Fact]
    public async Task NPlusOne_HighestDuplicate_RankedFirst()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "ModerateRepeat", duplicateQueries: 2));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "WorstRepeat", duplicateQueries: 10));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByNPlusOne[0].EntryPoint.Should().Be("WorstRepeat");
    }

    // ----------------------------------------------------------------
    //  Org isolation
    // ----------------------------------------------------------------

    [Fact]
    public async Task FiltersBy_OrgId_OnlyReturnsOwnOrgSnapshots()
    {
        // Insert snapshot for a different org then immediately dispose
        var otherOrgId = $"other_org_{Guid.NewGuid():N}";
        var otherDb = new MonitoringDatabaseService(otherOrgId);
        await otherDb.InsertSnapshotAsync(MakeSnapshot("other-log", "OtherTrigger", orgId: otherOrgId, soql: 90));
        otherDb.Dispose();

        // Deferred directory cleanup — best-effort after dispose
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackWidow", "orgs", otherOrgId);

        // Our org has no snapshots — the other org's data must not bleed through
        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopBySoql.Should().NotContain(r => r.EntryPoint == "OtherTrigger");
        result.TotalExecutions.Should().Be(0);

        try { if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true); } catch { }
    }

    // ----------------------------------------------------------------
    //  Date window filtering
    // ----------------------------------------------------------------

    [Fact]
    public async Task FiltersBy_DateWindow_ExcludesOldSnapshots()
    {
        // Insert an 8-day-old snapshot (outside default 7-day window)
        var old = MakeSnapshot("old-log", "OldTrigger", soql: 50);
        old.CapturedAt = DateTime.UtcNow.AddDays(-8);
        await _db.InsertSnapshotAsync(old);

        var result = await _db.GetGovernorArchaeologyAsync(days: 7);
        result.TotalExecutions.Should().Be(0);
        result.TopBySoql.Should().BeEmpty();
    }

    [Fact]
    public async Task FiltersBy_DateWindow_IncludesRecentSnapshots()
    {
        // Insert a 6-day-old snapshot (inside default 7-day window)
        var recent = MakeSnapshot("recent-log", "RecentTrigger", soql: 50);
        recent.CapturedAt = DateTime.UtcNow.AddDays(-6);
        await _db.InsertSnapshotAsync(recent);

        var result = await _db.GetGovernorArchaeologyAsync(days: 7);
        result.TotalExecutions.Should().Be(1);
        result.TopBySoql.Should().ContainSingle(r => r.EntryPoint == "RecentTrigger");
    }

    [Fact]
    public async Task CustomDayWindow_Respected()
    {
        // "3-day-old" snapshot — inside a 7-day window but outside a 2-day window
        var snap = MakeSnapshot("snap-log", "3DayOldTrigger", soql: 30);
        snap.CapturedAt = DateTime.UtcNow.AddDays(-3);
        await _db.InsertSnapshotAsync(snap);

        var resultWide = await _db.GetGovernorArchaeologyAsync(days: 7);
        var resultNarrow = await _db.GetGovernorArchaeologyAsync(days: 2);

        resultWide.TotalExecutions.Should().Be(1);
        resultNarrow.TotalExecutions.Should().Be(0);
    }

    // ----------------------------------------------------------------
    //  Aggregation across multiple snapshots
    // ----------------------------------------------------------------

    [Fact]
    public async Task MultipleSnapshots_SameEntryPoint_ComputeAvgCorrectly()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "MyTrigger", soql: 10));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "MyTrigger", soql: 20));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-3", "MyTrigger", soql: 30));

        var result = await _db.GetGovernorArchaeologyAsync();
        var row = result.TopBySoql.Single(r => r.EntryPoint == "MyTrigger");
        row.ExecutionCount.Should().Be(3);
        row.AvgSoqlCount.Should().BeApproximately(20.0, 0.5);
    }

    [Fact]
    public async Task MultipleSnapshots_SameEntryPoint_MaxSoqlIsCorrect()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "MyTrigger", soql: 10));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "MyTrigger", soql: 90));

        var result = await _db.GetGovernorArchaeologyAsync();
        var row = result.TopBySoql.Single(r => r.EntryPoint == "MyTrigger");
        row.MaxSoqlCount.Should().Be(90);
    }

    [Fact]
    public async Task MultipleEntryPoints_AllAppearInResults()
    {
        await _db.InsertSnapshotAsync(MakeSnapshot("log-1", "TriggerA", soql: 10));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-2", "TriggerB", soql: 20));
        await _db.InsertSnapshotAsync(MakeSnapshot("log-3", "TriggerC", soql: 30));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TotalExecutions.Should().Be(3);
        result.TopBySoql.Should().HaveCount(3);
    }

    // ----------------------------------------------------------------
    //  Top-10 cap
    // ----------------------------------------------------------------

    [Fact]
    public async Task TopBySoql_CappedAt10_WhenMoreThan10EntryPoints()
    {
        for (int i = 0; i < 15; i++)
            await _db.InsertSnapshotAsync(MakeSnapshot($"log-{i}", $"Trigger{i:D2}", soql: i + 1));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopBySoql.Should().HaveCount(10);
    }

    [Fact]
    public async Task TopByCpu_CappedAt10_WhenMoreThan10EntryPoints()
    {
        for (int i = 0; i < 15; i++)
            await _db.InsertSnapshotAsync(MakeSnapshot($"log-{i}", $"Trigger{i:D2}", cpu: (i + 1) * 100));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByCpu.Should().HaveCount(10);
    }

    [Fact]
    public async Task TopByNPlusOne_CappedAt10_WhenMoreThan10EntryPoints()
    {
        for (int i = 0; i < 15; i++)
            await _db.InsertSnapshotAsync(MakeSnapshot($"log-{i}", $"Trigger{i:D2}", duplicateQueries: i + 2));

        var result = await _db.GetGovernorArchaeologyAsync();
        result.TopByNPlusOne.Should().HaveCount(10);
    }

    // ----------------------------------------------------------------
    //  Error count aggregation
    // ----------------------------------------------------------------

    [Fact]
    public async Task ErrorCount_Aggregated_Correctly()
    {
        var s1 = MakeSnapshot("log-1", "FlakyTrigger");
        s1.HasErrors = true;
        s1.ErrorCount = 1;

        var s2 = MakeSnapshot("log-2", "FlakyTrigger");
        s2.HasErrors = true;
        s2.ErrorCount = 1;

        var s3 = MakeSnapshot("log-3", "FlakyTrigger");
        s3.HasErrors = false;
        s3.ErrorCount = 0;

        await _db.InsertSnapshotAsync(s1);
        await _db.InsertSnapshotAsync(s2);
        await _db.InsertSnapshotAsync(s3);

        var result = await _db.GetGovernorArchaeologyAsync();
        var row = result.TopBySoql.Single(r => r.EntryPoint == "FlakyTrigger");
        row.ErrorCount.Should().Be(2);
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    private LogSnapshot MakeSnapshot(
        string logId,
        string entryPoint,
        string? orgId = null,
        int soql = 5,
        int cpu = 500,
        int duration = 1500,
        int duplicateQueries = 0)
    {
        return new LogSnapshot
        {
            LogId = logId,
            OrgId = orgId ?? _testOrgId,
            CapturedAt = DateTime.UtcNow,
            EntryPoint = entryPoint,
            OperationType = "TRIGGERS",
            LogUser = "testuser@test.com",
            DurationMs = duration,
            CpuTimeMs = cpu,
            SoqlCount = soql,
            SoqlLimit = 100,
            DmlCount = 2,
            DmlLimit = 150,
            QueryRows = 50,
            QueryRowsLimit = 50000,
            HeapSize = 100000,
            HeapLimit = 6000000,
            CalloutCount = 0,
            CalloutLimit = 100,
            HealthScore = 80,
            HealthGrade = "B",
            BulkSafetyGrade = "A",
            HasErrors = false,
            ErrorCount = 0,
            DuplicateQueryCount = duplicateQueries,
            Source = "debug_log"
        };
    }
}
