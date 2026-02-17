# 🚀 NEXT STEPS - Monday Feb 17 Action Plan

**READ THIS FIRST when you sit down to work**

---

## ☀️ Morning Session (2-3 hours)

### Step 1: Get Context (30 minutes)
```powershell
# On your new computer, after cloning:
cd log_analyser

# Read these files in order:
# 1. PROJECT_STATUS.md (this tells you everything)
# 2. ISSUES_BACKLOG.md (read Issue #1 completely)
# 3. PROJECT_REVIEW_FEB_16_2026.md (skim for context)
```

### Step 2: Verify Environment (15 minutes)
```powershell
# Check .NET version
dotnet --version  # Should be 8.0.x

# Restore packages
dotnet restore

# Build to verify everything works
dotnet build      # Should succeed with 0 errors

# Run tests
cd Tests
dotnet test       # Should pass 7/7 tests
cd ..

# Run the app to see current state
dotnet run        # App should launch

# Close app, return to terminal
```

### Step 3: Activate Copilot PM (5 minutes)
```
In Copilot Chat, type:

/pm timeline

This will show you current sprint progress and remind Copilot of the project context.
Then type:

I'm starting Issue #1 (License validation system). What should I do first?
```

### Step 4: Choose Your Launch Strategy (10 minutes)

**Read the options in PROJECT_STATUS.md and decide:**

**Option 1:** Professional launch March 29 (+ 14 day delay)  
**Option 2:** Hybrid launch (Free beta March 15, Paid April 1)

**Once decided, commit to it:**
```powershell
# Update PROJECT_STATUS.md with your choice
# Then tell Copilot:

I choose Option [1 or 2]. Let's execute that plan.
```

---

## 🔨 Afternoon Session (4-6 hours) - BUILD

### Issue #1: License Validation System (Start Today)

**Goal for today:** Create the foundation - data structures and local storage

#### Task 1: Create the License Models (30 min)

Create file: `Models/LicenseModels.cs`

```csharp
namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// License tier (Free, Pro, Team, Enterprise)
/// </summary>
public enum LicenseTier
{
    Free,
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
/// License information stored locally (encrypted)
/// </summary>
public class License
{
    public string LicenseKey { get; set; } = string.Empty;
    public LicenseTier Tier { get; set; } = LicenseTier.Free;
    public string Email { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime ExpiresDate { get; set; }
    public DateTime LastValidated { get; set; }
    public bool IsTrialLicense { get; set; }
    public int MaxDevices { get; set; } = 1;
    public string DeviceFingerprint { get; set; } = string.Empty;
    
    // Feature flags based on tier
    public bool CanGroupTransactions => Tier != LicenseTier.Free;
    public bool CanUseCliStreaming => Tier != LicenseTier.Free;
    public bool CanExportReports => Tier != LicenseTier.Free;
    public int MaxLogSizeMB => Tier == LicenseTier.Free ? 30 : int.MaxValue;
}
```

**Test it compiles:**
```powershell
dotnet build  # Should succeed
```

#### Task 2: Create LicenseService Shell (45 min)

Create file: `Services/LicenseService.cs`

```csharp
using SalesforceDebugAnalyzer.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Manages license validation, storage, and feature gating
/// </summary>
public class LicenseService
{
    private readonly string _licensePath;
    private License? _currentLicense;
    private static readonly byte[] _encryptionKey = Convert.FromBase64String(
        "YOUR_32_BYTE_KEY_HERE_GENERATE_THIS"); // TODO: Generate proper key
    
    public LicenseService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackWidow"
        );
        Directory.CreateDirectory(appDataPath);
        _licensePath = Path.Combine(appDataPath, "license.dat");
    }
    
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
                LicenseKey = "FREE",
                IssuedDate = DateTime.UtcNow,
                ExpiresDate = DateTime.MaxValue,
                Email = "free@user.local"
            };
        }
        
        return _currentLicense;
    }
    
    /// <summary>
    /// Check if user can access a Pro feature
    /// </summary>
    public async Task<bool> CanUseProFeatureAsync(string featureName)
    {
        var license = await GetCurrentLicenseAsync();
        // TODO: Add specific feature checks
        return license.Tier != LicenseTier.Free;
    }
    
    /// <summary>
    /// Apply a new license key
    /// </summary>
    public async Task<bool> ApplyLicenseAsync(string licenseKey, string email)
    {
        // TODO: Validate license format
        // TODO: Check with online API
        // TODO: Save to disk encrypted
        // TODO: Update _currentLicense
        
        throw new NotImplementedException("Will build tomorrow");
    }
    
    /// <summary>
    /// Validate license online (call every 30 days)
    /// </summary>
    public async Task<LicenseStatus> ValidateOnlineAsync()
    {
        // TODO: HTTP call to validation API
        // TODO: Handle offline grace period
        
        throw new NotImplementedException("Will build tomorrow");
    }
    
    // Private helper methods
    
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
            return null;
        }
    }
    
    private async Task SaveLicenseToDiskAsync(License license)
    {
        var json = JsonSerializer.Serialize(license);
        var encryptedBytes = EncryptData(json);
        await File.WriteAllBytesAsync(_licensePath, encryptedBytes);
    }
    
    private byte[] EncryptData(string plaintext)
    {
        // TODO: Implement AES-256 encryption
        // For now, just return plaintext bytes (FIX TOMORROW)
        return System.Text.Encoding.UTF8.GetBytes(plaintext);
    }
    
    private string DecryptData(byte[] ciphertext)
    {
        // TODO: Implement AES-256 decryption
        // For now, just return plaintext (FIX TOMORROW)
        return System.Text.Encoding.UTF8.GetString(ciphertext);
    }
    
    private string GenerateDeviceFingerprint()
    {
        // Simple fingerprint: hash of machine name + username
        var identifier = $"{Environment.MachineName}|{Environment.UserName}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(identifier);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
```

