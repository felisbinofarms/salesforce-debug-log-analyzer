using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class ConnectionsView : UserControl
{
    private readonly SalesforceApiService _apiService;
    private readonly string _connectionsFilePath;
    private ObservableCollection<SavedConnection> _recentConnections;

    private static readonly System.Net.Http.HttpClient _httpClient = new();

    public event EventHandler<SalesforceApiService>? ConnectionEstablished;
    public event EventHandler<string>? LogFileDropped;
    public event EventHandler? LoadFolderRequested;
    public event EventHandler<string>? FolderDropped;

    public ConnectionsView()
    {
        InitializeComponent();

        _apiService = new SalesforceApiService();
        _connectionsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SalesforceDebugAnalyzer",
            "connections.json"
        );
        _recentConnections = new ObservableCollection<SavedConnection>();

        AddHandler(DragDrop.DragOverEvent, DropZone_DragOver);
        AddHandler(DragDrop.DropEvent, DropZone_Drop);

        LoadRecentConnections();
    }

    public ConnectionsView(SalesforceApiService apiService) : this()
    {
        _apiService = apiService;
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
                    RecentConnectionsPanel.IsVisible = true;
                }
            }
        }
        catch
        {
            // Silently fail if connections file is corrupt
        }
    }

    private async void ConnectProduction_Click(object? sender, RoutedEventArgs e)
    {
        await ConnectWithOAuth(false);
    }

    private async void ConnectSandbox_Click(object? sender, RoutedEventArgs e)
    {
        await ConnectWithOAuth(true);
    }

    private async Task ConnectWithOAuth(bool useSandbox)
    {
        try
        {
            // TODO: Implement OAuthBrowserDialog for Avalonia
            // For now, show a message directing to manual token
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow == null)
            {
                return;
            }

            var dialog = new ConnectionDialog(useSandbox);
            var result = await dialog.ShowDialog<ConnectionDialogResult?>(parentWindow);

            if (result is { Success: true })
            {
                await _apiService.AuthenticateAsync(result.InstanceUrl, result.AccessToken, result.RefreshToken);

                var orgName = await GetOrgNameAsync(result.InstanceUrl, result.AccessToken);

                SaveConnection(new SavedConnection
                {
                    OrgName = orgName,
                    InstanceUrl = result.InstanceUrl,
                    IsSandbox = useSandbox,
                    LastUsedDate = DateTime.Now
                });

                ConnectionEstablished?.Invoke(this, _apiService);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConnectionsView] OAuth error: {ex.Message}");
        }
    }

    private async void ReconnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SavedConnection connection)
        {
            await ConnectWithOAuth(connection.IsSandbox);
        }
    }

    private async void ManualConnect_Click(object? sender, RoutedEventArgs e)
    {
        var instanceUrl = ManualInstanceUrl?.Text?.Trim() ?? string.Empty;
        var accessToken = ManualAccessToken?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(accessToken))
        {
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
            System.Diagnostics.Debug.WriteLine($"[ConnectionsView] Manual connect error: {ex.Message}");
        }
    }

    private async void DropZone_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Debug Log File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log Files") { Patterns = new[] { "*.log", "*.txt" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                LogFileDropped?.Invoke(this, path);
            }
        }
    }

    private void LoadFolder_Click(object? sender, RoutedEventArgs e)
    {
        LoadFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var item in files)
                {
                    var path = item.TryGetLocalPath();
                    if (path == null)
                    {
                        continue;
                    }

                    if (Directory.Exists(path))
                    {
                        FolderDropped?.Invoke(this, path);
                        break;
                    }
                    else if (IsLogFile(path) && File.Exists(path))
                    {
                        LogFileDropped?.Invoke(this, path);
                    }
                }
            }
        }
        e.Handled = true;
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
                var doc = System.Text.Json.JsonDocument.Parse(json);

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

            connections.RemoveAll(c => c.InstanceUrl == connection.InstanceUrl);
            connections.Insert(0, connection);

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
    public string LastUsed => $"Last used: {LastUsedDate:MMM dd, yyyy HH:mm}";
}

public class ConnectionDialogResult
{
    public bool Success { get; set; }
    public string InstanceUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string? Error { get; set; }
}
