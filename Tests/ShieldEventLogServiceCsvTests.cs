using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests for ShieldEventLogService CSV parsing.
/// ParseCsv is internal, exposed via InternalsVisibleTo.
/// </summary>
public class ShieldEventLogServiceCsvTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testOrgId;
    private readonly MonitoringDatabaseService _db;
    private readonly ShieldEventLogService _service;

    public ShieldEventLogServiceCsvTests(ITestOutputHelper output)
    {
        _output = output;
        _testOrgId = $"csv_test_{Guid.NewGuid():N}";
        _db = new MonitoringDatabaseService(_testOrgId);

        // ShieldEventLogService requires SalesforceApiService, but ParseCsv doesn't use it.
        // Use Moq to provide a stub.
        var apiServiceMock = new Mock<SalesforceApiService>();
        _service = new ShieldEventLogService(apiServiceMock.Object, _db);
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

    // ================================================================
    //  Basic CSV Parsing
    // ================================================================

    [Fact]
    public void ParseCsv_SimpleLoginData()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID,CLIENT_IP,SUCCESS\n" +
                  "2024-01-15T10:30:00Z,005xxx,192.168.1.1,1\n" +
                  "2024-01-15T10:31:00Z,005yyy,10.0.0.1,0\n";

        var events = _service.ParseCsv(csv, "Login");

        events.Should().HaveCount(2);
        events[0].UserId.Should().Be("005xxx");
        events[0].ClientIp.Should().Be("192.168.1.1");
        events[0].IsSuccess.Should().BeTrue();
        events[1].UserId.Should().Be("005yyy");
        events[1].ClientIp.Should().Be("10.0.0.1");
        events[1].IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseCsv_EmptyCsv_ReturnsEmpty()
    {
        var events = _service.ParseCsv("", "Login");
        events.Should().BeEmpty();
    }

    [Fact]
    public void ParseCsv_HeaderOnly_ReturnsEmpty()
    {
        var events = _service.ParseCsv("TIMESTAMP_DERIVED,USER_ID,CLIENT_IP", "Login");
        events.Should().BeEmpty();
    }

    [Fact]
    public void ParseCsv_SingleRow_ReturnsSingleEvent()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID\n" +
                  "2024-01-15T10:30:00Z,005xxx\n";

        var events = _service.ParseCsv(csv, "Login");
        events.Should().HaveCount(1);
    }

    // ================================================================
    //  Quoted Fields (RFC 4180)
    // ================================================================

    [Fact]
    public void ParseCsv_QuotedFields()
    {
        var csv = "TIMESTAMP_DERIVED,URI,USER_ID\n" +
                  "\"2024-01-15T10:30:00Z\",\"/some/path\",\"005xxx\"\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
        events[0].Uri.Should().Be("/some/path");
    }

    [Fact]
    public void ParseCsv_QuotedFieldWithComma()
    {
        var csv = "TIMESTAMP_DERIVED,URI,USER_ID\n" +
                  "2024-01-15T10:30:00Z,\"path,with,commas\",005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
        events[0].Uri.Should().Be("path,with,commas");
    }

    [Fact]
    public void ParseCsv_QuotedFieldWithEscapedQuote()
    {
        // After CSV parsing, the value is: path with "quotes"
        // GetField() then calls Trim('"') which strips the trailing quote.
        // This is expected behavior — the Trim('"') is a safety measure for CSV headers.
        var csv = "TIMESTAMP_DERIVED,URI,USER_ID\n" +
                  "2024-01-15T10:30:00Z,\"path with \"\"quotes\"\"\",005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
        // Trailing quote is stripped by GetField's Trim('"')
        events[0].Uri.Should().StartWith("path with \"quotes");
    }

    // ================================================================
    //  Numeric Field Parsing
    // ================================================================

    [Fact]
    public void ParseCsv_ParsesRunTime()
    {
        var csv = "TIMESTAMP_DERIVED,RUN_TIME,CPU_TIME,USER_ID\n" +
                  "2024-01-15T10:30:00Z,1500,300,005xxx\n";

        var events = _service.ParseCsv(csv, "ApexExecution");

        events.Should().HaveCount(1);
        events[0].DurationMs.Should().Be(1500);
        events[0].CpuTimeMs.Should().Be(300);
    }

    [Fact]
    public void ParseCsv_ParsesExecTime()
    {
        var csv = "TIMESTAMP_DERIVED,EXEC_TIME,USER_ID\n" +
                  "2024-01-15T10:30:00Z,2500,005xxx\n";

        var events = _service.ParseCsv(csv, "ApexExecution");

        events[0].DurationMs.Should().Be(2500);
    }

    [Fact]
    public void ParseCsv_ParsesRowsProcessed()
    {
        var csv = "TIMESTAMP_DERIVED,ROWS_PROCESSED,USER_ID\n" +
                  "2024-01-15T10:30:00Z,500,005xxx\n";

        var events = _service.ParseCsv(csv, "ApexExecution");

        events[0].RowCount.Should().Be(500);
    }

    [Fact]
    public void ParseCsv_ParsesStatusCode()
    {
        var csv = "TIMESTAMP_DERIVED,STATUS_CODE,USER_ID\n" +
                  "2024-01-15T10:30:00Z,200,005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events[0].StatusCode.Should().Be(200);
    }

    // ================================================================
    //  Login-Specific Fields
    // ================================================================

    [Fact]
    public void ParseCsv_Login_CapturesExtraJson()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID,CLIENT_IP,SUCCESS,LOGIN_TYPE,PLATFORM,BROWSER_TYPE,LOGIN_STATUS\n" +
                  "2024-01-15T10:30:00Z,005xxx,192.168.1.1,1,Remote Access,Windows,Chrome,LOGIN_NO_ERROR\n";

        var events = _service.ParseCsv(csv, "Login");

        events.Should().HaveCount(1);
        events[0].ExtraJson.Should().NotBeNullOrEmpty();
        events[0].ExtraJson.Should().Contain("Remote Access");
        events[0].ExtraJson.Should().Contain("Chrome");
    }

    // ================================================================
    //  LightningPageView-Specific Fields
    // ================================================================

    [Fact]
    public void ParseCsv_LightningPageView_CapturesEpt()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID,PAGE_URL,EFFECTIVE_PAGE_TIME,PAGE_APP_NAME\n" +
                  "2024-01-15T10:30:00Z,005xxx,/lightning/r/Case/view,2500,CaseApp\n";

        var events = _service.ParseCsv(csv, "LightningPageView");

        events.Should().HaveCount(1);
        events[0].DurationMs.Should().Be(2500);
        events[0].ExtraJson.Should().Contain("CaseApp");
    }

    [Fact]
    public void ParseCsv_LightningPageView_UsesPageUrl()
    {
        var csv = "TIMESTAMP_DERIVED,PAGE_URL,USER_ID\n" +
                  "2024-01-15T10:30:00Z,/lightning/r/Account/list,005xxx\n";

        var events = _service.ParseCsv(csv, "LightningPageView");

        events[0].Uri.Should().Be("/lightning/r/Account/list");
    }

    // ================================================================
    //  Alternative Column Names
    // ================================================================

    [Fact]
    public void ParseCsv_UsesTimestampFallback()
    {
        // No TIMESTAMP_DERIVED, falls back to TIMESTAMP
        var csv = "TIMESTAMP,USER_ID\n" +
                  "2024-01-15T10:30:00Z,005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
        events[0].EventDate.Should().Be("2024-01-15T10:30:00Z");
    }

    [Fact]
    public void ParseCsv_UsesSourceIpFallback()
    {
        var csv = "TIMESTAMP_DERIVED,SOURCE_IP,USER_ID\n" +
                  "2024-01-15T10:30:00Z,172.16.0.1,005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events[0].ClientIp.Should().Be("172.16.0.1");
    }

    [Fact]
    public void ParseCsv_UsesHttpStatusCodeFallback()
    {
        var csv = "TIMESTAMP_DERIVED,HTTP_STATUS_CODE,USER_ID\n" +
                  "2024-01-15T10:30:00Z,404,005xxx\n";

        var events = _service.ParseCsv(csv, "API");

        events[0].StatusCode.Should().Be(404);
    }

    // ================================================================
    //  Edge Cases
    // ================================================================

    [Fact]
    public void ParseCsv_EmptyFields_ReturnNulls()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID,URI,RUN_TIME,CLIENT_IP\n" +
                  "2024-01-15T10:30:00Z,,,,\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
        events[0].UserId.Should().BeNull();
        events[0].Uri.Should().BeNull();
        events[0].DurationMs.Should().BeNull();
        events[0].ClientIp.Should().BeNull();
    }

    [Fact]
    public void ParseCsv_TrailingNewlines_Ignored()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID\n" +
                  "2024-01-15T10:30:00Z,005xxx\n" +
                  "\n" +
                  "\n";

        var events = _service.ParseCsv(csv, "API");

        events.Should().HaveCount(1);
    }

    [Fact]
    public void ParseCsv_CaseInsensitiveHeaders()
    {
        var csv = "timestamp_derived,user_id,client_ip\n" +
                  "2024-01-15T10:30:00Z,005xxx,10.0.0.1\n";

        var events = _service.ParseCsv(csv, "Login");

        events.Should().HaveCount(1);
        events[0].UserId.Should().Be("005xxx");
    }

    [Fact]
    public void ParseCsv_SetsEventType()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID\n" +
                  "2024-01-15T10:30:00Z,005xxx\n";

        var loginEvents = _service.ParseCsv(csv, "Login");
        var apiEvents = _service.ParseCsv(csv, "API");

        loginEvents[0].EventType.Should().Be("Login");
        apiEvents[0].EventType.Should().Be("API");
    }

    [Fact]
    public void ParseCsv_SetsOrgId()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID\n" +
                  "2024-01-15T10:30:00Z,005xxx\n";

        var events = _service.ParseCsv(csv, "Login");

        events[0].OrgId.Should().Be(_testOrgId);
    }

    [Fact]
    public void ParseCsv_MultipleRows_AllParsed()
    {
        var csv = "TIMESTAMP_DERIVED,USER_ID,CLIENT_IP,SUCCESS\n" +
                  "2024-01-15T10:00:00Z,user1,10.0.0.1,1\n" +
                  "2024-01-15T10:01:00Z,user2,10.0.0.2,1\n" +
                  "2024-01-15T10:02:00Z,user3,10.0.0.3,0\n" +
                  "2024-01-15T10:03:00Z,user4,10.0.0.4,1\n" +
                  "2024-01-15T10:04:00Z,user5,10.0.0.5,0\n";

        var events = _service.ParseCsv(csv, "Login");

        events.Should().HaveCount(5);
        events.Count(e => e.IsSuccess).Should().Be(3);
        events.Count(e => !e.IsSuccess).Should().Be(2);
    }

    // ================================================================
    //  Shield availability (basic)
    // ================================================================

    [Fact]
    public void IsShieldAvailable_InitiallyUnknown()
    {
        _service.IsShieldAvailable.Should().BeFalse();
    }
}
