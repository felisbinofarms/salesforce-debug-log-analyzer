using Newtonsoft.Json;
using Serilog;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Downloads and parses Shield EventLogFile data from Salesforce.
/// Probes for Shield availability, queries new EventLogFiles, downloads CSV,
/// and parses into typed ShieldEvent records.
/// </summary>
public class ShieldEventLogService
{
    private readonly SalesforceApiService _apiService;
    private readonly MonitoringDatabaseService _dbService;
    private bool? _shieldAvailable;

    /// <summary>
    /// Event types we monitor from Shield.
    /// </summary>
    private static readonly string[] MonitoredEventTypes =
    {
        "ApexExecution",
        "API",
        "Login",
        "ReportExport",
        "SetupAuditTrail",
        "BulkApi",
        "LightningPageView",
        "ApexUnexpectedException"
    };

    public bool IsShieldAvailable => _shieldAvailable == true;

    public ShieldEventLogService(SalesforceApiService apiService, MonitoringDatabaseService dbService)
    {
        _apiService = apiService;
        _dbService = dbService;
    }

    /// <summary>
    /// Probes the org to check if Shield EventLogFile is available.
    /// Caches the result so subsequent calls are instant.
    /// </summary>
    public async Task<bool> ProbeShieldAvailabilityAsync()
    {
        if (_shieldAvailable.HasValue) return _shieldAvailable.Value;

        try
        {
            if (!_apiService.IsConnected || _apiService.Connection == null)
            {
                _shieldAvailable = false;
                return false;
            }

            var result = await _apiService.QueryAsync<EventLogFileRecord>(
                "SELECT Id, EventType, LogDate FROM EventLogFile LIMIT 1");

            _shieldAvailable = true;
            Log.Information("Shield EventLogFile is available in this org");
            return true;
        }
        catch
        {
            _shieldAvailable = false;
            Log.Information("Shield EventLogFile not available (expected for non-Shield orgs)");
            return false;
        }
    }

    /// <summary>
    /// Downloads and processes new EventLogFiles for monitored event types.
    /// Returns the total number of new events processed.
    /// </summary>
    public async Task<int> PollAndProcessAsync()
    {
        if (_shieldAvailable != true || !_apiService.IsConnected || _apiService.Connection == null)
            return 0;

        var authOk = await _apiService.EnsureAuthenticatedAsync();
        if (!authOk)
        {
            Log.Warning("Auth check failed during Shield poll");
            return 0;
        }

        var totalEvents = 0;

        foreach (var eventType in MonitoredEventTypes)
        {
            try
            {
                var newEvents = await ProcessEventTypeAsync(eventType);
                totalEvents += newEvents;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to process Shield event type {EventType}", eventType);
            }
        }

        if (totalEvents > 0)
            Log.Information("Shield poll: processed {Count} new events", totalEvents);

        return totalEvents;
    }

    private async Task<int> ProcessEventTypeAsync(string eventType)
    {
        // Query recent EventLogFiles for this type (last 24 hours of hourly logs)
        var query = $"SELECT Id, EventType, LogDate, LogFileLength, Interval " +
                    $"FROM EventLogFile " +
                    $"WHERE EventType = '{eventType}' " +
                    $"AND LogDate = LAST_N_DAYS:1 " +
                    $"AND Interval = 'Hourly' " +
                    $"ORDER BY LogDate DESC LIMIT 24";

        QueryResult<EventLogFileRecord> result;
        try
        {
            result = await _apiService.QueryAsync<EventLogFileRecord>(query);
        }
        catch
        {
            return 0;
        }

        if (result.Records == null || result.Records.Count == 0)
            return 0;

        var totalEvents = 0;

        foreach (var logFile in result.Records)
        {
            // Skip already-processed files
            if (await _dbService.IsLogFileProcessedAsync(logFile.Id))
                continue;

            try
            {
                // Download CSV content
                var csvContent = await DownloadEventLogFileAsync(logFile.Id);
                if (string.IsNullOrEmpty(csvContent))
                    continue;

                // Parse CSV into events
                var events = ParseCsv(csvContent, eventType);

                // Persist events
                await _dbService.InsertShieldEventsAsync(events);

                // Record that we processed this file
                await _dbService.InsertShieldLogFileRecordAsync(new ShieldLogFileRecord
                {
                    OrgId = _dbService.OrgId,
                    EventType = eventType,
                    LogDate = logFile.LogDate,
                    LogFileId = logFile.Id,
                    IntervalType = logFile.Interval,
                    ProcessedAt = DateTime.UtcNow,
                    RecordCount = events.Count,
                    FileSize = logFile.LogFileLength
                });

                totalEvents += events.Count;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to download/parse EventLogFile {Id}", logFile.Id);
            }
        }

        return totalEvents;
    }

