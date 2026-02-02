using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Provides caching functionality for expensive API operations
/// </summary>
public class CacheService
{
    private List<DebugLevel>? _cachedDebugLevels;
    private DateTime? _debugLevelsCacheTime;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets debug levels from cache or API if cache is expired
    /// </summary>
    public async Task<List<DebugLevel>> GetDebugLevelsAsync(
        SalesforceApiService apiService,
        bool forceRefresh = false)
    {
        // Return cached data if available and not expired
        if (!forceRefresh &&
            _cachedDebugLevels != null &&
            _debugLevelsCacheTime.HasValue &&
            DateTime.Now - _debugLevelsCacheTime.Value < _cacheExpiration)
        {
            return _cachedDebugLevels;
        }

        // Fetch fresh data
        var debugLevels = await apiService.QueryDebugLevelsAsync().ConfigureAwait(false);

        // Update cache
        _cachedDebugLevels = debugLevels;
        _debugLevelsCacheTime = DateTime.Now;

        return debugLevels;
    }

    /// <summary>
    /// Clears all cached data
    /// </summary>
    public void ClearCache()
    {
        _cachedDebugLevels = null;
        _debugLevelsCacheTime = null;
    }

    /// <summary>
    /// Clears only debug levels cache
    /// </summary>
    public void ClearDebugLevelsCache()
    {
        _cachedDebugLevels = null;
        _debugLevelsCacheTime = null;
    }
}
