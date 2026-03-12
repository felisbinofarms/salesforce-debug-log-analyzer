using SalesforceDebugAnalyzer.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages license validation, storage, and feature gating.
/// Uses the LemonSqueezy License Key API (no backend server required).
/// - Activate:   POST https://api.lemonsqueezy.com/v1/licenses/activate
/// - Validate:   POST https://api.lemonsqueezy.com/v1/licenses/validate
/// - Deactivate: POST https://api.lemonsqueezy.com/v1/licenses/deactivate
/// </summary>
public class LicenseService
{
    private readonly string _licensePath;
    private readonly string _appDataPath;
    private License? _currentLicense;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string LsApiBase = "https://api.lemonsqueezy.com/v1/licenses/";

    // Checkout URLs — replace with real LemonSqueezy variant share URLs after creating products
    public const string CheckoutMonthlyUrl   = "https://blackwidow.lemonsqueezy.com/buy/pro-monthly";
    public const string CheckoutAnnualUrl    = "https://blackwidow.lemonsqueezy.com/buy/pro-annual";
    public const string CheckoutTeamUrl      = "https://blackwidow.lemonsqueezy.com/buy/team-monthly";
    public const string CustomerPortalUrl    = "https://app.lemonsqueezy.com/my-orders";

    // AES-256 encryption key derived at build time
    private static readonly byte[] _encryptionKey = SHA256.HashData(
        Encoding.UTF8.GetBytes("BlackWidow-2026-Production-Key-Do-Not-Share"));
    