    /// <summary>
    /// Downloads the CSV body of an EventLogFile via the REST API.
    /// Reuses the authenticated HttpClient from SalesforceApiService.
    /// </summary>
    private async Task<string?> DownloadEventLogFileAsync(string logFileId)
    {
        if (_apiService.Connection == null) return null;

        var endpoint = $"/services/data/{SalesforceApiService.ApiVersionString}/sobjects/EventLogFile/{logFileId}/LogFile";

        try
        {
            return await _apiService.GetAuthenticatedStringAsync(endpoint);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CSV download failed for EventLogFile {Id}", logFileId);
            return null;
        }
    }

    /// <summary>
    /// Parses RFC 4180 CSV content into Shield events.
    /// Each CSV has a header row followed by data rows.
    /// Handles quoted fields that span multiple lines (e.g., exception stack traces).
    /// </summary>
    internal List<ShieldEvent> ParseCsv(string csvContent, string eventType)
    {
        var events = new List<ShieldEvent>();
        var rows = SplitCsvRows(csvContent);
        if (rows.Count < 2) return events;

        // Parse header
        var headers = ParseCsvLine(rows[0]);
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            headerIndex[headers[i].Trim('"').Trim()] = i;

        // Parse data rows
        for (int row = 1; row < rows.Count; row++)
        {
            var line = rows[row].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line);
            try
            {
                var rawDate = GetField(fields, headerIndex, "TIMESTAMP_DERIVED")
                              ?? GetField(fields, headerIndex, "TIMESTAMP") ?? DateTime.UtcNow.ToString("O");

                var ev = new ShieldEvent
                {
                    OrgId = _dbService.OrgId,
                    EventType = eventType,
                    EventDate = NormalizeEventDate(rawDate),
                    UserId = GetField(fields, headerIndex, "USER_ID"),
                    Uri = GetField(fields, headerIndex, "URI")
                          ?? GetField(fields, headerIndex, "PAGE_URL"),
                    IsSuccess = GetField(fields, headerIndex, "SUCCESS")?.Equals("1", StringComparison.Ordinal) ?? true,
                    ClientIp = GetField(fields, headerIndex, "CLIENT_IP")
                               ?? GetField(fields, headerIndex, "SOURCE_IP"),
                };

                // Extract numeric fields based on event type
                if (double.TryParse(GetField(fields, headerIndex, "RUN_TIME")
                                    ?? GetField(fields, headerIndex, "EXEC_TIME")
                                    ?? GetField(fields, headerIndex, "DURATION"), out var duration))
                    ev.DurationMs = duration;

                if (double.TryParse(GetField(fields, headerIndex, "CPU_TIME"), out var cpuTime))
                    ev.CpuTimeMs = cpuTime;

                if (int.TryParse(GetField(fields, headerIndex, "ROWS_PROCESSED")
                                 ?? GetField(fields, headerIndex, "ROW_COUNT"), out var rowCount))
                    ev.RowCount = rowCount;

                if (int.TryParse(GetField(fields, headerIndex, "STATUS_CODE")
                                 ?? GetField(fields, headerIndex, "HTTP_STATUS_CODE"), out var statusCode))
                    ev.StatusCode = statusCode;

                // For Login events, capture extra details and derive IsSuccess from LOGIN_STATUS
                if (eventType == "Login")
                {
                    var loginType = GetField(fields, headerIndex, "LOGIN_TYPE");
                    var platform = GetField(fields, headerIndex, "PLATFORM");
                    var browser = GetField(fields, headerIndex, "BROWSER_TYPE");
                    var loginStatus = GetField(fields, headerIndex, "LOGIN_STATUS");

                    // Salesforce Login CSVs don't have a "SUCCESS" column — derive from LOGIN_STATUS
                    if (loginStatus != null)
                        ev.IsSuccess = loginStatus.Equals("LOGIN_NO_ERROR", StringComparison.OrdinalIgnoreCase);

                    if (loginType != null || platform != null || browser != null || loginStatus != null)
                    {
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            loginType,
                            platform,
                            browser,
                            loginStatus
                        });
                    }
                }

