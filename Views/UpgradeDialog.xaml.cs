using SalesforceDebugAnalyzer.Services;
using System.Diagnostics;
using System.Windows;

namespace SalesforceDebugAnalyzer.Views;

public partial class UpgradeDialog : Window
{
    private readonly LicenseService _licenseService;

    public UpgradeDialog()
    {
        InitializeComponent();
        _licenseService = new LicenseService();
    }

    // ── Trial ──────────────────────────────────────────────────────────

    private void StartTrialButton_Click(object sender, RoutedEventArgs e)
    {
        _licenseService.StartTrial();

        MessageBox.Show(
            "✅ Your 14-day Pro trial is now active!\n\n" +
            "All Pro features are unlocked. Enjoy!\n\n" +
            "• Transaction grouping\n" +
            "• Unlimited file sizes\n" +
            "• Live streaming\n" +
            "• PDF report export",
            "Pro Trial Started 🎉",
            MessageBoxButton.OK,
            MessageBoxImage.None);

        DialogResult = true;
        Close();
    }

    // ── Purchase ───────────────────────────────────────────────────────

    private void BuyMonthlyButton_Click(object sender, RoutedEventArgs e)
    {
        // Opens the Stripe Checkout page in the default browser.
        // The success URL will carry a session_id that the app can use
        // to auto-download the license (Issue #3: Stripe integration).
        OpenUrl("https://buy.stripe.com/blackwidow-pro-monthly");
    }

    private void BuyYearlyButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://buy.stripe.com/blackwidow-pro-yearly");
    }

    // ── License key activation ─────────────────────────────────────────

    private async void ActivateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowActivationStatus("Please enter a license key.", isError: true);
            return;
        }

        ActivateKeyButton.IsEnabled = false;
        ActivateKeyButton.Content   = "Activating…";
        ShowActivationStatus(string.Empty, isError: false);

        var result = await _licenseService.ActivateLicenseAsync(key);

        ActivateKeyButton.IsEnabled = true;
        ActivateKeyButton.Content   = "Activate License";

        if (result.IsValid)
        {
            ShowActivationStatus($"✅ License activated! Tier: {result.Tier}", isError: false);

            await Task.Delay(1200);
            DialogResult = true;
            Close();
        }
        else
        {
            ShowActivationStatus($"❌ {result.ErrorMessage ?? "Activation failed. Check your key and try again."}", isError: true);
        }
    }

    // ── Close ──────────────────────────────────────────────────────────

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void ShowActivationStatus(string message, bool isError)
    {
        ActivationStatusText.Text       = message;
        ActivationStatusText.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("Danger")
            : (System.Windows.Media.Brush)FindResource("Success");
        ActivationStatusText.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open browser.\n\nPlease visit manually:\n{url}\n\nError: {ex.Message}",
                "Browser Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
