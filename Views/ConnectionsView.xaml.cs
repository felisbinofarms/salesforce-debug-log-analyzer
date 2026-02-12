using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace SalesforceDebugAnalyzer.Views;

public partial class ConnectionsView : UserControl
{
    private readonly SalesforceApiService _apiService;
    private readonly string _connectionsFilePath;
    private ObservableCollection<SavedConnection> _recentConnections;
    
    // Reuse HttpClient instance to avoid socket exhaustion
    private static readonly System.Net.Http.HttpClient _httpClient = new();

    public event EventHandler<SalesforceApiService>? ConnectionEstablished;
    public event EventHandler<string>? LogFileDropped;

    public ConnectionsView(SalesforceApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        _connectionsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SalesforceDebugAnalyzer",
            "connections.json"
        );
        _recentConnections = new ObservableCollection<SavedConnection>();
        LoadRecentConnections();
    }

    private void LoadRecentConnections()
    {
        try
        {
            if (File.Exists(_connectionsFilePath))
            {
                var json = File.ReadAllText(_connectionsFilePath);
                var connections = JsonSerializer.Deserialize<List<SavedConnection>>(json);
                
                if (connections != null && connections.Any())
                {
                    _recentConnections = new ObservableCollection<SavedConnection>(
                        connections.OrderByDescending(c => c.LastUsedDate).Take(5)
                    );
                    RecentConnectionsList.ItemsSource = _recentConnections;
                    NoRecentConnectionsText.Visibility = Visibility.Collapsed;
                    RecentConnectionsPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    NoRecentConnectionsText.Visibility = Visibility.Visible;
                    RecentConnectionsPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                NoRecentConnectionsText.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            NoRecentConnectionsText.Visibility = Visibility.Visible;
        }
    }

    private async void ConnectProduction_Click(object sender, RoutedEventArgs e)
    {
        await ConnectWithOAuth(false);
    }

    private async void ConnectSandbox_Click(object sender, RoutedEventArgs e)
    {
        await ConnectWithOAuth(true);
    }

    private async Task ConnectWithOAuth(bool useSandbox)
    {
        try
        {
            var browserDialog = new OAuthBrowserDialog(useSandbox)
            {
                Owner = Window.GetWindow(this)
            };

            var resultTask = browserDialog.AuthenticateAsync();
            browserDialog.ShowDialog();

            var result = await resultTask;

            if (result.Success)
            {
                await _apiService.AuthenticateAsync(result.InstanceUrl, result.AccessToken, result.RefreshToken);
                
                // Get org info for saving
                var orgName = await GetOrgNameAsync(result.InstanceUrl, result.AccessToken);
                
                // Save connection
                SaveConnection(new SavedConnection
                {
                    OrgName = orgName,
                    InstanceUrl = result.InstanceUrl,
                    IsSandbox = useSandbox,
                    LastUsedDate = DateTime.Now
                });

                ConnectionEstablished?.Invoke(this, _apiService);
            }
            else if (!string.IsNullOrEmpty(result.Error) && !result.Error.Contains("cancelled"))
            {
                MessageBox.Show(result.Error, "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SavedConnection connection)
        {
            await ConnectWithOAuth(connection.IsSandbox);
        }
    }

    private async void ManualConnect_Click(object sender, RoutedEventArgs e)
    {
        var instanceUrl = ManualInstanceUrl.Text.Trim();
        var accessToken = ManualAccessToken.Text.Trim();

        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(accessToken))
        {
            MessageBox.Show("Please enter both Instance URL and Access Token", 
                "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _apiService.AuthenticateAsync(instanceUrl, accessToken);
            
            var orgName = await GetOrgNameAsync(instanceUrl, accessToken);
            
            SaveConnection(new SavedConnection
            {
                OrgName = orgName,
                InstanceUrl = instanceUrl,
                IsSandbox = instanceUrl.Contains("sandbox") || instanceUrl.Contains("test.salesforce"),
                LastUsedDate = DateTime.Now
            });

            ConnectionEstablished?.Invoke(this, _apiService);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<string> GetOrgNameAsync(string instanceUrl, string accessToken)
    {
        try
        {
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
                $"{instanceUrl}/services/data/v60.0/query?q=SELECT+Name+FROM+Organization+LIMIT+1");
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("records", out var records) && 
                    records.GetArrayLength() > 0)
                {
                    return records[0].GetProperty("Name").GetString() ?? "Unknown Org";
                }
            }
        }
        catch
        {
            // Fallback to domain name
        }

        // Extract domain from instance URL
        var uri = new Uri(instanceUrl);
        return uri.Host.Split('.')[0];
    }

    private void SaveConnection(SavedConnection connection)
    {
        try
        {
            var directory = Path.GetDirectoryName(_connectionsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<SavedConnection> connections;
            if (File.Exists(_connectionsFilePath))
            {
                var json = File.ReadAllText(_connectionsFilePath);
                connections = JsonSerializer.Deserialize<List<SavedConnection>>(json) ?? new List<SavedConnection>();
            }
            else
            {
                connections = new List<SavedConnection>();
            }

            // Remove existing connection with same instance URL
            connections.RemoveAll(c => c.InstanceUrl == connection.InstanceUrl);
            
            // Add new connection at the top
            connections.Insert(0, connection);
            
            // Keep only last 10 connections
            if (connections.Count > 10)
            {
                connections = connections.Take(10).ToList();
            }

            var updatedJson = JsonSerializer.Serialize(connections, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_connectionsFilePath, updatedJson);
        }
        catch
        {
            // Silently fail if we can't save
        }
    }

    // Drag-Drop handlers for log files
    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && IsLogFile(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x3A, 0x2D)); // Dark green
                DropZoneText.Text = "Drop to analyze!";
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2)); // Original blue
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22)); // Original dark
        DropZoneText.Text = "Drop a .log file here";
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone_DragLeave(sender, e); // Reset visual state
        
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && IsLogFile(files[0]) && File.Exists(files[0]))
            {
                LogFileDropped?.Invoke(this, files[0]);
            }
        }
        e.Handled = true;
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        // Show paste dialog
        var dialog = new Window
        {
            Title = "Paste Log File Path",
            Width = 500,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x33, 0x38)),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock 
        { 
            Text = "Paste the full path to your .log file:", 
            Foreground = Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(label, 0);

        var textBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x50, 0x58)),
            Padding = new Thickness(10),
            FontSize = 13
        };
        Grid.SetRow(textBox, 1);

        var buttonPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);

        var okButton = new Button
        {
            Content = "Load File",
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 0)
        };
        okButton.Click += (s, args) =>
        {
            var path = textBox.Text.Trim().Trim('"');
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && IsLogFile(path))
            {
                dialog.DialogResult = true;
                dialog.Close();
                LogFileDropped?.Invoke(this, path);
            }
            else
            {
                MessageBox.Show("Please enter a valid path to a .log or .txt file.", "Invalid Path", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x4E, 0x50, 0x58)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        cancelButton.Click += (s, args) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(label);
        grid.Children.Add(textBox);
        grid.Children.Add(buttonPanel);

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private static bool IsLogFile(string path)
    {
        return path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }
}

public class SavedConnection
{
    public string OrgName { get; set; } = string.Empty;
    public string InstanceUrl { get; set; } = string.Empty;
    public bool IsSandbox { get; set; }
    public DateTime LastUsedDate { get; set; }
    
    public string Icon => IsSandbox ? "TestTube" : "Cloud";
    public string LastUsed => $"Last used: {LastUsedDate:MMM dd, yyyy HH:mm}";
}
