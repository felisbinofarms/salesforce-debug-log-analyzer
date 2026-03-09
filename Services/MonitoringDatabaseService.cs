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
    private const int SchemaVersion = 3;

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

        if (fromVersion < 3)
        {
            // Add affected_user_count to alerts table (schema v3)
            using var cmd3 = connection.CreateCommand();
            cmd3.Transaction = transaction;
            cmd3.CommandText = "ALTER TABLE alerts ADD COLUMN affected_user_count INTEGER;";
            try { cmd3.ExecuteNonQuery(); }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column")) { }
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
                    related_log_id, notified_via, affected_user_count)
                VALUES (@orgId, @createdAt, @alertType, @severity, @title, @description,
                    @entryPoint, @metricName, @currentValue, @baselineValue, @thresholdValue,
                    @relatedLogId, @notifiedVia, @affectedUserCount);
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
            cmd.Parameters.AddWithValue("@affectedUserCount", (object?)alert.AffectedUserCount ?? DBNull.Value);

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
    /// Get the latest event_date across all Shield events for this org.
    /// Returns null if no events exist.
    /// </summary>
    public async Task<DateTime?> GetLatestShieldEventDateAsync()
    {
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(event_date) FROM shield_events WHERE org_id = @orgId";
        cmd.Parameters.AddWithValue("@orgId", _orgId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string dateStr && DateTime.TryParse(dateStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        return null;
    }

    // ================================================================
    //  SHIELD DASHBOARD AGGREGATION
    // ================================================================

    /// <summary>
    /// Build the full Shield dashboard with actionable insights.
    /// Compares current 24h vs previous 24h for trend analysis.
    /// All heavy work is done in SQL to handle millions of events efficiently.
    /// </summary>
    public async Task<ShieldDashboardData> GetShieldDashboardDataAsync(int hours = 24)
    {
        var data = new ShieldDashboardData();
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var anchor = await GetLatestShieldEventDateAsync() ?? DateTime.UtcNow;
        var since = anchor.AddHours(-hours).ToString("O");
        var sincePrev = anchor.AddHours(-hours * 2).ToString("O"); // previous period for trend comparison

        // 1. Date range + summary
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT MIN(event_date), MAX(event_date), COUNT(*), COUNT(DISTINCT user_id)
                                FROM shield_events WHERE org_id = @orgId AND event_date >= @since";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync() && !r.IsDBNull(0))
            {
                DateTime.TryParse(r.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind, out var from);
                DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var to);
                data.DataFrom = from;
                data.DataTo = to;
                data.TotalEvents = r.GetInt64(2);
                data.UniqueUsers = r.GetInt32(3);
            }
        }

        // 2. Apex exceptions — the most actionable: what code is breaking?
        await BuildExceptionInsights(connection, data, since, sincePrev);

        // 3. Failed logins — brute force detection + reason breakdown
        await BuildLoginInsights(connection, data, since, sincePrev);

        // 4. Page performance degradation — what got slower vs 7-day baseline?
        await BuildPageInsights(connection, data, since, anchor);

        // 5. API performance — what's slow and getting worse?
        await BuildApiInsights(connection, data, since, anchor);

        // 6. Top API endpoints (reference table, kept for detail)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT uri, COUNT(*) as cnt, AVG(duration_ms), MAX(duration_ms), COUNT(DISTINCT user_id)
                                FROM shield_events
                                WHERE org_id = @orgId AND event_type = 'API' AND event_date >= @since AND uri IS NOT NULL
                                GROUP BY uri ORDER BY cnt DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                data.TopApiEndpoints.Add(new ShieldDashboardRow
                {
                    Label = r.GetString(0), Count = r.GetInt64(1),
                    AvgDurationMs = r.IsDBNull(2) ? null : r.GetDouble(2),
                    MaxDurationMs = r.IsDBNull(3) ? null : r.GetDouble(3),
                    UniqueUsers = r.GetInt32(4)
                });
        }

        // Sort insights by impact score (highest first)
        data.Insights.Sort((a, b) => b.ImpactScore.CompareTo(a.ImpactScore));

        // Populate anomaly-affected user count (deduplicated across all anomaly types)
        data.AnomalyAffectedUsers = await GetAffectedUsersCountAsync(hours);

        // Build 24h hourly sparkline (total events per hour bucket)
        await BuildActivitySparklineAsync(connection, data, since);

        return data;
    }

    private async Task BuildActivitySparklineAsync(
        SqliteConnection connection, ShieldDashboardData data, string since)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT substr(event_date, 1, 13) AS hour_bucket, COUNT(*) AS cnt
            FROM shield_events
            WHERE org_id = @orgId AND event_date >= @since
            GROUP BY hour_bucket
            ORDER BY hour_bucket ASC
            LIMIT 48";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@since", since);

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var bucket = r.GetString(0); // e.g. "2026-03-15T14"
            if (DateTime.TryParse(bucket + ":00:00Z", null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                data.ActivitySparkline.Add(new SparklinePoint(dt, r.GetDouble(1)));
            }
        }
    }

    private async Task BuildExceptionInsights(SqliteConnection connection, ShieldDashboardData data, string since, string sincePrev)
    {
        // Current period: exceptions grouped by type with messages + stack traces
        var exceptions = new List<(string type, long count, int users, string? message, string? stackTrace)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(NULLIF(uri, ''), 'Unknown') as ex_type,
                       COUNT(*) as cnt, COUNT(DISTINCT user_id) as users
                FROM shield_events
                WHERE org_id = @orgId AND event_type = 'ApexUnexpectedException' AND event_date >= @since
                GROUP BY ex_type ORDER BY cnt DESC LIMIT 15";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                exceptions.Add((r.GetString(0), r.GetInt64(1), r.GetInt32(2), null, null));
        }

        data.ExceptionTotal = (int)exceptions.Sum(e => e.count);

        // Enrich each exception type with message + stack trace from extra_json
        for (int i = 0; i < exceptions.Count; i++)
        {
            var ex = exceptions[i];
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT extra_json FROM shield_events
                                WHERE org_id = @orgId AND event_type = 'ApexUnexpectedException'
                                      AND event_date >= @since AND COALESCE(NULLIF(uri, ''), 'Unknown') = @uri
                                      AND extra_json IS NOT NULL LIMIT 1";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@uri", ex.type);
            var jsonStr = await cmd.ExecuteScalarAsync() as string;
            if (jsonStr != null)
            {
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(jsonStr);
                    var msg = json["exceptionMessage"]?.ToString();
                    var stack = json["stackTrace"]?.ToString();
                    exceptions[i] = (ex.type, ex.count, ex.users, msg, stack);
                }
                catch { }
            }
        }

        // Previous period: count for trend comparison
        var prevCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(NULLIF(uri, ''), 'Unknown'), COUNT(*)
                FROM shield_events
                WHERE org_id = @orgId AND event_type = 'ApexUnexpectedException'
                      AND event_date >= @sincePrev AND event_date < @since
                GROUP BY 1";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@sincePrev", sincePrev);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                prevCounts[r.GetString(0)] = r.GetInt64(1);
        }

        // Generate insights
        foreach (var (type, count, users, message, stackTrace) in exceptions)
        {
            prevCounts.TryGetValue(type, out var prevCount);
            var trend = prevCount > 0
                ? $"↑ {(count - prevCount) * 100 / prevCount}% vs previous {(since.Contains("24") ? "24h" : "period")}"
                : prevCount == 0 && count > 0 ? "🆕 New — not seen in previous period" : null;
            if (prevCount > 0 && count <= prevCount)
                trend = count == prevCount ? "→ Same as previous period" : $"↓ {(prevCount - count) * 100 / prevCount}% vs previous period";

            var severity = count >= 50 || users >= 5 ? "critical" : count >= 5 ? "warning" : "info";
            var title = type.Length > 80 ? type[..80] + "…" : type;

            var detail = "";
            if (!string.IsNullOrEmpty(message))
                detail += message + "\n";
            if (!string.IsNullOrEmpty(stackTrace))
                detail += "\n" + stackTrace;

            var recommendation = InferExceptionRecommendation(type, message, stackTrace);

            data.Insights.Add(new ShieldInsight
            {
                Severity = severity,
                Category = "exception",
                Title = title,
                Description = $"{count:N0} occurrences | {users} user{(users == 1 ? "" : "s")} affected",
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
                Recommendation = recommendation,
                Count = count,
                AffectedUsers = users,
                TrendText = trend,
                ImpactScore = count * (severity == "critical" ? 3.0 : severity == "warning" ? 2.0 : 1.0) + users * 10
            });

            data.ApexExceptions.Add(new ShieldDashboardRow
            {
                Label = type, Count = count, UniqueUsers = users,
                SubLabel = message, SeverityColor = "#F85149"
            });
        }
    }

    private static readonly Dictionary<string, string> LoginTypeMap = new()
    {
        ["3"] = "Partner Portal", ["4"] = "SSO SAML", ["5"] = "Customer Portal",
        ["6"] = "OAuth Refresh Token", ["7"] = "AppExchange", ["8"] = "SAML SSO",
        ["9"] = "SAML SSO Internal", ["A"] = "Application Login",
        ["I"] = "OAuth API", ["i"] = "OAuth JS-API", ["r"] = "Remote Access 2.0"
    };

    private static readonly Dictionary<string, string> LoginStatusMap = new()
    {
        ["LOGIN_SAML_INVALID_IN_RES_TO"] = "SSO/IdP misconfiguration (stale SAML assertion)",
        ["LOGIN_TWOFACTOR_REQ"] = "MFA challenge required",
        ["LOGIN_ERROR_INVALID_PASSWORD"] = "Invalid password",
        ["LOGIN_OAUTH_NO_CONSUMER"] = "Deleted/disabled Connected App",
        ["OAUTH_TOKEN_IN_PROCESS"] = "Token refresh race condition",
        ["OAUTH_APP_ACCESS_DENIED"] = "OAuth app access denied",
        ["LOGIN_ERROR_USER_INACTIVE"] = "Inactive user account",
        ["LOGIN_ERROR_ORG_LOCKOUT"] = "Org locked out",
        ["LOGIN_ERROR_SECURITY_TOKEN_REQUIRED"] = "Security token required",
        ["LOGIN_CHALLENGE_SENT"] = "Verification code sent",
        ["LOGIN_ERROR_OAUTH_INVALID_GRANT"] = "Invalid OAuth grant",
    };

    private static string DecodeLoginType(string? code) =>
        code != null && LoginTypeMap.TryGetValue(code, out var name) ? name : code ?? "Unknown";

    private static string DecodeBrowser(string? browser)
    {
        if (string.IsNullOrEmpty(browser)) return "Unknown";
        if (browser.Contains("Web Service Connector", StringComparison.OrdinalIgnoreCase))
            return "Salesforce WSC (Java Integration)";
        if (browser.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (browser.Contains("CriOS", StringComparison.OrdinalIgnoreCase)) return "Chrome (iOS)";
        if (browser.Contains("Safari", StringComparison.OrdinalIgnoreCase) &&
            browser.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (browser.Contains("Safari", StringComparison.OrdinalIgnoreCase)) return "Safari";
        if (browser.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (browser.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            browser.Contains("Mobile", StringComparison.OrdinalIgnoreCase)) return "Mobile Browser";
        return browser.Length > 40 ? browser[..40] + "…" : browser;
    }

    private static string FriendlyReason(string? status) =>
        status != null && LoginStatusMap.TryGetValue(status, out var friendly) ? friendly : status ?? "Unknown";

    private async Task BuildLoginInsights(SqliteConnection connection, ShieldDashboardData data, string since, string sincePrev)
    {
        // Rich per-IP analysis with reason, browser, login type breakdown
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT client_ip, COUNT(*) as attempts, COUNT(DISTINCT user_id) as targets,
                       GROUP_CONCAT(DISTINCT user_id) as user_list
                FROM shield_events
                WHERE org_id = @orgId AND event_type = 'Login' AND is_success = 0
                      AND event_date >= @since AND client_ip IS NOT NULL
                GROUP BY client_ip HAVING attempts >= 3
                ORDER BY attempts DESC LIMIT 15";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var ip = r.GetString(0);
                var detail = new LoginFailureDetail
                {
                    IpAddress = ip,
                    Attempts = r.GetInt64(1),
                    UniqueTargets = r.GetInt32(2),
                    UserIds = (r.IsDBNull(3) ? "" : r.GetString(3)).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Severity = r.GetInt64(1) >= 20 || r.GetInt32(2) >= 5 ? "critical"
                             : r.GetInt64(1) >= 5 ? "warning" : "info"
                };
                data.LoginDetails.Add(detail);
            }
        }

        // For each IP, get the reason/browser/loginType breakdown
        foreach (var detail in data.LoginDetails)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT json_extract(extra_json, '$.loginStatus') as reason,
                       json_extract(extra_json, '$.loginType') as lt,
                       json_extract(extra_json, '$.browser') as browser,
                       COUNT(*) as cnt
                FROM shield_events
                WHERE org_id = @orgId AND event_type = 'Login' AND is_success = 0
                      AND event_date >= @since AND client_ip = @ip
                GROUP BY reason, lt, browser ORDER BY cnt DESC";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@ip", detail.IpAddress);
            using var r = await cmd.ExecuteReaderAsync();
            string? topReason = null, topLoginType = null, topBrowser = null;
            while (await r.ReadAsync())
            {
                var reason = r.IsDBNull(0) ? "Unknown" : r.GetString(0);
                var lt = r.IsDBNull(1) ? null : r.GetString(1);
                var browser = r.IsDBNull(2) ? null : r.GetString(2);
                var cnt = r.GetInt64(3);

                if (topReason == null) { topReason = reason; topLoginType = lt; topBrowser = browser; }

                if (!detail.ReasonBreakdown.ContainsKey(FriendlyReason(reason)))
                    detail.ReasonBreakdown[FriendlyReason(reason)] = cnt;
                else
                    detail.ReasonBreakdown[FriendlyReason(reason)] += cnt;
            }
            detail.PrimaryReason = topReason ?? "Unknown";
            detail.PrimaryReasonFriendly = FriendlyReason(topReason);
            detail.LoginTypeDecoded = DecodeLoginType(topLoginType);
            detail.BrowserOrApp = DecodeBrowser(topBrowser);
        }

        // Also keep the simple FailedLogins reference table (by reason)
        using (var cmd2 = connection.CreateCommand())
        {
            cmd2.CommandText = @"
                SELECT json_extract(extra_json, '$.loginStatus') as reason,
                       COUNT(*) as cnt, COUNT(DISTINCT user_id) as users, COUNT(DISTINCT client_ip) as ips
                FROM shield_events
                WHERE org_id = @orgId AND event_type = 'Login' AND event_date >= @since AND is_success = 0
                GROUP BY reason ORDER BY cnt DESC LIMIT 15";
            cmd2.Parameters.AddWithValue("@orgId", _orgId);
            cmd2.Parameters.AddWithValue("@since", since);
            using var r = await cmd2.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var reason = r.IsDBNull(0) ? "Unknown" : r.GetString(0);
                var cnt = r.GetInt64(1);
                data.FailedLogins.Add(new ShieldDashboardRow
                {
                    Label = FriendlyReason(reason), Count = cnt, UniqueUsers = r.GetInt32(2),
                    SubLabel = $"{r.GetInt32(3)} IPs",
                    SeverityColor = cnt > 20 ? "#F85149" : "#D29922"
                });
            }
        }

        data.FailedLoginTotal = (int)data.FailedLogins.Sum(f => f.Count);

        // Insights: suspicious IPs get insight cards
        foreach (var detail in data.LoginDetails.Where(d => d.Attempts >= 5))
        {
            var severity = detail.Severity;
            var targetDesc = detail.UniqueTargets == 1 ? "1 account" : $"{detail.UniqueTargets} accounts";

            // Determine what this IP is doing
            string recommendation;
            if (detail.BrowserOrApp.Contains("WSC", StringComparison.OrdinalIgnoreCase) ||
                detail.BrowserOrApp.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            {
                recommendation = $"A Java integration ({detail.BrowserOrApp}) is failing with {detail.PrimaryReasonFriendly}. " +
                    "Check and update the service account credentials used by this integration.";
            }
            else if (detail.PrimaryReason == "LOGIN_SAML_INVALID_IN_RES_TO")
            {
                recommendation = "Your SSO Identity Provider (Okta/Azure AD) is sending stale SAML assertions. " +
                    "Check the IdP configuration — this affects multiple users and is a systemic SSO issue.";
            }
            else if (detail.PrimaryReason == "LOGIN_TWOFACTOR_REQ")
            {
                recommendation = "These are MFA challenges, not true failures. Salesforce logs them as 'failed' during the verification step. " +
                    "This is normal unless the count is unusually high.";
            }
            else
            {
                recommendation = detail.UniqueTargets >= 3
                    ? "Multiple accounts targeted from one IP. Possible brute force attack — consider blocking this IP."
                    : "Monitor this IP. Reset the user's password if they don't recognize this activity.";
            }

            data.Insights.Add(new ShieldInsight
            {
                Severity = severity,
                Category = "security",
                Title = $"Failed logins from {detail.IpAddress}",
                Description = $"{detail.Attempts} attempts → {targetDesc} | {detail.LoginTypeDecoded} via {detail.BrowserOrApp}",
                Detail = $"Top reason: {detail.PrimaryReasonFriendly}" +
                    (detail.HasMultipleReasons ? $"\nAll reasons: {detail.ReasonsDisplay}" : ""),
                Recommendation = recommendation,
                Count = detail.Attempts,
                AffectedUsers = detail.UniqueTargets,
                ImpactScore = detail.Attempts * (severity == "critical" ? 3.0 : 2.0) + detail.UniqueTargets * 20
            });
        }

        // Overall trend insight (only if no suspicious IPs already created insights)
        long prevFailures = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM shield_events
                                WHERE org_id = @orgId AND event_type = 'Login' AND is_success = 0
                                      AND event_date >= @sincePrev AND event_date < @since";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@sincePrev", sincePrev);
            cmd.Parameters.AddWithValue("@since", since);
            prevFailures = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }

        if (data.FailedLoginTotal > 0 && !data.LoginDetails.Any(d => d.Attempts >= 5))
        {
            var trend = prevFailures > 0
                ? (data.FailedLoginTotal > prevFailures
                    ? $"↑ {(data.FailedLoginTotal - prevFailures) * 100 / prevFailures}% vs previous period"
                    : $"↓ {(prevFailures - data.FailedLoginTotal) * 100 / prevFailures}% vs previous period")
                : null;
            data.Insights.Add(new ShieldInsight
            {
                Severity = data.FailedLoginTotal >= 50 ? "warning" : "info",
                Category = "security",
                Title = $"{data.FailedLoginTotal} failed login attempts",
                Description = $"Top reason: {data.FailedLogins.FirstOrDefault()?.Label ?? "Unknown"} | {data.FailedLogins.Sum(f => f.UniqueUsers)} users",
                TrendText = trend,
                Count = data.FailedLoginTotal,
                AffectedUsers = data.FailedLogins.Sum(f => f.UniqueUsers),
                ImpactScore = data.FailedLoginTotal * 0.5
            });
        }
    }

    private async Task BuildPageInsights(SqliteConnection connection, ShieldDashboardData data, string since, DateTime anchor)
    {
        // Compare current 24h EPT vs 7-day baseline (excluding current period)
        var since7d = anchor.AddDays(-7).ToString("O");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                WITH current_ept AS (
                    SELECT COALESCE(uri, 'Unknown') as page, AVG(duration_ms) as avg_now,
                           MAX(duration_ms) as max_now, COUNT(*) as views_now, COUNT(DISTINCT user_id) as users_now
                    FROM shield_events
                    WHERE org_id = @orgId AND event_type = 'LightningPageView' AND event_date >= @since
                          AND duration_ms IS NOT NULL
                    GROUP BY page HAVING views_now >= 3
                ),
                baseline_ept AS (
                    SELECT COALESCE(uri, 'Unknown') as page, AVG(duration_ms) as avg_baseline, COUNT(*) as views_base
                    FROM shield_events
                    WHERE org_id = @orgId AND event_type = 'LightningPageView'
                          AND event_date >= @since7d AND event_date < @since
                          AND duration_ms IS NOT NULL
                    GROUP BY page HAVING views_base >= 10
                )
                SELECT c.page, c.avg_now, b.avg_baseline, c.max_now, c.views_now, c.users_now,
                       ROUND((c.avg_now - b.avg_baseline) * 100.0 / b.avg_baseline, 0) as pct_change
                FROM current_ept c
                JOIN baseline_ept b ON c.page = b.page
                WHERE c.avg_now > b.avg_baseline * 1.3
                ORDER BY (c.avg_now - b.avg_baseline) DESC
                LIMIT 10";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@since7d", since7d);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var page = r.GetString(0);
                var avgNow = r.GetDouble(1);
                var avgBaseline = r.GetDouble(2);
                var maxNow = r.GetDouble(3);
                var views = r.GetInt64(4);
                var users = r.GetInt32(5);
                var pctChange = r.GetDouble(6);
                var severity = avgNow > 5000 ? "critical" : avgNow > 3000 || pctChange > 100 ? "warning" : "info";

                data.Insights.Add(new ShieldInsight
                {
                    Severity = severity,
                    Category = "performance",
                    Title = $"{page} got {pctChange:F0}% slower",
                    Description = $"EPT: {avgBaseline:F0}ms → {avgNow:F0}ms (max {maxNow:F0}ms) | {views} views, {users} users",
                    Recommendation = avgNow > 3000
                        ? $"Page load exceeds 3s. Check recently deployed components on this layout. Review Aura/LWC component load times."
                        : $"Performance degraded but still acceptable. Monitor for further regression.",
                    TrendText = $"↑ {pctChange:F0}% vs 7-day average",
                    Count = views,
                    AffectedUsers = users,
                    ImpactScore = pctChange * users * 0.1 + (avgNow > 3000 ? 100 : 0)
                });
                data.SlowPageCount++;
            }
        }

        // Top pages (reference table)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT COALESCE(uri, 'Unknown'), COUNT(*), AVG(duration_ms), MAX(duration_ms), COUNT(DISTINCT user_id)
                                FROM shield_events
                                WHERE org_id = @orgId AND event_type = 'LightningPageView' AND event_date >= @since
                                GROUP BY 1 ORDER BY 2 DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                data.TopPages.Add(new ShieldDashboardRow
                {
                    Label = r.GetString(0), Count = r.GetInt64(1),
                    AvgDurationMs = r.IsDBNull(2) ? null : r.GetDouble(2),
                    MaxDurationMs = r.IsDBNull(3) ? null : r.GetDouble(3),
                    UniqueUsers = r.GetInt32(4),
                    SeverityColor = (r.IsDBNull(2) ? 0 : r.GetDouble(2)) > 3000 ? "#F85149"
                                  : (r.IsDBNull(2) ? 0 : r.GetDouble(2)) > 1000 ? "#D29922" : null
                });
        }
    }

    private async Task BuildApiInsights(SqliteConnection connection, ShieldDashboardData data, string since, DateTime anchor)
    {
        // Slowest APIs vs 7-day baseline
        var since7d = anchor.AddDays(-7).ToString("O");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                WITH current_api AS (
                    SELECT uri, AVG(duration_ms) as avg_now, MAX(duration_ms) as max_now,
                           COUNT(*) as calls_now, COUNT(DISTINCT user_id) as users_now
                    FROM shield_events
                    WHERE org_id = @orgId AND event_type = 'API' AND event_date >= @since
                          AND uri IS NOT NULL AND duration_ms IS NOT NULL
                    GROUP BY uri HAVING calls_now >= 10
                ),
                baseline_api AS (
                    SELECT uri, AVG(duration_ms) as avg_baseline, COUNT(*) as calls_base
                    FROM shield_events
                    WHERE org_id = @orgId AND event_type = 'API' AND event_date >= @since7d AND event_date < @since
                          AND uri IS NOT NULL AND duration_ms IS NOT NULL
                    GROUP BY uri HAVING calls_base >= 20
                )
                SELECT c.uri, c.avg_now, b.avg_baseline, c.max_now, c.calls_now, c.users_now,
                       ROUND((c.avg_now - b.avg_baseline) * 100.0 / b.avg_baseline, 0) as pct_change
                FROM current_api c
                JOIN baseline_api b ON c.uri = b.uri
                WHERE c.avg_now > b.avg_baseline * 1.5 AND c.avg_now > 500
                ORDER BY (c.avg_now - b.avg_baseline) * c.calls_now DESC
                LIMIT 10";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            cmd.Parameters.AddWithValue("@since7d", since7d);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var uri = r.GetString(0);
                var avgNow = r.GetDouble(1);
                var avgBaseline = r.GetDouble(2);
                var maxNow = r.GetDouble(3);
                var calls = r.GetInt64(4);
                var users = r.GetInt32(5);
                var pctChange = r.GetDouble(6);
                var severity = avgNow > 5000 ? "critical" : avgNow > 2000 || pctChange > 200 ? "warning" : "info";

                data.Insights.Add(new ShieldInsight
                {
                    Severity = severity,
                    Category = "performance",
                    Title = $"API degradation: {(uri.Length > 60 ? uri[..60] + "…" : uri)}",
                    Description = $"Avg: {avgBaseline:F0}ms → {avgNow:F0}ms (max {maxNow:F0}ms) | {calls:N0} calls, {users} users",
                    Recommendation = $"This API slowed {pctChange:F0}%. Check for new triggers/flows on the affected object, N+1 queries, or increased data volume.",
                    TrendText = $"↑ {pctChange:F0}% vs 7-day average",
                    Count = calls,
                    AffectedUsers = users,
                    ImpactScore = pctChange * calls * 0.001 + (avgNow > 3000 ? 100 : 0)
                });
            }
        }

        // Slowest APIs reference table (absolute, not relative)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT uri, COUNT(*), AVG(duration_ms), MAX(duration_ms), COUNT(DISTINCT user_id)
                                FROM shield_events
                                WHERE org_id = @orgId AND event_type = 'API' AND event_date >= @since
                                      AND uri IS NOT NULL AND duration_ms IS NOT NULL
                                GROUP BY uri HAVING COUNT(*) >= 10 ORDER BY 3 DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@since", since);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                data.SlowestApiEndpoints.Add(new ShieldDashboardRow
                {
                    Label = r.GetString(0), Count = r.GetInt64(1),
                    AvgDurationMs = r.IsDBNull(2) ? null : r.GetDouble(2),
                    MaxDurationMs = r.IsDBNull(3) ? null : r.GetDouble(3),
                    UniqueUsers = r.GetInt32(4)
                });
        }
    }

    private static string? InferExceptionRecommendation(string exceptionType, string? message, string? stackTrace)
    {
        if (string.IsNullOrEmpty(exceptionType) && string.IsNullOrEmpty(message))
            return "Review the exception details in Salesforce Setup → Debug Logs or Apex Exception Email notifications.";

        var combined = $"{exceptionType} {message} {stackTrace}".ToLowerInvariant();

        if (combined.Contains("null") || combined.Contains("de-reference"))
            return "NullPointerException: Add null checks before accessing object properties. Check if SOQL queries return results before using them.";
        if (combined.Contains("soql") || combined.Contains("101") || combined.Contains("too many"))
            return "Governor limit hit: Move queries outside of loops. Use collections and maps for bulk processing. Consider @future or Queueable for heavy operations.";
        if (combined.Contains("dml") || combined.Contains("150"))
            return "DML limit hit: Collect records into lists and perform single DML operations. Use Database.insert with allOrNone=false for partial success.";
        if (combined.Contains("cpu") || combined.Contains("timeout"))
            return "CPU/timeout: Optimize loops and nested iterations. Move heavy processing to async (@future/Queueable). Check for recursive triggers.";
        if (combined.Contains("callout"))
            return "Callout exception: Check external service availability. Add retry logic with exponential backoff. Verify endpoint URL and authentication.";
        if (combined.Contains("visualforce") || combined.Contains("viewstate"))
            return "Visualforce issue: Reduce view state size by using transient variables. Consider migrating to Lightning Web Components.";
        if (combined.Contains("flow") || combined.Contains("process"))
            return "Flow/Process Builder error: Review the flow for missing null checks on record variables. Test with different record types and field values.";
        if (combined.Contains("trigger"))
            return "Trigger exception: Add recursion control (static boolean flag). Ensure trigger handles bulk operations (200+ records).";
        if (combined.Contains("mixed_dml"))
            return "Mixed DML: Separate setup object DML from non-setup object DML. Use @future method for the setup object operation.";

        return "Review the stack trace to identify the root cause. Check recent deployments that may have introduced this error.";
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
            FeedbackAt = reader.IsDBNull(reader.GetOrdinal("feedback_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("feedback_at")), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            AffectedUserCount = reader.IsDBNull(reader.GetOrdinal("affected_user_count")) ? null : reader.GetInt32(reader.GetOrdinal("affected_user_count"))
        };
    }

    /// <summary>
    /// Updates is_success=0 for Login events that had failed login statuses.
    /// </summary>
    public async Task RepairLoginSuccessAsync(List<ShieldEvent> failedLogins)
    {
        if (failedLogins.Count == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            foreach (var evt in failedLogins)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE shield_events SET is_success = 0 WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", evt.Id);
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
    /// Deletes shield events with corrupted event_date values (from broken CSV parsing).
    /// </summary>
    public async Task<int> DeleteCorruptedEventsAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            // Delete events where event_date contains 'column' (garbled CSV) or doesn't start with a digit
            cmd.CommandText = @"
                DELETE FROM shield_events 
                WHERE event_date LIKE '%column%' 
                   OR (event_date NOT LIKE '2%' AND event_date NOT GLOB '[0-9]*')";
            return await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Clears processed log file records for a given event type so they'll be re-downloaded.
    /// </summary>
    public async Task<int> ClearProcessedLogFilesAsync(string eventType)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            // Also delete the events for this type so they can be re-parsed
            using var delCmd = connection.CreateCommand();
            delCmd.CommandText = "DELETE FROM shield_events WHERE org_id = @orgId AND event_type = @eventType";
            delCmd.Parameters.AddWithValue("@orgId", _orgId);
            delCmd.Parameters.AddWithValue("@eventType", eventType);
            await delCmd.ExecuteNonQueryAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM shield_log_files WHERE org_id = @orgId AND event_type = @eventType";
            cmd.Parameters.AddWithValue("@orgId", _orgId);
            cmd.Parameters.AddWithValue("@eventType", eventType);
            return await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writeLock.Dispose();
            _disposed = true;
        }
    }

    // ================================================================
    //  GOVERNOR ARCHAEOLOGY
    // ================================================================

    /// <summary>
    /// Aggregates governor limit statistics across all analysed debug logs for this org
    /// over the past <paramref name="days"/> days.
    /// Returns entry points ranked by average SOQL count, CPU time, and N+1 query patterns.
    /// </summary>
    public async Task<GovernorArchaeologyData> GetGovernorArchaeologyAsync(int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var data = new GovernorArchaeologyData { DaysAnalyzed = days, Since = since };

        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT entry_point,
                   operation_type,
                   COUNT(*) as exec_count,
                   AVG(soql_count)            as avg_soql,
                   MAX(soql_count)            as max_soql,
                   MAX(soql_limit)            as soql_limit,
                   AVG(query_rows)            as avg_rows,
                   MAX(query_rows)            as max_rows,
                   AVG(cpu_time_ms)           as avg_cpu,
                   MAX(cpu_time_ms)           as max_cpu,
                   AVG(duration_ms)           as avg_duration,
                   AVG(duplicate_query_count) as avg_dup,
                   SUM(CASE WHEN has_errors = 1 THEN 1 ELSE 0 END) as error_count
            FROM log_snapshots
            WHERE org_id = @orgId
              AND captured_at >= @since
              AND entry_point IS NOT NULL
              AND entry_point != ''
            GROUP BY entry_point, operation_type
            HAVING COUNT(*) >= 1
            ORDER BY avg_soql DESC
            LIMIT 100";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));

        var allRows = new List<GovernorArchaeologyRow>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                allRows.Add(new GovernorArchaeologyRow
                {
                    EntryPoint = reader.GetString(0),
                    OperationType = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ExecutionCount = reader.GetInt32(2),
                    AvgSoqlCount = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    MaxSoqlCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    SoqlLimit = reader.IsDBNull(5) ? 100 : reader.GetInt32(5),
                    AvgQueryRows = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    MaxQueryRows = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    AvgCpuMs = reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                    MaxCpuMs = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    AvgDurationMs = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                    AvgDuplicateQueryCount = reader.IsDBNull(11) ? 0 : reader.GetDouble(11),
                    ErrorCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12)
                });
            }
        }

        // Count total executions in window
        using (var cntCmd = connection.CreateCommand())
        {
            cntCmd.CommandText = "SELECT COUNT(*) FROM log_snapshots WHERE org_id = @orgId AND captured_at >= @since";
            cntCmd.Parameters.AddWithValue("@orgId", _orgId);
            cntCmd.Parameters.AddWithValue("@since", since.ToString("O"));
            var cnt = await cntCmd.ExecuteScalarAsync();
            data.TotalExecutions = cnt is long l ? (int)l : 0;
        }

        // Top 10 by average SOQL (already sorted)
        data.TopBySoql = allRows.Take(10).ToList();

        // Top 10 by average CPU time
        data.TopByCpu = allRows.OrderByDescending(r => r.AvgCpuMs).Take(10).ToList();

        // Top 10 by N+1 / duplicate query patterns
        data.TopByNPlusOne = allRows
            .Where(r => r.AvgDuplicateQueryCount >= 1)
            .OrderByDescending(r => r.AvgDuplicateQueryCount)
            .Take(10)
            .ToList();

        return data;
    }

    /// <summary>
    /// Returns the count of unique Salesforce users affected by anomaly alerts in the last 24 hours.
    /// Counts distinct user_id values in shield_events where is_anomaly = 1.
    /// </summary>
    public async Task<int> GetAffectedUsersCountAsync(int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hours);
        using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(DISTINCT user_id)
            FROM shield_events
            WHERE org_id = @orgId
              AND is_anomaly = 1
              AND event_date >= @since
              AND user_id IS NOT NULL
              AND user_id != ''";
        cmd.Parameters.AddWithValue("@orgId", _orgId);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));

        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }
}
