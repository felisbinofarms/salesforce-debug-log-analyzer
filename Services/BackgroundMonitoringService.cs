using System.Collections.Concurrent;
using Serilog;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Orchestrates background log monitoring on an independent polling loop.
/// Downloads new debug logs, parses them, persists snapshots, and fires alert events.
/// Completely independent from the live streaming service (SalesforceCliService).
/// </summary>
public class BackgroundMonitoringService : IDisposable
{
    private readonly SalesforceApiService _apiService;
    private readonly LogParserService _parserService;
    private readonly SettingsService _settingsService;
    private MonitoringDatabaseService? _dbService;
    private TrendAnalysisService? _trendService;
    private ToastNotificationService? _toastService;
    private ShieldEventLogService? _shieldService;
    private ShieldAnomalyDetector? _shieldDetector;

    private System.Threading.Timer? _pollTimer;
    private System.Threading.Timer? _analysisTimer;
    private System.Threading.Timer? _shieldTimer;
    private readonly ConcurrentDictionary<string, byte> _processedLogIds = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private bool _isRunning;
    private bool _disposed;
    private int _logsProcessedTotal;
    private DateTime _lastPollTime;

    public bool IsRunning => _isRunning;
    public int LogsProcessedTotal => _logsProcessedTotal;
    public DateTime LastPollTime => _lastPollTime;
    public MonitoringDatabaseService? DatabaseService => _dbService;

    /// <summary>Fired when a new monitoring alert is generated.</summary>
    public event EventHandler<MonitoringAlert>? AlertGenerated;

    /// <summary>Fired when monitoring status changes (started, stopped, error).</summary>
    public event EventHandler<string>? StatusChanged;

    public BackgroundMonitoringService(
        SalesforceApiService apiService,
        LogParserService parserService,
        SettingsService settingsService)
    {
        _apiService = apiService;
        _parserService = parserService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Start the background monitoring loop.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning) return;
        if (!_apiService.IsConnected || _apiService.Connection == null)
        {
            StatusChanged?.Invoke(this, "Cannot start monitoring — not connected to Salesforce");
            return;
        }

