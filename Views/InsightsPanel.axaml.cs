using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SalesforceDebugAnalyzer.ViewModels;

namespace SalesforceDebugAnalyzer.Views;

public partial class InsightsPanel : UserControl
{
    public InsightsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
            UpdateSpeedBadge(vm.SpeedRating);
            UpdateResourceBars(vm.SoqlPercent, vm.DmlPercent, vm.CpuPercent);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SpeedRating):
                UpdateSpeedBadge(vm.SpeedRating);
                break;
            case nameof(MainViewModel.SoqlPercent):
            case nameof(MainViewModel.DmlPercent):
            case nameof(MainViewModel.CpuPercent):
                UpdateResourceBars(vm.SoqlPercent, vm.DmlPercent, vm.CpuPercent);
                break;
        }
    }

    private void UpdateSpeedBadge(string speedRating)
    {
        var (bg, fg, text) = speedRating switch
        {
            "Fast" => ("#10B981", "#FFFFFF", "⚡ Fast"),
            "Moderate" => ("#3B82F6", "#FFFFFF", "👍 Moderate"),
            "Slow" => ("#F59E0B", "#000000", "🐌 Slow"),
            "Very Slow" => ("#EF4444", "#FFFFFF", "🔥 Very Slow"),
            _ => ("#6B7280", "#FFFFFF", speedRating)
        };

        SpeedBadge.Background = new SolidColorBrush(Color.Parse(bg));
        SpeedRatingText.Foreground = new SolidColorBrush(Color.Parse(fg));
        SpeedRatingText.Text = text;
    }

    private void UpdateResourceBars(double soqlPercent, double dmlPercent, double cpuPercent)
    {
        SetBarWidth(SoqlBar, soqlPercent);
        SetBarWidth(DmlBar, dmlPercent);
        SetBarWidth(CpuBar, cpuPercent);

        SoqlBar.Background = new SolidColorBrush(GetSeverityColor(soqlPercent));
        DmlBar.Background = new SolidColorBrush(GetSeverityColor(dmlPercent));
        CpuBar.Background = new SolidColorBrush(GetSeverityColor(cpuPercent));
    }

    private void SetBarWidth(Border bar, double percent)
    {
        var parent = bar.Parent as Grid;
        if (parent == null)
        {
            return;
        }

        var clampedPercent = Math.Max(0, Math.Min(100, percent));
        // Use relative width via binding later; for now set via pixel estimate
        bar.Width = (parent.Bounds.Width > 0 ? parent.Bounds.Width : 300) * (clampedPercent / 100.0);
    }

    private static Color GetSeverityColor(double percent) => percent switch
    {
        >= 80 => Color.Parse("#EF4444"), // Danger red
        >= 50 => Color.Parse("#F59E0B"), // Warning yellow
        _ => Color.Parse("#10B981")      // Safe green
    };
}
