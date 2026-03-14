using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SalesforceDebugAnalyzer.Views;

public class StreamingOptions
{
    public bool FilterByUser { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IncludeApex { get; set; } = true;
    public bool IncludeFlows { get; set; } = true;
    public bool IncludeValidation { get; set; } = true;
    public bool IncludeLightning { get; set; } = true;
    public bool IncludeApi { get; set; } = true;
    public bool IncludeBatch { get; set; } = true;
    public bool OnlyErrors { get; set; }
    public bool SkipSlowLogs { get; set; }
    public bool AutoSwitch { get; set; } = true;
}

public partial class StreamingOptionsDialog : Window
{
    public StreamingOptions? Options { get; private set; }

    public StreamingOptionsDialog() : this(null) { }

    public StreamingOptionsDialog(string? defaultUsername)
    {
        InitializeComponent();

        if (!string.IsNullOrEmpty(defaultUsername))
        {
            UsernameTextBox.Text = defaultUsername;
            FilterByUserCheck.IsChecked = true;
        }
    }

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        Options = new StreamingOptions
        {
            FilterByUser = FilterByUserCheck.IsChecked == true,
            Username = UsernameTextBox.Text?.Trim() ?? "",
            IncludeApex = IncludeApexCheck.IsChecked == true,
            IncludeFlows = IncludeFlowsCheck.IsChecked == true,
            IncludeValidation = IncludeValidationCheck.IsChecked == true,
            IncludeLightning = IncludeLightningCheck.IsChecked == true,
            IncludeApi = IncludeApiCheck.IsChecked == true,
            IncludeBatch = IncludeBatchCheck.IsChecked == true,
            OnlyErrors = OnlyErrorsCheck.IsChecked == true,
            SkipSlowLogs = SkipSlowCheck.IsChecked == true,
            AutoSwitch = AutoSwitchCheck.IsChecked == true,
        };
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
