using SalesforceDebugAnalyzer.Services;
using System.Diagnostics;

var logPath = @"C:\Users\felis\Downloads\apex-07LWH00000OGWxV2AX.log";

Console.WriteLine($"Testing parser with: {logPath}");
Console.WriteLine($"File exists: {File.Exists(logPath)}");

if (!File.Exists(logPath))
{
    Console.WriteLine("ERROR: File not found!");
    return;
}

var content = File.ReadAllText(logPath);
Console.WriteLine($"File size: {content.Length:N0} bytes");
Console.WriteLine($"Lines: {content.Split('\n').Length:N0}");

var parser = new LogParserService();
var sw = Stopwatch.StartNew();
var analysis = parser.ParseLog(content, "apex-07LWH00000OGWxV2AX.log");
sw.Stop();

Console.WriteLine();
Console.WriteLine($"=== PARSER RESULTS ===");
Console.WriteLine($"Parsing took: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Log Name: {analysis.LogName}");
Console.WriteLine($"Line Count: {analysis.LineCount}");
Console.WriteLine($"Duration: {analysis.DurationMs}ms");
Console.WriteLine($"Summary: {analysis.Summary}");
Console.WriteLine($"Has Errors: {analysis.HasErrors}");
Console.WriteLine($"Error Count: {analysis.Errors?.Count ?? 0}");
Console.WriteLine($"DB Operations: {analysis.DatabaseOperations?.Count ?? 0}");
Console.WriteLine($"Limit Snapshots: {analysis.LimitSnapshots?.Count ?? 0}");
Console.WriteLine();
Console.WriteLine("SUCCESS: Parser completed without errors!");
