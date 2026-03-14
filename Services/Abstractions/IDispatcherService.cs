namespace SalesforceDebugAnalyzer.Services.Abstractions;

/// <summary>
/// Abstracts UI thread dispatching so ViewModels don't reference WPF/Avalonia directly.
/// </summary>
public interface IDispatcherService
{
    /// <summary>Run an action on the UI thread synchronously.</summary>
    void Invoke(Action action);

    /// <summary>Run an action on the UI thread asynchronously.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Run an async func on the UI thread.</summary>
    Task InvokeAsync(Func<Task> func);
}
