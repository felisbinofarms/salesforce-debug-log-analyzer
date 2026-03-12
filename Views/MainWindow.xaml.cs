using System.Windows;
using System.IO;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SalesforceDebugAnalyzer.ViewModels;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ConnectionsView? _connectionsView;
    private SystemTrayService? _trayService;
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _searchDebounce;

    /// <summary>
    /// When true, Close() actually closes the window instead of hiding to tray.
    /// Set by App.xaml.cs when "Exit Black Widow" is chosen from the tray menu.
    /// </summary>
    public bool ForceClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        var logParser = new LogParserService();
        var salesforceApi = new SalesforceApiService();
        var oauthService = new OAuthService();

        _viewModel = new MainViewModel(salesforceApi, logParser, oauthService);
        DataContext = _viewModel;

        // Set up search debounce (300ms delay)
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += SearchDebounce_Tick;

        // Wire up the alert center panel
        AlertCenterPanel.SetViewModel(_viewModel);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Enable drag-and-drop on entire window
        AllowDrop = true;
        Drop += MainWindow_Drop;
        DragOver += MainWindow_DragOver;

        // Keyboard shortcuts (Ctrl+K, Ctrl+F, Escape, etc.)
        KeyDown += Window_KeyDown;

        // Show connections view initially
        ShowConnectionsView(salesforceApi);
    }

    /// <summary>
    /// Called by App.xaml.cs to provide the system tray service reference.
    /// </summary>
    public void SetTrayService(SystemTrayService trayService)
    {
        _trayService = trayService;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ForceClose && _settingsService.Load().MinimizeToTray && _trayService != null)
        {
            // Hide to tray instead of closing
            e.Cancel = true;
            Hide();
        }
        else
        {
            // Actually closing — clean up resources
            _searchDebounce.Stop();
            _searchDebounce.Tick -= SearchDebounce_Tick;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();

            base.OnClosing(e);
        }
    }
    
    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            // Only accept .log or .txt files
            if (files?.Length > 0 && (files[0].EndsWith(".log", StringComparison.OrdinalIgnoreCase) || 
                                       files[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.Copy;
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
    
    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                var filePath = files[0];
                if (File.Exists(filePath) && (filePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                                               filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    // If still on connection screen, transition to main view before loading
                    if (ConnectionsViewContainer.Visibility == Visibility.Visible)
                    {
                        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
                        MainContentGrid.Visibility = Visibility.Visible;
                    }
                    await _viewModel.LoadLogFromPath(filePath);
                }
                else
                {
                    var ext = Path.GetExtension(filePath);
                    _viewModel.StatusMessage = $"⚠️ Can't open '{ext}' files — drop a .log or .txt debug log file";
                }
            }
        }
        e.Handled = true;
    }

    private void ShowConnectionsView(SalesforceApiService apiService)
    {
        _connectionsView = new ConnectionsView(apiService);
        _connectionsView.ConnectionEstablished += OnConnectionEstablished;
        _connectionsView.LogFileDropped += OnLogFileDropped;
        _connectionsView.LoadFolderRequested += OnLoadFolderRequested;
        _connectionsView.FolderDropped += OnFolderDropped;
        ConnectionsViewContainer.Content = _connectionsView;
        ConnectionsViewContainer.Visibility = Visibility.Visible;
        MainContentGrid.Visibility = Visibility.Collapsed;
    }

    private async void OnLogFileDropped(object? sender, string filePath)
    {
        // Hide connections view and show main content
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        
        // Load the dropped file
        await _viewModel.LoadLogFromPath(filePath);
    }
    
    private async void OnLoadFolderRequested(object? sender, EventArgs e)
    {
        // Hide connections view and show main content
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        
        // Trigger the LoadLogFolderCommand from ViewModel
        if (_viewModel.LoadLogFolderCommand.CanExecute(null))
        {
            await _viewModel.LoadLogFolderCommand.ExecuteAsync(null);
        }
    }

    private async void OnFolderDropped(object? sender, string folderPath)
    {
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
        await _viewModel.LoadLogFolderFromPath(folderPath);
    }

    private void OnConnectionEstablished(object? sender, SalesforceApiService apiService)
    {
        // Hide connections view
        ConnectionsViewContainer.Visibility = Visibility.Collapsed;
        
        // Skip wizard - go straight to main dashboard
        // (User can manually open wizard from menu if needed)
        ConnectionsViewContainer.Content = null;
        MainContentGrid.Visibility = Visibility.Visible;
        _viewModel.OnConnected();
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    
    private void InteractionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.DataContext is Interaction interaction)
        {
            _viewModel.ViewInteractionCommand.Execute(interaction);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Reset the debounce timer on each keystroke
        _searchDebounce.Stop();
        _searchDebounce.Tag = (sender as TextBox)?.Text?.Trim() ?? "";
        _searchDebounce.Start();
    }

    private void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounce.Stop();

        var searchText = _searchDebounce.Tag as string ?? "";
        var view = CollectionViewSource.GetDefaultView(_viewModel.Logs);
        if (view == null) return;

        if (string.IsNullOrEmpty(searchText))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = item =>
            {
                if (item is LogAnalysis log)
                {
                    return (log.LogName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           (log.Summary?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           (log.EntryPoint?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);
                }
                return false;
            };
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ShowAlertCenter" or "UnreadAlertCount" or "MonitoringAlerts")
        {
            AlertCenterPanel.RefreshAlerts();
        }
    }

    // ─── Keyboard shortcut helpers (called from XAML InputBindings / KeyDown) ──

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        // Ctrl+K → command palette
        if (e.Key == Key.K && ctrl)
        {
            _viewModel.ToggleCommandPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+Shift+O → open folder
        if (e.Key == Key.O && ctrl && shift)
        {
            _viewModel.LoadLogFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+O → open file
        if (e.Key == Key.O && ctrl)
        {
            _viewModel.UploadLogCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+E → export
        if (e.Key == Key.E && ctrl)
        {
            _viewModel.ExportReportCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+, → settings
        if (e.Key == Key.OemComma && ctrl)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+F → focus search box
        if (e.Key == Key.F && ctrl)
        {
            if (SearchBox != null)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                _ = _viewModel.ShowToastAsync("Search focused (Ctrl+F)", "info", 1200);
            }
            e.Handled = true;
            return;
        }
        // Escape → close command palette, or clear selection
        if (e.Key == Key.Escape)
        {
            if (_viewModel.IsCommandPaletteOpen)
            {
                _viewModel.CloseCommandPaletteCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        // Number keys 1-6 → switch tabs (only when not typing in a TextBox)
        if (!ctrl && !shift && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            var tabKey = e.Key switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                Key.D4 => 3,
                Key.D5 => 4,
                Key.D6 => 5,
                _ => -1
            };
            if (tabKey >= 0)
            {
                _viewModel.SelectTabCommand.Execute(tabKey);
                e.Handled = true;
                return;
            }
        }
    }

    private void CommandPaletteBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var first = _viewModel.PaletteResults.FirstOrDefault();
            if (first != null)
                _viewModel.ExecutePaletteItemCommand.Execute(first);
            e.Handled = true;
        }
    }

    private void CommandPaletteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _viewModel.CommandPaletteQuery = tb.Text;
    }

    private void CommandPaletteBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Close palette when clicking the dark backdrop (outside the palette box)
        if (e.OriginalSource == sender)
            _viewModel.CloseCommandPaletteCommand.Execute(null);
    }
}