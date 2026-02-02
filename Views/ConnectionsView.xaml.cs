using System.Windows;
using System.Windows.Controls;
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
                }
                else
                {
                    NoRecentConnectionsText.Visibility = Visibility.Visible;
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
