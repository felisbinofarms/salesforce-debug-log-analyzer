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
    private readonly LogMetadataExtractor _metadataExtractor;
    private readonly LogGroupService _groupService;

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
    private ObservableCollection<LogGroup> _logGroups = new();

    [ObservableProperty]
    private LogGroup? _selectedLogGroup;

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
        _metadataExtractor = new LogMetadataExtractor();
        _groupService = new LogGroupService();
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
    private async Task LoadLogFolder()
    {
        StatusMessage = "Select folder containing debug logs...";

        try
        {
            // Use FolderBrowserDialog for WPF compatibility
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Folder with Debug Logs",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                StatusMessage = "Scanning folder for logs...";
                IsLoading = true;

                var folderPath = folderDialog.SelectedPath;

                // Extract metadata from all logs quickly
                var metadata = await Task.Run(() => _metadataExtractor.ExtractMetadataFromDirectory(folderPath));

                if (metadata.Count == 0)
                {
                    StatusMessage = "No log files found in folder";
                    return;
                }

                StatusMessage = $"Found {metadata.Count} logs, grouping by transaction...";

                // Group related logs
                var groups = await Task.Run(() => _groupService.GroupRelatedLogs(metadata));

                LogGroups.Clear();
                foreach (var group in groups)
                {
                    LogGroups.Add(group);
                }

                StatusMessage = $"✓ Loaded {metadata.Count} logs grouped into {groups.Count} transaction(s)";

                // Auto-select first group
                if (LogGroups.Any())
                {
                    SelectedLogGroup = LogGroups.First();
                }
            }
            else
            {
                StatusMessage = "Folder selection cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading folder: {ex.Message}";
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
            // Open trace flag dialog to view/manage logs
            StatusMessage = "Opening logs view...";
            
            var dialog = new TraceFlagDialog(_apiService, _parserService);
            
            if (dialog.ShowDialog() == true && dialog.DownloadedLogAnalysis != null)
            {
                // Add the downloaded log to the list
                Logs.Insert(0, dialog.DownloadedLogAnalysis);
                SelectedLog = dialog.DownloadedLogAnalysis;
                StatusMessage = "✓ Log downloaded and analyzed";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
