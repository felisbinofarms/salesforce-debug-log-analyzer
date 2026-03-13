using Avalonia.Controls;
using Avalonia.Interactivity;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class TraceFlagDialog : Window
{
    private readonly SalesforceApiService? _apiService;
    private readonly LogParserService? _parserService;
    private List<DebugLevel> _debugLevels = new();
    private List<TraceFlag> _traceFlags = new();
    private List<ApexLog> _logs = new();

    public ApexLog? SelectedLog { get; private set; }
    public LogAnalysis? DownloadedLogAnalysis { get; private set; }

    public TraceFlagDialog() : this(null!, null!) { }

    public TraceFlagDialog(SalesforceApiService apiService, LogParserService parserService)
    {
        InitializeComponent();
        _apiService = apiService;
        _parserService = parserService;

        Loaded += TraceFlagDialog_Loaded;

        DurationSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property == Slider.ValueProperty && DurationLabel != null)
            {
                var hours = (int)DurationSlider.Value;
                DurationLabel.Text = $"Expires in {hours} hour{(hours > 1 ? "s" : "")}";
            }
        };
    }

    private async void TraceFlagDialog_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null) return;
        await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        SetLoading(true, "Loading data...");

        try
        {
            var connection = _apiService!.Connection;
            if (connection != null)
            {
                CurrentUserTextBlock.Text = $"User ID: {connection.UserId}\nOrg ID: {connection.OrgId}";
            }

            _debugLevels = await _apiService.QueryDebugLevelsAsync();
            DebugLevelComboBox.ItemsSource = _debugLevels.Select(d => d.MasterLabel).ToList();
            if (_debugLevels.Any())
                DebugLevelComboBox.SelectedIndex = 0;

            await RefreshTraceFlagsAsync();
            await RefreshLogsAsync();
            SetLoading(false, "Ready");
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Error: {ex.Message}");
        }
    }

    private async void EnableLoggingButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null || DebugLevelComboBox.SelectedIndex < 0) return;

        var selectedDebugLevel = _debugLevels[DebugLevelComboBox.SelectedIndex];
        var connection = _apiService.Connection;
        if (connection == null || string.IsNullOrEmpty(connection.UserId)) return;

        SetLoading(true, "Creating trace flag...");
        EnableLoggingButton.IsEnabled = false;

        try
        {
            var duration = (int)DurationSlider.Value;
            var expirationDate = DateTime.UtcNow.AddHours(duration);

            await _apiService.CreateTraceFlagAsync(
                connection.UserId,
                selectedDebugLevel.Id,
                expirationDate);

            SetLoading(false, $"✓ Debug logging enabled for {duration} hours");
            await RefreshTraceFlagsAsync();
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed: {ex.Message}");
        }
        finally
        {
            EnableLoggingButton.IsEnabled = true;
        }
    }

    private async void RefreshTraceFlagsButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshTraceFlagsAsync();
    }

    private async Task RefreshTraceFlagsAsync()
    {
        SetLoading(true, "Loading trace flags...");

        try
        {
            _traceFlags = await _apiService!.QueryTraceFlagsAsync();
            TraceFlagsDataGrid.ItemsSource = _traceFlags;

            var hasFlags = _traceFlags.Any();
            TraceFlagsDataGrid.IsVisible = hasFlags;
            NoTraceFlagsTextBlock.IsVisible = !hasFlags;

            SetLoading(false, $"Found {_traceFlags.Count} active trace flag(s)");
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed to load trace flags: {ex.Message}");
        }
    }

    private async void RefreshLogsButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshLogsAsync();
    }

    private async Task RefreshLogsAsync()
    {
        SetLoading(true, "Loading logs...");

        try
        {
            _logs = await _apiService!.QueryLogsAsync(50);
            LogsDataGrid.ItemsSource = _logs;
            SetLoading(false, $"Found {_logs.Count} log(s)");
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed to load logs: {ex.Message}");
        }
    }

    private async void DownloadLogButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null || _parserService == null) return;

        var selectedLog = LogsDataGrid.SelectedItem as ApexLog;
        if (selectedLog == null) return;

        SetLoading(true, $"Downloading log (Size: {selectedLog.LogLength / 1024}KB)...");
        DownloadLogButton.IsEnabled = false;

        try
        {
            var logBody = await _apiService.GetLogBodyAsync(selectedLog.Id);
            if (string.IsNullOrEmpty(logBody))
            {
                SetLoading(false, "Download returned empty log body");
                DownloadLogButton.IsEnabled = true;
                return;
            }

            SetLoading(true, "Parsing log...");
            var analysis = await Task.Run(() => _parserService.ParseLog(logBody, selectedLog.Id));

            SelectedLog = selectedLog;
            DownloadedLogAnalysis = analysis;
            SetLoading(false, "✓ Log downloaded and parsed");

            Close(true);
        }
        catch (Exception ex)
        {
            SetLoading(false, $"Failed to download log: {ex.Message}");
        }
        finally
        {
            DownloadLogButton.IsEnabled = true;
        }
    }

    private async void CreateDebugLevelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null) return;

        var dialog = new DebugLevelDialog(_apiService);
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            await LoadInitialDataAsync();
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetLoading(bool isLoading, string message)
    {
        LoadingProgressBar.IsVisible = isLoading;
        StatusTextBlock.Text = message;
    }
}
