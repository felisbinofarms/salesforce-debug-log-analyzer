namespace SalesforceDebugAnalyzer.Models;

/// <summary>
/// Represents a Salesforce org connection
/// </summary>
public class SalesforceConnection
{
    public string InstanceUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public DateTime TokenIssuedAt { get; set; }
    public DateTime TokenExpiresAt { get; set; }
    public bool IsConnected => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < TokenExpiresAt;
}

/// <summary>
/// Represents an ApexLog record from Salesforce
/// </summary>
public class ApexLog
{
    public string Id { get; set; } = string.Empty;
    public string Application { get; set; } = string.Empty;
    public int DurationMilliseconds { get; set; }
    public string Location { get; set; } = string.Empty;
    public int LogLength { get; set; }
    public string LogUserId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Request { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Represents a TraceFlag for debug logging
/// </summary>
public class TraceFlag
{
    public string Id { get; set; } = string.Empty;
    public string TracedEntityId { get; set; } = string.Empty;
    public string LogType { get; set; } = "USER_DEBUG";
    public string DebugLevelId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime ExpirationDate { get; set; }
}

/// <summary>
/// Represents a DebugLevel configuration
/// </summary>
public class DebugLevel
{
    public string Id { get; set; } = string.Empty;
    public string DeveloperName { get; set; } = string.Empty;
    public string MasterLabel { get; set; } = string.Empty;
    public string ApexCode { get; set; } = "FINEST";
    public string ApexProfiling { get; set; } = "FINEST";
    public string Database { get; set; } = "FINEST";
    public string System { get; set; } = "DEBUG";
    public string Validation { get; set; } = "INFO";
    public string Visualforce { get; set; } = "INFO";
    public string Workflow { get; set; } = "INFO";
    public string Callout { get; set; } = "INFO";
}
