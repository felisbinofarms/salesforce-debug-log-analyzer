using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
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

    public MainViewModel()
    {
        _oauthService = new OAuthService();
        _apiService = new SalesforceApiService();
        _parserService = new LogParserService();
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
        StatusMessage = "Connecting to Salesforce...";
        IsLoading = true;

        try
        {
            // For now, use manual token authentication (will implement full OAuth later)
            // You can get a token from Workbench or SF CLI: sf org display --target-org myOrg
            
            StatusMessage = "Opening browser for authentication...";
            var result = await _oauthService.AuthenticateAsync(useSandbox: false);

            if (result.Success)
            {
                await _apiService.AuthenticateAsync(result.InstanceUrl, result.AccessToken, result.RefreshToken);
                
                IsConnected = true;
                ConnectionStatus = $"Connected to {result.InstanceUrl}";
                StatusMessage = "✓ Connected successfully";

                // Load recent logs
                await LoadRecentLogsAsync();
            }
            else
            {
                StatusMessage = $"Connection failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
        finally
        {
            IsLoading = false;
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
}
