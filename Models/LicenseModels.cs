namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// License tiers available in Black Widow
/// </summary>
public enum LicenseTier
{
    Free,
    Trial,     // 14-day free trial, full Pro features
    Pro,       // $29/month
    Team,      // $99/month (5 users)
    Enterprise // Contact sales
}

/// <summary>
/// Features that are gated behind Pro/Team tier
/// </summary>
public enum LicenseFeature
{
    /// <summary>Group related logs from a single user action into a transaction</summary>
    TransactionGrouping,

    /// <summary>Analyze log files larger than 30MB</summary>
    UnlimitedFileSize,

    /// <summary>Real-time log streaming via Salesforce CLI</summary>
    LiveStreaming,

    /// <summary>Export governance reports to PDF</summary>
    ReportExport,

    /// <summary>VSCode editor bridge integration</summary>
    EditorBridge,

    /// <summary>Team collaboration features (comments, assignments)</summary>
    TeamFeatures
}

/// <summary>
/// The locally-stored license record (AES-256 encrypted on disk)
/// </summary>
public class LicenseInfo
{
    public LicenseTier Tier { get; set; } = LicenseTier.Free;
    public string? LicenseKey { get; set; }
    public string? Email { get; set; }

    /// <summary>When a paid license expires (null = never for active subscriptions)</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>When the 14-day trial started (null if not on Trial tier)</summary>
    public DateTime? TrialStartedAt { get; set; }

    /// <summary>Last time we successfully validated with the API (used for 30-day check + grace period)</summary>
    public DateTime LastValidatedAt { get; set; } = DateTime.MinValue;

    /// <summary>Device fingerprint at time of activation (for 2-device enforcement)</summary>
    public string? DeviceFingerprint { get; set; }

    public bool IsValid { get; set; }
}

/// <summary>
/// Result returned from validation operations
/// </summary>
public class LicenseValidationResult
{
    public bool IsValid { get; set; }
    public LicenseTier Tier { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>True when network was unreachable but we're within the 7-day offline grace period</summary>
    public bool IsOfflineGracePeriod { get; set; }
}

/// <summary>
/// DTO for API responses from the Black Widow license server
/// </summary>
internal class ValidationApiResponse
{
    public bool IsValid { get; set; }
    public string Tier { get; set; } = "Free";
    public DateTime? ExpiresAt { get; set; }
    public string? Message { get; set; }
}
