using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using SalesforceDebugAnalyzer.ViewModels;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ConnectionsView? _connectionsView;

    public MainWindow()
    {
        InitializeComponent();
        
        var logParser = new LogParserService();
        var salesforceApi = new SalesforceApiService();
        var oauthService = new OAuthService();
        
        _viewModel = new MainViewModel(salesforceApi, logParser, oauthService);
        DataContext = _viewModel;
        
        // Enable drag-and-drop on entire window
        AllowDrop = true;
        Drop += MainWindow_Drop;
        DragOver += MainWindow_DragOver;

        // Show connections view initially
        ShowConnectionsView(salesforceApi);
    }
    
    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            // Only accept .log or .txt files
            if (files?.Length > 0 && (files[0].EndsWith(".log", StringComparison.OrdinalIgnoreCase) || 
                                       files[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    
    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                var filePath = files[0];
                if (File.Exists(filePath) && (filePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || 
                                               filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    await _viewModel.LoadLogFromPath(filePath);
                }
                else
                {
                    var ext = Path.GetExtension(filePath);
                    _viewModel.StatusMessage = $"⚠️ Can't open '{ext}' files — drop a .log or .txt debug log file";
                }
            }
        }
        e.Handled = true;
    }

    private void ShowConnectionsView(SalesforceApiService apiService)
    {
        _connectionsView = new ConnectionsView(apiService);
        _connectionsView.ConnectionEstablished += OnConnectionEstablished;
        _connectionsView.LogFileDropped += OnLogFileDropped;
        _connectionsView.LoadFolderRequested += OnLoadFolderRequested;
        _connectionsView.FolderDropped += OnFolderDropped;
        ConnectionsViewContainer.Content = _connectionsView;
        ConnectionsViewContainer.Visibility = Visibility.Visible;
        MainContentGrid.Visibility = Visibility.Collapsed;
    }

    private async void OnLogFileDropped(object? sender, string filePath)
    {
        // Hide connections view and show main content
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        
        // Load the dropped file
        await _viewModel.LoadLogFromPath(filePath);
    }
    
    private async void OnLoadFolderRequested(object? sender, EventArgs e)
    {
        // Hide connections view and show main content
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        
        // Trigger the LoadLogFolderCommand from ViewModel
        if (_viewModel.LoadLogFolderCommand.CanExecute(null))
        {
            await _viewModel.LoadLogFolderCommand.ExecuteAsync(null);
        }
    }

    private async void OnFolderDropped(object? sender, string folderPath)
    {
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        await _viewModel.LoadLogFolderFromPath(folderPath);
    }

    private void OnConnectionEstablished(object? sender, SalesforceApiService apiService)
    {
        // Hide connections view
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        
        // Skip wizard - go straight to main dashboard
        // (User can manually open wizard from menu if needed)
        ConnectionsViewContainer.Content = null;
        MainContentGrid.Visibility = Visibility.Visible;
        _viewModel.OnConnected();
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Copy buttons for summary sections
    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== LOG ANALYSIS SUMMARY ===");
        if (!string.IsNullOrEmpty(_viewModel.SummaryText))
            sb.AppendLine(_viewModel.SummaryText);
        sb.AppendLine();

        if (_viewModel.HasHealthData)
        {
            sb.AppendLine($"Health Score: {_viewModel.HealthScore}/100 — {_viewModel.HealthPlainStatement}");
            sb.AppendLine($"SOQL: {_viewModel.SoqlStatusIcon}  DML: {_viewModel.DmlStatusIcon}  CPU: {_viewModel.CpuStatusIcon}  Errors: {_viewModel.ErrorStatusIcon}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(_viewModel.FullSummaryText))
        {
            sb.AppendLine(_viewModel.FullSummaryText);
            sb.AppendLine();
        }

        if (_viewModel.CriticalIssues?.Count > 0)
        {
            sb.AppendLine("CRITICAL ISSUES:");
            foreach (var issue in _viewModel.CriticalIssues.Take(3))
            {
                sb.AppendLine($"  • {issue.Title}");
                if (!string.IsNullOrEmpty(issue.Fix))
                    sb.AppendLine($"    Fix: {issue.Fix}");
            }
            sb.AppendLine();
        }

        if (_viewModel.HighPriorityIssues?.Count > 0)
        {
            sb.AppendLine("HIGH PRIORITY:");
            foreach (var issue in _viewModel.HighPriorityIssues.Take(3))
            {
                sb.AppendLine($"  • {issue.Title}");
                if (!string.IsNullOrEmpty(issue.Fix))
                    sb.AppendLine($"    Fix: {issue.Fix}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Generated by Black Widow Log Analyzer");

        var text = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            ShowCopyFeedback(sender as System.Windows.Controls.Button);
        }
    }

    private void CopyStackAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.StackAnalysisSummary))
        {
            Clipboard.SetText(_viewModel.StackAnalysisSummary);
            ShowCopyFeedback(sender as System.Windows.Controls.Button);
        }
    }

    private void CopyIssues_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Issues?.Count > 0)
        {
            var text = string.Join("\n\n", _viewModel.Issues);
            Clipboard.SetText(text);
            ShowCopyFeedback(sender as System.Windows.Controls.Button);
        }
    }

    private void CopyRecommendations_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Recommendations?.Count > 0)
        {
            var text = string.Join("\n\n", _viewModel.Recommendations);
            Clipboard.SetText(text);
            ShowCopyFeedback(sender as System.Windows.Controls.Button);
        }
    }

    private async void ShowCopyFeedback(System.Windows.Controls.Button? button)
    {
        if (button == null) return;
        
        var originalContent = button.Content;
        button.Content = "✓ Copied!";
        button.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0xA5, 0x5D)); // Green
        
        try
        {
            await Task.Delay(1500);
            button.Content = originalContent;
            button.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4E, 0x50, 0x58)); // Original gray
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { /* Window may have closed */ }
    }
    
    private void InteractionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.DataContext is Interaction interaction)
        {
            _viewModel.ViewInteractionCommand.Execute(interaction);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = (sender as TextBox)?.Text?.Trim() ?? "";
        var view = CollectionViewSource.GetDefaultView(_viewModel.Logs);
        if (view == null) return;

        if (string.IsNullOrEmpty(searchText))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = item =>
            {
                if (item is LogAnalysis log)
                {
                    return (log.LogName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           (log.Summary?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           (log.EntryPoint?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);
                }
                return false;
            };
        }
    }
}