using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Views;

public partial class DebugSetupWizard : UserControl
{
    private readonly SalesforceApiService _apiService;
    private List<DebugLevel> _debugLevels = new();
    private int _currentStep = 1;
    private string? _currentUserId;
    private string? _selectedUserId;
    private string? _selectedDebugLevelId;
    
    public event EventHandler? WizardCompleted;

    public bool LoggingEnabled { get; private set; }
    public string? TraceFlagId { get; private set; }

    public DebugSetupWizard(SalesforceApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        
        // Wire up radio button events
        AnotherUserRadio.Checked += AnotherUserRadio_Checked;
        AnotherUserRadio.Unchecked += AnotherUserRadio_Unchecked;
        CustomLevelRadio.Checked += CustomLevelRadio_Checked;
        CustomLevelRadio.Unchecked += CustomLevelRadio_Unchecked;
        
        Loaded += DebugSetupWizard_Loaded;
    }

    private async void DebugSetupWizard_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            // Load current user info
            var connection = _apiService.Connection;
            if (connection != null)
            {
                _currentUserId = connection.UserId;
                CurrentUserInfo.Text = $"✓ {connection.UserId}";
            }

            // Load debug levels
            _debugLevels = await _apiService.QueryDebugLevelsAsync();
            DebugLevelComboBox.ItemsSource = _debugLevels;
            if (_debugLevels.Any())
            {
                DebugLevelComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load initial data: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AnotherUserRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (UserIdPanel != null)
            UserIdPanel.Visibility = Visibility.Visible;
    }

