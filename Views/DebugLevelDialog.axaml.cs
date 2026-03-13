using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class DebugLevelDialog : Window
{
    private readonly SalesforceApiService? _apiService;

    public string? CreatedDebugLevelId { get; private set; }

    public DebugLevelDialog() : this(null!) { }

    public DebugLevelDialog(SalesforceApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_apiService == null) return;

        var name = NameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;

        CreateButton.IsEnabled = false;

        try
        {
            var debugLevel = new DebugLevel
            {
                DeveloperName = SanitizeDeveloperName(name),
                MasterLabel = name,
                ApexCode = GetComboBoxValue(ApexCodeComboBox),
                Database = GetComboBoxValue(DatabaseComboBox),
                System = GetComboBoxValue(SystemComboBox),
                Workflow = GetComboBoxValue(WorkflowComboBox),
                Validation = GetComboBoxValue(ValidationComboBox),
                ApexProfiling = "INFO",
                Visualforce = "INFO",
                Callout = "INFO"
            };

            var id = await _apiService.CreateDebugLevelAsync(debugLevel);
            CreatedDebugLevelId = id;
            Close(true);
        }
        catch (Exception ex)
        {
            // Show error inline - could enhance with a status TextBlock
            CreateButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private static string SanitizeDeveloperName(string name)
    {
        var sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');
        if (sanitized.Length == 0 || !char.IsLetter(sanitized[0]))
            sanitized = "BW_" + sanitized;
        return sanitized;
    }

    private static string GetComboBoxValue(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? "INFO";
        return "INFO";
    }
}
