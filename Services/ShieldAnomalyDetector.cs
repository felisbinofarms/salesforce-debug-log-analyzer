using Serilog;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Analyzes Shield EventLogFile data for anomalies:
/// - Login anomalies (new IPs, unusual hours, failed login spikes)
/// - API usage spikes
/// - Lightning page EPT degradation
/// - Apex unexpected exceptions
/// </summary>
public class ShieldAnomalyDetector
{
    private readonly MonitoringDatabaseService _db;
    private readonly SettingsService? _settingsService;

    // Known IPs per user (built from recent login history)
    private readonly Dictionary<string, HashSet<string>> _knownUserIps = new();

    // Detection anchor — the latest event timestamp, used instead of DateTime.UtcNow
    // because Shield event log files have a 1-3 hour lag from Salesforce
    private DateTime _anchor = DateTime.UtcNow;

    // Threshold defaults (used when settings are not available)
    private const int DefaultFailedLoginThreshold = 5;
    private const double DefaultApiSpikeZScore = 2.5;
    private const double DefaultEptDegradationMs = 3000;
    private const int QuietHourStart = 0;
    private const int QuietHourEnd = 5;
    private const int DefaultApiFailureSpikeThreshold = 3;
    private const double DefaultApiFailureRateThreshold = 0.20;
    private const int DefaultReportExportRowThreshold = 5000;

    // Configurable threshold accessors
    private int FailedLoginSpikeThreshold => _settingsService?.Load().ShieldFailedLoginThreshold ?? DefaultFailedLoginThreshold;
    private double ApiSpikeZScore => _settingsService?.Load().ShieldApiSpikeZScore ?? DefaultApiSpikeZScore;
    private double EptDegradationMs => _settingsService?.Load().ShieldEptDegradationMs ?? DefaultEptDegradationMs;
    private int ApiFailureSpikeThreshold => _settingsService?.Load().ShieldApiFailureThreshold ?? DefaultApiFailureSpikeThreshold;
    private double ApiFailureRateThreshold => _settingsService?.Load().ShieldApiFailureRate ?? DefaultApiFailureRateThreshold;
    private int ReportExportRowThreshold => _settingsService?.Load().ShieldReportExportRowThreshold ?? DefaultReportExportRowThreshold;

    /// <summary>Fired when a new anomaly-based alert should be created.</summary>
    public event EventHandler<MonitoringAlert>? AlertGenerated;

    /// <summary>
    /// Fired when an Apex exception spike is detected and an automatic Trace Flag
    /// should be set on the affected user so the next occurrence is captured in a debug log.
    /// Carries the Salesforce User ID of the affected user.
    /// </summary>
    public event EventHandler<string>? AutoTraceFlagRequested;

