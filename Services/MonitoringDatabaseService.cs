using Microsoft.Data.Sqlite;
using SalesforceDebugAnalyzer.Models;
using Serilog;
using System.IO;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages a per-org SQLite database for monitoring data persistence.
/// Thread-safe: uses SemaphoreSlim for writes, WAL mode for concurrent reads.
/// </summary>
public class MonitoringDatabaseService : IDisposable
{
    private const int SchemaVersion = 2;

    private readonly string _dbPath;
    private readonly string _orgId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public string OrgId => _orgId;

    public MonitoringDatabaseService(string orgId)
    {
        _orgId = orgId;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var orgDir = Path.Combine(appData, "BlackWidow", "orgs", orgId);
        Directory.CreateDirectory(orgDir);

        _dbPath = Path.Combine(orgDir, "monitoring.db");
        InitializeDatabase();
    }

    private string ConnectionString => $"Data Source={_dbPath}";

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        // Enable WAL mode for concurrent reads
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // Check schema version
        int currentVersion;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            currentVersion = Convert.ToInt32(cmd.ExecuteScalar());
        }

        if (currentVersion < SchemaVersion)
        {
            ApplyMigrations(connection, currentVersion);
        }
    }

    private void ApplyMigrations(SqliteConnection connection, int fromVersion)
    {
        using var transaction = connection.BeginTransaction();

        if (fromVersion < 1)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS log_snapshots (
                    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    log_id                  TEXT    NOT NULL UNIQUE,
                    org_id                  TEXT    NOT NULL,
                    captured_at             TEXT    NOT NULL,
                    entry_point             TEXT,
                    operation_type          TEXT,
                    log_user                TEXT,
                    duration_ms             REAL,
                    cpu_time_ms             INTEGER,
                    soql_count              INTEGER,
                    soql_limit              INTEGER,
                    dml_count               INTEGER,
                    dml_limit               INTEGER,
                    query_rows              INTEGER,
                    query_rows_limit        INTEGER,
                    heap_size               INTEGER,
                    heap_limit              INTEGER,
                    callout_count           INTEGER,
                    callout_limit           INTEGER,
                    health_score            INTEGER,
                    health_grade            TEXT,
                    bulk_safety_grade       TEXT,
                    has_errors              INTEGER DEFAULT 0,
                    transaction_failed      INTEGER DEFAULT 0,
                    error_count             INTEGER DEFAULT 0,
                    handled_exception_count INTEGER DEFAULT 0,
                    duplicate_query_count   INTEGER DEFAULT 0,
                    n_plus_one_worst        INTEGER DEFAULT 0,
                    stack_depth_max         INTEGER DEFAULT 0,
                    is_async                INTEGER DEFAULT 0,
                    is_truncated            INTEGER DEFAULT 0,
                    source                  TEXT    DEFAULT 'debug_log'
                );

                CREATE INDEX IF NOT EXISTS idx_snapshots_org_time
                    ON log_snapshots(org_id, captured_at);
                CREATE INDEX IF NOT EXISTS idx_snapshots_entry_point
                    ON log_snapshots(org_id, entry_point, captured_at);

                CREATE TABLE IF NOT EXISTS metric_aggregates (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id         TEXT NOT NULL,
                    period_start   TEXT NOT NULL,
                    period_type    TEXT NOT NULL,
                    entry_point    TEXT,
                    metric_name    TEXT NOT NULL,
                    sample_count   INTEGER,
                    avg_value      REAL,
                    min_value      REAL,
                    max_value      REAL,
                    p50_value      REAL,
                    p90_value      REAL,
                    p99_value      REAL,
                    stddev_value   REAL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS idx_metrics_unique
                    ON metric_aggregates(org_id, period_start, period_type, entry_point, metric_name);

                CREATE TABLE IF NOT EXISTS baselines (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id          TEXT NOT NULL,
                    entry_point     TEXT NOT NULL,
                    metric_name     TEXT NOT NULL,
                    baseline_value  REAL,
                    stddev          REAL,
                    sample_count    INTEGER,
                    last_updated    TEXT NOT NULL,
                    window_days     INTEGER DEFAULT 14
                );

                CREATE UNIQUE INDEX IF NOT EXISTS idx_baselines_unique
                    ON baselines(org_id, entry_point, metric_name);

                CREATE TABLE IF NOT EXISTS alerts (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id           TEXT    NOT NULL,
                    created_at       TEXT    NOT NULL,
                    alert_type       TEXT    NOT NULL,
                    severity         TEXT    NOT NULL,
                    title            TEXT    NOT NULL,
                    description      TEXT    NOT NULL,
                    entry_point      TEXT,
                    metric_name      TEXT,
                    current_value    REAL,
                    baseline_value   REAL,
                    threshold_value  REAL,
                    is_read          INTEGER DEFAULT 0,
                    is_dismissed     INTEGER DEFAULT 0,
                    dismissed_at     TEXT,
                    action_taken     TEXT,
                    related_log_id   TEXT,
                    notified_via     TEXT,
                    user_feedback    TEXT,
                    feedback_at      TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_alerts_org_time
                    ON alerts(org_id, created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_alerts_unread
                    ON alerts(org_id, is_read, is_dismissed);

                CREATE TABLE IF NOT EXISTS monitoring_config (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id       TEXT NOT NULL,
                    config_key   TEXT NOT NULL,
                    config_value TEXT NOT NULL,
                    updated_at   TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS idx_config_unique
                    ON monitoring_config(org_id, config_key);

                CREATE TABLE IF NOT EXISTS shield_log_files (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id        TEXT NOT NULL,
                    event_type    TEXT NOT NULL,
                    log_date      TEXT NOT NULL,
                    log_file_id   TEXT NOT NULL UNIQUE,
                    interval_type TEXT NOT NULL,
                    processed_at  TEXT NOT NULL,
                    record_count  INTEGER,
                    file_size     INTEGER
                );

                CREATE INDEX IF NOT EXISTS idx_shield_org_type
                    ON shield_log_files(org_id, event_type, log_date);

                CREATE TABLE IF NOT EXISTS shield_events (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    org_id         TEXT NOT NULL,
                    event_type     TEXT NOT NULL,
                    event_date     TEXT NOT NULL,
                    user_id        TEXT,
                    uri            TEXT,
                    duration_ms    REAL,
                    cpu_time_ms    REAL,
                    row_count      INTEGER,
                    status_code    INTEGER,
                    is_success     INTEGER DEFAULT 1,
                    client_ip      TEXT,
                    extra_json     TEXT,
                    is_anomaly     INTEGER DEFAULT 0,
                    anomaly_reason TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_shield_events_org
                    ON shield_events(org_id, event_type, event_date);
            ";
            cmd.ExecuteNonQuery();
        }

        if (fromVersion < 2)
        {
            // Add user feedback columns to alerts table (added in schema v2)
            using var cmd2 = connection.CreateCommand();
            cmd2.Transaction = transaction;
            cmd2.CommandText = @"
                ALTER TABLE alerts ADD COLUMN user_feedback TEXT;
                ALTER TABLE alerts ADD COLUMN feedback_at   TEXT;
            ";
            try { cmd2.ExecuteNonQuery(); }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Columns already exist — safe to ignore
            }
        }

        // Update schema version
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = $"PRAGMA user_version = {SchemaVersion};";
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
        Log.Information("Monitoring database migrated to schema version {Version}", SchemaVersion);
    }

    // ================================================================
    //  LOG SNAPSHOTS
    // ================================================================

    /// <summary>
    /// Insert a log analysis snapshot. Returns true if inserted, false if duplicate.
    /// </summary>
    public async Task<bool> InsertSnapshotAsync(LogSnapshot snapshot)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO log_snapshots (
                    log_id, org_id, captured_at, entry_point, operation_type, log_user,
                    duration_ms, cpu_time_ms,
                    soql_count, soql_limit, dml_count, dml_limit,
                    query_rows, query_rows_limit, heap_size, heap_limit,
                    callout_count, callout_limit,
                    health_score, health_grade, bulk_safety_grade,
                    has_errors, transaction_failed, error_count, handled_exception_count,
                    duplicate_query_count, n_plus_one_worst, stack_depth_max,
                    is_async, is_truncated, source
                ) VALUES (
                    @logId, @orgId, @capturedAt, @entryPoint, @operationType, @logUser,
                    @durationMs, @cpuTimeMs,
                    @soqlCount, @soqlLimit, @dmlCount, @dmlLimit,
                    @queryRows, @queryRowsLimit, @heapSize, @heapLimit,
                    @calloutCount, @calloutLimit,
                    @healthScore, @healthGrade, @bulkSafetyGrade,
                    @hasErrors, @transactionFailed, @errorCount, @handledExceptionCount,
                    @duplicateQueryCount, @nPlusOneWorst, @stackDepthMax,
                    @isAsync, @isTruncated, @source
                )";

            cmd.Parameters.AddWithValue("@logId", snapshot.LogId);
            cmd.Parameters.AddWithValue("@orgId", snapshot.OrgId);
            cmd.Parameters.AddWithValue("@capturedAt", snapshot.CapturedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@entryPoint", snapshot.EntryPoint ?? "");
            cmd.Parameters.AddWithValue("@operationType", snapshot.OperationType ?? "");
            cmd.Parameters.AddWithValue("@logUser", snapshot.LogUser ?? "");
            cmd.Parameters.AddWithValue("@durationMs", snapshot.DurationMs);
            cmd.Parameters.AddWithValue("@cpuTimeMs", snapshot.CpuTimeMs);
            cmd.Parameters.AddWithValue("@soqlCount", snapshot.SoqlCount);
            cmd.Parameters.AddWithValue("@soqlLimit", snapshot.SoqlLimit);
            cmd.Parameters.AddWithValue("@dmlCount", snapshot.DmlCount);
            cmd.Parameters.AddWithValue("@dmlLimit", snapshot.DmlLimit);
            cmd.Parameters.AddWithValue("@queryRows", snapshot.QueryRows);
            cmd.Parameters.AddWithValue("@queryRowsLimit", snapshot.QueryRowsLimit);
            cmd.Parameters.AddWithValue("@heapSize", snapshot.HeapSize);
            cmd.Parameters.AddWithValue("@heapLimit", snapshot.HeapLimit);
            cmd.Parameters.AddWithValue("@calloutCount", snapshot.CalloutCount);
            cmd.Parameters.AddWithValue("@calloutLimit", snapshot.CalloutLimit);
            cmd.Parameters.AddWithValue("@healthScore", snapshot.HealthScore);
            cmd.Parameters.AddWithValue("@healthGrade", snapshot.HealthGrade ?? "");
            cmd.Parameters.AddWithValue("@bulkSafetyGrade", snapshot.BulkSafetyGrade ?? "");
            cmd.Parameters.AddWithValue("@hasErrors", snapshot.HasErrors ? 1 : 0);
            cmd.Parameters.AddWithValue("@transactionFailed", snapshot.TransactionFailed ? 1 : 0);
            cmd.Parameters.AddWithValue("@errorCount", snapshot.ErrorCount);
            cmd.Parameters.AddWithValue("@handledExceptionCount", snapshot.HandledExceptionCount);
            cmd.Parameters.AddWithValue("@duplicateQueryCount", snapshot.DuplicateQueryCount);
            cmd.Parameters.AddWithValue("@nPlusOneWorst", snapshot.NPlusOneWorst);
            cmd.Parameters.AddWithValue("@stackDepthMax", snapshot.StackDepthMax);
            cmd.Parameters.AddWithValue("@isAsync", snapshot.IsAsync ? 1 : 0);
            cmd.Parameters.AddWithValue("@isTruncated", snapshot.IsTruncated ? 1 : 0);
            cmd.Parameters.AddWithValue("@source", snapshot.Source ?? "debug_log");

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to insert log snapshot {LogId}", snapshot.LogId);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Check if a log ID has already been persisted.
    /// </summary>
    public async Task<bool> IsLogPersistedAsync(string logId)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM log_snapshots WHERE log_id = @logId";
        cmd.Parameters.AddWithValue("@logId", logId);

        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    /// <summary>
    /// Get recent log IDs (for deduplication on startup).
    /// </summary>
    public async Task<HashSet<string>> GetRecentLogIdsAsync(int limit = 200)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT log_id FROM log_snapshots
            ORDER BY captured_at DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var ids = new HashSet<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    /// <summary>
    /// Get snapshots since a given time for a specific entry point (for trend analysis).
    /// </summary>
    public async Task<List<LogSnapshot>> GetSnapshotsSinceAsync(DateTime since, string? entryPoint = null)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var sql = @"
            SELECT * FROM log_snapshots
            WHERE org_id = @orgId AND captured_at >= @since";

        if (entryPoint != null)
            sql += " AND entry_point = @entryPoint";

        sql += " ORDER BY captured_at ASC";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));
        if (entryPoint != null)
            cmd.Parameters.AddWithValue("@entryPoint", entryPoint);

        var snapshots = new List<LogSnapshot>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshots.Add(ReadSnapshot(reader));
        }
        return snapshots;
    }

    /// <summary>
    /// Get distinct entry points seen in the database.
    /// </summary>
    public async Task<List<string>> GetDistinctEntryPointsAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT entry_point FROM log_snapshots
            WHERE org_id = @orgId AND entry_point IS NOT NULL AND entry_point != ''
            ORDER BY entry_point";
        cmd.Parameters.AddWithValue("@orgId", _orgId);

        var entryPoints = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entryPoints.Add(reader.GetString(0));
        }
        return entryPoints;
    }

    /// <summary>
    /// Get total snapshot count for this org.
    /// </summary>
    public async Task<long> GetSnapshotCountAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM log_snapshots WHERE org_id = @orgId";
        cmd.Parameters.AddWithValue("@orgId", _orgId);

        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // ================================================================
    //  BASELINES
    // ================================================================

    /// <summary>
    /// Get the baseline for a specific entry point and metric.
    /// </summary>
    public async Task<Baseline?> GetBaselineAsync(string entryPoint, string metricName)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM baselines
            WHERE org_id = @orgId AND entry_point = @entryPoint AND metric_name = @metricName";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@entryPoint", entryPoint);
        cmd.Parameters.AddWithValue("@metricName", metricName);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Baseline
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                OrgId = reader.GetString(reader.GetOrdinal("org_id")),
                EntryPoint = reader.GetString(reader.GetOrdinal("entry_point")),
                MetricName = reader.GetString(reader.GetOrdinal("metric_name")),
                BaselineValue = reader.GetDouble(reader.GetOrdinal("baseline_value")),
                Stddev = reader.GetDouble(reader.GetOrdinal("stddev")),
                SampleCount = reader.GetInt32(reader.GetOrdinal("sample_count")),
                LastUpdated = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_updated")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                WindowDays = reader.GetInt32(reader.GetOrdinal("window_days"))
            };
        }
        return null;
    }

    /// <summary>
    /// Insert or update a baseline.
    /// </summary>
    public async Task UpsertBaselineAsync(Baseline baseline)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO baselines (org_id, entry_point, metric_name, baseline_value, stddev, sample_count, last_updated, window_days)
                VALUES (@orgId, @entryPoint, @metricName, @baselineValue, @stddev, @sampleCount, @lastUpdated, @windowDays)
                ON CONFLICT(org_id, entry_point, metric_name)
                DO UPDATE SET baseline_value = @baselineValue, stddev = @stddev, sample_count = @sampleCount,
                              last_updated = @lastUpdated, window_days = @windowDays";

            cmd.Parameters.AddWithValue("@orgId", baseline.OrgId);
            cmd.Parameters.AddWithValue("@entryPoint", baseline.EntryPoint);
            cmd.Parameters.AddWithValue("@metricName", baseline.MetricName);
            cmd.Parameters.AddWithValue("@baselineValue", baseline.BaselineValue);
            cmd.Parameters.AddWithValue("@stddev", baseline.Stddev);
            cmd.Parameters.AddWithValue("@sampleCount", baseline.SampleCount);
            cmd.Parameters.AddWithValue("@lastUpdated", baseline.LastUpdated.ToString("O"));
            cmd.Parameters.AddWithValue("@windowDays", baseline.WindowDays);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ================================================================
    //  METRIC AGGREGATES
    // ================================================================

    /// <summary>
    /// Insert or update an aggregate metric.
    /// </summary>
    public async Task UpsertAggregateAsync(MetricAggregate aggregate)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO metric_aggregates (org_id, period_start, period_type, entry_point, metric_name,
                    sample_count, avg_value, min_value, max_value, p50_value, p90_value, p99_value, stddev_value)
                VALUES (@orgId, @periodStart, @periodType, @entryPoint, @metricName,
                    @sampleCount, @avgValue, @minValue, @maxValue, @p50Value, @p90Value, @p99Value, @stddevValue)
                ON CONFLICT(org_id, period_start, period_type, entry_point, metric_name)
                DO UPDATE SET sample_count = @sampleCount, avg_value = @avgValue, min_value = @minValue,
                              max_value = @maxValue, p50_value = @p50Value, p90_value = @p90Value,
                              p99_value = @p99Value, stddev_value = @stddevValue";

            cmd.Parameters.AddWithValue("@orgId", aggregate.OrgId);
            cmd.Parameters.AddWithValue("@periodStart", aggregate.PeriodStart.ToString("O"));
            cmd.Parameters.AddWithValue("@periodType", aggregate.PeriodType);
            cmd.Parameters.AddWithValue("@entryPoint", (object?)aggregate.EntryPoint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@metricName", aggregate.MetricName);
            cmd.Parameters.AddWithValue("@sampleCount", aggregate.SampleCount);
            cmd.Parameters.AddWithValue("@avgValue", aggregate.AvgValue);
            cmd.Parameters.AddWithValue("@minValue", aggregate.MinValue);
            cmd.Parameters.AddWithValue("@maxValue", aggregate.MaxValue);
            cmd.Parameters.AddWithValue("@p50Value", aggregate.P50Value);
            cmd.Parameters.AddWithValue("@p90Value", aggregate.P90Value);
            cmd.Parameters.AddWithValue("@p99Value", aggregate.P99Value);
            cmd.Parameters.AddWithValue("@stddevValue", aggregate.StddevValue);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Get daily aggregates for a metric over a time range (for trend charts).
    /// </summary>
    public async Task<List<MetricAggregate>> GetAggregatesAsync(
        string metricName, string periodType, DateTime since, string? entryPoint = null)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var sql = @"
            SELECT * FROM metric_aggregates
            WHERE org_id = @orgId AND metric_name = @metricName
              AND period_type = @periodType AND period_start >= @since";

        if (entryPoint != null)
            sql += " AND entry_point = @entryPoint";
        else
            sql += " AND entry_point IS NULL";

        sql += " ORDER BY period_start ASC";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@metricName", metricName);
        cmd.Parameters.AddWithValue("@periodType", periodType);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));
        if (entryPoint != null)
            cmd.Parameters.AddWithValue("@entryPoint", entryPoint);

        var aggregates = new List<MetricAggregate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            aggregates.Add(new MetricAggregate
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                OrgId = reader.GetString(reader.GetOrdinal("org_id")),
                PeriodStart = DateTime.Parse(reader.GetString(reader.GetOrdinal("period_start")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                PeriodType = reader.GetString(reader.GetOrdinal("period_type")),
                EntryPoint = reader.IsDBNull(reader.GetOrdinal("entry_point")) ? null : reader.GetString(reader.GetOrdinal("entry_point")),
                MetricName = reader.GetString(reader.GetOrdinal("metric_name")),
                SampleCount = reader.GetInt32(reader.GetOrdinal("sample_count")),
                AvgValue = reader.GetDouble(reader.GetOrdinal("avg_value")),
                MinValue = reader.GetDouble(reader.GetOrdinal("min_value")),
                MaxValue = reader.GetDouble(reader.GetOrdinal("max_value")),
                P50Value = reader.GetDouble(reader.GetOrdinal("p50_value")),
                P90Value = reader.GetDouble(reader.GetOrdinal("p90_value")),
                P99Value = reader.GetDouble(reader.GetOrdinal("p99_value")),
                StddevValue = reader.GetDouble(reader.GetOrdinal("stddev_value"))
            });
        }
        return aggregates;
    }

    // ================================================================
    //  ALERTS
    // ================================================================

    /// <summary>
    /// Insert a new alert.
    /// </summary>
    public async Task<long> InsertAlertAsync(MonitoringAlert alert)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO alerts (org_id, created_at, alert_type, severity, title, description,
                    entry_point, metric_name, current_value, baseline_value, threshold_value,
                    related_log_id, notified_via)
                VALUES (@orgId, @createdAt, @alertType, @severity, @title, @description,
                    @entryPoint, @metricName, @currentValue, @baselineValue, @thresholdValue,
                    @relatedLogId, @notifiedVia);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@orgId", alert.OrgId);
            cmd.Parameters.AddWithValue("@createdAt", alert.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@alertType", alert.AlertType);
            cmd.Parameters.AddWithValue("@severity", alert.Severity);
            cmd.Parameters.AddWithValue("@title", alert.Title);
            cmd.Parameters.AddWithValue("@description", alert.Description);
            cmd.Parameters.AddWithValue("@entryPoint", (object?)alert.EntryPoint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@metricName", (object?)alert.MetricName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@currentValue", (object?)alert.CurrentValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@baselineValue", (object?)alert.BaselineValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@thresholdValue", (object?)alert.ThresholdValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@relatedLogId", (object?)alert.RelatedLogId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@notifiedVia", (object?)alert.NotifiedVia ?? DBNull.Value);

            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            alert.Id = id;
            return id;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Check if a similar alert already exists within the dedup window (24h).
    /// </summary>
    public async Task<MonitoringAlert?> GetRecentAlertAsync(string alertType, string? entryPoint, string? metricName)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var since = DateTime.UtcNow.AddHours(-24).ToString("O");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM alerts
            WHERE org_id = @orgId AND alert_type = @alertType
              AND created_at >= @since AND is_dismissed = 0";

        if (entryPoint != null)
            cmd.CommandText += " AND entry_point = @entryPoint";
        if (metricName != null)
            cmd.CommandText += " AND metric_name = @metricName";

        cmd.CommandText += " ORDER BY created_at DESC LIMIT 1";

        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@alertType", alertType);
        cmd.Parameters.AddWithValue("@since", since);
        if (entryPoint != null)
            cmd.Parameters.AddWithValue("@entryPoint", entryPoint);
        if (metricName != null)
            cmd.Parameters.AddWithValue("@metricName", metricName);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadAlert(reader);

        return null;
    }

    /// <summary>
    /// Get alerts for the UI (with filtering).
    /// </summary>
    public async Task<List<MonitoringAlert>> GetAlertsAsync(int limit = 100, string? severityFilter = null, bool includeDismissed = false)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var sql = "SELECT * FROM alerts WHERE org_id = @orgId";

        if (!includeDismissed)
            sql += " AND is_dismissed = 0";
        if (severityFilter != null)
            sql += " AND severity = @severity";

        sql += " ORDER BY created_at DESC LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@limit", limit);
        if (severityFilter != null)
            cmd.Parameters.AddWithValue("@severity", severityFilter);

        var alerts = new List<MonitoringAlert>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            alerts.Add(ReadAlert(reader));
        }
        return alerts;
    }

    /// <summary>
    /// Get unread alert count.
    /// </summary>
    public async Task<int> GetUnreadCountAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alerts WHERE org_id = @orgId AND is_read = 0 AND is_dismissed = 0";
        cmd.Parameters.AddWithValue("@orgId", _orgId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Mark an alert as read.
    /// </summary>
    public async Task MarkAlertReadAsync(long alertId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE alerts SET is_read = 1 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", alertId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Dismiss an alert.
    /// </summary>
    public async Task DismissAlertAsync(long alertId, string? actionTaken = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE alerts SET is_dismissed = 1, dismissed_at = @dismissedAt, action_taken = @actionTaken
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", alertId);
            cmd.Parameters.AddWithValue("@dismissedAt", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@actionTaken", (object?)actionTaken ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Update user feedback for an alert (accurate or false_alarm).
    /// </summary>
    public async Task UpdateAlertFeedbackAsync(long alertId, string feedback)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE alerts SET user_feedback = @feedback, feedback_at = @feedbackAt
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", alertId);
            cmd.Parameters.AddWithValue("@feedback", feedback);
            cmd.Parameters.AddWithValue("@feedbackAt", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Mark all alerts as read.
    /// </summary>
    public async Task MarkAllReadAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE alerts SET is_read = 1 WHERE org_id = @orgId AND is_read = 0";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ================================================================
    //  MONITORING CONFIG
    // ================================================================

    /// <summary>
    /// Get a config value.
    /// </summary>
    public async Task<string?> GetConfigAsync(string key)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT config_value FROM monitoring_config WHERE org_id = @orgId AND config_key = @key";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    /// <summary>
    /// Set a config value.
    /// </summary>
    public async Task SetConfigAsync(string key, string value)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO monitoring_config (org_id, config_key, config_value, updated_at)
                VALUES (@orgId, @key, @value, @updatedAt)
                ON CONFLICT(org_id, config_key)
                DO UPDATE SET config_value = @value, updated_at = @updatedAt";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ================================================================
    //  DATA RETENTION / PRUNING
    // ================================================================

    /// <summary>
    /// Prune old data based on retention policies.
    /// </summary>
    public async Task PruneOldDataAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            var now = DateTime.UtcNow;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM log_snapshots WHERE captured_at < @snapshotCutoff;
                DELETE FROM metric_aggregates WHERE period_type = 'hourly' AND period_start < @hourlyCutoff;
                DELETE FROM metric_aggregates WHERE period_type = 'daily' AND period_start < @dailyCutoff;
                DELETE FROM alerts WHERE is_dismissed = 1 AND dismissed_at < @dismissedCutoff;
                DELETE FROM alerts WHERE created_at < @alertCutoff;
                DELETE FROM shield_events WHERE event_date < @shieldCutoff;
            ";

            cmd.Parameters.AddWithValue("@snapshotCutoff", now.AddDays(-90).ToString("O"));
            cmd.Parameters.AddWithValue("@hourlyCutoff", now.AddDays(-30).ToString("O"));
            cmd.Parameters.AddWithValue("@dailyCutoff", now.AddDays(-365).ToString("O"));
            cmd.Parameters.AddWithValue("@dismissedCutoff", now.AddDays(-30).ToString("O"));
            cmd.Parameters.AddWithValue("@alertCutoff", now.AddDays(-180).ToString("O"));
            cmd.Parameters.AddWithValue("@shieldCutoff", now.AddDays(-30).ToString("O"));

            var deleted = await cmd.ExecuteNonQueryAsync();
            if (deleted > 0)
                Log.Information("Pruned {Count} old monitoring records", deleted);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ================================================================
    //  SHIELD EVENT LOG METHODS
    // ================================================================

    /// <summary>
    /// Check if a Shield EventLogFile has already been processed.
    /// </summary>
    public async Task<bool> IsLogFileProcessedAsync(string logFileId)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM shield_log_files WHERE log_file_id = @id AND org_id = @orgId";
        cmd.Parameters.AddWithValue("@id", logFileId);
        cmd.Parameters.AddWithValue("@orgId", _orgId);

        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    /// <summary>
    /// Record that a Shield EventLogFile has been processed.
    /// </summary>
    public async Task InsertShieldLogFileRecordAsync(ShieldLogFileRecord record)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO shield_log_files (org_id, event_type, log_date, log_file_id, interval_type, processed_at, record_count, file_size)
                VALUES (@orgId, @eventType, @logDate, @logFileId, @interval, @processedAt, @recordCount, @fileSize)";

            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@eventType", record.EventType);
            cmd.Parameters.AddWithValue("@logDate", record.LogDate);
            cmd.Parameters.AddWithValue("@logFileId", record.LogFileId);
            cmd.Parameters.AddWithValue("@interval", record.IntervalType);
            cmd.Parameters.AddWithValue("@processedAt", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@recordCount", record.RecordCount);
            cmd.Parameters.AddWithValue("@fileSize", record.FileSize);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Insert a batch of parsed Shield events.
    /// </summary>
    public async Task InsertShieldEventsAsync(List<ShieldEvent> events)
    {
        if (events.Count == 0) return;

        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            foreach (var ev in events)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO shield_events (org_id, event_type, event_date, user_id, uri, duration_ms, cpu_time_ms,
                                                row_count, status_code, is_success, client_ip, extra_json, is_anomaly, anomaly_reason)
                    VALUES (@orgId, @eventType, @eventDate, @userId, @uri, @durationMs, @cpuTimeMs,
                            @rowCount, @statusCode, @isSuccess, @clientIp, @extraJson, @isAnomaly, @anomalyReason)";

                cmd.Parameters.AddWithValue("@orgId", _orgId);
                cmd.Parameters.AddWithValue("@eventType", ev.EventType);
                cmd.Parameters.AddWithValue("@eventDate", ev.EventDate);
                cmd.Parameters.AddWithValue("@userId", (object?)ev.UserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@uri", (object?)ev.Uri ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@durationMs", (object?)ev.DurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cpuTimeMs", (object?)ev.CpuTimeMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rowCount", (object?)ev.RowCount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@statusCode", (object?)ev.StatusCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@isSuccess", ev.IsSuccess ? 1 : 0);
                cmd.Parameters.AddWithValue("@clientIp", (object?)ev.ClientIp ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@extraJson", (object?)ev.ExtraJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@isAnomaly", ev.IsAnomaly ? 1 : 0);
                cmd.Parameters.AddWithValue("@anomalyReason", (object?)ev.AnomalyReason ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Get Shield events of a specific type from a recent time window.
    /// </summary>
    public async Task<List<ShieldEvent>> GetRecentShieldEventsAsync(string eventType, DateTime since)
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM shield_events
            WHERE org_id = @orgId AND event_type = @eventType AND event_date >= @since
            ORDER BY event_date DESC";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@eventType", eventType);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));

        var events = new List<ShieldEvent>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new ShieldEvent
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                OrgId = reader.GetString(reader.GetOrdinal("org_id")),
                EventType = reader.GetString(reader.GetOrdinal("event_type")),
                EventDate = reader.GetString(reader.GetOrdinal("event_date")),
                UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : reader.GetString(reader.GetOrdinal("user_id")),
                Uri = reader.IsDBNull(reader.GetOrdinal("uri")) ? null : reader.GetString(reader.GetOrdinal("uri")),
                DurationMs = reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetDouble(reader.GetOrdinal("duration_ms")),
                CpuTimeMs = reader.IsDBNull(reader.GetOrdinal("cpu_time_ms")) ? null : reader.GetDouble(reader.GetOrdinal("cpu_time_ms")),
                RowCount = reader.IsDBNull(reader.GetOrdinal("row_count")) ? null : reader.GetInt32(reader.GetOrdinal("row_count")),
                StatusCode = reader.IsDBNull(reader.GetOrdinal("status_code")) ? null : reader.GetInt32(reader.GetOrdinal("status_code")),
                IsSuccess = reader.GetInt32(reader.GetOrdinal("is_success")) != 0,
                ClientIp = reader.IsDBNull(reader.GetOrdinal("client_ip")) ? null : reader.GetString(reader.GetOrdinal("client_ip")),
                ExtraJson = reader.IsDBNull(reader.GetOrdinal("extra_json")) ? null : reader.GetString(reader.GetOrdinal("extra_json")),
                IsAnomaly = reader.GetInt32(reader.GetOrdinal("is_anomaly")) != 0,
                AnomalyReason = reader.IsDBNull(reader.GetOrdinal("anomaly_reason")) ? null : reader.GetString(reader.GetOrdinal("anomaly_reason"))
            });
        }
        return events;
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private static LogSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        return new LogSnapshot
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            LogId = reader.GetString(reader.GetOrdinal("log_id")),
            OrgId = reader.GetString(reader.GetOrdinal("org_id")),
            CapturedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("captured_at")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            EntryPoint = reader.IsDBNull(reader.GetOrdinal("entry_point")) ? "" : reader.GetString(reader.GetOrdinal("entry_point")),
            OperationType = reader.IsDBNull(reader.GetOrdinal("operation_type")) ? "" : reader.GetString(reader.GetOrdinal("operation_type")),
            LogUser = reader.IsDBNull(reader.GetOrdinal("log_user")) ? "" : reader.GetString(reader.GetOrdinal("log_user")),
            DurationMs = reader.GetDouble(reader.GetOrdinal("duration_ms")),
            CpuTimeMs = reader.GetInt32(reader.GetOrdinal("cpu_time_ms")),
            SoqlCount = reader.GetInt32(reader.GetOrdinal("soql_count")),
            SoqlLimit = reader.GetInt32(reader.GetOrdinal("soql_limit")),
            DmlCount = reader.GetInt32(reader.GetOrdinal("dml_count")),
            DmlLimit = reader.GetInt32(reader.GetOrdinal("dml_limit")),
            QueryRows = reader.GetInt32(reader.GetOrdinal("query_rows")),
            QueryRowsLimit = reader.GetInt32(reader.GetOrdinal("query_rows_limit")),
            HeapSize = reader.GetInt32(reader.GetOrdinal("heap_size")),
            HeapLimit = reader.GetInt32(reader.GetOrdinal("heap_limit")),
            CalloutCount = reader.GetInt32(reader.GetOrdinal("callout_count")),
            CalloutLimit = reader.GetInt32(reader.GetOrdinal("callout_limit")),
            HealthScore = reader.GetInt32(reader.GetOrdinal("health_score")),
            HealthGrade = reader.IsDBNull(reader.GetOrdinal("health_grade")) ? "" : reader.GetString(reader.GetOrdinal("health_grade")),
            BulkSafetyGrade = reader.IsDBNull(reader.GetOrdinal("bulk_safety_grade")) ? "" : reader.GetString(reader.GetOrdinal("bulk_safety_grade")),
            HasErrors = reader.GetInt32(reader.GetOrdinal("has_errors")) != 0,
            TransactionFailed = reader.GetInt32(reader.GetOrdinal("transaction_failed")) != 0,
            ErrorCount = reader.GetInt32(reader.GetOrdinal("error_count")),
            HandledExceptionCount = reader.GetInt32(reader.GetOrdinal("handled_exception_count")),
            DuplicateQueryCount = reader.GetInt32(reader.GetOrdinal("duplicate_query_count")),
            NPlusOneWorst = reader.GetInt32(reader.GetOrdinal("n_plus_one_worst")),
            StackDepthMax = reader.GetInt32(reader.GetOrdinal("stack_depth_max")),
            IsAsync = reader.GetInt32(reader.GetOrdinal("is_async")) != 0,
            IsTruncated = reader.GetInt32(reader.GetOrdinal("is_truncated")) != 0,
            Source = reader.IsDBNull(reader.GetOrdinal("source")) ? "debug_log" : reader.GetString(reader.GetOrdinal("source"))
        };
    }

    private static MonitoringAlert ReadAlert(SqliteDataReader reader)
    {
        return new MonitoringAlert
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            OrgId = reader.GetString(reader.GetOrdinal("org_id")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            AlertType = reader.GetString(reader.GetOrdinal("alert_type")),
            Severity = reader.GetString(reader.GetOrdinal("severity")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            EntryPoint = reader.IsDBNull(reader.GetOrdinal("entry_point")) ? null : reader.GetString(reader.GetOrdinal("entry_point")),
            MetricName = reader.IsDBNull(reader.GetOrdinal("metric_name")) ? null : reader.GetString(reader.GetOrdinal("metric_name")),
            CurrentValue = reader.IsDBNull(reader.GetOrdinal("current_value")) ? null : reader.GetDouble(reader.GetOrdinal("current_value")),
            BaselineValue = reader.IsDBNull(reader.GetOrdinal("baseline_value")) ? null : reader.GetDouble(reader.GetOrdinal("baseline_value")),
            ThresholdValue = reader.IsDBNull(reader.GetOrdinal("threshold_value")) ? null : reader.GetDouble(reader.GetOrdinal("threshold_value")),
            IsRead = reader.GetInt32(reader.GetOrdinal("is_read")) != 0,
            IsDismissed = reader.GetInt32(reader.GetOrdinal("is_dismissed")) != 0,
            DismissedAt = reader.IsDBNull(reader.GetOrdinal("dismissed_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("dismissed_at")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            ActionTaken = reader.IsDBNull(reader.GetOrdinal("action_taken")) ? null : reader.GetString(reader.GetOrdinal("action_taken")),
            RelatedLogId = reader.IsDBNull(reader.GetOrdinal("related_log_id")) ? null : reader.GetString(reader.GetOrdinal("related_log_id")),
            NotifiedVia = reader.IsDBNull(reader.GetOrdinal("notified_via")) ? null : reader.GetString(reader.GetOrdinal("notified_via")),
            UserFeedback = reader.IsDBNull(reader.GetOrdinal("user_feedback")) ? null : reader.GetString(reader.GetOrdinal("user_feedback")),
            FeedbackAt = reader.IsDBNull(reader.GetOrdinal("feedback_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("feedback_at")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writeLock.Dispose();
            _disposed = true;
        }
    }
}
