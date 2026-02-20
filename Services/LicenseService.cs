using Newtonsoft.Json;
using SalesforceDebugAnalyzer.Models;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages Black Widow license state: local encrypted storage, device fingerprinting,
/// 30-day online revalidation, 14-day free trials, and 7-day offline grace period.
/// </summary>
public class LicenseService
{
    // ─────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────

    private const string LicenseFileName       = "license.enc";
    private const int    ValidationIntervalDays = 30;   // Revalidate every 30 days
    private const int    OfflineGraceDays       = 7;    // Allow 7 days without internet
    private const int    TrialDurationDays      = 14;   // 14-day free trial
    public  const int    FreeTierMaxFileSizeMB  = 30;   // 30 MB cap on Free tier

    private const string ValidationUrl = "https://api.blackwidow.dev/v1/licenses/validate";
    private const string ActivationUrl = "https://api.blackwidow.dev/v1/licenses/activate";

    // ─────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────

    private readonly string     _licensePath;
    private readonly HttpClient _httpClient;
    private          LicenseInfo? _cachedLicense;

    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    public LicenseService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackWidow"
        );

        Directory.CreateDirectory(appDataPath);
        _licensePath = Path.Combine(appDataPath, LicenseFileName);
        _httpClient  = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public: Tier & Feature Access
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Current license tier (reads cached value — no network call)</summary>
    public LicenseTier CurrentTier => GetCachedLicense()?.Tier ?? LicenseTier.Free;

    /// <summary>True for Trial, Pro, Team, or Enterprise</summary>
    public bool IsProOrAbove =>
        CurrentTier is LicenseTier.Trial
                    or LicenseTier.Pro
                    or LicenseTier.Team
                    or LicenseTier.Enterprise;

    /// <summary>Check whether a specific feature is available on the current tier</summary>
    public bool IsFeatureAvailable(LicenseFeature feature) => feature switch
    {
        LicenseFeature.TransactionGrouping => IsProOrAbove,
        LicenseFeature.UnlimitedFileSize   => IsProOrAbove,
        LicenseFeature.LiveStreaming        => IsProOrAbove,
        LicenseFeature.ReportExport        => IsProOrAbove,
        LicenseFeature.EditorBridge        => IsProOrAbove,
        LicenseFeature.TeamFeatures        => CurrentTier is LicenseTier.Team or LicenseTier.Enterprise,
        _                                  => false
    };

    /// <summary>Days remaining in the current trial (0 if not on Trial tier)</summary>
    public int TrialDaysRemaining()
    {
        var license = GetCachedLicense();
        if (license?.Tier != LicenseTier.Trial || license.TrialStartedAt == null)
            return 0;

        var elapsed = (DateTime.UtcNow - license.TrialStartedAt.Value).TotalDays;
        return Math.Max(0, TrialDurationDays - (int)elapsed);
    }

    /// <summary>Whether the trial has run out of time</summary>
    public bool IsTrialExpired() =>
        CurrentTier == LicenseTier.Trial && TrialDaysRemaining() <= 0;

    /// <summary>Get full license info for the About/Settings dialog</summary>
    public LicenseInfo? GetCurrentLicenseInfo() => GetCachedLicense();

    // ─────────────────────────────────────────────────────────────────────
    // Public: Trial & Activation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start a 14-day Pro trial. Called from the Upgrade dialog "Start Free Trial" button.
    /// No network needed — purely local.
    /// </summary>
    public void StartTrial(string? email = null)
    {
        var license = GetCachedLicense() ?? new LicenseInfo();

        // Don't restart a trial that already ran
        if (license.Tier == LicenseTier.Trial && license.TrialStartedAt.HasValue)
            return;

        license.Tier             = LicenseTier.Trial;
        license.TrialStartedAt   = DateTime.UtcNow;
        license.Email            = email;
        license.DeviceFingerprint = GetDeviceFingerprint();
        license.LastValidatedAt  = DateTime.UtcNow;
        license.IsValid          = true;

        SaveLicense(license);
        _cachedLicense = license;
    }

    /// <summary>
    /// Activate a purchased license key (calls the API).
    /// Returns a result with IsValid=true on success.
    /// </summary>
    public async Task<LicenseValidationResult> ActivateLicenseAsync(string licenseKey)
    {
        try
        {
            var fingerprint = GetDeviceFingerprint();
            var payload = new
            {
                licenseKey,
                deviceFingerprint = fingerprint,
                appVersion        = GetAppVersion()
            };

            var content  = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(ActivationUrl, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body   = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonConvert.DeserializeObject<ValidationApiResponse>(body);

                if (result is { IsValid: true })
                {
                    var tier = ParseTier(result.Tier);
                    var license = new LicenseInfo
                    {
                        LicenseKey        = licenseKey,
                        Tier              = tier,
                        ExpiresAt         = result.ExpiresAt,
                        DeviceFingerprint = fingerprint,
                        LastValidatedAt   = DateTime.UtcNow,
                        IsValid           = true
                    };

                    SaveLicense(license);
                    _cachedLicense = license;

                    return new LicenseValidationResult
                    {
                        IsValid   = true,
                        Tier      = tier,
                        ExpiresAt = result.ExpiresAt
                    };
                }

                return new LicenseValidationResult
                {
                    IsValid      = false,
                    ErrorMessage = result?.Message ?? "License key not recognized"
                };
            }

            return new LicenseValidationResult
            {
                IsValid      = false,
                ErrorMessage = $"Server returned {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new LicenseValidationResult
            {
                IsValid      = false,
                ErrorMessage = $"Could not reach license server: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Check license validity on startup. Only hits the network if:
    /// - It's a paid tier AND 30+ days since last validation.
    /// Falls back to 7-day offline grace period on network failure.
    /// </summary>
    public async Task<LicenseValidationResult> RevalidateIfNeededAsync()
    {
        var license = GetCachedLicense();

        // ── No license file at all → Free tier
        if (license == null)
            return new LicenseValidationResult { IsValid = true, Tier = LicenseTier.Free };

        // ── Free tier → always valid, nothing to check
        if (license.Tier == LicenseTier.Free)
            return new LicenseValidationResult { IsValid = true, Tier = LicenseTier.Free };

        // ── Trial tier → check expiry locally (no network needed)
        if (license.Tier == LicenseTier.Trial)
        {
            if (IsTrialExpired())
            {
                // Downgrade to Free
                license.Tier    = LicenseTier.Free;
                license.IsValid = false;
                SaveLicense(license);
                _cachedLicense = license;

                return new LicenseValidationResult
                {
                    IsValid      = false,
                    Tier         = LicenseTier.Free,
                    ErrorMessage = "Your 14-day trial has expired. Upgrade to Pro to continue."
                };
            }

            return new LicenseValidationResult { IsValid = true, Tier = LicenseTier.Trial };
        }

        // ── Pro / Team / Enterprise → revalidate every 30 days
        var daysSinceValidation = (DateTime.UtcNow - license.LastValidatedAt).TotalDays;

        if (daysSinceValidation < ValidationIntervalDays)
            return new LicenseValidationResult { IsValid = true, Tier = license.Tier };

        // 30-day mark reached — call the API
        try
        {
            var fingerprint = GetDeviceFingerprint();
            var payload     = new { licenseKey = license.LicenseKey, deviceFingerprint = fingerprint };
            var content     = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response    = await _httpClient.PostAsync(ValidationUrl, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body   = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonConvert.DeserializeObject<ValidationApiResponse>(body);

                if (result != null)
                {
                    license.Tier              = result.IsValid ? ParseTier(result.Tier) : LicenseTier.Free;
                    license.IsValid           = result.IsValid;
                    license.ExpiresAt         = result.ExpiresAt;
                    license.LastValidatedAt   = DateTime.UtcNow;

                    SaveLicense(license);
                    _cachedLicense = license;

                    return new LicenseValidationResult
                    {
                        IsValid      = result.IsValid,
                        Tier         = license.Tier,
                        ExpiresAt    = result.ExpiresAt,
                        ErrorMessage = result.IsValid ? null : (result.Message ?? "License is no longer active")
                    };
                }
            }
        }
        catch
        {
            // Network failure — check grace period
        }

        // ── Offline grace period (7 days beyond the 30-day window)
        if (daysSinceValidation <= ValidationIntervalDays + OfflineGraceDays)
        {
            return new LicenseValidationResult
            {
                IsValid              = true,
                Tier                 = license.Tier,
                IsOfflineGracePeriod = true
            };
        }

        // ── Grace period also expired → downgrade to Free
        license.Tier    = LicenseTier.Free;
        license.IsValid = false;
        SaveLicense(license);
        _cachedLicense = license;

        return new LicenseValidationResult
        {
            IsValid      = false,
            Tier         = LicenseTier.Free,
            ErrorMessage = "License revalidation required. Please connect to the internet."
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private: Encryption helpers
    // ─────────────────────────────────────────────────────────────────────

    private LicenseInfo? GetCachedLicense()
    {
        if (_cachedLicense != null) return _cachedLicense;
        _cachedLicense = LoadLicense();
        return _cachedLicense;
    }

    private LicenseInfo? LoadLicense()
    {
        if (!File.Exists(_licensePath)) return null;

        try
        {
            var encryptedData = File.ReadAllBytes(_licensePath);
            var json          = DecryptData(encryptedData);
            return JsonConvert.DeserializeObject<LicenseInfo>(json);
        }
        catch
        {
            // Corrupted or tampered file — treat as Free
            return null;
        }
    }

    private void SaveLicense(LicenseInfo license)
    {
        var json      = JsonConvert.SerializeObject(license, Formatting.Indented);
        var encrypted = EncryptData(json);
        File.WriteAllBytes(_licensePath, encrypted);
    }

    /// <summary>
    /// Device fingerprint: SHA-256 of machine + user + CPU count, truncated to 16 hex chars.
    /// Used to detect the 2-device-per-license limit.
    /// </summary>
    private static string GetDeviceFingerprint()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.ProcessorCount}|{(int)Environment.OSVersion.Platform}";
        using var sha   = SHA256.Create();
        var       bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16];
    }

    private static string GetAppVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>AES-256-CBC encrypt with PBKDF2-derived key. Prepends IV to ciphertext.</summary>
    private static byte[] EncryptData(string plaintext)
    {
        var key = DeriveKey();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length); // IV first

        using var encryptor = aes.CreateEncryptor();
        using var cs        = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        var       data      = Encoding.UTF8.GetBytes(plaintext);
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();

        return ms.ToArray();
    }

    /// <summary>AES-256-CBC decrypt. Reads IV from first block of ciphertext.</summary>
    private static string DecryptData(byte[] ciphertext)
    {
        var key = DeriveKey();

        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[aes.BlockSize / 8];
        Array.Copy(ciphertext, iv, iv.Length);
        aes.IV = iv;

        using var ms        = new MemoryStream(ciphertext, iv.Length, ciphertext.Length - iv.Length);
        using var decryptor = aes.CreateDecryptor();
        using var cs        = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader    = new StreamReader(cs, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Derives a 256-bit AES key from the device fingerprint using PBKDF2/SHA-256.
    /// The static salt is acceptable here because the key is already device-bound.
    /// </summary>
    private static byte[] DeriveKey()
    {
        var fingerprint = GetDeviceFingerprint();
        var salt        = Encoding.UTF8.GetBytes("BW-License-Salt-v1-2026");

        using var kdf = new Rfc2898DeriveBytes(fingerprint, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32); // 256-bit key
    }

    private static LicenseTier ParseTier(string raw) =>
        Enum.TryParse<LicenseTier>(raw, ignoreCase: true, out var t) ? t : LicenseTier.Free;
}
