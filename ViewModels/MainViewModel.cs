using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

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
    private readonly SalesforceCliService _cliService;

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

    // Stack Analysis display
    [ObservableProperty]
    private string _stackAnalysisSummary = "";

    [ObservableProperty]
    private bool _hasStackRisk = false;

    // ===== VISUAL DASHBOARD PROPERTIES =====
    
    // Hero Stats
    [ObservableProperty]
    private string _heroStatus = ""; // "SUCCESS", "WARNING", "FAILED"
    
    [ObservableProperty]
    private string _heroDuration = "0ms";
    
    [ObservableProperty]
    private string _heroEntryPoint = "";
    
    // Stat Cards
    [ObservableProperty]
    private int _statSoqlCount = 0;
    
    [ObservableProperty]
    private int _statSoqlLimit = 100;
    
    [ObservableProperty]
    private int _statDmlCount = 0;
    
    [ObservableProperty]
    private int _statDmlLimit = 150;
    
    [ObservableProperty]
    private int _statCpuTime = 0;
    
    [ObservableProperty]
    private int _statCpuLimit = 10000;
    
    [ObservableProperty]
    private int _statMethodCount = 0;
    
    [ObservableProperty]
    private int _statErrorCount = 0;
    
    [ObservableProperty]
    private int _statWarningCount = 0;
    
    // Governor Limit Percentages (for progress bars)
    [ObservableProperty]
    private double _soqlPercent = 0;
    
    [ObservableProperty]
    private double _dmlPercent = 0;
    
    [ObservableProperty]
    private double _cpuPercent = 0;
    
    // Timing breakdown
    [ObservableProperty]
    private double _wallClockMs = 0;
    
    [ObservableProperty]
    private double _cpuTimeMs = 0;
    
    [ObservableProperty]
    private double _overheadMs = 0;
    
    [ObservableProperty]
    private bool _showTimingBreakdown = false;

    // Filter properties
    [ObservableProperty]
    private bool _showSoqlOperations = true;

    [ObservableProperty]
    private bool _showDmlOperations = true;

    [ObservableProperty]
    private bool _showErrorsOnly = false;

    [ObservableProperty]
    private bool _showDebugStatements = false;

    // Tab selection (0=Summary, 1=Tree, 2=Timeline, 3=Queries)
    [ObservableProperty]
    private int _selectedTabIndex = 0;

    // Streaming properties
    [ObservableProperty]
    private bool _isStreaming = false;

    [ObservableProperty]
    private bool _isCliInstalled = false;

    [ObservableProperty]
    private string _streamingStatus = "";

    [ObservableProperty]
    private ObservableCollection<StreamingLogEntry> _streamingLogs = new();

    [ObservableProperty]
    private string _streamingUsername = "";

    // ===== INTERACTION RECORDING PROPERTIES =====
    
    [ObservableProperty]
    private bool _isRecording = false;
    
    [ObservableProperty]
    private DateTime _recordingStartTime;
    
    [ObservableProperty]
    private string _recordingElapsedTime = "00:00";
    
    /// <summary>Buffer for logs captured during recording</summary>
    private List<LogAnalysis> _recordingBuffer = new();
    
    /// <summary>Timer to update elapsed time display</summary>
    private System.Windows.Threading.DispatcherTimer? _recordingTimer;
    
    [ObservableProperty]
    private ObservableCollection<Interaction> _interactions = new();
    
    [ObservableProperty]
    private Interaction? _selectedInteraction;

    public MainViewModel(SalesforceApiService salesforceApi, LogParserService parserService, OAuthService oauthService)
    {
        _apiService = salesforceApi;
        _parserService = parserService;
        _oauthService = oauthService;
        _metadataExtractor = new LogMetadataExtractor();
        _groupService = new LogGroupService();
        _cliService = new SalesforceCliService();
        
        // Check CLI installation
        IsCliInstalled = _cliService.IsInstalled;
        
        // Subscribe to CLI events
        _cliService.StatusChanged += OnCliStatusChanged;
        _cliService.LogReceived += OnLogReceived;
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
            SummaryText = value.Summary ?? "No summary available";
            Issues = new ObservableCollection<string>(value.Issues ?? new List<string>());
            Recommendations = new ObservableCollection<string>(value.Recommendations ?? new List<string>());
            DatabaseOperations = new ObservableCollection<DatabaseOperation>(value.DatabaseOperations ?? new List<DatabaseOperation>());
            
            // Stack analysis display
            if (value.StackAnalysis != null)
            {
                StackAnalysisSummary = value.StackAnalysis.Summary ?? "";
                HasStackRisk = value.StackAnalysis.RiskLevel >= StackRiskLevel.Warning;
            }
            else
            {
                StackAnalysisSummary = "";
                HasStackRisk = false;
            }
            
            // ===== POPULATE VISUAL DASHBOARD =====
            
            // Hero section
            HeroEntryPoint = value.EntryPoint ?? "";
            HeroDuration = FormatDuration(value.DurationMs);
            
            if (value.TransactionFailed)
                HeroStatus = "FAILED";
            else if (value.HandledExceptions?.Count > 0 || value.HasErrors)
                HeroStatus = "WARNING";
            else
                HeroStatus = "SUCCESS";
            
            // Stat cards
            StatSoqlCount = value.DatabaseOperations?.Count(d => d.OperationType == "SOQL") ?? 0;
            StatDmlCount = value.DatabaseOperations?.Count(d => d.OperationType == "DML") ?? 0;
            StatMethodCount = value.MethodStats?.Count ?? 0;
            StatErrorCount = value.Errors?.Count ?? 0;
            StatWarningCount = value.HandledExceptions?.Count ?? 0;
            
            // Governor limits from snapshot
            var lastSnapshot = value.LimitSnapshots?.LastOrDefault();
            if (lastSnapshot != null)
            {
                StatSoqlLimit = lastSnapshot.SoqlQueriesLimit;
                StatDmlLimit = lastSnapshot.DmlStatementsLimit;
                StatCpuTime = lastSnapshot.CpuTime;
                StatCpuLimit = lastSnapshot.CpuTimeLimit;
                
                // Calculate percentages for progress bars
                SoqlPercent = lastSnapshot.SoqlQueriesLimit > 0 
                    ? (lastSnapshot.SoqlQueries * 100.0) / lastSnapshot.SoqlQueriesLimit 
                    : 0;
                DmlPercent = lastSnapshot.DmlStatementsLimit > 0 
                    ? (lastSnapshot.DmlStatements * 100.0) / lastSnapshot.DmlStatementsLimit 
                    : 0;
                CpuPercent = lastSnapshot.CpuTimeLimit > 0 
                    ? (lastSnapshot.CpuTime * 100.0) / lastSnapshot.CpuTimeLimit 
                    : 0;
            }
            else
            {
                SoqlPercent = 0;
                DmlPercent = 0;
                CpuPercent = 0;
            }
            
            // Timing breakdown
            WallClockMs = value.WallClockMs > 0 ? value.WallClockMs : value.DurationMs;
            CpuTimeMs = value.CpuTimeMs;
            OverheadMs = WallClockMs - CpuTimeMs;
            ShowTimingBreakdown = OverheadMs > 1000 && WallClockMs > 2000;
        }
        else
        {
            SummaryText = "";
            Issues.Clear();
            Recommendations.Clear();
            DatabaseOperations.Clear();
            StackAnalysisSummary = "";
            HasStackRisk = false;
            
            // Reset visual dashboard
            HeroStatus = "";
            HeroDuration = "";
            HeroEntryPoint = "";
            StatSoqlCount = 0;
            StatDmlCount = 0;
            StatMethodCount = 0;
            StatErrorCount = 0;
            StatWarningCount = 0;
            SoqlPercent = 0;
            DmlPercent = 0;
            CpuPercent = 0;
            ShowTimingBreakdown = false;
        }
    }
    
    private string FormatDuration(double ms)
    {
        if (ms < 1000) return $"{ms:N0}ms";
        if (ms < 60000) return $"{ms / 1000.0:N1}s";
        return $"{ms / 60000.0:N1}m";
    }

    [RelayCommand]
    private void SelectLog(LogAnalysis? log)
    {
        if (log != null)
        {
            SelectedLog = log;
            StatusMessage = $"Selected: {log.LogName}";
        }
    }

    [RelayCommand]
    private void SelectTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }

    [RelayCommand]
    private async Task ConnectToSalesforce()
    {
        StatusMessage = "Opening connection dialog...";

        try
        {
            var result = await Task.Run(() =>
            {
                var connectionDialog = new ConnectionDialog(_oauthService, _apiService);
                return connectionDialog.ShowDialog() == true;
            });

            if (result)
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
        StatusMessage = "Opening file selector...";
        
        try
        {
            // Show drag-and-drop dialog (no slow file browser)
            var filePath = await Task.Run(() => ShowDragDropDialog());
            
            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "Drag a log file onto the window, or paste the path";
                return;
            }
            
            await LoadLogFromPath(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Loads and parses a log file from the given path (called from drag-drop or paste)
    /// </summary>
    public async Task LoadLogFromPath(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"File not found: {filePath}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            StatusMessage = "Reading log file...";
            IsLoading = true;

            var fileName = Path.GetFileName(filePath);
            
            // Read file on background thread to prevent UI freeze
            var logContent = await Task.Run(() => File.ReadAllText(filePath));

            StatusMessage = "Parsing log...";
            var analysis = await Task.Run(() => _parserService.ParseLog(logContent, fileName));
            
            Logs.Insert(0, analysis);
            SelectedLog = analysis;

            StatusMessage = $"✓ Log parsed successfully - {analysis.Summary}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load log: {ex.Message}";
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
            
            var result = await Task.Run(() =>
            {
                var dialog = new TraceFlagDialog(_apiService, _parserService);
                var success = dialog.ShowDialog() == true;
                return new { Success = success, Analysis = dialog.DownloadedLogAnalysis };
            });
            
            if (result.Success && result.Analysis != null)
            {
                // Add the downloaded log to the list
                Logs.Insert(0, result.Analysis);
                SelectedLog = result.Analysis;
                StatusMessage = "✓ Log downloaded and analyzed";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleStreaming()
    {
        if (IsStreaming)
        {
            StopStreaming();
        }
        else
        {
            await StartStreamingAsync();
        }
    }
    
    [RelayCommand]
    private void StartRecording()
    {
        if (!IsStreaming)
        {
            StatusMessage = "⚠️ Start streaming first before recording an interaction";
            return;
        }
        
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        _recordingBuffer.Clear();
        RecordingElapsedTime = "00:00";
        
        // Start timer to update elapsed time
        _recordingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _recordingTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - RecordingStartTime;
            RecordingElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        };
        _recordingTimer.Start();
        
        StatusMessage = "🔴 Recording interaction... Perform your action in Salesforce";
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = "🔴 Recording started - Perform your action in Salesforce, then click Stop Recording",
            IsError = false
        });
    }
    
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (!IsRecording) return;
        
        _recordingTimer?.Stop();
        _recordingTimer = null;
        IsRecording = false;
        
        var endTime = DateTime.Now;
        var interaction = new Interaction
        {
            StartTime = RecordingStartTime,
            EndTime = endTime,
            CapturedLogs = new List<LogAnalysis>(_recordingBuffer)
        };
        
        // Group the captured logs into transactions
        if (_recordingBuffer.Count > 1)
        {
            try
            {
                // Use consistent UserId for all logs so they can be grouped together
                // The grouping algorithm matches by UserId + time window
                var streamingUserId = StreamingUsername?.GetHashCode().ToString("X8") ?? "UNKNOWN";
                
                var metadata = _recordingBuffer.Select(log => new DebugLogMetadata
                {
                    LogId = log.LogId,
                    Timestamp = log.ParsedAt,
                    DurationMs = log.DurationMs,
                    UserName = StreamingUsername,
                    UserId = streamingUserId, // All logs get same UserId for grouping
                    MethodName = log.EntryPoint,
                    HasErrors = log.HasErrors,
                    SoqlQueries = log.LimitSnapshots.FirstOrDefault()?.SoqlQueries ?? 0,
                    DmlStatements = log.LimitSnapshots.FirstOrDefault()?.DmlStatements ?? 0,
                    CpuTime = log.CpuTimeMs
                }).ToList();
                
                interaction.LogGroups = await Task.Run(() => _groupService.GroupRelatedLogs(metadata));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to group interaction logs: {ex.Message}");
            }
        }
        
        // Add to interactions list
        Interactions.Insert(0, interaction);
        SelectedInteraction = interaction;
        
        _recordingBuffer.Clear();
        
        var durationStr = interaction.UserWaitTimeMs < 1000 
            ? $"{interaction.UserWaitTimeMs:N0}ms" 
            : $"{interaction.UserWaitTimeMs / 1000.0:N1}s";
        
        StatusMessage = $"✅ Interaction recorded: {interaction.CapturedLogs.Count} logs, {durationStr} total wait time";
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = $"✅ Recording stopped - Captured {interaction.CapturedLogs.Count} logs ({durationStr})",
            IsError = false
        });
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cliService.StopStreaming();
        IsStreaming = false;
        StreamingUsername = null;
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = "⏸️ Streaming stopped",
            IsError = false
        });
        
        StatusMessage = "Streaming stopped";
    }

    private async Task StartStreamingAsync()
    {
        if (!_apiService.IsConnected || _apiService.Connection == null)
        {
            StatusMessage = "⚠️ Please connect to Salesforce first";
            return;
        }

        // Use the connected org's username
        var username = _apiService.Connection.Username;
        if (string.IsNullOrEmpty(username))
        {
            // Fall back to instance URL parsing or ask user
            StatusMessage = "⚠️ Unable to determine username for streaming";
            return;
        }

        StreamingUsername = username;
        StatusMessage = $"🕷️ Starting log stream for {username}...";
        StreamingStatus = "Connecting...";

        // Pass the API service to CLI service (no CLI needed anymore!)
        var success = await _cliService.StartStreamingAsync(_apiService, username);
        
        if (success)
        {
            IsStreaming = true;
            StreamingStatus = $"🔴 LIVE - {username}";
            StatusMessage = $"🕷️ Black Widow is watching... Waiting for logs from {username}";
        }
        else
        {
            StreamingStatus = "Failed to start";
        }
    }

    private void OnCliStatusChanged(object? sender, string status)
    {
        // Marshal to UI thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            // Add to streaming log
            StreamingLogs.Insert(0, new StreamingLogEntry
            {
                Timestamp = DateTime.Now,
                Message = status,
                IsError = status.Contains("⚠️") || status.Contains("Failed")
            });

            // Keep only last 100 entries
            while (StreamingLogs.Count > 100)
            {
                StreamingLogs.RemoveAt(StreamingLogs.Count - 1);
            }
        });
    }

    private void OnLogReceived(object? sender, LogReceivedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            try
            {
                StatusMessage = "🕷️ New log detected! Parsing...";

                // Parse the log content
                var analysis = await Task.Run(() => _parserService.ParseLog(e.LogContent, $"Stream_{e.Timestamp:HHmmss}"));
                
                // Add to logs list
                Logs.Insert(0, analysis);
                SelectedLog = analysis;
                
                // If recording, add to buffer
                if (IsRecording)
                {
                    _recordingBuffer.Add(analysis);
                    
                    StreamingLogs.Insert(0, new StreamingLogEntry
                    {
                        Timestamp = e.Timestamp,
                        Message = $"🔴 [Recording #{_recordingBuffer.Count}] {analysis.Summary}",
                        IsError = false,
                        Analysis = analysis
                    });
                }
                else
                {
                    // Add notification to streaming log
                    StreamingLogs.Insert(0, new StreamingLogEntry
                    {
                        Timestamp = e.Timestamp,
                        Message = $"✅ Log captured and parsed - {analysis.Summary}",
                        IsError = false,
                        Analysis = analysis
                    });
                }

                StatusMessage = IsRecording 
                    ? $"🔴 Recording... Log #{_recordingBuffer.Count} captured" 
                    : $"🕷️ Caught a log! {analysis.Summary}";
            }
            catch (Exception ex)
            {
                StreamingLogs.Insert(0, new StreamingLogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"❌ Failed to parse log: {ex.Message}",
                    IsError = true
                });
            }
        });
    }
    
    /// <summary>
    /// Shows a simple dialog to paste a file path
    /// </summary>
    private string? ShowDragDropDialog()
    {
        string? resultPath = null;
        
        var dialog = new Window
        {
            Title = "Open Debug Log",
            Width = 550,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 49, 54))
        };
        
        var mainStack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        
        // Instructions
        var label = new System.Windows.Controls.TextBlock
        {
            Text = "Paste log file path (right-click file → Copy as path):",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        
        // Path input row
        var inputRow = new System.Windows.Controls.Grid();
        inputRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        
        var textBox = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 68, 75)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        System.Windows.Controls.Grid.SetColumn(textBox, 0);
        
        var openButton = new System.Windows.Controls.Button
        {
            Content = "Open",
            Width = 80,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 101, 242)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        System.Windows.Controls.Grid.SetColumn(openButton, 1);
        
        openButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                resultPath = textBox.Text.Trim().Trim('"');
                dialog.DialogResult = true;
                dialog.Close();
            }
        };
        
        // Allow Enter key to submit
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                resultPath = textBox.Text.Trim().Trim('"');
                dialog.DialogResult = true;
                dialog.Close();
            }
        };
        
        inputRow.Children.Add(textBox);
        inputRow.Children.Add(openButton);
        
        mainStack.Children.Add(label);
        mainStack.Children.Add(inputRow);
        
        dialog.Content = mainStack;
        
        // Focus the textbox after dialog loads
        dialog.Loaded += (s, e) => textBox.Focus();
        
        dialog.ShowDialog();
        return resultPath;
    }
}

/// <summary>
/// Entry in the streaming log panel
/// </summary>
public class StreamingLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
    public bool IsError { get; set; }
    public LogAnalysis? Analysis { get; set; }
}