    public LicenseService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackWidow"
        );
        Directory.CreateDirectory(_appDataPath);
        _licensePath = Path.Combine(_appDataPath, "license.dat");
    }
    
    #region Public Methods
    
    /// <summary>
    /// Get current license status
    /// </summary>
    public async Task<License> GetCurrentLicenseAsync()
    {
        if (_currentLicense != null)
            return _currentLicense;
        
        // Try to load from disk
        _currentLicense = await LoadLicenseFromDiskAsync().ConfigureAwait(false);
        
        // If no license, create free tier default
        if (_currentLicense == null)
        {
            _currentLicense = new License
            {
                Tier = LicenseTier.Free,
                Status = LicenseStatus.Active,
                LicenseKey = "FREE",
                IssuedDate = DateTime.UtcNow,
                ExpiresDate = DateTime.MaxValue,
                LastValidated = DateTime.UtcNow,
                Email = "free@user.local",
                DeviceFingerprint = GenerateDeviceFingerprint()
            };
            await SaveLicenseToDiskAsync(_currentLicense).ConfigureAwait(false);
        }
        
        // Check if validation needed
        if (_currentLicense.NeedsOnlineValidation && _currentLicense.Tier != LicenseTier.Free)
        {
            _ = Task.Run(async () => await ValidateOnlineAsync());
        }
        
        return _currentLicense;
    }
    
    /// <summary>
    /// Check if user can access a Pro feature.
    /// Currently all features are unlocked — monetization gates will be
    /// re-enabled once the product is validated and ready for distribution.
    /// </summary>
    public async Task<bool> CanUseProFeatureAsync(string featureName)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }
    
    /// <summary>
    /// Apply (activate) a LemonSqueezy license key on this device.
    /// Calls the LS /activate endpoint and stores the returned instance_id for future validation.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyLicenseAsync(string licenseKey, string email)
    {
        try
        {
            licenseKey = licenseKey.Trim();
            if (string.IsNullOrWhiteSpace(licenseKey) || licenseKey.Length < 10)
                return (false, "Invalid license key format.");

            // Local trial keys bypass the online API
            if (licenseKey.StartsWith("TRIAL-", StringComparison.OrdinalIgnoreCase))
                return await ActivateTrialLocallyAsync(licenseKey, email).ConfigureAwait(false);

            var instanceName = GenerateDeviceInstanceName();
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("license_key", licenseKey),
                new KeyValuePair<string, string>("instance_name", instanceName)
            });

            var response = await _http.PostAsync(LsApiBase + "activate", body).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<LsActivateResponse>(json, LsJsonOptions);

            if (result?.Activated != true)
            {
                var err = result?.Error ?? "License activation failed. Check your key and try again.";
                return (false, err);
            }

            var tier = DetectTierFromLsResponse(result.Meta);
            var license = new License
            {
                LicenseKey        = licenseKey,
                Email             = email.Trim(),
                Tier              = tier,
                Status            = LicenseStatus.Active,
                IsTrialLicense    = false,
                IssuedDate        = DateTime.UtcNow,
                ExpiresDate       = result.LicenseKeyData?.ExpiresAt ?? DateTime.MaxValue,
                LastValidated     = DateTime.UtcNow,
                MaxDevices        = result.LicenseKeyData?.ActivationLimit ?? 2,
                DeviceFingerprint = GenerateDeviceFingerprint(),
                LemonSqueezyInstanceId = result.Instance?.Id ?? string.Empty
            };

            await SaveLicenseToDiskAsync(license).ConfigureAwait(false);
            _currentLicense = license;

            return (true, $"License activated! {tier} tier is now active on this device.");
        }
        catch (HttpRequestException)
        {
            return (false, "Could not reach the license server. Check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            return (false, $"Activation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate the current license online against LemonSqueezy.
    /// Called automatically every 30 days as a background fire-and-forget.
    /// </summary>
    public async Task<LicenseStatus> ValidateOnlineAsync()
    {
        var license = await GetCurrentLicenseAsync().ConfigureAwait(false);

        if (license.Tier == LicenseTier.Free)
            return LicenseStatus.Active;

        // Local trials validate against their expiry date only
        if (license.IsTrialLicense)
        {
            var status = license.IsExpired ? LicenseStatus.Expired : LicenseStatus.Trial;
            license.Status = status;
            license.LastValidated = DateTime.UtcNow;
            await SaveLicenseToDiskAsync(license).ConfigureAwait(false);
            _currentLicense = license;
            return status;
        }

        try
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("license_key", license.LicenseKey),
                new KeyValuePair<string, string>("instance_id", license.LemonSqueezyInstanceId)
            });

            var response = await _http.PostAsync(LsApiBase + "validate", body).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<LsValidateResponse>(json, LsJsonOptions);

            if (result?.Valid != true)
            {
                license.Status = LicenseStatus.Invalid;
                await SaveLicenseToDiskAsync(license).ConfigureAwait(false);
                _currentLicense = license;
                return LicenseStatus.Invalid;
            }

            // Refresh expiry from server
            if (result.LicenseKeyData?.ExpiresAt.HasValue == true)
                license.ExpiresDate = result.LicenseKeyData.ExpiresAt.Value;

            license.Status = license.IsExpired ? LicenseStatus.Expired : LicenseStatus.Active;
            license.LastValidated = DateTime.UtcNow;
            await SaveLicenseToDiskAsync(license).ConfigureAwait(false);
            _currentLicense = license;
            return license.Status;
        }
        catch (HttpRequestException)
        {
            // Offline grace: 7 days before locking out
            if ((DateTime.UtcNow - license.LastValidated).TotalDays <= 7)
            {
                license.Status = LicenseStatus.Offline;
                return LicenseStatus.Offline;
            }
            license.Status = LicenseStatus.Invalid;
            return LicenseStatus.Invalid;
        }
        catch
        {
            return LicenseStatus.Invalid;
        }
    }

    /// <summary>
    /// Deactivate this device's license (e.g. before transferring to another machine).
    /// </summary>
    public async Task<(bool Success, string Message)> DeactivateLicenseAsync()
    {
        var license = await GetCurrentLicenseAsync().ConfigureAwait(false);
        if (license.Tier == LicenseTier.Free || license.IsTrialLicense)
        {
            // Nothing to deactivate on LS — just wipe local
            await ResetToFreeAsync().ConfigureAwait(false);
            return (true, "License removed from this device.");
        }

        try
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("license_key", license.LicenseKey),
                new KeyValuePair<string, string>("instance_id", license.LemonSqueezyInstanceId)
            });
            await _http.PostAsync(LsApiBase + "deactivate", body).ConfigureAwait(false);
        }
        catch { /* best-effort */ }

        await ResetToFreeAsync().ConfigureAwait(false);
        return (true, "License deactivated. You can now activate on another device.");
    }
    
    /// <summary>
    /// Get upgrade message for feature gating
    /// </summary>
    public string GetUpgradeMessage(string featureName)
    {
        var messages = new Dictionary<string, string>
        {
            ["TransactionGrouping"] = "📊 Transaction grouping is a Pro feature.\n\nFree tier: Analyze individual logs\nPro tier: Group related logs to see the complete user journey\n\nStart your 14-day free trial!",
            ["CliStreaming"] = "⚡ Real-time CLI streaming is a Pro feature.\n\nFree tier: Upload log files manually\nPro tier: Stream logs directly from Salesforce CLI\n\nStart your 14-day free trial!",
            ["ExportReports"] = "📄 Export reports is a Pro feature.\n\nFree tier: View analysis in app\nPro tier: Export to PDF, Excel, or JSON\n\nStart your 14-day free trial!",
            ["FolderImport"] = "📁 Folder import is a Pro feature.\n\nFree tier: Upload individual log files (up to 30MB)\nPro tier: Load entire folders for batch analysis\n\nStart your 14-day free trial!",
            ["MarketplaceSubmit"] = "🏪 Marketplace submission is a Pro feature.\n\nFree tier: Browse and use marketplace templates\nPro tier: Submit your own analysis templates\n\nUpgrade to Pro!"
        };
        
        return messages.TryGetValue(featureName, out var message) 
            ? message 
            : "This is a Pro feature. Upgrade to unlock!";
    }
    
    /// <summary>
    /// Start a 14-day local Pro trial (no credit card, no server call).
    /// </summary>
    public async Task<(bool Success, string Message)> StartTrialAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "Please enter a valid email address.");

        var currentLicense = await GetCurrentLicenseAsync().ConfigureAwait(false);

        if (currentLicense.Tier != LicenseTier.Free)
            return (false, "You already have an active license on this device.");

        var trialKey = $"TRIAL-{Guid.NewGuid():N}";
        return await ActivateTrialLocallyAsync(trialKey, email).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Check if a specific feature is available for the current license.
    /// Currently all features are unlocked — monetization gates will be
    /// re-enabled once the product is validated and ready for distribution.
    /// </summary>
    public bool IsFeatureAvailable(LicenseFeature feature)
    {
        return true;
    }

    /// <summary>Maximum file size (MB) allowed on the Free tier</summary>
    public static int FreeTierMaxFileSizeMB => 30;

    /// <summary>Current license tier (synchronous convenience property)</summary>
    public LicenseTier CurrentTier
    {
        get
        {
            var license = GetCurrentLicenseAsync().GetAwaiter().GetResult();
            return license.Tier;
        }
    }

    /// <summary>True if the license is Pro, Team, or Enterprise and not expired</summary>
    public bool IsProOrAbove
    {
        get
        {
            var license = GetCurrentLicenseAsync().GetAwaiter().GetResult();
            if (license.Tier == LicenseTier.Free || license.Tier == LicenseTier.Trial)
                return false;
            return !(license.IsExpired && !license.InGracePeriod);
        }
    }

    /// <summary>Days remaining on a trial or pro license (0 if expired / free)</summary>
    public int TrialDaysRemaining()
    {
        var license = GetCurrentLicenseAsync().GetAwaiter().GetResult();
        if (license.ExpiresDate == default) return 0;
        return (int)Math.Max(0, (license.ExpiresDate - DateTime.UtcNow).TotalDays);
    }

    /// <summary>Re-validate the license online if it needs revalidation; returns a result object</summary>
    public async Task<LicenseValidationResult> RevalidateIfNeededAsync()
    {
        try
        {
            var license = await GetCurrentLicenseAsync().ConfigureAwait(false);
            if (license.Tier == LicenseTier.Free)
                return new LicenseValidationResult { IsValid = true };
            if (!license.NeedsOnlineValidation)
                return new LicenseValidationResult { IsValid = true };

            var status = await ValidateOnlineAsync().ConfigureAwait(false);
            return new LicenseValidationResult
            {
                IsValid = status == LicenseStatus.Active || status == LicenseStatus.Trial,
                IsOfflineGracePeriod = status == LicenseStatus.Offline,
                ErrorMessage = status == LicenseStatus.Expired ? "License has expired" : null
            };
        }
        catch (Exception ex)
        {
            return new LicenseValidationResult { IsValid = false, ErrorMessage = ex.Message };
        }
    }

    #endregion

    #region Private Helper Methods
    
    private async Task<License?> LoadLicenseFromDiskAsync()
    {
        if (!File.Exists(_licensePath))
            return null;
        
        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_licensePath).ConfigureAwait(false);
            var decryptedJson = DecryptData(encryptedBytes);
            return JsonSerializer.Deserialize<License>(decryptedJson);
        }
        catch
        {
            // Corrupt license file - delete and start fresh
            try { File.Delete(_licensePath); } catch { }
            return null;
        }
    }
    
    private async Task SaveLicenseToDiskAsync(License license)
    {
        try
        {
            var json = JsonSerializer.Serialize(license, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            var encryptedBytes = EncryptData(json);
            await File.WriteAllBytesAsync(_licensePath, encryptedBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save license: {ex.Message}");
        }
    }
    
    private byte[] EncryptData(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        
        // Write IV first (needed for decryption)
        ms.Write(aes.IV, 0, aes.IV.Length);
        
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plaintext);
        }
        
        return ms.ToArray();
    }
    
    private string DecryptData(byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        
        // Read IV from beginning of ciphertext
        var iv = new byte[aes.IV.Length];
        Array.Copy(ciphertext, 0, iv, 0, iv.Length);
        aes.IV = iv;
        
        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(ciphertext, iv.Length, ciphertext.Length - iv.Length);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        
        return sr.ReadToEnd();
    }
    
    private async Task<(bool Success, string Message)> ActivateTrialLocallyAsync(string trialKey, string email)
    {
        var expires = DateTime.UtcNow.AddDays(14);
        var license = new License
        {
            LicenseKey             = trialKey,
            Email                  = email.Trim(),
            Tier                   = LicenseTier.Trial,
            Status                 = LicenseStatus.Trial,
            IsTrialLicense         = true,
            IssuedDate             = DateTime.UtcNow,
            ExpiresDate            = expires,
            LastValidated          = DateTime.UtcNow,
            MaxDevices             = 1,
            DeviceFingerprint      = GenerateDeviceFingerprint(),
            LemonSqueezyInstanceId = string.Empty
        };
        await SaveLicenseToDiskAsync(license).ConfigureAwait(false);
        _currentLicense = license;
        return (true, $"14-day Pro trial activated! Expires {expires:MMM dd, yyyy}. No credit card required.");
    }

    private async Task ResetToFreeAsync()
    {
        _currentLicense = new License
        {
            Tier              = LicenseTier.Free,
            Status            = LicenseStatus.Active,
            LicenseKey        = "FREE",
            IssuedDate        = DateTime.UtcNow,
            ExpiresDate       = DateTime.MaxValue,
            LastValidated     = DateTime.UtcNow,
            Email             = string.Empty,
            DeviceFingerprint = GenerateDeviceFingerprint()
        };
        await SaveLicenseToDiskAsync(_currentLicense).ConfigureAwait(false);
    }

    private static LicenseTier DetectTierFromLsResponse(LsMeta? meta)
    {
        if (meta == null) return LicenseTier.Pro;
        // Map LemonSqueezy variant names to our tiers
        var name = meta.VariantName?.ToLowerInvariant() ?? string.Empty;
        if (name.Contains("team"))       return LicenseTier.Team;
        if (name.Contains("enterprise")) return LicenseTier.Enterprise;
        return LicenseTier.Pro;
    }

    private static string GenerateDeviceInstanceName()
        => $"{Environment.MachineName}|{Environment.UserName}";

    private static readonly JsonSerializerOptions LsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string GenerateDeviceFingerprint()
    {
        // Combine machine name and username for device identification
        var identifier = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion}";
        var bytes = Encoding.UTF8.GetBytes(identifier);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
    
    #endregion
}
// ── LemonSqueezy API response DTOs ──────────────────────────────────────────

