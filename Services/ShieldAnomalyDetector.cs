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

    // Known IPs per user (built from recent login history)
    private readonly Dictionary<string, HashSet<string>> _knownUserIps = new();

    // Thresholds
    private const int FailedLoginSpikeThreshold = 5;     // 5+ failed logins in 1 hour
    private const double ApiSpikeZScore = 2.5;            // API calls z-score threshold
    private const double EptDegradationMs = 3000;         // EPT > 3s = degraded
    private const int QuietHourStart = 0;                 // Midnight
    private const int QuietHourEnd = 5;                   // 5 AM (unusual login hours)

    /// <summary>Fired when a new anomaly-based alert should be created.</summary>
    public event EventHandler<MonitoringAlert>? AlertGenerated;

    public ShieldAnomalyDetector(MonitoringDatabaseService db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs all Shield anomaly detection checks.
    /// Should be called after ShieldEventLogService processes new events.
    /// </summary>
    public async Task RunDetectionAsync()
    {
        try
        {
            await DetectLoginAnomalies();
            await DetectApiSpikes();
            await DetectPagePerformanceDegradation();
            await DetectApexExceptions();
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
        var recentLogins = await _db.GetRecentShieldEventsAsync("Login", DateTime.UtcNow.AddHours(-1));
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

        // 3. Unusual hour logins
        foreach (var login in recentLogins.Where(l => l.IsSuccess))
        {
            if (DateTime.TryParse(login.EventDate, out var loginTime))
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
        var recentApi = await _db.GetRecentShieldEventsAsync("API", DateTime.UtcNow.AddHours(-1));
        var historicalApi = await _db.GetRecentShieldEventsAsync("API", DateTime.UtcNow.AddDays(-7));

        if (recentApi.Count == 0 || historicalApi.Count < 24) return; // Not enough data

        // Calculate hourly average from last 7 days
        var totalHours = (DateTime.UtcNow - DateTime.UtcNow.AddDays(-7)).TotalHours;
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
    /// Detects Lightning page performance degradation using EPT (Effective Page Time).
    /// </summary>
    private async Task DetectPagePerformanceDegradation()
    {
        var recentPages = await _db.GetRecentShieldEventsAsync("LightningPageView", DateTime.UtcNow.AddHours(-1));
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
            "ApexUnexpectedException", DateTime.UtcNow.AddHours(-1));

        if (recentExceptions.Count == 0) return;

        // Group by URI (class/method)
        var exceptionGroups = recentExceptions
            .GroupBy(e => e.Uri ?? "Unknown")
            .Where(g => g.Count() >= 2); // At least 2 exceptions from same source

        foreach (var group in exceptionGroups)
        {
            await TryCreateAlert(new MonitoringAlert
            {
                OrgId = _db.OrgId,
                AlertType = "error_spike",
                Severity = group.Count() >= 5 ? "critical" : "warning",
                Title = $"{group.Key}: {group.Count()} unhandled exceptions",
                Description = $"Apex class/method '{group.Key}' threw {group.Count()} unhandled exceptions " +
                              $"in the last hour. These errors are captured by Shield and may not appear in debug logs.",
                EntryPoint = group.Key,
                MetricName = "apex_exceptions",
                CurrentValue = group.Count()
            });
        }
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
