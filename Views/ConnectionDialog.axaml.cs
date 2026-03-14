using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SalesforceDebugAnalyzer.Views;

public partial class ConnectionDialog : Window
{
    public bool IsSandbox { get; set; }

    public ConnectionDialog() : this(false) { }

    public ConnectionDialog(bool isSandbox)
    {
        IsSandbox = isSandbox;
        DataContext = this;
        InitializeComponent();
        UseSandboxCheckBox.IsChecked = isSandbox;
    }

    private void OAuthLoginButton_Click(object? sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Opening browser...";
        OAuthLoginButton.IsEnabled = false;

        try
        {
            // TODO: Implement full OAuth browser flow for Avalonia (GH#5)
            // For now, direct user to manual token tab
            StatusTextBlock.Text = "OAuth browser flow coming soon — use Access Token tab for now.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error: {ex.Message}";
        }
        finally
        {
            OAuthLoginButton.IsEnabled = true;
        }
    }

    private async void ManualConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        var instanceUrl = InstanceUrlTextBox?.Text?.Trim() ?? string.Empty;
        var accessToken = AccessTokenTextBox?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(accessToken))
        {
            StatusTextBlock.Text = "Please enter both Instance URL and Access Token.";
            return;
        }

        StatusTextBlock.Text = "Connecting...";
        ManualConnectButton.IsEnabled = false;

        try
        {
            var result = new ConnectionDialogResult
            {
                Success = true,
                InstanceUrl = instanceUrl,
                AccessToken = accessToken
            };

            await Task.Delay(100); // Brief visual feedback

            StatusTextBlock.Text = "Connected!";
            Close(result);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            ManualConnectButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