internal sealed class LsActivateResponse
{
    [JsonPropertyName("activated")]   public bool Activated { get; set; }
    [JsonPropertyName("error")]       public string? Error { get; set; }
    [JsonPropertyName("instance")]    public LsInstance? Instance { get; set; }
    [JsonPropertyName("license_key")] public LsLicenseKeyData? LicenseKeyData { get; set; }
    [JsonPropertyName("meta")]        public LsMeta? Meta { get; set; }
}

internal sealed class LsValidateResponse
{
    [JsonPropertyName("valid")]       public bool Valid { get; set; }
    [JsonPropertyName("error")]       public string? Error { get; set; }
    [JsonPropertyName("instance")]    public LsInstance? Instance { get; set; }
    [JsonPropertyName("license_key")] public LsLicenseKeyData? LicenseKeyData { get; set; }
}

internal sealed class LsInstance
{
    [JsonPropertyName("id")]   public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

internal sealed class LsLicenseKeyData
{
    [JsonPropertyName("status")]           public string Status { get; set; } = string.Empty;
    [JsonPropertyName("activation_limit")] public int? ActivationLimit { get; set; }
    [JsonPropertyName("activation_usage")] public int? ActivationUsage { get; set; }
    [JsonPropertyName("expires_at")]       public DateTime? ExpiresAt { get; set; }
}

internal sealed class LsMeta
{
    [JsonPropertyName("store_id")]     public int StoreId { get; set; }
    [JsonPropertyName("product_id")]   public int ProductId { get; set; }
    [JsonPropertyName("variant_id")]   public int VariantId { get; set; }
    [JsonPropertyName("variant_name")] public string? VariantName { get; set; }
}