using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SalesforceDebugAnalyzer.Services;
using System.Diagnostics;

namespace SalesforceDebugAnalyzer.Views;

public partial class UpgradeDialog : Window
{
    private readonly LicenseService _licenseService;
    private readonly Action? _onUpgradeComplete;

    public UpgradeDialog() : this(new LicenseService()) { }

    public UpgradeDialog(LicenseService licenseService, Action? onUpgradeComplete = null)
    {
        InitializeComponent();
        _licenseService = licenseService;
        _onUpgradeComplete = onUpgradeComplete;
    }

    private async void StartTrial_Click(object? sender, RoutedEventArgs e)
    {
        var email = await PromptForEmailAsync("Start Free Trial", "Your email address", "you@example.com");
        if (string.IsNullOrWhiteSpace(email)) return;

        try
        {
            StartTrialButton.IsEnabled = false;
            StartTrialButton.Content = "Activating...";

            var result = await _licenseService.StartTrialAsync(email);
            if (result.Success)
            {
                _onUpgradeComplete?.Invoke();
                Close(true);
            }
        }
        catch { }
        finally
        {
            StartTrialButton.IsEnabled = true;
            StartTrialButton.Content = "Start 14-Day Free Trial";
        }
    }

    private void BuyNow_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LicenseService.CheckoutMonthlyUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void ActivateKey_Click(object? sender, RoutedEventArgs e)
    {
        var (key, email) = await PromptForKeyAsync();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(email)) return;

        try
        {
            var result = await _licenseService.ApplyLicenseAsync(key, email);
            if (result.Success)
            {
                _onUpgradeComplete?.Invoke();
                Close(true);
            }
        }
        catch { }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async Task<string?> PromptForEmailAsync(string title, string label, string watermark)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1E1F22"))
        };

        string? result = null;
        var emailBox = new TextBox
        {
            Watermark = watermark,
            Background = new SolidColorBrush(Color.Parse("#272D36")),
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            Margin = new Avalonia.Thickness(0, 0, 0, 16)
        };

        var okBtn = new Button
        {
            Content = "Continue",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(20, 8)
        };
        okBtn.Click += (_, _) => { result = emailBox.Text; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#C9D1D9")), Margin = new Avalonia.Thickness(0,0,0,8) },
                emailBox,
                okBtn
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<(string? key, string? email)> PromptForKeyAsync()
    {
        var dialog = new Window
        {
            Title = "Activate License Key",
            Width = 440, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#1E1F22"))
        };

        string? keyResult = null, emailResult = null;
        var keyBox = new TextBox
        {
            Watermark = "Paste your license key...",
            Background = new SolidColorBrush(Color.Parse("#272D36")),
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        var emailBox = new TextBox
        {
            Watermark = "you@example.com",
            Background = new SolidColorBrush(Color.Parse("#272D36")),
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            Margin = new Avalonia.Thickness(0, 0, 0, 16)
        };
        var okBtn = new Button
        {
            Content = "Activate",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(20, 8)
        };
        okBtn.Click += (_, _) => { keyResult = keyBox.Text; emailResult = emailBox.Text; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock { Text = "License Key", Foreground = new SolidColorBrush(Color.Parse("#C9D1D9")), Margin = new Avalonia.Thickness(0,0,0,8) },
                keyBox,
                new TextBlock { Text = "Email", Foreground = new SolidColorBrush(Color.Parse("#C9D1D9")), Margin = new Avalonia.Thickness(0,0,0,8) },
                emailBox,
                okBtn
            }
        };

        await dialog.ShowDialog(this);
        return (keyResult, emailResult);
    }
}
