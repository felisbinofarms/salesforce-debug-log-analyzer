using System.Windows;

namespace SalesforceDebugAnalyzer.Views;

public partial class StreamingOptionsDialog : Window
{
    public StreamingOptions Options { get; private set; }
    
    public StreamingOptionsDialog(string? defaultUsername = null)
    {
        InitializeComponent();
        
        Options = new StreamingOptions();
        
        // Pre-fill username if provided
        if (!string.IsNullOrEmpty(defaultUsername))
        {
            FilterByUserCheckBox.IsChecked = true;
            UsernameTextBox.Text = defaultUsername;
        }
        
        DataContext = Options;
    }
    
    private void FilterByUserCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (FilterByUserCheckBox.IsChecked == true)
        {
            UsernameTextBox.Focus();
        }
    }
    
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate username if filter is enabled
        if (FilterByUserCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            MessageBox.Show(
                "Please enter a username to filter by, or uncheck the user filter option.",
                "Username Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UsernameTextBox.Focus();
            return;
        }
        
        // Capture all settings
        Options.FilterByUser = FilterByUserCheckBox.IsChecked == true;
        Options.Username = UsernameTextBox.Text?.Trim();
        Options.IncludeApex = IncludeApexCheckBox.IsChecked == true;
        Options.IncludeFlows = IncludeFlowsCheckBox.IsChecked == true;
        Options.IncludeValidation = IncludeValidationCheckBox.IsChecked == true;
        Options.IncludeLightning = IncludeLightningCheckBox.IsChecked == true;
        Options.IncludeApi = IncludeApiCheckBox.IsChecked == true;
        Options.IncludeBatch = IncludeBatchCheckBox.IsChecked == true;
        Options.OnlyErrors = OnlyErrorsCheckBox.IsChecked == true;
        Options.SkipSlowLogs = SkipSlowLogsCheckBox.IsChecked == true;
        Options.AutoSwitchToNewest = AutoSwitchCheckBox.IsChecked == false; // Default OFF for safety
        
        DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// Configuration options for live log streaming
/// </summary>
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
    public bool AutoSwitchToNewest { get; set; } // Default FALSE to prevent crash
}
