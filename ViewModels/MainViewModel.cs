using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Views;
using System.Collections.ObjectModel;
using System.IO;

namespace SalesforceDebugAnalyzer.ViewModels;

/// <summary>
/// Main ViewModel for the application
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly OAuthService _oauthService;
    private readonly SalesforceApiService _apiService;
    private readonly LogParserService _parserService;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private ObservableCollection<LogAnalysis> _logs = new();

    [ObservableProperty]
    private LogAnalysis? _selectedLog;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private ObservableCollection<string> _issues = new();

    [ObservableProperty]
    private ObservableCollection<string> _recommendations = new();

    [ObservableProperty]
    private ObservableCollection<DatabaseOperation> _databaseOperations = new();

    public MainViewModel(SalesforceApiService salesforceApi, LogParserService parserService, OAuthService oauthService)
    {
        _apiService = salesforceApi;
        _parserService = parserService;
        _oauthService = oauthService;
    }

    public void OnConnected()
    {
        IsConnected = true;
        ConnectionStatus = $"Connected: {_apiService.Connection?.InstanceUrl}";
        StatusMessage = "Ready";
    }

    partial void OnSelectedLogChanged(LogAnalysis? value)
    {
        if (value != null)
        {
            SummaryText = value.Summary;
            Issues = new ObservableCollection<string>(value.Issues);
            Recommendations = new ObservableCollection<string>(value.Recommendations);
            DatabaseOperations = new ObservableCollection<DatabaseOperation>(value.DatabaseOperations);
        }
        else
        {
            SummaryText = "";
            Issues.Clear();
            Recommendations.Clear();
            DatabaseOperations.Clear();
        }
    }

    [RelayCommand]
    private async Task ConnectToSalesforce()
    {
        StatusMessage = "Opening connection dialog...";

        try
        {
            var connectionDialog = new ConnectionDialog(_oauthService, _apiService);
            if (connectionDialog.ShowDialog() == true)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected to {_apiService.Connection?.InstanceUrl}";
                StatusMessage = "✓ Connected successfully";

                // Optionally load recent logs
                // await LoadRecentLogsAsync();
            }
            else
            {
                StatusMessage = "Connection cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task UploadLog()
    {
        StatusMessage = "Opening file dialog...";
        
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Salesforce Debug Log",
                Filter = "Log Files (*.log;*.txt)|*.log;*.txt|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                StatusMessage = "Reading log file...";
                IsLoading = true;

                var filePath = openFileDialog.FileName;
                var fileName = Path.GetFileName(filePath);
                var logContent = await File.ReadAllTextAsync(filePath);

                StatusMessage = "Parsing log...";
                var analysis = await Task.Run(() => _parserService.ParseLog(logContent, fileName));
                
                Logs.Insert(0, analysis);
                SelectedLog = analysis;

                StatusMessage = $"✓ Log parsed successfully - {analysis.Summary}";
            }
            else
            {
                StatusMessage = "File selection cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadRecentLogsAsync()
    {
        if (!_apiService.IsConnected)
        {
            StatusMessage = "Not connected to Salesforce";
            return;
        }

        StatusMessage = "Loading logs from Salesforce...";
        IsLoading = true;

        try
        {
            var apexLogs = await _apiService.QueryLogsAsync(20);
            StatusMessage = $"Found {apexLogs.Count} logs";

            // You could auto-load and parse these, but for now just notify
            StatusMessage = $"✓ Found {apexLogs.Count} logs (click to download and analyze)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        StatusMessage = "Opening settings...";
        // TODO: Implement settings dialog
    }

    [RelayCommand]
    private void Disconnect()
    {
        _apiService.Disconnect();
        IsConnected = false;
        ConnectionStatus = "Not connected";
        StatusMessage = "Disconnected from Salesforce";
    }

    [RelayCommand]
    private async Task ManageDebugLogs()
    {
        if (!_apiService.IsConnected)
        {
            StatusMessage = "⚠️ Please connect to Salesforce first";
            return;
        }

        try
        {
            // Show the guided setup wizard for first-time or easy setup
            var wizard = new DebugSetupWizard(_apiService);

            if (wizard.ShowDialog() == true && wizard.LoggingEnabled)
            {
                // Wizard completed, now show the trace flag dialog to view logs
                StatusMessage = "✓ Debug logging enabled. Opening logs view...";
                
                var dialog = new TraceFlagDialog(_apiService, _parserService);
                
                if (dialog.ShowDialog() == true && dialog.DownloadedLogAnalysis != null)
                {
                    // Add the downloaded log to the list
                    Logs.Insert(0, dialog.DownloadedLogAnalysis);
                    SelectedLog = dialog.DownloadedLogAnalysis;
                    StatusMessage = "✓ Log downloaded and analyzed";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
