namespace SalesforceDebugAnalyzer.Models;

public class StreamingOptions
{
    public bool FilterByUser { get; set; }
    public string? Username { get; set; }

    // Operation type filters
    public bool IncludeApex { get; set; } = true;
    public bool IncludeFlows { get; set; } = true;
    public bool IncludeValidation { get; set; } = true;
    public bool IncludeLightning { get; set; } = true;
    public bool IncludeApi { get; set; } = true;
    public bool IncludeBatch { get; set; } = true;

    // Performance options
    public bool OnlyErrors { get; set; }
    public bool SkipSlowLogs { get; set; }
    public bool AutoSwitchToNewest { get; set; }
}
