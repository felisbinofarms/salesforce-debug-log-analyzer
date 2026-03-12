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
        var emailDialog = new EmailPromptDialog { Owner = this };

        if (emailDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(emailDialog.Email))
            return;

        try
        {
            StartTrialButton.IsEnabled = false;
            StartTrialButton.Content = "Activating...";

            var result = await _licenseService.StartTrialAsync(emailDialog.Email);

            if (result.Success)
            {
                MessageBox.Show(
                    result.Message + "\n\nAll Pro features are now unlocked. Enjoy!",
                    "Trial Activated! 🎉",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                _onUpgradeComplete?.Invoke();
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(result.Message, "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start trial: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartTrialButton.IsEnabled = true;
            StartTrialButton.Content = "Start Free Trial";
        }
    }

    private void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LicenseService.CheckoutMonthlyUrl,
                UseShellExecute = true
            });

            // Show license key entry after they purchase
            MessageBox.Show(
                "Your browser will open the checkout page.\n\n" +
                "After purchasing, check your email for a license key,\n" +
                "then use Settings → License → Activate Key to unlock Pro.",
                "Opening Checkout",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open checkout: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ActivateKey_Click(object sender, RoutedEventArgs e)
    {
        var emailDialog = new EmailPromptDialog { Owner = this, Mode = EmailPromptMode.ActivateKey };

        if (emailDialog.ShowDialog() != true)
            return;

        try
        {
            if (sender is System.Windows.Controls.Button btn) btn.IsEnabled = false;

            var result = await _licenseService.ApplyLicenseAsync(emailDialog.LicenseKey, emailDialog.Email);

            if (result.Success)
            {
                MessageBox.Show(
                    result.Message,
                    "License Activated! 🎉",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _onUpgradeComplete?.Invoke();
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(result.Message, "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Activation error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (sender is System.Windows.Controls.Button btn2) btn2.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public enum EmailPromptMode { Trial, ActivateKey }

/// <summary>
/// Prompts for email (trial) or license key + email (activation).
/// </summary>
public class EmailPromptDialog : Window
{
    public string Email { get; private set; } = string.Empty;
    public string LicenseKey { get; private set; } = string.Empty;
    public EmailPromptMode Mode { get; init; } = EmailPromptMode.Trial;

    private System.Windows.Controls.TextBox? _emailBox;
    private System.Windows.Controls.TextBox? _keyBox;

    public EmailPromptDialog()
    {
        Title = "Activate License";
        Width = 440;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1E, 0x1F, 0x22));
        ResizeMode = ResizeMode.NoResize;
        Loaded += (_, _) => BuildUI();
    }

    private void BuildUI()
    {
        var isKeyMode = Mode == EmailPromptMode.ActivateKey;
        Height = isKeyMode ? 280 : 240;

        var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };

        root.Children.Add(MakeLabel(isKeyMode ? "License Key" : "Your email address"));
        if (isKeyMode)
        {
            _keyBox = MakeTextBox("Paste your license key here...");
            root.Children.Add(_keyBox);
            root.Children.Add(new System.Windows.Controls.TextBlock
            {
                Height = 8, Text = ""
            });
        }

        root.Children.Add(MakeLabel("Email address"));
        _emailBox = MakeTextBox("you@example.com");
        root.Children.Add(_emailBox);

        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var okBtn = new System.Windows.Controls.Button
        {
            Content = isKeyMode ? "Activate" : "Start Trial",
            Width = 110, Height = 34,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x58, 0x65, 0xF2)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 0)
        };
        okBtn.Click += OkClick;

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Cancel", Width = 80, Height = 34
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        root.Children.Add(btnRow);

        Content = root;
        _emailBox.Focus();
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        Email = _emailBox?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            MessageBox.Show("Please enter a valid email address.", "Invalid Email",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Mode == EmailPromptMode.ActivateKey)
        {
            LicenseKey = _keyBox?.Text.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(LicenseKey))
            {
                MessageBox.Show("Please paste your license key.", "Missing Key",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private static System.Windows.Controls.TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 13,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB5, 0xBA, 0xC1)),
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static System.Windows.Controls.TextBox MakeTextBox(string placeholder) =>
        new()
        {
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x31, 0x33, 0x38)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Tag = placeholder
        };
}
