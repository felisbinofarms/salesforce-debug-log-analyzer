using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.ViewModels;

namespace SalesforceDebugAnalyzer.Views;

public partial class AlertCenterPanel : UserControl
{
    private MainViewModel? _viewModel;
    private string _severityFilter = "all";

    public AlertCenterPanel()
    {
        InitializeComponent();
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        RefreshAlerts();
    }

    public void RefreshAlerts()
    {
        if (_viewModel == null) return;

        var alerts = _viewModel.MonitoringAlerts;

        if (_severityFilter == "all")
        {
            AlertList.ItemsSource = alerts;
            EmptyState.IsVisible = alerts.Count == 0;
        }
        else
        {
            var filtered = alerts.Where(a => a.Severity == _severityFilter).ToList();
            AlertList.ItemsSource = filtered;
            EmptyState.IsVisible = !filtered.Any();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ShowAlertCenter = false;
    }

    private void AlertItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is MonitoringAlert alert && _viewModel != null)
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null) return;

            var dialog = new AlertDetailDialog(alert, _viewModel);
            dialog.ShowDialog(parentWindow);
            RefreshAlerts();
        }
    }

    private void MarkAllRead_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.MarkAllAlertsReadCommand.Execute(null);
        RefreshAlerts();
    }

    private void DismissAlert_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long alertId)
        {
            _viewModel?.DismissAlertCommand.Execute(alertId);
            RefreshAlerts();
        }
    }

    private void FilterRadio_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.IsChecked == true)
        {
            if (radio == FilterAllRadio) _severityFilter = "all";
            else if (radio == FilterCriticalRadio) _severityFilter = "critical";
            else if (radio == FilterWarningRadio) _severityFilter = "warning";
            RefreshAlerts();
        }
    }
}
