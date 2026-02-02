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
        // Hide connections view
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        
        // Show Debug Setup Wizard in main content area
        var wizard = new DebugSetupWizard(apiService);
        wizard.WizardCompleted += (s, e) =>
        {
            // Show main dashboard after wizard completes
            ConnectionsViewContainer.Content = null;
            MainContentGrid.Visibility = Visibility.Visible;
            _viewModel.OnConnected();
        };
        wizard.WizardCancelled += (s, e) =>
        {
            // Go back to connection view if cancelled
            ConnectionsViewContainer.Content = _connectionsView;
            ConnectionsViewContainer.Visibility = Visibility.Visible;
        };
        
        ConnectionsViewContainer.Content = wizard;
        ConnectionsViewContainer.Visibility = Visibility.Visible;
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
}