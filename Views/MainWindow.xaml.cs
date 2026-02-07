using System.Windows;
using System.IO;
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
            }
        }
        e.Handled = true;
    }

    private void ShowConnectionsView(SalesforceApiService apiService)
    {
        _connectionsView = new ConnectionsView(apiService);
        _connectionsView.ConnectionEstablished += OnConnectionEstablished;
        _connectionsView.LogFileDropped += OnLogFileDropped;
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
        if (!string.IsNullOrEmpty(_viewModel.SummaryText))
        {
            Clipboard.SetText(_viewModel.SummaryText);
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
        
        await Task.Delay(1500);
        
        button.Content = originalContent;
        button.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x4E, 0x50, 0x58)); // Original gray
    }
    
    private void InteractionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.DataContext is Interaction interaction)
        {
            _viewModel.ViewInteractionCommand.Execute(interaction);
        }
    }
}