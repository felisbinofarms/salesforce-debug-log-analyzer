using SalesforceDebugAnalyzer.Services;
using System.Diagnostics;
using System.Windows;

namespace SalesforceDebugAnalyzer.Views;

public partial class UpgradeDialog : Window
{
    private readonly LicenseService _licenseService;
    private readonly Action? _onUpgradeComplete;

    public UpgradeDialog(LicenseService licenseService, Action? onUpgradeComplete = null)
    {
        InitializeComponent();
        _licenseService = licenseService;
        _onUpgradeComplete = onUpgradeComplete;
    }

    private async void StartTrial_Click(object sender, RoutedEventArgs e)
    {
        // Prompt for email
        var emailDialog = new EmailPromptDialog
        {
            Owner = this
        };

        if (emailDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(emailDialog.Email))
        {
            try
            {
                StartTrialButton.IsEnabled = false;
                StartTrialButton.Content = "Activating...";

                var result = await _licenseService.StartTrialAsync(emailDialog.Email);

                if (result.Success)
                {
                    MessageBox.Show(
                        result.Message + "\n\nRestart the application to see Pro features.",
                        "Trial Activated! 🎉",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    _onUpgradeComplete?.Invoke();
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        result.Message,
                        "Trial Activation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to start trial: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                StartTrialButton.IsEnabled = true;
                StartTrialButton.Content = "Start Free Trial";
            }
        }
    }

    private void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Integrate Stripe Checkout (Issue #3)
        // For now, open a placeholder URL
        try
        {
            var checkoutUrl = "https://blackwidow.dev/pricing";
            Process.Start(new ProcessStartInfo
            {
                FileName = checkoutUrl,
                UseShellExecute = true
            });

            MessageBox.Show(
                "Opening checkout page in your browser...\n\n" +
                "Note: Stripe integration coming soon! (Issue #3)",
                "Payment Flow",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open checkout: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// Simple dialog to prompt for email address
/// </summary>
public class EmailPromptDialog : Window
{
    public string Email { get; private set; } = string.Empty;

    public EmailPromptDialog()
    {
        Title = "Enter Your Email";
        Width = 400;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        ResizeMode = ResizeMode.NoResize;

        var grid = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(20)
        };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var label = new System.Windows.Controls.TextBlock
        {
            Text = "Enter your email to start the Pro trial:",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Controls.Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new System.Windows.Controls.TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 20)
        };
        System.Windows.Controls.Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(buttonPanel, 3);

        var okButton = new System.Windows.Controls.Button
        {
            Content = "Start Trial",
            Width = 100,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0)
        };
        okButton.Click += (s, e) =>
        {
            Email = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(Email) || !Email.Contains("@"))
            {
                MessageBox.Show("Please enter a valid email address", "Invalid Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32
        };
        cancelButton.Click += (s, e) =>
        {
            DialogResult = false;
            Close();
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        grid.Children.Add(buttonPanel);

        Content = grid;

        textBox.Focus();
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        };
    }
}