        // Initialize database for the connected org
        var orgId = ExtractOrgIdentifier();
        try
        {
            _dbService?.Dispose();
            _dbService = new MonitoringDatabaseService(orgId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize monitoring database");
            StatusChanged?.Invoke(this, $"Database error: {ex.Message}");
            return;
        }

        // Seed the dedup set from recent DB entries to avoid re-processing
        try
        {
            var recentIds = await _dbService.GetRecentLogIdsAsync(500);
            foreach (var id in recentIds)
                _processedLogIds.TryAdd(id, 0);
            Log.Information("Seeded {Count} already-processed log IDs from database", recentIds.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to seed processed log IDs");
        }

        _isRunning = true;
        var interval = _settingsService.Load().MonitoringPollIntervalSeconds;
        _pollTimer = new System.Threading.Timer(
            OnPollTimerElapsed,
            null,
            TimeSpan.FromSeconds(5),  // Initial delay — start quickly
            TimeSpan.FromSeconds(interval));

        // Start trend analysis on a separate timer (every 5 minutes)
        _trendService = new TrendAnalysisService(_dbService);
        _trendService.AlertGenerated += OnAlertGenerated;
        _toastService = new ToastNotificationService(_settingsService);
        _analysisTimer = new System.Threading.Timer(
            OnAnalysisTimerElapsed,
            null,
            TimeSpan.FromMinutes(1),   // First analysis after 1 minute
            TimeSpan.FromMinutes(5));

        Log.Information("Background monitoring started (interval: {Interval}s)", interval);
        StatusChanged?.Invoke(this, "Monitoring active");

        // Probe for Shield and start if available
        _ = InitializeShieldAsync();
    }

    /// <summary>
    /// Probes for Shield EventLogFile availability and starts the Shield polling timer if available.
    /// </summary>
    private async Task InitializeShieldAsync()
    {
        if (_dbService == null) return;

        try
        {
            _shieldService = new ShieldEventLogService(_apiService, _dbService);
            var shieldAvailable = await _shieldService.ProbeShieldAvailabilityAsync();

            if (shieldAvailable)
            {
                _shieldDetector = new ShieldAnomalyDetector(_dbService);
                _shieldDetector.AlertGenerated += OnAlertGenerated;
                await _shieldDetector.SeedKnownIpsAsync();

                _shieldTimer = new System.Threading.Timer(
                    OnShieldTimerElapsed,
                    null,
                    TimeSpan.FromMinutes(2),   // First Shield poll after 2 minutes
                    TimeSpan.FromMinutes(15));  // Then every 15 minutes

                Log.Information("Shield EventLogFile monitoring enabled");
                StatusChanged?.Invoke(this, "Shield monitoring enabled");
            }
            else
            {
                Log.Information("Shield not available — monitoring debug logs only");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize Shield monitoring");
        }
    }

    /// <summary>
    /// Stop the background monitoring loop.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _pollTimer?.Dispose();
        _pollTimer = null;
        _analysisTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _analysisTimer?.Dispose();
        _analysisTimer = null;
        _shieldTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _shieldTimer?.Dispose();
        _shieldTimer = null;
        _isRunning = false;

        Log.Information("Background monitoring stopped");
        StatusChanged?.Invoke(this, "Monitoring paused");
    }

    /// <summary>
    /// Called by the live streaming pipeline to register that a streamed log has already been
    /// persisted, so the background poller skips it.
    /// </summary>
    public void MarkLogProcessed(string logId)
    {
        _processedLogIds.TryAdd(logId, 0);
    }

    private async void OnPollTimerElapsed(object? state)
    {
        if (!_pollLock.Wait(0)) return; // Skip if previous poll still running
        try
        {
            await PollAndProcessLogsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Monitoring poll cycle failed");
            StatusChanged?.Invoke(this, $"Poll error: {ex.Message}");
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private async void OnAnalysisTimerElapsed(object? state)
    {
        if (_trendService == null) return;
        try
        {
            await _trendService.RunAnalysisCycleAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Trend analysis cycle failed");
        }
    }

    private async void OnShieldTimerElapsed(object? state)
    {
        if (_shieldService == null || _shieldDetector == null) return;
        try
        {
            var eventsProcessed = await _shieldService.PollAndProcessAsync();
            if (eventsProcessed > 0)
            {
                // Run anomaly detection after processing new Shield events
                await _shieldDetector.RunDetectionAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shield monitoring cycle failed");
        }
    }

    private void OnAlertGenerated(object? sender, MonitoringAlert alert)
    {
        // Fire toast notification
        _toastService?.ShowAlert(alert);
        // Relay to subscribers (e.g., MainViewModel for in-app alert center)
        AlertGenerated?.Invoke(this, alert);
    }

    private async Task PollAndProcessLogsAsync()
    {
        if (!_apiService.IsConnected || _dbService == null) return;

        // Refresh token if needed
        var authOk = await _apiService.EnsureAuthenticatedAsync();
        if (!authOk)
        {
            Log.Warning("Authentication check failed during monitoring poll");
            StatusChanged?.Invoke(this, "Authentication expired — reconnect to Salesforce");
            return;
        }

        // Query recent logs from Salesforce
        List<ApexLog> recentLogs;
        try
        {
            recentLogs = await _apiService.QueryLogsAsync(50);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to query logs during monitoring poll");
            return;
        }

        var newLogsCount = 0;
        foreach (var log in recentLogs)
        {
            if (_processedLogIds.ContainsKey(log.Id)) continue;

            try
            {
                // Check DB as well (covers logs persisted from streaming)
                if (await _dbService.IsLogPersistedAsync(log.Id))
                {
                    _processedLogIds.TryAdd(log.Id, 0);
                    continue;
                }

                // Download the log body
                var logBody = await _apiService.GetLogBodyAsync(log.Id);
                if (string.IsNullOrEmpty(logBody))
                {
                    _processedLogIds.TryAdd(log.Id, 0);
                    continue;
                }

                // Parse on background thread
                var analysis = await Task.Run(() => _parserService.ParseLog(logBody, log.Id));

                // Create and persist snapshot
                var snapshot = LogSnapshot.FromAnalysis(analysis, _dbService.OrgId);
                snapshot.LogId = log.Id; // Use the real Salesforce log ID
                snapshot.CapturedAt = log.StartTime;
                await _dbService.InsertSnapshotAsync(snapshot);

                _processedLogIds.TryAdd(log.Id, 0);
                newLogsCount++;
                _logsProcessedTotal++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to process log {LogId} during monitoring", log.Id);
                _processedLogIds.TryAdd(log.Id, 0); // Don't retry failed logs
            }
        }

        _lastPollTime = DateTime.UtcNow;

        if (newLogsCount > 0)
        {
            Log.Information("Monitoring poll: processed {NewLogs} new logs ({Total} total)",
                newLogsCount, _logsProcessedTotal);
            StatusChanged?.Invoke(this, $"Processed {newLogsCount} new logs");
        }
    }

    private string ExtractOrgIdentifier()
    {
        // Prefer using OrgId (globally unique) when available
        var orgId = _apiService.Connection?.OrgId;
        if (!string.IsNullOrEmpty(orgId))
            return orgId;

        var instanceUrl = _apiService.Connection?.InstanceUrl ?? "";
        if (instanceUrl.Contains(".my.salesforce.com"))
        {
            var start = instanceUrl.IndexOf("//") + 2;
            var end = instanceUrl.IndexOf(".my.salesforce.com");
            if (end > start) return instanceUrl[start..end];
        }
        else if (instanceUrl.Contains(".sandbox.salesforce.com"))
        {
            // Extract subdomain to differentiate sandboxes (e.g., "cs42" from "cs42.sandbox.salesforce.com")
            var start = instanceUrl.IndexOf("//") + 2;
            var end = instanceUrl.IndexOf(".sandbox.salesforce.com");
            if (end > start) return $"sandbox-{instanceUrl[start..end]}";
        }
        return "unknown";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _dbService?.Dispose();
        _pollLock.Dispose();
    }
}