    public ShieldAnomalyDetector(MonitoringDatabaseService db, SettingsService? settingsService = null)
    {
        _db = db;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Runs all Shield anomaly detection checks.
    /// Should be called after ShieldEventLogService processes new events.
    /// </summary>
    public async Task RunDetectionAsync()
    {
        try
        {
            // Use latest event timestamp as anchor — Shield data lags 1-3 hours behind real-time
            _anchor = await _db.GetLatestShieldEventDateAsync() ?? DateTime.UtcNow;
            Log.Information("Shield detection running (anchor: {Anchor:u}, lag: {Lag:F1}h)",
                _anchor, (DateTime.UtcNow - _anchor).TotalHours);

            await DetectLoginAnomalies();
            await DetectApiSpikes();
            await DetectApiFailures();
            await DetectPagePerformanceDegradation();
            await DetectApexExceptions();
            await DetectDataExfiltration();
            await DetectPermissionChanges();
            await GenerateActivitySummary();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shield anomaly detection cycle failed");
        }
    }

    /// <summary>
    /// Seed known IPs from historical login data so first-time detection is accurate.
    /// Call once after Shield service starts.
    /// </summary>
    public async Task SeedKnownIpsAsync()
    {
        try
        {
            var recentLogins = await _db.GetRecentShieldEventsAsync("Login", DateTime.UtcNow.AddDays(-14));
            foreach (var login in recentLogins.Where(l => l.IsSuccess && !string.IsNullOrEmpty(l.ClientIp)))
            {
                var userId = login.UserId ?? "unknown";
                if (!_knownUserIps.ContainsKey(userId))
                    _knownUserIps[userId] = new HashSet<string>();
                _knownUserIps[userId].Add(login.ClientIp!);
            }
            Log.Information("Seeded known IPs for {UserCount} users from login history", _knownUserIps.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to seed known IPs");
        }
    }

    /// <summary>
    /// Detects login anomalies: new IPs, unusual hours, failed login spikes.
    /// </summary>
    private async Task DetectLoginAnomalies()
    {
        var recentLogins = await _db.GetRecentShieldEventsAsync("Login", _anchor.AddHours(-1));
        if (recentLogins.Count == 0) return;

        // 1. Failed login spike
        var failedLogins = recentLogins.Where(l => !l.IsSuccess).ToList();
        if (failedLogins.Count >= FailedLoginSpikeThreshold)
        {
            var uniqueUsers = failedLogins.Select(l => l.UserId).Distinct().Count();
            var uniqueIps = failedLogins.Select(l => l.ClientIp).Where(ip => ip != null).Distinct().Count();

            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "shield_login_anomaly",
                Severity = failedLogins.Count >= FailedLoginSpikeThreshold * 2 ? "critical" : "warning",
                Title = $"{failedLogins.Count} failed logins in the last hour",
                Description = $"{failedLogins.Count} failed login attempts from {uniqueIps} IPs " +
                              $"targeting {uniqueUsers} users in the last hour.",
                MetricName = "failed_logins",
                CurrentValue = failedLogins.Count,
                ThresholdValue = FailedLoginSpikeThreshold
            });
        }

        // 2. New IP detection (for successful logins)
        foreach (var login in recentLogins.Where(l => l.IsSuccess && !string.IsNullOrEmpty(l.ClientIp)))
        {
            var userId = login.UserId ?? "unknown";
            if (!_knownUserIps.TryGetValue(userId, out var knownIps))
            {
                knownIps = new HashSet<string>();
                _knownUserIps[userId] = knownIps;
            }

            if (!knownIps.Contains(login.ClientIp!))
            {
                // New IP for this user
                knownIps.Add(login.ClientIp!);

                // Only alert if user had previous login history (not first-time users)
                if (knownIps.Count > 1)
                {
                    await TryCreateAlert(new MonitoringAlert
                    {
                        OrgId = _db.OrgId,
                        AlertType = "shield_login_anomaly",
                        Severity = "warning",
                        Title = $"Login from new IP for user {userId}",
                        Description = $"User {userId} logged in from a new IP address ({login.ClientIp}). " +
                                      $"This user has {knownIps.Count} known IPs.",
                        EntryPoint = userId,
                        MetricName = "new_ip_login"
                    });
                }
            }
        }

        // 3. Unusual hour logins (check UTC hour to avoid timezone confusion)
        foreach (var login in recentLogins.Where(l => l.IsSuccess))
        {
            if (DateTime.TryParse(login.EventDate, null,
                    System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var loginTime))
            {
                var hour = loginTime.Hour;
                if (hour >= QuietHourStart && hour < QuietHourEnd)
                {
                    await TryCreateAlert(new MonitoringAlert
                    {
                        OrgId = _db.OrgId,
                        AlertType = "shield_login_anomaly",
                        Severity = "info",
                        Title = $"Login at unusual hour ({loginTime:HH:mm} UTC)",
                        Description = $"User {login.UserId} logged in at {loginTime:HH:mm} UTC " +
                                      $"from {login.ClientIp ?? "unknown IP"}. This is outside normal business hours.",
                        EntryPoint = login.UserId,
                        MetricName = "unusual_hour_login"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Detects API usage spikes by comparing recent hour to historical average.
    /// </summary>
    private async Task DetectApiSpikes()
    {
        var recentApi = await _db.GetRecentShieldEventsAsync("API", _anchor.AddHours(-1));
        var historicalApi = await _db.GetRecentShieldEventsAsync("API", _anchor.AddDays(-7));

        if (recentApi.Count == 0 || historicalApi.Count < 24) return; // Not enough data

        // Calculate hourly average from last 7 days
        var totalHours = 7 * 24.0; // 168 hours in 7 days
        var avgPerHour = historicalApi.Count / totalHours;

        if (avgPerHour < 1) return; // Too few API calls to be meaningful

        // Calculate standard deviation of hourly counts
        // Group historical events by hour
        var hourlyBuckets = historicalApi
            .Where(e => DateTime.TryParse(e.EventDate, out _))
            .GroupBy(e =>
            {
                DateTime.TryParse(e.EventDate, out var dt);
                return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0);
            })
            .Select(g => (double)g.Count())
            .ToList();

        if (hourlyBuckets.Count < 3) return;

        var mean = hourlyBuckets.Average();
        var stddev = Math.Sqrt(hourlyBuckets.Sum(v => (v - mean) * (v - mean)) / (hourlyBuckets.Count - 1));

        if (stddev < 1) return; // Not enough variance

        var z = (recentApi.Count - mean) / stddev;

        if (z >= ApiSpikeZScore)
        {
            var pctIncrease = mean > 0 ? ((recentApi.Count - mean) / mean * 100) : 0;
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "shield_api_spike",
                Severity = z >= 3.5 ? "critical" : "warning",
                Title = $"API calls up {pctIncrease:F0}% vs normal",
                Description = $"{recentApi.Count} API calls in the last hour (normal: {mean:F0}/hr, " +
                              $"z-score: {z:F1}). Possible runaway integration or load test.",
                MetricName = "api_calls_per_hour",
                CurrentValue = recentApi.Count,
                BaselineValue = mean
            });
        }
    }

    /// <summary>
    /// Detects API failure spikes: endpoints returning 4xx/5xx or marked !IsSuccess.
    /// Also flags when the overall API failure rate exceeds the threshold.
    /// Correlates with debug log callout failures in the same time window.
    /// </summary>
    private async Task DetectApiFailures()
    {
        var recentApi = await _db.GetRecentShieldEventsAsync("API", _anchor.AddHours(-1));
        if (recentApi.Count == 0) return;

        var failedApi = recentApi
            .Where(e => !e.IsSuccess || (e.StatusCode.HasValue && e.StatusCode.Value >= 400))
            .ToList();

        if (failedApi.Count == 0) return;

        // --- Per-endpoint failure spikes ---
        var endpointGroups = failedApi
            .GroupBy(e => e.Uri ?? "unknown")
            .Where(g => g.Count() >= ApiFailureSpikeThreshold);

        // Fetch callout-related snapshots once (for correlation note)
        var calloutFailureSnapshots = await _db.GetSnapshotsSinceAsync(_anchor.AddHours(-2), null);
        var calloutFailures = calloutFailureSnapshots
            .Where(s => s.CalloutCount > 0 && s.TransactionFailed)
            .ToList();
        var correlationNote = calloutFailures.Count > 0
            ? $" ⚠️ {calloutFailures.Count} debug log(s) with callout failures may be related."
            : string.Empty;

        foreach (var group in endpointGroups)
        {
            var affectedUsers = group
                .Select(e => e.UserId)
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .Count();

            var statusCodes = group
                .Where(e => e.StatusCode.HasValue)
                .Select(e => e.StatusCode!.Value)
                .Distinct()
                .OrderBy(c => c)
                .Take(3)
                .ToList();

            var statusText = statusCodes.Count > 0
                ? $" (HTTP {string.Join(", ", statusCodes)})"
                : string.Empty;

            var userNote = affectedUsers > 0 ? $" Affected users: {affectedUsers}." : string.Empty;

            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "api_failure_spike",
                Severity = group.Count() >= 10 ? "critical" : "warning",
                Title = $"{group.Key}: {group.Count()} API failures",
                Description = $"{group.Count()} failed API calls to '{group.Key}'{statusText} in the last hour.{userNote}{correlationNote}",
                EntryPoint = group.Key,
                MetricName = "api_failure_count",
                CurrentValue = group.Count(),
                ThresholdValue = ApiFailureSpikeThreshold,
                AffectedUserCount = affectedUsers > 0 ? affectedUsers : null
            });
        }

        // --- Overall failure rate alert ---
        if (recentApi.Count >= 10)
        {
            var failureRate = (double)failedApi.Count / recentApi.Count;
            if (failureRate >= ApiFailureRateThreshold)
            {
                var uniqueEndpoints = failedApi.Select(e => e.Uri).Distinct().Count();
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "api_failure_rate",
                    Severity = failureRate >= 0.5 ? "critical" : "warning",
                    Title = $"API failure rate: {failureRate:P0} ({failedApi.Count}/{recentApi.Count} calls)",
                    Description = $"{failedApi.Count} of {recentApi.Count} API calls failed this hour " +
                                  $"({failureRate:P0} failure rate) across {uniqueEndpoints} endpoint(s). " +
                                  "This may indicate a broken integration or authentication issue.",
                    MetricName = "api_failure_rate",
                    CurrentValue = Math.Round(failureRate * 100, 1),
                    ThresholdValue = ApiFailureRateThreshold * 100
                });
            }
        }
    }

    /// <summary>
    /// Detects Lightning page performance degradation using EPT (Effective Page Time).
    /// </summary>
    private async Task DetectPagePerformanceDegradation()
    {
        var recentPages = await _db.GetRecentShieldEventsAsync("LightningPageView", _anchor.AddHours(-1));
        if (recentPages.Count < 5) return; // Need meaningful sample

        // Group by page/URI and check EPT
        var pageGroups = recentPages
            .Where(p => p.DurationMs.HasValue && p.DurationMs > 0)
            .GroupBy(p => p.Uri ?? "unknown")
            .Where(g => g.Count() >= 3); // At least 3 views

        foreach (var pageGroup in pageGroups)
        {
            var avgEpt = pageGroup.Average(p => p.DurationMs!.Value);

            if (avgEpt >= EptDegradationMs)
            {
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "shield_page_slow",
                    Severity = avgEpt >= EptDegradationMs * 2 ? "critical" : "warning",
                    Title = $"{pageGroup.Key}: EPT {avgEpt:F0}ms ({pageGroup.Count()} views)",
                    Description = $"Lightning page '{pageGroup.Key}' has an average Effective Page Time of " +
                                  $"{avgEpt:F0}ms across {pageGroup.Count()} recent views. " +
                                  $"Threshold: {EptDegradationMs}ms.",
                    EntryPoint = pageGroup.Key,
                    MetricName = "page_ept_ms",
                    CurrentValue = avgEpt,
                    ThresholdValue = EptDegradationMs
                });
            }
        }
    }

    /// <summary>
    /// Detects spikes in Apex unexpected exceptions from Shield data.
    /// </summary>
    private async Task DetectApexExceptions()
    {
        var recentExceptions = await _db.GetRecentShieldEventsAsync(
            "ApexUnexpectedException", _anchor.AddHours(-1));

        if (recentExceptions.Count == 0) return;

        // Group by URI/exception type
        var exceptionGroups = recentExceptions
            .GroupBy(e => e.Uri ?? "Unknown")
            .Where(g => g.Count() >= 2); // At least 2 exceptions from same source

        foreach (var group in exceptionGroups)
        {
            // Build a richer description from ExtraJson if available
            var descParts = new List<string>();
            descParts.Add($"{group.Count()} unhandled exceptions in the last hour.");

            // Extract unique exception messages from the group
            var messages = group
                .Where(e => e.ExtraJson != null)
                .Select(e => {
                    try { return Newtonsoft.Json.Linq.JObject.Parse(e.ExtraJson!)["exceptionMessage"]?.ToString(); }
                    catch { return null; }
                })
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .Take(3)
                .ToList();

            if (messages.Count > 0)
                descParts.Add("Messages: " + string.Join(" | ", messages));

            // Collect affected user IDs for auto trace flag
            var affectedUserIds = group
                .Select(e => e.UserId)
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .ToList();

            if (affectedUserIds.Count > 0)
            {
                descParts.Add($"Affected users: {affectedUserIds.Count}.");
                descParts.Add("⚡ Auto trace flag set — next occurrence will be captured in a debug log.");
            }
            else
            {
                descParts.Add("These errors are captured by Shield and may not appear in debug logs.");
            }

            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "error_spike",
                Severity = group.Count() >= 5 ? "critical" : "warning",
                Title = $"{group.Key}: {group.Count()} unhandled exceptions",
                Description = string.Join(" ", descParts),
                EntryPoint = group.Key,
                MetricName = "apex_exceptions",
                CurrentValue = group.Count(),
                AffectedUserCount = affectedUserIds.Count > 0 ? affectedUserIds.Count : null
            });

            // Request automatic trace flag creation for each affected user
            foreach (var userId in affectedUserIds)
                AutoTraceFlagRequested?.Invoke(this, userId!);
        }
    }

    /// <summary>
    /// Detects potential data exfiltration via large report exports or off-hours bulk operations.
    /// Monitors ReportExport and BulkApi event types.
    /// </summary>
    private async Task DetectDataExfiltration()
    {
        var since = _anchor.AddHours(-24);

        // Check report exports
        var reportExports = await _db.GetRecentShieldEventsAsync("ReportExport", since);
        if (reportExports.Count > 0)
        {
            // Large single export
            var largeExport = reportExports
                .Where(e => e.RowCount.HasValue && e.RowCount >= ReportExportRowThreshold)
                .ToList();

            foreach (var export in largeExport.GroupBy(e => e.UserId))
            {
                var maxRows = export.Max(e => e.RowCount ?? 0);
                var userId = export.Key ?? "unknown";

                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "shield_data_exfiltration",
                    Severity = maxRows >= ReportExportRowThreshold * 4 ? "critical" : "warning",
                    Title = $"Large report export by user {userId}",
                    Description = $"User {userId} exported {maxRows:N0} rows from a report. " +
                                  $"This exceeds the {ReportExportRowThreshold:N0}-row threshold. " +
                                  $"Review if this is an expected business operation.",
                    EntryPoint = userId,
                    MetricName = "report_export_rows",
                    CurrentValue = maxRows,
                    ThresholdValue = ReportExportRowThreshold
                });
            }

            // Off-hours export (during quiet hours)
            var offHoursExports = reportExports
                .Where(e =>
                {
                    if (!DateTime.TryParse(e.EventDate, out var dt)) return false;
                    var hour = dt.Hour;
                    return hour >= QuietHourStart && hour < QuietHourEnd;
                })
                .GroupBy(e => e.UserId)
                .ToList();

            foreach (var group in offHoursExports)
            {
                var userId = group.Key ?? "unknown";
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "shield_data_exfiltration",
                    Severity = "warning",
                    Title = $"Off-hours report export by {userId}",
                    Description = $"User {userId} ran {group.Count()} report export(s) between " +
                                  $"{QuietHourStart}:00–{QuietHourEnd}:00 UTC. " +
                                  $"Exports during off-hours may warrant review.",
                    EntryPoint = userId,
                    MetricName = "off_hours_export"
                });
            }
        }

        // Check BulkApi operations (large-volume data movement)
        var bulkOps = await _db.GetRecentShieldEventsAsync("BulkApi", since);
        if (bulkOps.Count > 0)
        {
            var highVolumeBulk = bulkOps
                .Where(e => e.RowCount.HasValue && e.RowCount >= ReportExportRowThreshold * 2)
                .GroupBy(e => e.UserId)
                .ToList();

            foreach (var group in highVolumeBulk)
            {
                var userId = group.Key ?? "unknown";
                var totalRows = group.Sum(e => e.RowCount ?? 0);
                await TryCreateAlert(new MonitoringAlert
                {
                    OrgId = _db.OrgId,
                    AlertType = "shield_data_exfiltration",
                    Severity = "warning",
                    Title = $"High-volume Bulk API operation by {userId}",
                    Description = $"User {userId} processed {totalRows:N0} rows via Bulk API " +
                                  $"in the last 24 hours. Verify this is an expected data migration or ETL job.",
                    EntryPoint = userId,
                    MetricName = "bulk_api_rows",
                    CurrentValue = totalRows
                });
            }
        }
    }

    /// <summary>
    /// Detects Salesforce setup and permission changes via SetupAuditTrail events.
    /// Flags profile changes, permission set modifications, field-level security changes, and IP restrictions.
    /// </summary>
    private async Task DetectPermissionChanges()
    {
        var since = _anchor.AddHours(-24);
        var setupEvents = await _db.GetRecentShieldEventsAsync("SetupAuditTrail", since);
        if (setupEvents.Count == 0) return;

        // High-risk sections that warrant immediate alerting
        var criticalSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PermissionSets", "PermissionSetGroups", "Profiles",
            "IpWhitelisting", "NetworkAccess", "SessionManagement",
            "FieldPermissions", "ObjectPermissions", "SystemPermissions"
        };

        var highRiskEvents = setupEvents
            .Where(e =>
            {
                if (e.ExtraJson == null) return false;
                try
                {
                    var j = Newtonsoft.Json.Linq.JObject.Parse(e.ExtraJson);
                    var section = j["section"]?.ToString();
                    return section != null && criticalSections.Contains(section);
                }
                catch { return false; }
            })
            .ToList();

        if (highRiskEvents.Count > 0)
        {
            var sections = highRiskEvents
                .Select(e =>
                {
                    try { return Newtonsoft.Json.Linq.JObject.Parse(e.ExtraJson!)["section"]?.ToString(); }
                    catch { return null; }
                })
                .Where(s => s != null)
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            var uniqueUsers = highRiskEvents.Select(e => e.UserId).Distinct().Count();

            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "shield_permission_change",
                Severity = highRiskEvents.Count >= 5 ? "critical" : "warning",
                Title = $"{highRiskEvents.Count} permission/profile change(s) detected",
                Description = $"{highRiskEvents.Count} high-risk setup change(s) by {uniqueUsers} admin(s) " +
                              $"in the last 24 hours. Sections modified: {string.Join(", ", sections)}. " +
                              $"Review the Setup Audit Trail to verify these changes were authorized.",
                MetricName = "permission_changes",
                CurrentValue = highRiskEvents.Count,
                AffectedUserCount = uniqueUsers
            });
        }

        // Flag any off-hours setup changes
        var offHoursSetup = setupEvents
            .Where(e =>
            {
                if (!DateTime.TryParse(e.EventDate, out var dt)) return false;
                var hour = dt.Hour;
                return hour >= QuietHourStart && hour < QuietHourEnd;
            })
            .ToList();

        if (offHoursSetup.Count >= 3)
        {
            var uniqueAdmins = offHoursSetup.Select(e => e.UserId).Distinct().Count();
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "shield_permission_change",
                Severity = "warning",
                Title = $"Off-hours setup changes ({offHoursSetup.Count})",
                Description = $"{offHoursSetup.Count} Salesforce setup changes were made between " +
                              $"{QuietHourStart}:00–{QuietHourEnd}:00 UTC by {uniqueAdmins} admin(s). " +
                              $"Verify these changes were authorized.",
                MetricName = "off_hours_setup_changes",
                CurrentValue = offHoursSetup.Count,
                AffectedUserCount = uniqueAdmins
            });
        }
    }

    /// <summary>
    /// Generates a periodic activity summary so users know Shield is working even without anomalies.
    /// Fires once per day as an info-level alert.
    /// </summary>
    private async Task GenerateActivitySummary()
    {
        var recentLogins = await _db.GetRecentShieldEventsAsync("Login", _anchor.AddHours(-24));
        var recentApi = await _db.GetRecentShieldEventsAsync("API", _anchor.AddHours(-24));
        var recentExceptions = await _db.GetRecentShieldEventsAsync("ApexUnexpectedException", _anchor.AddHours(-24));
        var recentPages = await _db.GetRecentShieldEventsAsync("LightningPageView", _anchor.AddHours(-24));

        var totalEvents = recentLogins.Count + recentApi.Count + recentExceptions.Count + recentPages.Count;
        if (totalEvents == 0) return;

        var uniqueUsers = recentLogins.Select(l => l.UserId).Where(u => u != null).Distinct().Count();
        var failedLogins = recentLogins.Count(l => !l.IsSuccess);
        var exceptionCount = recentExceptions.Count;

        var parts = new List<string>();
        parts.Add($"{uniqueUsers} users logged in");
        parts.Add($"{recentApi.Count:N0} API calls");
        if (recentPages.Count > 0) parts.Add($"{recentPages.Count:N0} page views");
        if (failedLogins > 0) parts.Add($"{failedLogins} failed logins");
        if (exceptionCount > 0) parts.Add($"{exceptionCount} Apex exceptions");

        await TryCreateAlert(new MonitoringAlert
        {
            OrgId = _db.OrgId,
            AlertType = "shield_activity_summary",
            Severity = exceptionCount > 0 || failedLogins > 0 ? "info" : "info",
            Title = $"Shield 24h: {totalEvents:N0} events from {uniqueUsers} users",
            Description = string.Join(" · ", parts),
            MetricName = "activity_summary",
            CurrentValue = totalEvents
        });
    }

    /// <summary>
    /// Creates an alert if one hasn't been created for the same type/entry/metric in the last 24 hours.
    /// </summary>
    private async Task TryCreateAlert(MonitoringAlert alert)
    {
        alert.CreatedAt = DateTime.UtcNow;

        var existing = await _db.GetRecentAlertAsync(alert.AlertType, alert.EntryPoint, alert.MetricName);
        if (existing != null) return;

        await _db.InsertAlertAsync(alert);
        AlertGenerated?.Invoke(this, alert);
        Log.Information("Shield alert: [{Severity}] {Title}", alert.Severity, alert.Title);
    }
}