    private void AnotherUserRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        if (UserIdPanel != null)
            UserIdPanel.Visibility = Visibility.Collapsed;
    }

    private void CustomLevelRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomLevelPanel != null)
            CustomLevelPanel.Visibility = Visibility.Visible;
    }

    private void CustomLevelRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        if (CustomLevelPanel != null)
            CustomLevelPanel.Visibility = Visibility.Collapsed;
    }

    private void NextToStep2_Click(object sender, RoutedEventArgs e)
    {
        // Validate user selection
        if (AnotherUserRadio.IsChecked == true)
        {
            var userId = UserIdTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(userId) || (userId.Length != 15 && userId.Length != 18))
            {
                MessageBox.Show("Please enter a valid 15 or 18-character Salesforce User ID", "Invalid User ID", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selectedUserId = userId;
        }
        else
        {
            _selectedUserId = _currentUserId;
        }

        GoToStep(2);
    }

    private void BackToStep1_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(1);
    }

    private void NextToStep3_Click(object sender, RoutedEventArgs e)
    {
        // Determine selected debug level
        if (CustomLevelRadio.IsChecked == true)
        {
            var selectedLevel = DebugLevelComboBox.SelectedItem as DebugLevel;
            if (selectedLevel == null)
            {
                MessageBox.Show("Please select a debug level", "Missing Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selectedDebugLevelId = selectedLevel.Id;
        }
        else
        {
            // Use preset levels (we'll create them on the fly if needed)
            _selectedDebugLevelId = null; // Will be handled in EnableLogging
        }

        GoToStep(3);
    }

    private void BackToStep2_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(2);
    }

    private void NextToStep4_Click(object sender, RoutedEventArgs e)
    {
        // Update summary
        SummaryUser.Text = AnotherUserRadio.IsChecked == true 
            ? $"User ID: {_selectedUserId}" 
            : "Yourself (Current User)";
        
        SummaryLevel.Text = CustomLevelRadio.IsChecked == true 
            ? (DebugLevelComboBox.SelectedItem as DebugLevel)?.MasterLabel ?? "Custom" 
            : (StandardLevelRadio.IsChecked == true ? "Standard" : "Detailed");
        
        var hours = (int)DurationSlider.Value;
        SummaryDuration.Text = $"{hours} hour{(hours > 1 ? "s" : "")}";

        GoToStep(4);
    }

    private void BackToStep3_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(3);
    }

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationDisplay == null || DurationRecommendation == null)
            return;

        var hours = (int)e.NewValue;
        DurationDisplay.Text = $"Expires in {hours} hour{(hours > 1 ? "s" : "")}";

        if (hours <= 4)
        {
            DurationRecommendation.Text = "✓ Good for quick testing (recommended)";
            DurationRecommendation.Foreground = System.Windows.Media.Brushes.Green;
        }
        else if (hours <= 12)
        {
            DurationRecommendation.Text = "⚡ Moderate duration - good for troubleshooting over several hours";
            DurationRecommendation.Foreground = System.Windows.Media.Brushes.Orange;
        }
        else
        {
            DurationRecommendation.Text = "⚠️ Long duration - logs may grow large. Monitor log sizes.";
            DurationRecommendation.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private async void EnableLogging_Click(object sender, RoutedEventArgs e)
    {
        EnableButton.IsEnabled = false;
        EnableProgressBar.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "Enabling debug logging...";

        try
        {
            // Get or create debug level
            string debugLevelId;
            if (CustomLevelRadio.IsChecked == true)
            {
                debugLevelId = _selectedDebugLevelId!;
            }
            else
            {
                // Find or create preset debug level
                var levelName = StandardLevelRadio.IsChecked == true ? "SFDC_DevConsole" : "DB_APEX_CODE";
                var level = _debugLevels.FirstOrDefault(l => l.MasterLabel == levelName);
                var fallback = _debugLevels.FirstOrDefault();
                debugLevelId = level?.Id ?? fallback?.Id ?? throw new InvalidOperationException(
                    "No debug levels found in your org. Please create one first using Setup > Debug Levels.");
            }

            // Create trace flag
            var duration = (int)DurationSlider.Value;
            var expirationDate = DateTime.UtcNow.AddHours(duration);

            if (string.IsNullOrEmpty(_selectedUserId))
            {
                throw new InvalidOperationException(
                    "No user selected. Please go back to Step 1 and select a user.");
            }

            TraceFlagId = await _apiService.CreateTraceFlagAsync(
                _selectedUserId, 
                debugLevelId, 
                expirationDate);

            LoggingEnabled = true;
            EnableProgressBar.Visibility = Visibility.Collapsed;
            StatusText.Text = $"✓ Debug logging enabled successfully!\nExpires: {expirationDate.ToLocalTime():MM/dd HH:mm}";
            StatusText.Foreground = System.Windows.Media.Brushes.Green;

            ViewLogsButton.Visibility = Visibility.Visible;

            MessageBox.Show(
                $"Debug logging is now active!\n\n" +
                $"What to do next:\n" +
                $"1. Have the user perform the action you want to troubleshoot\n" +
                $"2. Wait a few seconds for the log to generate\n" +
                $"3. Click 'View Active Logs' to download and analyze\n\n" +
                $"Logging expires: {expirationDate.ToLocalTime():MM/dd/yyyy HH:mm}", 
                "Success!", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            EnableProgressBar.Visibility = Visibility.Collapsed;
            StatusText.Text = $"❌ Failed to enable logging: {ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            
            MessageBox.Show($"Failed to enable debug logging:\n\n{ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            
            EnableButton.IsEnabled = true;
        }
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        WizardCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void CreateDebugLevel_Click(object sender, RoutedEventArgs e)
    {
        var createDialog = new DebugLevelDialog(_apiService);
        createDialog.Owner = Window.GetWindow(this);

        if (createDialog.ShowDialog() == true)
        {
            // Reload debug levels
            _debugLevels = await _apiService.QueryDebugLevelsAsync();
            DebugLevelComboBox.ItemsSource = _debugLevels;
            
            // Select the newly created debug level
            if (createDialog.CreatedDebugLevelId != null)
            {
                var newLevel = _debugLevels.FirstOrDefault(l => l.Id == createDialog.CreatedDebugLevelId);
                if (newLevel != null)
                {
                    DebugLevelComboBox.SelectedItem = newLevel;
                }
            }
        }
    }

    private void GoToStep(int step)
    {
        _currentStep = step;

        // Hide all steps
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Collapsed;
        Step3Panel.Visibility = Visibility.Collapsed;
        Step4Panel.Visibility = Visibility.Collapsed;

        // Reset all step borders
        Step1Border.Background = System.Windows.Media.Brushes.Transparent;
        Step2Border.Background = System.Windows.Media.Brushes.Transparent;
        Step3Border.Background = System.Windows.Media.Brushes.Transparent;
        Step4Border.Background = System.Windows.Media.Brushes.Transparent;

        // Show current step
        var activeColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0x65, 0xF2));
        switch (step)
        {
            case 1:
                Step1Panel.Visibility = Visibility.Visible;
                Step1Border.Background = activeColor;
                break;
            case 2:
                Step2Panel.Visibility = Visibility.Visible;
                Step2Border.Background = activeColor;
                break;
            case 3:
                Step3Panel.Visibility = Visibility.Visible;
                Step3Border.Background = activeColor;
                break;
            case 4:
                Step4Panel.Visibility = Visibility.Visible;
                Step4Border.Background = activeColor;
                break;
        }
    }
}
