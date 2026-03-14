namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// License tier (Free, Pro, Team, Enterprise)
/// </summary>
public enum LicenseTier
{
    Free,
    Trial,
    Pro,
    Team,
    Enterprise
}

/// <summary>
/// Current license status
/// </summary>
public enum LicenseStatus
{
    Active,      // Valid and within date
    Trial,       // Free trial active
    Expired,     // Past expiration date
    Invalid,     // Signature/format invalid
    Offline,     // Couldn't validate online, using grace period
    Revoked      // Manually revoked
}

/// <summary>
/// Features that can be gated by license tier
/// </summary>
public enum LicenseFeature
{
    UnlimitedFileSize,
    LiveStreaming,
    TransactionGrouping,
    FolderImport,
    ExportReports,
    MarketplaceSubmit
}

/// <summary>
/// License information stored locally (encrypted)
/// </summary>
public class License
{
    public string LicenseKey { get; set; } = string.Empty;
    public LicenseTier Tier { get; set; } = LicenseTier.Free;
    public LicenseStatus Status { get; set; } = LicenseStatus.Active;
    public string Email { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime ExpiresDate { get; set; }
    public DateTime LastValidated { get; set; }
    public bool IsTrialLicense { get; set; }
    public int MaxDevices { get; set; } = 1;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public int DaysUntilExpiration => (ExpiresDate - DateTime.UtcNow).Days;

    // Feature flags based on tier
    public bool CanGroupTransactions => Tier != LicenseTier.Free;
    public bool CanUseCliStreaming => Tier != LicenseTier.Free;
    public bool CanExportReports => Tier != LicenseTier.Free;
    public bool CanSubmitToMarketplace => Tier != LicenseTier.Free;
    public int MaxLogSizeMB => Tier == LicenseTier.Free ? 30 : int.MaxValue;

    // Validation helpers
    public bool IsExpired => DateTime.UtcNow > ExpiresDate;
    public bool NeedsOnlineValidation => (DateTime.UtcNow - LastValidated).Days >= 30;
    public bool InGracePeriod => Status == LicenseStatus.Offline && (DateTime.UtcNow - LastValidated).Days <= 7;

    /// <summary>LemonSqueezy instance ID returned on activation — required for validation/deactivation</summary>
    public string LemonSqueezyInstanceId { get; set; } = string.Empty;
}

/// <summary>Result returned by LicenseService.RevalidateIfNeededAsync()</summary>
public class LicenseValidationResult
{
    public bool IsValid { get; set; }
    public bool IsOfflineGracePeriod { get; set; }
    public string? ErrorMessage { get; set; }
}
