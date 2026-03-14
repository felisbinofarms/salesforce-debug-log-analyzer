using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.ViewModels;

namespace SalesforceDebugAnalyzer.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _searchDebounce;

    public MainWindow()
    {
        InitializeComponent();

        // Wire up ViewModel
        var api = new SalesforceApiService();
        var parser = new LogParserService();
        var oauth = new OAuthService();
        DataContext = new MainViewModel(api, parser, oauth);

        // Keyboard shortcuts
        KeyDown += OnWindowKeyDown;

        // Drag-drop support
        AddHandler(DragDrop.DropEvent, OnFileDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // ── Window Controls ──────────────────────────────────────────
    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ── Keyboard Shortcuts ───────────────────────────────────────
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.K:
                    vm.IsCommandPaletteOpen = !vm.IsCommandPaletteOpen;
                    if (vm.IsCommandPaletteOpen)
                    {
                        CommandPaletteBox?.Focus();
                    }

                    e.Handled = true;
                    break;
                case Key.O:
                    if (vm.UploadLogCommand.CanExecute(null))
                    {
                        vm.UploadLogCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.E:
                    if (vm.ExportReportCommand.CanExecute(null))
                    {
                        vm.ExportReportCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.F:
                    SearchBox?.Focus();
                    e.Handled = true;
                    break;
                case Key.OemComma:
                    if (vm.OpenSettingsCommand.CanExecute(null))
                    {
                        vm.OpenSettingsCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
            }
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (e.Key == Key.O)
            {
                if (vm.LoadLogFolderCommand.CanExecute(null))
                {
                    vm.LoadLogFolderCommand.Execute(null);
                }

                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.IsCommandPaletteOpen)
            {
                vm.IsCommandPaletteOpen = false;
                e.Handled = true;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key >= Key.D1 && e.Key <= Key.D6)
        {
            // Number keys 1-6 switch tabs (when not in a text box)
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
            {
                int tabIndex = e.Key - Key.D1;
                vm.SelectTabCommand.Execute(tabIndex);
                e.Handled = true;
            }
        }
    }

    // ── Command Palette ──────────────────────────────────────────
    private void CommandPaletteBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsCommandPaletteOpen = false;
        }
    }

    private void CommandPaletteBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // Debounce palette filtering
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            // ViewModel can filter PaletteResults based on CommandPaletteBox.Text
        };
        _searchDebounce.Start();
    }

    private void CommandPaletteBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { } vm)
        {
            vm.IsCommandPaletteOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Execute selected palette command
            if (PaletteResultsList?.SelectedItem != null)
            {
                // TODO: Execute PaletteCommand action
            }
            if (ViewModel is { } vm2)
            {
                vm2.IsCommandPaletteOpen = false;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (PaletteResultsList != null && PaletteResultsList.ItemCount > 0)
            {
                PaletteResultsList.SelectedIndex = Math.Min(
                    PaletteResultsList.SelectedIndex + 1,
                    PaletteResultsList.ItemCount - 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteResultsList != null)
            {
                PaletteResultsList.SelectedIndex = Math.Max(PaletteResultsList.SelectedIndex - 1, 0);
                e.Handled = true;
            }
        }
    }

    // ── Drag & Drop ──────────────────────────────────────────────
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        foreach (var item in files)
        {
            if (item is Avalonia.Platform.Storage.IStorageFile file)
            {
                var path = file.Path.LocalPath;
                if (path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    await vm.LoadLogFromPath(path);
                }
            }
        }
    }
}
