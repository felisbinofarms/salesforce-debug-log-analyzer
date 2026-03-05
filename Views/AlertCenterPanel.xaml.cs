using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
            EmptyState.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            var view = CollectionViewSource.GetDefaultView(alerts);
            view.Filter = item => item is MonitoringAlert a && a.Severity == _severityFilter;
            AlertList.ItemsSource = view;
            EmptyState.Visibility = view.Cast<object>().Any() ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.ShowAlertCenter = false;
    }

    private void AlertItem_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is MonitoringAlert alert && _viewModel != null)
        {
            var dialog = new AlertDetailDialog(alert, _viewModel)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            RefreshAlerts(); // Refresh in case feedback was submitted
        }
    }

    private void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.MarkAllAlertsReadCommand.Execute(null);
        RefreshAlerts();
    }

    private void DismissAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long alertId)
        {
            _viewModel?.DismissAlertCommand.Execute(alertId);
            RefreshAlerts();
        }
    }

    private void FilterAll_Checked(object sender, RoutedEventArgs e)
    {
        _severityFilter = "all";
        RefreshAlerts();
    }

    private void FilterCritical_Checked(object sender, RoutedEventArgs e)
    {
        _severityFilter = "critical";
        RefreshAlerts();
    }

    private void FilterWarning_Checked(object sender, RoutedEventArgs e)
    {
        _severityFilter = "warning";
        RefreshAlerts();
    }
}
