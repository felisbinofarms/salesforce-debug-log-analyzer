using System.Text.RegularExpressions;
using System.IO;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for quickly extracting metadata from debug log files without full parsing
/// </summary>
public class LogMetadataExtractor
{
    private static readonly Regex UserInfoPattern = new(@"USER_INFO\|\[EXTERNAL\]\|(\w+)\|([^|]+)\|", RegexOptions.Compiled);
    private static readonly Regex CodeUnitPattern = new(@"CODE_UNIT_STARTED\|\[EXTERNAL\]\|(.+)$", RegexOptions.Compiled);
    private static readonly Regex ExecutionStartPattern = new(@"(\d{2}:\d{2}:\d{2}\.\d+)\s+\(\d+\)\|EXECUTION_STARTED", RegexOptions.Compiled);
    private static readonly Regex ExecutionFinishPattern = new(@"(\d{2}:\d{2}:\d{2}\.\d+)\s+\(\d+\)\|EXECUTION_FINISHED", RegexOptions.Compiled);
    private static readonly Regex LimitPattern = new(@"Number of (\w+(?:\s+\w+)*?):\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex CpuPattern = new(@"Maximum CPU time:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex HeapPattern = new(@"Maximum heap size:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ExceptionPattern = new(@"EXCEPTION_THROWN|FATAL_ERROR", RegexOptions.Compiled);
    private static readonly Regex RecordIdPattern = new(@"\b([a-zA-Z0-9]{15}|[a-zA-Z0-9]{18})\b", RegexOptions.Compiled);

    /// <summary>
    /// Extract metadata from a log file without full parsing
    /// </summary>
    public DebugLogMetadata ExtractMetadata(string logFilePath)
    {
        var metadata = new DebugLogMetadata
        {
            FilePath = logFilePath,
            LogId = Path.GetFileNameWithoutExtension(logFilePath).Replace("apex-", "")
        };

        try
        {
            // Read only the first 5000 and last 1000 lines for performance
            var lines = File.ReadAllLines(logFilePath);
            var headerLines = lines.Take(5000).ToList();
            var footerLines = lines.Skip(Math.Max(0, lines.Length - 1000)).ToList();
            var allSampleLines = headerLines.Concat(footerLines).ToList();

            // Extract user info
            foreach (var line in headerLines.Take(50))
            {
                var userMatch = UserInfoPattern.Match(line);
                if (userMatch.Success)
                {
                    metadata.UserId = userMatch.Groups[1].Value;
                    metadata.UserName = userMatch.Groups[2].Value;
                    break;
                }
            }

            // Extract execution timing
            DateTime? startTime = null;
            DateTime? endTime = null;

            foreach (var line in headerLines.Take(100))
            {
                var startMatch = ExecutionStartPattern.Match(line);
                if (startMatch.Success)
                {
                    startTime = ParseTimestamp(startMatch.Groups[1].Value);
                    metadata.Timestamp = startTime.Value;
                    break;
                }
            }

            foreach (var line in footerLines)
            {
                var endMatch = ExecutionFinishPattern.Match(line);
                if (endMatch.Success)
                {
                    endTime = ParseTimestamp(endMatch.Groups[1].Value);
                    break;
                }
            }

            if (startTime.HasValue && endTime.HasValue)
            {
                var duration = (endTime.Value - startTime.Value).TotalMilliseconds;
                // Guard against midnight-crossing logs where end timestamp wraps to next day
                if (duration < 0) duration += TimeSpan.FromDays(1).TotalMilliseconds;
                metadata.DurationMs = duration;
            }

            // Extract code unit (method/class name)
            foreach (var line in headerLines.Take(200))
            {
                var codeUnitMatch = CodeUnitPattern.Match(line);
                if (codeUnitMatch.Success)
                {
                    var codeUnit = codeUnitMatch.Groups[1].Value;
                    metadata.CodeUnitName = codeUnit;

                    // Extract method name from code unit
                    if (codeUnit.Contains("."))
                    {
                        var parts = codeUnit.Split('.');
                        if (parts.Length > 1)
                        {
                            metadata.MethodName = parts[parts.Length - 1];
                        }
                    }
                    else
                    {
                        metadata.MethodName = codeUnit;
                    }
                    break;
                }
            }

            // Extract governor limits from footer
            foreach (var line in footerLines)
            {
                if (line.Contains("LIMIT_USAGE_FOR_NS"))
                {
                    // Found the limits section, parse the next several lines
                    var limitIndex = footerLines.IndexOf(line);
                    var limitLines = footerLines.Skip(limitIndex).Take(20).ToList();

                    foreach (var limitLine in limitLines)
                    {
                        if (limitLine.Contains("SOQL queries"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.SoqlQueries = int.Parse(match.Groups[1].Value);
                        }
                        else if (limitLine.Contains("query rows"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.QueryRows = int.Parse(match.Groups[1].Value);
                        }
                        else if (limitLine.Contains("DML statements"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.DmlStatements = int.Parse(match.Groups[1].Value);
                        }
                        else if (limitLine.Contains("DML rows"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.DmlRows = int.Parse(match.Groups[1].Value);
                        }
                        else if (limitLine.Contains("CPU time"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.CpuTime = int.Parse(match.Groups[1].Value);
                        }
                        else if (limitLine.Contains("heap size"))
                        {
                            var match = Regex.Match(limitLine, @"(\d+)\s+out of");
                            if (match.Success) metadata.HeapSize = int.Parse(match.Groups[1].Value);
                        }
                    }
                    break;
                }
            }

            // Check for errors/exceptions
            foreach (var line in allSampleLines)
            {
                if (ExceptionPattern.IsMatch(line))
                {
                    metadata.HasErrors = true;
                    break;
                }
            }

            // Try to extract record ID
            foreach (var line in headerLines.Take(500))
            {
                if (line.Contains("CODE_UNIT_STARTED") || line.Contains("USER_DEBUG"))
                {
                    var recordMatch = RecordIdPattern.Match(line);
                    if (recordMatch.Success)
                    {
                        var id = recordMatch.Value;
                        // Validate it looks like a common Salesforce record ID
                        if (IsValidRecordId(id))
                        {
                            metadata.RecordId = id;
                            break;
                        }
                    }
                }
            }

            // If no timestamp was found, use file creation time
            if (metadata.Timestamp == DateTime.MinValue)
            {
                var fileInfo = new FileInfo(logFilePath);
                metadata.Timestamp = fileInfo.CreationTime;
            }

            // Detect execution context
            metadata.Context = DetectExecutionContext(headerLines, metadata.CodeUnitName, metadata.MethodName);
        }
        catch (Exception ex)
        {
            // Log error but don't fail - return partial metadata
            metadata.MethodName = $"Error reading log: {ex.Message}";
        }

        return metadata;
    }

    /// <summary>
    /// Extract metadata from multiple log files
    /// </summary>
    public List<DebugLogMetadata> ExtractMetadataFromDirectory(string directoryPath)
    {
        var metadata = new List<DebugLogMetadata>();

        if (!Directory.Exists(directoryPath))
        {
            return metadata;
        }

        // Search recursively; collect .log, .txt, and no-extension files that look like SF logs
        var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
        
        System.Diagnostics.Debug.WriteLine($"[LogMetadataExtractor] Scanning folder: {directoryPath}");
        System.Diagnostics.Debug.WriteLine($"[LogMetadataExtractor] Total files found: {allFiles.Length}");

        var logFiles = allFiles.Where(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var fileName = Path.GetFileName(f);
            
            if (ext == ".log" || ext == ".txt")
            {
                System.Diagnostics.Debug.WriteLine($"  ✓ Accepted: {fileName} (extension: {ext})");
                return true;
            }
            
            // No extension: accept if filename looks like a Salesforce log ID (07L...)
            // or if it starts with the numeric debug log prefix
            if (ext == "")
            {
                var name = Path.GetFileNameWithoutExtension(f);
                bool matches = name.StartsWith("07L", StringComparison.OrdinalIgnoreCase) || LooksLikeSalesforceLog(f);
                if (matches)
                    System.Diagnostics.Debug.WriteLine($"  ✓ Accepted: {fileName} (no extension, SF log pattern)");
                else
                    System.Diagnostics.Debug.WriteLine($"  ✗ Rejected: {fileName} (no extension, not SF pattern)");
                return matches;
            }
            
            System.Diagnostics.Debug.WriteLine($"  ✗ Rejected: {fileName} (extension: {ext})");
            return false;
        }).ToList();

        System.Diagnostics.Debug.WriteLine($"[LogMetadataExtractor] Accepted files: {logFiles.Count}");

        foreach (var logFile in logFiles)
        {
            var meta = ExtractMetadata(logFile);
            metadata.Add(meta);
        }

        return metadata.OrderBy(m => m.Timestamp).ToList();
    }

    /// <summary>
    /// Quick peek at a file (first 3 lines) to see if it looks like a Salesforce debug log.
    /// Used for extension-less files.
    /// </summary>
    private static bool LooksLikeSalesforceLog(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            for (int i = 0; i < 3; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (line.Contains("APEX_CODE") || line.Contains("Execute Anonymous") ||
                    line.Contains("USER_INFO") || line.Contains("EXECUTION_STARTED"))
                    return true;
            }
        }
        catch { /* ignore unreadable files */ }
        return false;
    }

    private DateTime ParseTimestamp(string timestamp)
    {
        if (TimeSpan.TryParse(timestamp, out var timeSpan))
        {
            return DateTime.Today.Add(timeSpan);
        }
        return DateTime.MinValue;
    }

    private bool IsValidRecordId(string id)
    {
        if (id.Length != 15 && id.Length != 18)
            return false;

        // Common Salesforce object prefixes
        var validPrefixes = new[]
        {
            "001", "003", "005", "006", "00Q", "00D", "00G", "00O", "00X",
            "500", "501", "800", "801",
            "01t", "01p", "01I", "01J", "01N", "01Z",
            "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9"
        };

        return validPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detect execution context (Interactive, Batch, Integration, etc.)
    /// </summary>
    private Models.ExecutionContext DetectExecutionContext(List<string> headerLines, string codeUnitName, string methodName)
    {
        var combinedText = string.Join(" ", headerLines.Take(500)).ToLower();

        // Check for Batch Apex
        if (combinedText.Contains("batchable") || 
            combinedText.Contains("database.batchable") ||
            codeUnitName.Contains("Batch", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("execute") && codeUnitName.Contains("Batch"))
        {
            return Models.ExecutionContext.Batch;
        }

        // Check for API/Integration (REST, SOAP, Connected App)
        if (combinedText.Contains("/services/apexrest/") ||
            combinedText.Contains("/services/soap/") ||
            combinedText.Contains("restcontext") ||
            combinedText.Contains("soaptype") ||
            combinedText.Contains("connected app") ||
            codeUnitName.StartsWith("REST:", StringComparison.OrdinalIgnoreCase))
        {
            return Models.ExecutionContext.Integration;
        }

        // Check for Scheduled Apex/Flow
        if (combinedText.Contains("schedulable") ||
            combinedText.Contains("scheduled flow") ||
            combinedText.Contains("time-based workflow") ||
            codeUnitName.Contains("Schedule", StringComparison.OrdinalIgnoreCase))
        {
            return Models.ExecutionContext.Scheduled;
        }

        // Check for Async operations
        if (combinedText.Contains("@future") ||
            combinedText.Contains("queueable") ||
            combinedText.Contains("platform event") ||
            methodName.Contains("@future"))
        {
            return Models.ExecutionContext.Async;
        }

        // Check for Lightning/Aura components (Interactive)
        if (combinedText.Contains("aura") ||
            combinedText.Contains("lightning") ||
            combinedText.Contains("@auraenabled") ||
            codeUnitName.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
            codeUnitName.Contains("LWC", StringComparison.OrdinalIgnoreCase))
        {
            return Models.ExecutionContext.Interactive;
        }

        // Check for triggers (could be interactive or async)
        if (codeUnitName.Contains("Trigger", StringComparison.OrdinalIgnoreCase))
        {
            // If trigger name contains batch/schedule keywords, classify accordingly
            if (codeUnitName.Contains("Batch") || codeUnitName.Contains("Schedule"))
            {
                return Models.ExecutionContext.Async;
            }
            // Otherwise assume interactive (user-initiated DML)
            return Models.ExecutionContext.Interactive;
        }

        // Default to Unknown if we can't determine
        return Models.ExecutionContext.Unknown;
    }
}