**Test it compiles:**
```powershell
dotnet build  # Should succeed
```

#### Task 3: Wire Into MainViewModel (30 min)

Open `ViewModels/MainViewModel.cs` and add:

```csharp
// Add to fields section (around line 40):
private readonly LicenseService _licenseService;

// Add to constructor (around line 80):
public MainViewModel(...)
{
    // existing code...
    _licenseService = new LicenseService();
    
    // Add initialization call
    _ = InitializeAsync();
}

// Add new properties (around line 260, after other observable properties):
[ObservableProperty]
private License? _currentLicense;

// Add new method:
private async Task InitializeAsync()
{
    CurrentLicense = await _licenseService.GetCurrentLicenseAsync();
    
    // Show trial warning if needed
    if (CurrentLicense.IsTrialLicense)
    {
        var daysLeft = (CurrentLicense.ExpiresDate - DateTime.UtcNow).Days;
        StatusMessage = $"⚠️ Pro trial: {daysLeft} days remaining";
    }
}
```

#### Task 4: Add Feature Gating Example (15 min)

In `ViewModels/MainViewModel.cs`, find the `LoadLogFolder` command and add:

```csharp
[RelayCommand]
private async Task LoadLogFolder()
{
    // NEW: Check Pro feature
    if (!await _licenseService.CanUseProFeatureAsync("FolderImport"))
    {
        MessageBox.Show(
            "📁 Folder import is a Pro feature.\n\n" +
            "Free tier: Upload individual log files (up to 30MB)\n" +
            "Pro tier: Load entire folders for transaction analysis\n\n" +
            "Start your 14-day free trial to unlock this feature!",
            "Upgrade to Pro",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }
    
    // existing code continues...
}
```

**Test it compiles:**
```powershell
dotnet build  # Should succeed
```

---

## 📝 End of Day Checklist

Before closing your laptop today:

```powershell
# 1. Commit your progress
git add Models/LicenseModels.cs Services/LicenseService.cs ViewModels/MainViewModel.cs
git commit -m "Issue #1: Add license models and service foundation

- Created LicenseTier and LicenseStatus enums
- Created License model with feature flags
- Created LicenseService shell with encryption stubs
- Wired into MainViewModel
- Added sample feature gating for folder import

TODO tomorrow:
- Implement encryption/decryption
- Build online validation
- Complete device fingerprinting"

git push origin main
```

```powershell
# 2. Update PROJECT_STATUS.md progress tracker
# Change line "- [ ] Mon: LicenseService skeleton + storage" to:
# "- [x] Mon: LicenseService skeleton + storage"
```

```
# 3. Tell Copilot what you did:

In Copilot Chat:

/pm standup

What I completed today: Created license models, LicenseService shell, and basic feature gating

What I'm working on tomorrow: Implement encryption, online validation, device fingerprinting

Blockers: None
```

---

## 🎯 Tomorrow's Plan (Feb 18)

**Goal:** Complete online validation and encryption

**Tasks:**
1. Implement AES-256 encryption/decryption (2 hours)
2. Build device fingerprinting logic (1 hour)
3. Create mock validation API endpoint (2 hours)
4. Implement ValidateOnlineAsync method (2 hours)
5. Add 7-day grace period logic (1 hour)

**By end of day Tuesday:** LicenseService should be ~70% complete

---

## 🆘 If You Get Stuck

**Ask Copilot:**
```
How do I implement AES-256 encryption in C#?

Show me an example of device fingerprinting

How do I make an HTTP POST request to validate a license?
```

**Or type:**
```
/pm review

[paste your LicenseService code]

Am I over-engineering this?
```

Remember: Simple is better. Get it working, then optimize.

---

## 🎉 Motivation

**You're making real progress!**

Today you'll lay the foundation for monetization. By Wednesday, you'll have working license validation. By Friday, you'll have a complete upgrade flow.

**In 2 weeks, you'll be able to charge users!**

That's the difference between a hobby project and a business.

Stay focused. Build Issues #1-3. Launch March 29. Acquire 50 paying customers by June.

**You've got this!** 🚀

---

**Last Updated:** February 17, 2026  
**Next Update:** After Monday's work session  
**Status:** READY FOR ACTION ✅
