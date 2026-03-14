using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class DebugSetupWizard : UserControl
{
    private readonly SalesforceApiService? _apiService;
    private List<DebugLevel> _debugLevels = new();
    private int _currentStep = 1;
    private string? _currentUserId;
    private string? _selectedUserId;
    private string? _selectedDebugLevelId;

    public event EventHandler? WizardCompleted;

    public bool LoggingEnabled { get; private set; }
    public string? TraceFlagId { get; private set; }

    public DebugSetupWizard() : this(null!) { }

    public DebugSetupWizard(SalesforceApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;

        AnotherUserRadio.IsCheckedChanged += AnotherUserRadio_CheckedChanged;
        CustomLevelRadio.IsCheckedChanged += CustomLevelRadio_CheckedChanged;

        Loaded += DebugSetupWizard_Loaded;
    }

    private async void DebugSetupWizard_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null)
        {
            return;
        }

        await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            var connection = _apiService!.Connection;
            if (connection != null)
            {
                _currentUserId = connection.UserId;
                CurrentUserInfo.Text = $"✓ {connection.UserId}";
            }

            _debugLevels = await _apiService.QueryDebugLevelsAsync();
            DebugLevelComboBox.ItemsSource = _debugLevels.Select(d => d.MasterLabel).ToList();
            if (_debugLevels.Any())
            {
                DebugLevelComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            var msgBox = new Window
            {
                Title = "Error",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = $"Failed to load initial data: {ex.Message}", TextWrapping = TextWrapping.Wrap },
                        new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                    }
                }
            };
            ((Button)((StackPanel)msgBox.Content).Children[1]).Click += (_, _) => msgBox.Close();
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window parentWindow)
            {
                await msgBox.ShowDialog(parentWindow);
            }
        }
    }

    private void AnotherUserRadio_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (UserIdPanel != null)
        {
            UserIdPanel.IsVisible = AnotherUserRadio.IsChecked == true;
        }
    }

    private void CustomLevelRadio_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (CustomLevelPanel != null)
        {
            CustomLevelPanel.IsVisible = CustomLevelRadio.IsChecked == true;
        }
    }

    private void NextToStep2_Click(object? sender, RoutedEventArgs e)
    {
        if (AnotherUserRadio.IsChecked == true)
        {
            var userId = UserIdTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(userId) || (userId.Length != 15 && userId.Length != 18))
            {
                // Show inline validation instead of message box
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

    private void BackToStep1_Click(object? sender, RoutedEventArgs e) => GoToStep(1);

    private void NextToStep3_Click(object? sender, RoutedEventArgs e)
    {
        if (CustomLevelRadio.IsChecked == true)
        {
            if (DebugLevelComboBox.SelectedIndex < 0)
            {
                return;
            }

            _selectedDebugLevelId = _debugLevels[DebugLevelComboBox.SelectedIndex].Id;
        }
        else
        {
            _selectedDebugLevelId = null;
        }
        GoToStep(3);
    }

    private void BackToStep2_Click(object? sender, RoutedEventArgs e) => GoToStep(2);

    private void NextToStep4_Click(object? sender, RoutedEventArgs e)
    {
        SummaryUser.Text = AnotherUserRadio.IsChecked == true
            ? $"User ID: {_selectedUserId}"
            : "Yourself (Current User)";

        SummaryLevel.Text = CustomLevelRadio.IsChecked == true
            ? (_debugLevels.ElementAtOrDefault(DebugLevelComboBox.SelectedIndex)?.MasterLabel ?? "Custom")
            : (StandardLevelRadio.IsChecked == true ? "Standard" : "Detailed");

        var hours = (int)DurationSlider.Value;
        SummaryDuration.Text = $"{hours} hour{(hours > 1 ? "s" : "")}";

        GoToStep(4);
    }

    private void BackToStep3_Click(object? sender, RoutedEventArgs e) => GoToStep(3);

    private void DurationSlider_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty)
        {
            return;
        }

        if (DurationDisplay == null || DurationRecommendation == null)
        {
            return;
        }

        var hours = (int)(double)e.NewValue!;
        DurationDisplay.Text = $"Expires in {hours} hour{(hours > 1 ? "s" : "")}";

        if (hours <= 4)
        {
            DurationRecommendation.Text = "✓ Good for quick testing (recommended)";
            DurationRecommendation.Foreground = new SolidColorBrush(Color.Parse("#4ade80"));
        }
        else if (hours <= 12)
        {
            DurationRecommendation.Text = "⚡ Moderate duration — good for troubleshooting over several hours";
            DurationRecommendation.Foreground = new SolidColorBrush(Color.Parse("#f59e0b"));
        }
        else
        {
            DurationRecommendation.Text = "⚠️ Long duration — logs may grow large. Monitor log sizes.";
            DurationRecommendation.Foreground = new SolidColorBrush(Color.Parse("#f97316"));
        }
    }

    private async void EnableLogging_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null)
        {
            return;
        }

        EnableButton.IsEnabled = false;
        EnableProgressBar.IsVisible = true;
        StatusText.IsVisible = true;
        StatusText.Text = "Enabling debug logging...";

        try
        {
            string debugLevelId;
            if (CustomLevelRadio.IsChecked == true)
            {
                debugLevelId = _selectedDebugLevelId!;
            }
            else
            {
                var levelName = StandardLevelRadio.IsChecked == true ? "SFDC_DevConsole" : "DB_APEX_CODE";
                var level = _debugLevels.FirstOrDefault(l => l.MasterLabel == levelName);
                var fallback = _debugLevels.FirstOrDefault();
                debugLevelId = level?.Id ?? fallback?.Id ?? throw new InvalidOperationException(
                    "No debug levels found in your org. Please create one first using Setup > Debug Levels.");
            }

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
            EnableProgressBar.IsVisible = false;
            StatusText.Text = $"✓ Debug logging enabled successfully!\nExpires: {expirationDate.ToLocalTime():MM/dd HH:mm}";
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#4ade80"));
            ViewLogsButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            EnableProgressBar.IsVisible = false;
            StatusText.Text = $"❌ Failed to enable logging: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
            EnableButton.IsEnabled = true;
        }
    }

    private void ViewLogs_Click(object? sender, RoutedEventArgs e)
    {
        WizardCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async void CreateDebugLevel_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null)
        {
            return;
        }

        var createDialog = new DebugLevelDialog(_apiService);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window parentWindow)
        {
            var result = await createDialog.ShowDialog<bool>(parentWindow);
            if (result)
            {
                _debugLevels = await _apiService.QueryDebugLevelsAsync();
                DebugLevelComboBox.ItemsSource = _debugLevels.Select(d => d.MasterLabel).ToList();

                if (createDialog.CreatedDebugLevelId != null)
                {
                    var idx = _debugLevels.FindIndex(l => l.Id == createDialog.CreatedDebugLevelId);
                    if (idx >= 0)
                    {
                        DebugLevelComboBox.SelectedIndex = idx;
                    }
                }
            }
        }
    }

    private void GoToStep(int step)
    {
        _currentStep = step;

        Step1Panel.IsVisible = step == 1;
        Step2Panel.IsVisible = step == 2;
        Step3Panel.IsVisible = step == 3;
        Step4Panel.IsVisible = step == 4;

        var transparent = Brushes.Transparent;
        IBrush accentBrush;
        if (this.TryFindResource("AccentPrimary", this.ActualThemeVariant, out var res) && res is Color accentColor)
        {
            accentBrush = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        }
        else
        {
            accentBrush = new SolidColorBrush(Color.FromArgb(40, 139, 92, 246));
        }

        Step1Border.Background = step == 1 ? accentBrush : transparent;
        Step2Border.Background = step == 2 ? accentBrush : transparent;
        Step3Border.Background = step == 3 ? accentBrush : transparent;
        Step4Border.Background = step == 4 ? accentBrush : transparent;
    }
}
