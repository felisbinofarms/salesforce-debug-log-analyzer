using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Serilog;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.ViewModels;

namespace SalesforceDebugAnalyzer.Views;

public partial class AlertDetailDialog : Window
{
    private readonly MonitoringAlert? _alert;
    private readonly MainViewModel? _viewModel;

    public AlertDetailDialog() : this(null!, null!) { }

    public AlertDetailDialog(MonitoringAlert alert, MainViewModel viewModel)
    {
        InitializeComponent();
        _alert = alert;
        _viewModel = viewModel;

        if (_alert != null)
            LoadAlertDetails();
    }

    private void LoadAlertDetails()
    {
        if (_alert == null) return;

        TitleText.Text = _alert.Title;
        DescriptionText.Text = _alert.Description;

        // Severity badge
        ConfigureSeverityBadge(_alert.Severity);

        // Metrics
        if (_alert.CurrentValue.HasValue || _alert.BaselineValue.HasValue)
        {
            MetricsSection.IsVisible = true;
            CurrentValueText.Text = _alert.CurrentValue?.ToString("F1") ?? "-";
            BaselineValueText.Text = _alert.BaselineValue?.ToString("F1") ?? "-";

            if (_alert.CurrentValue.HasValue && _alert.BaselineValue.HasValue && _alert.BaselineValue.Value != 0)
            {
                var change = ((_alert.CurrentValue.Value - _alert.BaselineValue.Value) / _alert.BaselineValue.Value) * 100;
                ChangeValueText.Text = $"{change:+0;-0}%";
                ChangeValueText.Foreground = change > 0
                    ? new SolidColorBrush(Color.Parse("#EF4444"))
                    : new SolidColorBrush(Color.Parse("#10B981"));
            }
            else
            {
                ChangeValueText.Text = "-";
                ChangeValueText.Foreground = new SolidColorBrush(Color.Parse("#A0A0A0"));
            }
        }
        else
        {
            MetricsSection.IsVisible = false;
        }

        // Details
        EntryPointText.Text = _alert.EntryPoint ?? "Unknown";
        AlertTypeText.Text = FormatAlertType(_alert.AlertType);

        if (!string.IsNullOrEmpty(_alert.RelatedLogId))
        {
            ViewLogButton.IsVisible = true;
            RelatedLogText.Text = _alert.RelatedLogId;
        }

        // Feedback state
        if (!string.IsNullOrEmpty(_alert.UserFeedback))
        {
            FeedbackButtons.IsVisible = false;
            FeedbackSubmitted.IsVisible = true;
            FeedbackStatusText.Text = _alert.UserFeedback == "accurate"
                ? "Thanks! Marked as accurate."
                : "Thanks! Marked as false alarm.";
        }

        // Mark as read
        _alert.IsRead = true;
    }

    private void ConfigureSeverityBadge(string severity)
    {
        switch (severity.ToLowerInvariant())
        {
            case "critical":
                SeverityBadge.Background = new SolidColorBrush(Color.Parse("#EF4444"));
                SeverityText.Text = "CRITICAL";
                break;
            case "warning":
                SeverityBadge.Background = new SolidColorBrush(Color.Parse("#F59E0B"));
                SeverityText.Text = "WARNING";
                break;
            default:
                SeverityBadge.Background = new SolidColorBrush(Color.Parse("#3B82F6"));
                SeverityText.Text = "INFO";
                break;
        }
    }

    private static string FormatAlertType(string alertType) => alertType switch
    {
        "cpu_spike" => "CPU Spike",
        "soql_spike" => "SOQL Query Spike",
        "dml_spike" => "DML Operations Spike",
        "heap_spike" => "Heap Size Spike",
        "slow_execution" => "Slow Execution",
        "new_entry_point" => "New Entry Point",
        "limit_approach" => "Approaching Governor Limit",
        _ => alertType
    };

    private async void ThumbsUp_Click(object? sender, RoutedEventArgs e)
    {
        await SubmitFeedback("accurate");
    }

    private async void ThumbsDown_Click(object? sender, RoutedEventArgs e)
    {
        await SubmitFeedback("false_alarm");
    }

    private async Task SubmitFeedback(string feedback)
    {
        if (_alert == null || _viewModel == null) return;

        try
        {
            await _viewModel.SubmitAlertFeedback(_alert.Id, feedback);
            FeedbackButtons.IsVisible = false;
            FeedbackSubmitted.IsVisible = true;
            FeedbackStatusText.Text = feedback == "accurate"
                ? "Thanks! Marked as accurate."
                : "Thanks! Marked as false alarm.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to submit feedback");
            FeedbackStatusText.Text = "Failed to submit feedback";
            FeedbackSubmitted.IsVisible = true;
        }
    }

    private void Dismiss_Click(object? sender, RoutedEventArgs e)
    {
        if (_alert != null && _viewModel != null)
        {
            _viewModel.DismissAlertCommand.Execute(_alert.Id);
        }
        Close(false);
    }

    private async void ViewLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_alert?.RelatedLogId != null && _viewModel != null)
        {
            try
            {
                await _viewModel.NavigateToLogByIdAsync(_alert.RelatedLogId);
                Close(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to navigate to log {LogId}", _alert.RelatedLogId);
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
