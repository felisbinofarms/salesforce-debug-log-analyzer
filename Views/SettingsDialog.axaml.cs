using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private Button? _activeTabButton;

    public SettingsDialog()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        LoadTab(0);
        HighlightTab(GeneralTabBtn);
    }

    private void TabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int index))
        {
            LoadTab(index);
            HighlightTab(btn);
        }
    }

    private void HighlightTab(Button btn)
    {
        if (_activeTabButton != null)
        {
            _activeTabButton.Background = Brushes.Transparent;
        }

        _activeTabButton = btn;
        btn.Background = new SolidColorBrush(Color.Parse("#1A4493F8"));
    }

    private void LoadTab(int index)
    {
        ContentPanel.Children.Clear();
        switch (index)
        {
            case 0: LoadGeneralTab(); break;
            case 1: LoadConnectionsTab(); break;
            case 2: LoadParserTab(); break;
            case 3: LoadAdvancedTab(); break;
            case 4: LoadPrivacyTab(); break;
            case 5: LoadAboutTab(); break;
        }
    }

    private void LoadGeneralTab()
    {
        AddHeader("General");

        AddLabel("Theme");
        var themeCombo = CreateComboBox(new[] { "Dark", "Light", "Auto" }, _settings.Theme);
        themeCombo.SelectionChanged += (_, _) => _settings.Theme = themeCombo.SelectedItem?.ToString() ?? "Dark";
        ContentPanel.Children.Add(themeCombo);

        var autoUpdate = CreateCheckBox("Automatically check for updates", _settings.AutoCheckUpdates);
        autoUpdate.IsCheckedChanged += (_, _) => _settings.AutoCheckUpdates = autoUpdate.IsChecked ?? false;
        ContentPanel.Children.Add(autoUpdate);

        var welcome = CreateCheckBox("Show welcome screen on startup", _settings.ShowWelcomeOnStartup);
        welcome.IsCheckedChanged += (_, _) => _settings.ShowWelcomeOnStartup = welcome.IsChecked ?? false;
        ContentPanel.Children.Add(welcome);

        AddLabel("Language");
        var langCombo = CreateComboBox(new[] { "English (US)" }, "English (US)");
        langCombo.IsEnabled = false;
        ContentPanel.Children.Add(langCombo);
        AddHelper("Additional languages coming in v1.1");
    }

    private void LoadConnectionsTab()
    {
        AddHeader("Connections");

        AddLabel("Default Salesforce Organization");
        var orgBox = CreateTextBox(_settings.DefaultOrgUsername ?? "", "Leave empty to always prompt");
        orgBox.TextChanged += (_, _) => _settings.DefaultOrgUsername = string.IsNullOrWhiteSpace(orgBox.Text) ? null : orgBox.Text;
        ContentPanel.Children.Add(orgBox);

        var saveHistory = CreateCheckBox("Save connection history", _settings.SaveConnectionHistory);
        saveHistory.IsCheckedChanged += (_, _) => _settings.SaveConnectionHistory = saveHistory.IsChecked ?? false;
        ContentPanel.Children.Add(saveHistory);

        AddLabel("Maximum connections to remember");
        var maxBox = CreateTextBox(_settings.MaxConnectionHistory.ToString(), "10");
        maxBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxBox.Text, out int v))
            {
                _settings.MaxConnectionHistory = Math.Max(1, Math.Min(50, v));
            }
        };
        ContentPanel.Children.Add(maxBox);
    }

    private void LoadParserTab()
    {
        AddHeader("Parser");

        var debug = CreateCheckBox("Show debug statements", _settings.ShowDebugStatements);
        debug.IsCheckedChanged += (_, _) => _settings.ShowDebugStatements = debug.IsChecked ?? false;
        ContentPanel.Children.Add(debug);

        var system = CreateCheckBox("Show system logs", _settings.ShowSystemLogs);
        system.IsCheckedChanged += (_, _) => _settings.ShowSystemLogs = system.IsChecked ?? false;
        ContentPanel.Children.Add(system);

        var errors = CreateCheckBox("Highlight errors", _settings.HighlightErrors);
        errors.IsCheckedChanged += (_, _) => _settings.HighlightErrors = errors.IsChecked ?? false;
        ContentPanel.Children.Add(errors);

        var groupTx = CreateCheckBox("Group related transactions", _settings.GroupTransactions);
        groupTx.IsCheckedChanged += (_, _) => _settings.GroupTransactions = groupTx.IsChecked ?? false;
        ContentPanel.Children.Add(groupTx);

        AddLabel("Maximum log file size (MB)");
        var maxLog = CreateTextBox(_settings.MaxLogSizeMB.ToString(), "100");
        maxLog.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxLog.Text, out int v))
            {
                _settings.MaxLogSizeMB = Math.Max(1, Math.Min(500, v));
            }
        };
        ContentPanel.Children.Add(maxLog);
    }

    private void LoadAdvancedTab()
    {
        AddHeader("Advanced");

        var editor = CreateCheckBox("Enable VS Code EditorBridge", _settings.EnableEditorBridge);
        editor.IsCheckedChanged += (_, _) => _settings.EnableEditorBridge = editor.IsChecked ?? false;
        ContentPanel.Children.Add(editor);

        var cache = CreateCheckBox("Enable caching", _settings.EnableCaching);
        cache.IsCheckedChanged += (_, _) => _settings.EnableCaching = cache.IsChecked ?? false;
        ContentPanel.Children.Add(cache);

        AddLabel("Cache retention (days)");
        var cacheBox = CreateTextBox(_settings.CacheRetentionDays.ToString(), "7");
        cacheBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(cacheBox.Text, out int v))
            {
                _settings.CacheRetentionDays = Math.Max(1, Math.Min(90, v));
            }
        };
        ContentPanel.Children.Add(cacheBox);

        var verbose = CreateCheckBox("Verbose logging", _settings.VerboseLogging);
        verbose.IsCheckedChanged += (_, _) => _settings.VerboseLogging = verbose.IsChecked ?? false;
        ContentPanel.Children.Add(verbose);
    }

    private void LoadPrivacyTab()
    {
        AddHeader("Privacy");

        var usage = CreateCheckBox("Send anonymous usage statistics", _settings.SendAnonymousUsageData);
        usage.IsCheckedChanged += (_, _) => _settings.SendAnonymousUsageData = usage.IsChecked ?? false;
        ContentPanel.Children.Add(usage);

        var crash = CreateCheckBox("Enable crash reporting", _settings.EnableCrashReporting);
        crash.IsCheckedChanged += (_, _) => _settings.EnableCrashReporting = crash.IsChecked ?? false;
        ContentPanel.Children.Add(crash);

        AddHelper("We never collect your Salesforce data, credentials, or log contents.");
    }

    private void LoadAboutTab()
    {
        AddHeader("About Black Widow");

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        AddLabel($"Version {version}");

        ContentPanel.Children.Add(new TextBlock
        {
            Text = "The only Salesforce debug log analyzer that groups related logs, " +
                   "detects execution phases, and explains the complete user experience journey.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 16)
        });

        ContentPanel.Children.Add(new TextBlock
        {
            Text = "© 2026 Black Widow. All rights reserved.",
            Foreground = new SolidColorBrush(Color.Parse("#484F58")),
            FontSize = 12
        });
    }

    // ===== UI Helpers =====

    private void AddHeader(string text)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            Margin = new Thickness(0, 0, 0, 16)
        });
    }

    private void AddLabel(string text)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#C9D1D9")),
            Margin = new Thickness(0, 8, 0, 4)
        });
    }

    private void AddHelper(string text)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#484F58")),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private CheckBox CreateCheckBox(string label, bool isChecked)
    {
        return new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
            Foreground = new SolidColorBrush(Color.Parse("#C9D1D9")),
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private ComboBox CreateComboBox(string[] items, string selected)
    {
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedItem = selected,
            MinWidth = 200,
            Background = new SolidColorBrush(Color.Parse("#272D36")),
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            Margin = new Thickness(0, 0, 0, 4)
        };
        return combo;
    }

    private TextBox CreateTextBox(string text, string watermark)
    {
        return new TextBox
        {
            Text = text,
            Watermark = watermark,
            Background = new SolidColorBrush(Color.Parse("#272D36")),
            Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2D333B")),
            MaxWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private void ResetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        _settings = new AppSettings();
        // Reload current tab to reflect defaults
        if (_activeTabButton?.Tag is string tag && int.TryParse(tag, out int index))
        {
            LoadTab(index);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _settingsService.Save(_settings);
        Close();
    }
}
