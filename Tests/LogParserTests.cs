using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.ViewModels;
using System.Diagnostics;
using System.IO;
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