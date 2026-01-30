using System.Windows;
using System.Windows.Controls;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class TraceFlagDialog : Window
{
    private readonly SalesforceApiService _apiService;
    private readonly LogParserService _parserService;
    private List<DebugLevel> _debugLevels = new();
    private List<TraceFlag> _traceFlags = new();
    private List<ApexLog> _logs = new();

    public ApexLog? SelectedLog { get; private set; }
    public LogAnalysis? DownloadedLogAnalysis { get; private set; }

    public TraceFlagDialog(SalesforceApiService apiService, LogParserService parserService)
    {
        InitializeComponent();
        _apiService = apiService;
        _parserService = parserService;
        
        Loaded += TraceFlagDialog_Loaded;
    }

    private async void TraceFlagDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        SetLoading(true, "Loading data...");

        try
        {
            // Load current user info
            var connection = _apiService.Connection;
            if (connection != null)
            {
                CurrentUserTextBlock.Text = $"User ID: {connection.UserId}\nOrg ID: {connection.OrgId}";
            }

            // Load debug levels
            _debugLevels = await _apiService.QueryDebugLevelsAsync();
            DebugLevelComboBox.ItemsSource = _debugLevels;
            if (_debugLevels.Any())
            {
                DebugLevelComboBox.SelectedIndex = 0;
            }

            // Load active trace flags
            await RefreshTraceFlagsAsync();

            // Load recent logs
            await RefreshLogsAsync();

            SetLoading(false, "Ready");
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Error: {ex.Message}");
            MessageBox.Show($"Failed to load initial data: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EnableLoggingButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedDebugLevel = DebugLevelComboBox.SelectedItem as DebugLevel;
        if (selectedDebugLevel == null)
        {
            MessageBox.Show("Please select a debug level", "Missing Information", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var connection = _apiService.Connection;
        if (connection == null || string.IsNullOrEmpty(connection.UserId))
        {
            MessageBox.Show("User information not available", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetLoading(true, "Creating trace flag...");
        EnableLoggingButton.IsEnabled = false;

        try
        {
            var duration = (int)DurationSlider.Value;
            var expirationDate = DateTime.UtcNow.AddHours(duration);

            var traceFlagId = await _apiService.CreateTraceFlagAsync(
                connection.UserId, 
                selectedDebugLevel.Id, 
                expirationDate);

            SetLoading(false, $"✓ Debug logging enabled for {duration} hours");
            MessageBox.Show($"Debug logging enabled successfully!\n\nTrace Flag ID: {traceFlagId}\nExpires: {expirationDate.ToLocalTime()}", 
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            await RefreshTraceFlagsAsync();
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed to enable logging");
            MessageBox.Show($"Failed to create trace flag: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EnableLoggingButton.IsEnabled = true;
        }
    }

    private async void RefreshTraceFlagsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTraceFlagsAsync();
    }

    private async Task RefreshTraceFlagsAsync()
    {
        SetLoading(true, "Loading trace flags...");

        try
        {
            _traceFlags = await _apiService.QueryTraceFlagsAsync();
            TraceFlagsDataGrid.ItemsSource = _traceFlags;

            if (_traceFlags.Any())
            {
                NoTraceFlagsTextBlock.Visibility = Visibility.Collapsed;
                TraceFlagsDataGrid.Visibility = Visibility.Visible;
            }
            else
            {
                NoTraceFlagsTextBlock.Visibility = Visibility.Visible;
                TraceFlagsDataGrid.Visibility = Visibility.Collapsed;
            }

            SetLoading(false, $"Found {_traceFlags.Count} active trace flag(s)");
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed to load trace flags");
            MessageBox.Show($"Failed to load trace flags: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteTraceFlagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string traceFlagId)
        {
            var result = MessageBox.Show("Are you sure you want to delete this trace flag?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SetLoading(true, "Deleting trace flag...");

                try
                {
                    await _apiService.DeleteTraceFlagAsync(traceFlagId);
                    SetLoading(false, "✓ Trace flag deleted");
                    await RefreshTraceFlagsAsync();
                }
                catch (Exception ex)
                {
                    SetLoading(false, "Failed to delete trace flag");
                    MessageBox.Show($"Failed to delete trace flag: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void RefreshLogsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLogsAsync();
    }

    private async Task RefreshLogsAsync()
    {
        SetLoading(true, "Loading logs...");

        try
        {
            _logs = await _apiService.QueryLogsAsync(50);
            LogsDataGrid.ItemsSource = _logs;
            SetLoading(false, $"Found {_logs.Count} log(s)");
        }
        catch (Exception ex)
        {
            SetLoading(false, "Failed to load logs");
            MessageBox.Show($"Failed to load logs: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DownloadLogButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedLog = LogsDataGrid.SelectedItem as ApexLog;
        if (selectedLog == null)
        {
            MessageBox.Show("Please select a log to download", "No Selection", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetLoading(true, $"Downloading log (Size: {selectedLog.LogLength / 1024}KB)...");
        DownloadLogButton.IsEnabled = false;

        try
        {
            var logBody = await _apiService.GetLogBodyAsync(selectedLog.Id);
            
            SetLoading(true, "Parsing log...");
            var analysis = await Task.Run(() => _parserService.ParseLog(logBody, selectedLog.Id));

            SelectedLog = selectedLog;
            DownloadedLogAnalysis = analysis;

            SetLoading(false, "✓ Log downloaded and parsed");
            
            var result = MessageBox.Show(
                $"Log downloaded successfully!\n\n{analysis.Summary}\n\nWould you like to close this dialog and view the analysis?", 
                "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            SetLoading(false, "Failed to download log");
            MessageBox.Show($"Failed to download log: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadLogButton.IsEnabled = true;
        }
    }

    private void CreateDebugLevelButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DebugLevelDialog(_apiService);
        if (dialog.ShowDialog() == true)
        {
            // Reload debug levels
            _ = LoadInitialDataAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetLoading(bool isLoading, string message)
    {
        LoadingProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        StatusTextBlock.Text = message;
    }
}
