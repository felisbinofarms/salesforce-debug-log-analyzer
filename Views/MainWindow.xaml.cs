using System.Windows;
using SalesforceDebugAnalyzer.ViewModels;
using SalesforceDebugAnalyzer.Services;

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

        // Show connections view initially
        ShowConnectionsView(salesforceApi);
    }

    private void ShowConnectionsView(SalesforceApiService apiService)
    {
        _connectionsView = new ConnectionsView(apiService);
        _connectionsView.ConnectionEstablished += OnConnectionEstablished;
        ConnectionsViewContainer.Content = _connectionsView;
        ConnectionsViewContainer.Visibility = Visibility.Visible;
        MainContentGrid.Visibility = Visibility.Collapsed;
    }

    private void OnConnectionEstablished(object? sender, SalesforceApiService apiService)
    {
        // Hide connections view, show main content
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        
        // Update view model
        _viewModel.OnConnected();
        
        // Show success message
        System.Windows.MessageBox.Show(
            "Successfully connected! Click 'Manage Debug Logs' to set up trace flags and monitor logs in real-time.",
            "Connected",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}