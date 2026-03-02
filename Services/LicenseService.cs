using SalesforceDebugAnalyzer.Models;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages license validation, storage, and feature gating
/// </summary>
public class LicenseService
{
    private readonly string _licensePath;
    private readonly string _appDataPath;
    private License? _currentLicense;
    
    // AES-256 encryption key (in production, this would be obfuscated or derived from machine-specific data)
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
        _currentLicense = await LoadLicenseFromDiskAsync();
        
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
            await SaveLicenseToDiskAsync(_currentLicense);
        }
        
        // Check if validation needed
        if (_currentLicense.NeedsOnlineValidation && _currentLicense.Tier != LicenseTier.Free)
        {
            _ = Task.Run(async () => await ValidateOnlineAsync());
        }
        
        return _currentLicense;
    }
    
    /// <summary>
    /// Check if user can access a Pro feature
    /// </summary>
    public async Task<bool> CanUseProFeatureAsync(string featureName)
    {
        var license = await GetCurrentLicenseAsync();
        
        // Free tier can't use Pro features
        if (license.Tier == LicenseTier.Free)
            return false;
        
        // Expired licenses revert to Free tier
        if (license.IsExpired && !license.InGracePeriod)
        {
            license.Status = LicenseStatus.Expired;
            return false;
        }
        
        // Check specific features
        return featureName switch
        {
            "TransactionGrouping" => license.CanGroupTransactions,
            "CliStreaming" => license.CanUseCliStreaming,
            "ExportReports" => license.CanExportReports,
            "MarketplaceSubmit" => license.CanSubmitToMarketplace,
            "FolderImport" => license.Tier != LicenseTier.Free,
            _ => license.Tier != LicenseTier.Free
        };
    }
    
    /// <summary>
    /// Apply a new license key
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyLicenseAsync(string licenseKey, string email)
    {
        try
        {
            // Validate license format (basic check)
            if (string.IsNullOrWhiteSpace(licenseKey) || licenseKey.Length < 20)
            {
                return (false, "Invalid license key format");
            }
            
            // In production, this would call the validation API
            // For now, create a trial license
            var license = new License
            {
                LicenseKey = licenseKey,
                Email = email,
                Tier = LicenseTier.Pro,
                Status = LicenseStatus.Trial,
                IsTrialLicense = true,
                IssuedDate = DateTime.UtcNow,
                ExpiresDate = DateTime.UtcNow.AddDays(14), // 14-day trial
                LastValidated = DateTime.UtcNow,
                MaxDevices = 2,
                DeviceFingerprint = GenerateDeviceFingerprint()
            };
            
            // Save to disk
            await SaveLicenseToDiskAsync(license);
            _currentLicense = license;
            
            return (true, $"Pro trial activated! Expires {license.ExpiresDate:MMM dd, yyyy}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to apply license: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Validate license online (call every 30 days)
    /// </summary>
    public async Task<LicenseStatus> ValidateOnlineAsync()
    {
        var license = await GetCurrentLicenseAsync();
        
        if (license.Tier == LicenseTier.Free)
        {
            return LicenseStatus.Active;
        }
        
        try
        {
            // In production, this would make HTTP call to validation API:
            // POST https://api.blackwidow.dev/v1/licenses/validate
            // Body: { licenseKey, deviceFingerprint }
            // Response: { valid, tier, expiresDate, message }
            
            // For now, simulate with a delay
            await Task.Delay(100);
            
            // Update last validated timestamp
            license.LastValidated = DateTime.UtcNow;
            license.Status = license.IsExpired ? LicenseStatus.Expired : LicenseStatus.Active;
            
            await SaveLicenseToDiskAsync(license);
            _currentLicense = license;
            
            return license.Status;
        }
        catch (HttpRequestException)
        {
            // Network error - enter grace period if within 7 days
            if ((DateTime.UtcNow - license.LastValidated).Days <= 7)
            {
                license.Status = LicenseStatus.Offline;
                return LicenseStatus.Offline;
            }
            else
            {
                license.Status = LicenseStatus.Invalid;
                return LicenseStatus.Invalid;
            }
        }
        catch (Exception)
        {
            return LicenseStatus.Invalid;
        }
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
    /// Start a Pro trial
    /// </summary>
    public async Task<(bool Success, string Message)> StartTrialAsync(string email)
    {
        var currentLicense = await GetCurrentLicenseAsync();
        
        // Already on trial or paid
        if (currentLicense.Tier != LicenseTier.Free)
        {
            return (false, "You already have a Pro license");
        }
        
        // Generate trial license key
        var trialKey = $"TRIAL-{Guid.NewGuid():N}";
        
        return await ApplyLicenseAsync(trialKey, email);
    }
    
    /// <summary>
    /// Check if a specific feature is available for the current license
    /// </summary>
    public bool IsFeatureAvailable(LicenseFeature feature)
    {
        var license = GetCurrentLicenseAsync().Result; // Sync version for easier use
        
        // Free tier can't use Pro features
        if (license.Tier == LicenseTier.Free)
            return false;
        
        // Expired licenses revert to Free tier
        if (license.IsExpired && !license.InGracePeriod)
            return false;
        
        // All features available for Pro+
        return true;
    }
    
    #endregion
    
    #region Private Helper Methods
    
    private async Task<License?> LoadLicenseFromDiskAsync()
    {
        if (!File.Exists(_licensePath))
            return null;
        
        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_licensePath);
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
            await File.WriteAllBytesAsync(_licensePath, encryptedBytes);
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
