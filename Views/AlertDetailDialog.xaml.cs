using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.ViewModels;

namespace SalesforceDebugAnalyzer.Views;

public partial class AlertDetailDialog : Window
{
    private readonly MonitoringAlert _alert;
    private readonly MainViewModel _viewModel;

    public AlertDetailDialog(MonitoringAlert alert, MainViewModel viewModel)
    {
        InitializeComponent();
        _alert = alert;
        _viewModel = viewModel;
        LoadAlertDetails();
    }

    private void LoadAlertDetails()
    {
        // Header
        TimeStamp.Text = $"{_alert.CreatedAt.ToLocalTime():MMM dd, yyyy 'at' h:mm tt} ({_alert.TimeAgo})";

        // Severity Badge
        ConfigureSeverityBadge();

        // Title & Description
        AlertTitle.Text = _alert.Title;
        AlertDescription.Text = _alert.Description;

        // Metrics Panel
        if (_alert.CurrentValue.HasValue || _alert.BaselineValue.HasValue)
        {
            MetricsPanel.Visibility = Visibility.Visible;
            CurrentValueText.Text = _alert.CurrentValue?.ToString("N0") ?? "—";
            BaselineValueText.Text = _alert.BaselineValue?.ToString("N0") ?? "—";
            MetricNameText.Text = _alert.MetricName ?? "occurrences";

            if (_alert.CurrentValue.HasValue && _alert.BaselineValue.HasValue && _alert.BaselineValue.Value > 0)
            {
                var percent = ((_alert.CurrentValue.Value - _alert.BaselineValue.Value) / _alert.BaselineValue.Value) * 100;
                ChangeText.Text = $"+{percent:F0}%";
                ChangeText.Foreground = (Brush)FindResource("Danger");
            }
            else
            {
                ChangeText.Text = "—";
            }
        }
        else
        {
            MetricsPanel.Visibility = Visibility.Collapsed;
        }

        // Details
        if (!string.IsNullOrEmpty(_alert.EntryPoint))
        {
            EntryPointRow.Visibility = Visibility.Visible;
            EntryPointText.Text = _alert.EntryPoint;
        }
        else
        {
            EntryPointRow.Visibility = Visibility.Collapsed;
        }

        AlertTypeText.Text = FormatAlertType(_alert.AlertType);

        if (!string.IsNullOrEmpty(_alert.RelatedLogId))
        {
            RelatedLogRow.Visibility = Visibility.Visible;
        }
        else
        {
            RelatedLogRow.Visibility = Visibility.Collapsed;
        }

        // Feedback state
        if (!string.IsNullOrEmpty(_alert.UserFeedback))
        {
            FeedbackButtons.Visibility = Visibility.Collapsed;
            FeedbackSubmitted.Visibility = Visibility.Visible;
            FeedbackMessage.Text = _alert.UserFeedback == "accurate" 
                ? "✓ You marked this as accurate" 
                : "✓ You marked this as a false alarm";
        }
    }

    private void ConfigureSeverityBadge()
    {
        switch (_alert.Severity.ToLower())
        {
            case "critical":
                SeverityBadge.Background = (Brush)FindResource("Danger");
                SeverityText.Text = "CRITICAL";
                SeverityText.Foreground = Brushes.White;
                SeverityIcon.Kind = PackIconKind.AlertCircle;
                SeverityIcon.Foreground = Brushes.White;
                break;
            case "warning":
                SeverityBadge.Background = (Brush)FindResource("Warning");
                SeverityText.Text = "WARNING";
                SeverityText.Foreground = Brushes.Black;
                SeverityIcon.Kind = PackIconKind.Alert;
                SeverityIcon.Foreground = Brushes.Black;
                break;
            default:
                SeverityBadge.Background = (Brush)FindResource("Info");
                SeverityText.Text = "INFO";
                SeverityText.Foreground = Brushes.White;
                SeverityIcon.Kind = PackIconKind.Information;
                SeverityIcon.Foreground = Brushes.White;
                break;
        }
    }

    private string FormatAlertType(string alertType)
    {
        return alertType switch
        {
            "exception_spike" => "Exception Spike",
            "cpu_spike" => "CPU Time Spike",
            "dml_spike" => "DML Operations Spike",
            "soql_spike" => "SOQL Query Spike",
            "heap_spike" => "Heap Size Spike",
            "governor_warning" => "Governor Limit Warning",
            "error_rate" => "Error Rate Increase",
            "performance_degradation" => "Performance Degradation",
            _ => alertType.Replace('_', ' ')
        };
    }

    private async void ThumbsUp_Click(object sender, RoutedEventArgs e)
    {
        await SubmitFeedback("accurate");
    }

    private async void ThumbsDown_Click(object sender, RoutedEventArgs e)
    {
        await SubmitFeedback("false_alarm");
    }

    private async Task SubmitFeedback(string feedback)
    {
        try
        {
            await _viewModel.SubmitAlertFeedback(_alert.Id, feedback);
            
            // Update UI
            FeedbackButtons.Visibility = Visibility.Collapsed;
            FeedbackSubmitted.Visibility = Visibility.Visible;
            FeedbackMessage.Text = feedback == "accurate" 
                ? "✓ Thanks! This helps improve alert accuracy." 
                : "✓ Thanks! We'll use this to reduce false alarms.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to submit feedback: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to dismiss this alert? It will be removed from your alert center.",
            "Dismiss Alert",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DismissAlert(_alert.Id);
            DialogResult = true;
            Close();
        }
    }

    private async void ViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_alert.RelatedLogId)) return;

        ViewLogButton.IsEnabled = false;
        ViewLogButton.Content = "Loading...";
        try
        {
            var success = await _viewModel.NavigateToLogByIdAsync(_alert.RelatedLogId);
            if (success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ViewLogButton.IsEnabled = true;
                ViewLogButton.Content = "View Log";
            }
        }
        catch
        {
            ViewLogButton.IsEnabled = true;
            ViewLogButton.Content = "View Log";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
