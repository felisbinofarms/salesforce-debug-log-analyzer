namespace SalesforceDebugAnalyzer.Services.Abstractions;

/// <summary>
/// Abstracts clipboard operations so ViewModels don't reference WPF/Avalonia directly.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
