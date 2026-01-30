using System.Windows;
using System.Windows.Controls;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class DebugLevelDialog : Window
{
    private readonly SalesforceApiService _apiService;

    public DebugLevelDialog(SalesforceApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a name for the debug level", "Missing Name", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CreateButton.IsEnabled = false;

        try
        {
            var debugLevel = new DebugLevel
            {
                DeveloperName = name.Replace(" ", "_"),
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

            MessageBox.Show($"Debug level created successfully!\n\nID: {id}", 
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create debug level: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string GetComboBoxValue(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content.ToString() ?? "INFO";
        }
        return "INFO";
    }
}
