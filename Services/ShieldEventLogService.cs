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
    /// </summary>
    internal List<ShieldEvent> ParseCsv(string csvContent, string eventType)
    {
        var events = new List<ShieldEvent>();
        var lines = csvContent.Split('\n');
        if (lines.Length < 2) return events;

        // Trim trailing \r from lines (handles \r\n line endings)
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');

        // Parse header
        var headers = ParseCsvLine(lines[0]);
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            headerIndex[headers[i].Trim('"').Trim()] = i;

        // Parse data rows
        for (int row = 1; row < lines.Length; row++)
        {
            var line = lines[row].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line);
            try
            {
                var ev = new ShieldEvent
                {
                    OrgId = _dbService.OrgId,
                    EventType = eventType,
                    EventDate = GetField(fields, headerIndex, "TIMESTAMP_DERIVED")
                                ?? GetField(fields, headerIndex, "TIMESTAMP") ?? DateTime.UtcNow.ToString("O"),
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

                // For Login events, capture extra details
                if (eventType == "Login")
                {
                    var loginType = GetField(fields, headerIndex, "LOGIN_TYPE");
                    var platform = GetField(fields, headerIndex, "PLATFORM");
                    var browser = GetField(fields, headerIndex, "BROWSER_TYPE");
                    if (loginType != null || platform != null || browser != null)
                    {
                        ev.ExtraJson = JsonConvert.SerializeObject(new
                        {
                            loginType,
                            platform,
                            browser,
                            loginStatus = GetField(fields, headerIndex, "LOGIN_STATUS")
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

    private static string? GetField(string[] fields, Dictionary<string, int> headerIndex, string columnName)
    {
        if (!headerIndex.TryGetValue(columnName, out var index) || index >= fields.Length)
            return null;
        var value = fields[index].Trim().Trim('"');
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
