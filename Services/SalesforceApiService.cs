using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for interacting with Salesforce APIs
/// </summary>
public class SalesforceApiService
{
    private readonly HttpClient _httpClient;
    private SalesforceConnection? _connection;

    public SalesforceApiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2) // Long timeout for large logs
        };
    }

    public bool IsConnected => _connection?.IsConnected ?? false;
    public SalesforceConnection? Connection => _connection;

    /// <summary>
    /// Authenticate with Salesforce using OAuth 2.0 tokens
    /// </summary>
    public async Task<SalesforceConnection> AuthenticateAsync(string instanceUrl, string accessToken, string refreshToken = "")
    {
        _connection = new SalesforceConnection
        {
            InstanceUrl = instanceUrl.TrimEnd('/'),
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenIssuedAt = DateTime.UtcNow,
            TokenExpiresAt = DateTime.UtcNow.AddHours(2) // Salesforce tokens typically expire after 2 hours
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Verify connection by getting user info
        try
        {
            var userInfoUrl = $"{_connection.InstanceUrl}/services/oauth2/userinfo";
            var response = await _httpClient.GetAsync(userInfoUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var userInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                
                if (userInfo != null)
                {
                    _connection.UserId = userInfo.ContainsKey("user_id") ? userInfo["user_id"].ToString() ?? "" : "";
                    _connection.OrgId = userInfo.ContainsKey("organization_id") ? userInfo["organization_id"].ToString() ?? "" : "";
                    _connection.Username = userInfo.ContainsKey("preferred_username") ? userInfo["preferred_username"].ToString() ?? "" : "";
                    _connection.OrgName = userInfo.ContainsKey("organization_id") ? userInfo["organization_id"].ToString() ?? "" : "";
                    
                    // Also try to get email if username not available
                    if (string.IsNullOrEmpty(_connection.Username) && userInfo.ContainsKey("email"))
                    {
                        _connection.Username = userInfo["email"].ToString() ?? "";
                    }
                }
            }
        }
        catch
        {
            // Continue even if user info fails
        }

        return _connection;
    }

    /// <summary>
    /// Query ApexLog records from Salesforce
    /// </summary>
    public async Task<List<ApexLog>> QueryLogsAsync(int limit = 100)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var query = $"SELECT Id,Application,DurationMilliseconds,Location,LogLength,LogUserId,Operation,Request,StartTime,Status " +
                    $"FROM ApexLog ORDER BY StartTime DESC LIMIT {limit}";

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/query/?q={encodedQuery}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<QueryResult<ApexLog>>(content);

        return result?.Records ?? new List<ApexLog>();
    }

    /// <summary>
    /// Retrieve the body content of a specific ApexLog
    /// </summary>
    public async Task<string?> GetLogBodyAsync(string logId)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/ApexLog/{logId}/Body";

        try
        {
            var response = await _httpClient.GetAsync(url);
            
            // If log was deleted, return null instead of throwing
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            // Log was deleted (24 hour auto-delete)
            return null;
        }
    }

    /// <summary>
    /// Delete old debug logs (Salesforce keeps logs for 24 hours)
    /// </summary>
    public async Task<int> DeleteOldLogsAsync(int hoursOld = 1)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        try
        {
            // Query logs older than specified hours
            var cutoffTime = DateTime.UtcNow.AddHours(-hoursOld);
            var query = $"SELECT Id FROM ApexLog WHERE StartTime < {cutoffTime:yyyy-MM-ddTHH:mm:ss.fffZ}";
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/query/?q={encodedQuery}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<QueryResult<ApexLog>>(content);

            if (result?.Records == null || result.Records.Count == 0)
                return 0;

            // Delete logs in batches (max 200 per request)
            int deleted = 0;
            foreach (var log in result.Records)
            {
                var deleteUrl = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/ApexLog/{log.Id}";
                var deleteResponse = await _httpClient.DeleteAsync(deleteUrl);
                if (deleteResponse.IsSuccessStatusCode)
                    deleted++;
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Query available DebugLevels
    /// </summary>
    public async Task<List<DebugLevel>> QueryDebugLevelsAsync()
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var query = "SELECT Id,DeveloperName,MasterLabel,ApexCode,ApexProfiling,Database,System,Validation,Visualforce,Workflow,Callout FROM DebugLevel";
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/query/?q={encodedQuery}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<QueryResult<DebugLevel>>(content);

        return result?.Records ?? new List<DebugLevel>();
    }

    /// <summary>
    /// Create a new TraceFlag for a user (or update existing one)
    /// </summary>
    public async Task<string> CreateTraceFlagAsync(string userId, string debugLevelId, DateTime expirationDate)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        // First, check if a TraceFlag already exists for this user
        var existingFlag = await GetExistingTraceFlagAsync(userId);
        
        if (existingFlag != null)
        {
            // Update the existing TraceFlag instead of creating a new one
            return await UpdateTraceFlagAsync(existingFlag.Id, debugLevelId, expirationDate);
        }

        var traceFlag = new
        {
            TracedEntityId = userId,
            LogType = "USER_DEBUG",
            DebugLevelId = debugLevelId,
            StartDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ExpirationDate = expirationDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/TraceFlag";
        var json = JsonConvert.SerializeObject(traceFlag);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create TraceFlag: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<CreateResult>(responseContent);

        return result?.Id ?? string.Empty;
    }

    /// <summary>
    /// Get existing TraceFlag for a user
    /// </summary>
    private async Task<TraceFlag?> GetExistingTraceFlagAsync(string userId)
    {
        if (_connection == null) return null;

        try
        {
            var query = $"SELECT Id,DebugLevelId,ExpirationDate,LogType,TracedEntityId,StartDate FROM TraceFlag WHERE TracedEntityId = '{userId}' AND LogType = 'USER_DEBUG'";
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/query/?q={encodedQuery}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<QueryResult<TraceFlag>>(content);

            return result?.Records?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Update an existing TraceFlag
    /// </summary>
    private async Task<string> UpdateTraceFlagAsync(string traceFlagId, string debugLevelId, DateTime expirationDate)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var updateData = new
        {
            DebugLevelId = debugLevelId,
            StartDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ExpirationDate = expirationDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/TraceFlag/{traceFlagId}";
        var json = JsonConvert.SerializeObject(updateData);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Use PATCH for update
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = content
        };

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to update TraceFlag: {errorContent}");
        }

        return traceFlagId; // Return the existing ID since update was successful
    }

    /// <summary>
    /// Create a new DebugLevel configuration
    /// </summary>
    public async Task<string> CreateDebugLevelAsync(DebugLevel debugLevel)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/DebugLevel";
        var json = JsonConvert.SerializeObject(debugLevel);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<CreateResult>(responseContent);

        return result?.Id ?? string.Empty;
    }

    /// <summary>
    /// Query active TraceFlags
    /// </summary>
    public async Task<List<TraceFlag>> QueryTraceFlagsAsync()
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var query = "SELECT Id,DebugLevelId,ExpirationDate,LogType,TracedEntityId,StartDate FROM TraceFlag";
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/query/?q={encodedQuery}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<QueryResult<TraceFlag>>(content);

        return result?.Records ?? new List<TraceFlag>();
    }

    /// <summary>
    /// Delete an ApexLog record
    /// </summary>
    public async Task DeleteLogAsync(string logId)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/ApexLog/{logId}";
        var response = await _httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Delete a TraceFlag
    /// </summary>
    public async Task DeleteTraceFlagAsync(string traceFlagId)
    {
        if (_connection == null || !_connection.IsConnected)
            throw new InvalidOperationException("Not connected to Salesforce");

        var url = $"{_connection.InstanceUrl}/services/data/v60.0/tooling/sobjects/TraceFlag/{traceFlagId}";
        var response = await _httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Disconnect and clear authentication
    /// </summary>
    public void Disconnect()
    {
        _connection = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private class QueryResult<T>
    {
        public List<T>? Records { get; set; }
        public int TotalSize { get; set; }
        public bool Done { get; set; }
    }

    private class CreateResult
    {
        public string? Id { get; set; }
        public bool Success { get; set; }
        public List<string>? Errors { get; set; }
    }
}