                // For ApexUnexpectedException, capture exception details
                if (eventType == "ApexUnexpectedException")
                {
                    var exType = GetField(fields, headerIndex, "EXCEPTION_TYPE");
                    var exMessage = GetField(fields, headerIndex, "EXCEPTION_MESSAGE");
                    var exCategory = GetField(fields, headerIndex, "EXCEPTION_CATEGORY");
                    var stackTrace = GetField(fields, headerIndex, "STACK_TRACE");

                    // Use EXCEPTION_TYPE as the grouping key if URI is missing
                    if (string.IsNullOrEmpty(ev.Uri) && !string.IsNullOrEmpty(exType))
                        ev.Uri = exType;

                    if (exType != null || exMessage != null || stackTrace != null)
                    {
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            exceptionType = exType,
                            exceptionMessage = exMessage,
                            exceptionCategory = exCategory,
                            stackTrace
                        });
                    }
                }

                // For LightningPageView, capture EPT
                if (eventType == "LightningPageView")
                {
                    if (double.TryParse(GetField(fields, headerIndex, "EFFECTIVE_PAGE_TIME")
                                        ?? GetField(fields, headerIndex, "PAGE_TIME"), out var ept))
                        ev.DurationMs = ept;

                    var pageName = GetField(fields, headerIndex, "PAGE_APP_NAME");
                    if (pageName != null)
                        ev.ExtraJson = JsonConvert.SerializeObject(new { pageName });
                }

                // For ReportExport, capture export details (data exfiltration detection)
                if (eventType == "ReportExport")
                {
                    var operation = GetField(fields, headerIndex, "OPERATION");
                    var sobjectType = GetField(fields, headerIndex, "SOBJECT_TYPE");
                    var reportId = GetField(fields, headerIndex, "REPORT_ID");
                    var format = GetField(fields, headerIndex, "FORMAT")
                                 ?? GetField(fields, headerIndex, "FILE_TYPE");
                    var rowsExported = GetField(fields, headerIndex, "ROWS_EXPORTED")
                                       ?? GetField(fields, headerIndex, "NUMBER_FIELDS");

                    if (int.TryParse(rowsExported, out var exportedRows))
                        ev.RowCount = exportedRows;

                    ev.Uri = reportId ?? sobjectType ?? "report";
                    if (operation != null || sobjectType != null || reportId != null)
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            operation,
                            sobjectType,
                            reportId,
                            format,
                            rowsExported
                        });
                }

                // For SetupAuditTrail, capture setup/permission changes
                if (eventType == "SetupAuditTrail")
                {
                    var action = GetField(fields, headerIndex, "ACTION");
                    var section = GetField(fields, headerIndex, "SECTION");
                    var display = GetField(fields, headerIndex, "DISPLAY");
                    var delegatedUser = GetField(fields, headerIndex, "DELEGATED_USER_NAME")
                                        ?? GetField(fields, headerIndex, "DELEGATED_USER_ID");

                    ev.Uri = section ?? action ?? "setup";
                    if (action != null || section != null || display != null)
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            action,
                            section,
                            display,
                            delegatedUser
                        });
                }

                // For BulkApi, capture operation details (bulk data movement)
                if (eventType == "BulkApi")
                {
                    var operation = GetField(fields, headerIndex, "OPERATION");
                    var sobjectType = GetField(fields, headerIndex, "SOBJECT_TYPE");
                    var jobId = GetField(fields, headerIndex, "JOB_ID");
                    var rowsProcessed = GetField(fields, headerIndex, "ROWS_PROCESSED")
                                        ?? GetField(fields, headerIndex, "NUMBER_RECORDS_LOADED");

                    if (int.TryParse(rowsProcessed, out var bulkRows))
                        ev.RowCount = bulkRows;

                    ev.Uri = sobjectType ?? operation ?? "bulk";
                    if (operation != null || sobjectType != null || jobId != null)
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            operation,
                            sobjectType,
                            jobId,
                            rowsProcessed
                        });
                }

                events.Add(ev);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse CSV row {Row} for {EventType}", row, eventType);
            }
        }

        return events;
    }

    /// <summary>
    /// Simple RFC 4180 CSV line parser that handles quoted fields.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// Splits CSV content into logical rows, handling quoted fields that span multiple lines.
    /// </summary>
    private static List<string> SplitCsvRows(string csvContent)
    {
        var rows = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csvContent.Length; i++)
        {
            char c = csvContent[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < csvContent.Length && csvContent[i + 1] == '"')
                {
                    current.Append('"');
                    current.Append('"');
                    i++; // Skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                }
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                // End of logical row (skip \r\n as a unit)
                if (c == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                    i++;

                var row = current.ToString().Trim();
                if (!string.IsNullOrEmpty(row))
                    rows.Add(row);
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        // Add last row
        var lastRow = current.ToString().Trim();
        if (!string.IsNullOrEmpty(lastRow))
            rows.Add(lastRow);

        return rows;
    }

    private static string? GetField(string[] fields, Dictionary<string, int> headerIndex, string columnName)
    {
        if (!headerIndex.TryGetValue(columnName, out var index) || index >= fields.Length)
            return null;
        var value = fields[index].Trim().Trim('"');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Normalizes Salesforce event dates to ISO 8601 format.
    /// Salesforce EventLogFiles may use "YYYYMMDDHHmmss.fff" format.
    /// </summary>
    private static string NormalizeEventDate(string rawDate)
    {
        // Already ISO 8601 format (starts with digit and contains '-')
        if (rawDate.Length > 10 && rawDate[4] == '-')
            return rawDate;

        // Salesforce compact format: "20260305020937.259"
        if (rawDate.Length >= 14 && char.IsDigit(rawDate[0]) && !rawDate.Contains('-'))
        {
            if (DateTime.TryParseExact(
                    rawDate.Length > 14 ? rawDate[..18] : rawDate,
                    new[] { "yyyyMMddHHmmss.fff", "yyyyMMddHHmmss" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var dt))
            {
                return dt.ToString("O");
            }
        }

        return rawDate; // Return as-is if unrecognized
    }

    /// <summary>
    /// Repairs existing data in the database:
    /// - Updates Login events that have is_success=1 but LOGIN_STATUS != LOGIN_NO_ERROR in extra_json
    /// - Removes corrupted events with garbled event_date values
    /// </summary>
    public async Task RepairExistingDataAsync()
    {
        try
        {
            // 1. Fix Login IsSuccess from extra_json
            var loginEvents = await _dbService.GetRecentShieldEventsAsync("Login", DateTime.MinValue);
            int fixedCount = 0;
            foreach (var evt in loginEvents)
            {
                if (evt.ExtraJson == null) continue;
                try
                {
                    var extra = Newtonsoft.Json.Linq.JObject.Parse(evt.ExtraJson);
                    var status = extra["loginStatus"]?.ToString();
                    if (status != null && !status.Equals("LOGIN_NO_ERROR", StringComparison.OrdinalIgnoreCase) && evt.IsSuccess)
                    {
                        evt.IsSuccess = false;
                        fixedCount++;
                    }
                }
                catch { /* skip malformed json */ }
            }

            if (fixedCount > 0)
            {
                await _dbService.RepairLoginSuccessAsync(loginEvents.Where(e => !e.IsSuccess).ToList());
                Log.Information("Data repair: fixed IsSuccess for {Count} failed login events", fixedCount);
            }

            // 2. Delete events with garbled event_date (contains "column" or can't be parsed)
            var deletedCount = await _dbService.DeleteCorruptedEventsAsync();
            if (deletedCount > 0)
                Log.Information("Data repair: removed {Count} corrupted shield events", deletedCount);

            // 3. Clear processed shield_log_files to allow re-download of ApexUnexpectedException
            // (these were parsed with broken multi-line CSV handling)
            var clearedCount = await _dbService.ClearProcessedLogFilesAsync("ApexUnexpectedException");
            if (clearedCount > 0)
                Log.Information("Data repair: cleared {Count} ApexUnexpectedException log file records for re-download", clearedCount);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Data repair encountered errors");
        }
    }
}
