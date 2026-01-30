using System.Windows;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class ConnectionDialog : Window
{
    private readonly OAuthService _oauthService;
    private readonly SalesforceApiService _apiService;
    
    public bool IsConnected { get; private set; }
    public SalesforceApiService ApiService => _apiService;

    public ConnectionDialog(OAuthService oauthService, SalesforceApiService apiService)
    {
        InitializeComponent();
        _oauthService = oauthService;
        _apiService = apiService;
    }

    private async void OAuthLoginButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Opening login window...";
        OAuthLoginButton.IsEnabled = false;

        try
        {
            var useSandbox = UseSandboxCheckBox.IsChecked ?? false;
            
            // Open embedded browser dialog
            var browserDialog = new OAuthBrowserDialog(useSandbox)
            {
                Owner = this
            };
            
            var resultTask = browserDialog.AuthenticateAsync();
            browserDialog.ShowDialog();
            
            var result = await resultTask;

            if (result.Success)
            {
                await _apiService.AuthenticateAsync(result.InstanceUrl, result.AccessToken, result.RefreshToken);
                
                StatusTextBlock.Text = "✓ Connected successfully!";
                IsConnected = true;
                
                await Task.Delay(1000);
                DialogResult = true;
                Close();
            }
            else
            {
                StatusTextBlock.Text = $"❌ Failed: {result.Error}";
                MessageBox.Show(result.Error, "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Error: {ex.Message}";
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OAuthLoginButton.IsEnabled = true;
        }
    }

    private async void ManualConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var instanceUrl = InstanceUrlTextBox.Text.Trim();
        var accessToken = AccessTokenTextBox.Text.Trim();

        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(accessToken))
        {
            MessageBox.Show("Please enter both Instance URL and Access Token", "Missing Information", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusTextBlock.Text = "Connecting...";
        ManualConnectButton.IsEnabled = false;

        try
        {
            await _apiService.AuthenticateAsync(instanceUrl, accessToken);
            
            StatusTextBlock.Text = "✓ Connected successfully!";
            IsConnected = true;
            
            await Task.Delay(1000);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Connection failed";
            MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ManualConnectButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
