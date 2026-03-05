using SalesforceDebugAnalyzer.Services;
using Serilog;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SalesforceDebugAnalyzer.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _currentSettings;
    private string _currentTab = "General";

    public SettingsDialog()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _currentSettings = _settingsService.Load();
        
        // Load initial tab
        LoadGeneralTab();
        HighlightTab(GeneralTabButton);
    }

    private void GeneralTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadGeneralTab();
        HighlightTab(GeneralTabButton);
    }

    private void ConnectionsTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadConnectionsTab();
        HighlightTab(ConnectionsTabButton);
    }

    private void ParserTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadParserTab();
        HighlightTab(ParserTabButton);
    }

    private void PrivacyTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadPrivacyTab();
        HighlightTab(PrivacyTabButton);
    }

    private void AdvancedTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAdvancedTab();
        HighlightTab(AdvancedTabButton);
    }

    private void MonitoringTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadMonitoringTab();
        HighlightTab(MonitoringTabButton);
    }

    private void AboutTabButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAboutTab();
        HighlightTab(AboutTabButton);
    }

    private void HighlightTab(Button selectedButton)
    {
        // Reset all buttons
        foreach (var child in ((StackPanel)selectedButton.Parent).Children)
        {
            if (child is Button btn)
            {
                btn.Background = Brushes.Transparent;
            }
        }

        // Highlight selected
        selectedButton.Background = (Brush)FindResource("AccentPrimary");
    }

    private void LoadGeneralTab()
    {
        _currentTab = "General";
        ContentPanel.Children.Clear();

        // Header
        AddHeader("General Settings");

        // Theme
        AddLabel("Theme");
        var themeCombo = new ComboBox
        {
            Style = (Style)FindResource("SettingComboBox"),
            ItemsSource = new[] { "Dark", "Light", "Auto" },
            SelectedItem = _currentSettings.Theme
        };
        themeCombo.SelectionChanged += (s, e) => _currentSettings.Theme = themeCombo.SelectedItem?.ToString() ?? "Dark";
        ContentPanel.Children.Add(themeCombo);

        // Auto-check updates
        var autoUpdateCheck = new CheckBox
        {
            Content = "Automatically check for updates on startup",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.AutoCheckUpdates
        };
        autoUpdateCheck.Checked += (s, e) => _currentSettings.AutoCheckUpdates = true;
        autoUpdateCheck.Unchecked += (s, e) => _currentSettings.AutoCheckUpdates = false;
        ContentPanel.Children.Add(autoUpdateCheck);

        // Show welcome
        var welcomeCheck = new CheckBox
        {
            Content = "Show welcome screen on startup",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.ShowWelcomeOnStartup
        };
        welcomeCheck.Checked += (s, e) => _currentSettings.ShowWelcomeOnStartup = true;
        welcomeCheck.Unchecked += (s, e) => _currentSettings.ShowWelcomeOnStartup = false;
        ContentPanel.Children.Add(welcomeCheck);

        // Language
        AddLabel("Language");
        var langCombo = new ComboBox
        {
            Style = (Style)FindResource("SettingComboBox"),
            ItemsSource = new[] { "English (US)", "Spanish", "French", "German" },
            SelectedIndex = 0,
            IsEnabled = false
        };
        ContentPanel.Children.Add(langCombo);
        AddHelperText("Additional languages coming in v1.1");
    }

    private void LoadConnectionsTab()
    {
        _currentTab = "Connections";
        ContentPanel.Children.Clear();

        AddHeader("Connection Settings");

        // Default org
        AddLabel("Default Salesforce Organization");
        var defaultOrgText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.DefaultOrgUsername ?? "",
            ToolTip = "Leave empty to always prompt for connection"
        };
        defaultOrgText.TextChanged += (s, e) => _currentSettings.DefaultOrgUsername = string.IsNullOrWhiteSpace(defaultOrgText.Text) ? null : defaultOrgText.Text;
        ContentPanel.Children.Add(defaultOrgText);

        // Save history
        var saveHistoryCheck = new CheckBox
        {
            Content = "Save connection history",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.SaveConnectionHistory
        };
        saveHistoryCheck.Checked += (s, e) => _currentSettings.SaveConnectionHistory = true;
        saveHistoryCheck.Unchecked += (s, e) => _currentSettings.SaveConnectionHistory = false;
        ContentPanel.Children.Add(saveHistoryCheck);

        // Max history
        AddLabel("Maximum connections to remember");
        var maxHistoryText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.MaxConnectionHistory.ToString()
        };
        maxHistoryText.TextChanged += (s, e) =>
        {
            if (int.TryParse(maxHistoryText.Text, out int value))
                _currentSettings.MaxConnectionHistory = Math.Max(1, Math.Min(50, value));
        };
        ContentPanel.Children.Add(maxHistoryText);
    }

    private void LoadParserTab()
    {
        _currentTab = "Parser";
        ContentPanel.Children.Clear();

        AddHeader("Parser Settings");

        // Show debug statements
        var debugCheck = new CheckBox
        {
            Content = "Show DEBUG statements in logs",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.ShowDebugStatements
        };
        debugCheck.Checked += (s, e) => _currentSettings.ShowDebugStatements = true;
        debugCheck.Unchecked += (s, e) => _currentSettings.ShowDebugStatements = false;
        ContentPanel.Children.Add(debugCheck);

        // Show system logs
        var systemCheck = new CheckBox
        {
            Content = "Show system logs (CODE_UNIT_STARTED, etc.)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.ShowSystemLogs
        };
        systemCheck.Checked += (s, e) => _currentSettings.ShowSystemLogs = true;
        systemCheck.Unchecked += (s, e) => _currentSettings.ShowSystemLogs = false;
        ContentPanel.Children.Add(systemCheck);

        // Highlight errors
        var highlightCheck = new CheckBox
        {
            Content = "Highlight errors and warnings",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.HighlightErrors
        };
        highlightCheck.Checked += (s, e) => _currentSettings.HighlightErrors = true;
        highlightCheck.Unchecked += (s, e) => _currentSettings.HighlightErrors = false;
        ContentPanel.Children.Add(highlightCheck);

        // Group transactions
        var groupCheck = new CheckBox
        {
            Content = "Enable transaction grouping (group related logs)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.GroupTransactions
        };
        groupCheck.Checked += (s, e) => _currentSettings.GroupTransactions = true;
        groupCheck.Unchecked += (s, e) => _currentSettings.GroupTransactions = false;
        ContentPanel.Children.Add(groupCheck);

        // Max log size
        AddLabel("Maximum log file size (MB)");
        var maxSizeText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.MaxLogSizeMB.ToString()
        };
        maxSizeText.TextChanged += (s, e) =>
        {
            if (int.TryParse(maxSizeText.Text, out int value))
                _currentSettings.MaxLogSizeMB = Math.Max(1, Math.Min(500, value));
        };
        ContentPanel.Children.Add(maxSizeText);
    }

    private void LoadPrivacyTab()
    {
        _currentTab = "Privacy";
        ContentPanel.Children.Clear();

        AddHeader("Privacy Settings");

        AddHelperText("Black Widow respects your privacy. All data processing happens locally on your machine.");

        // Anonymous usage
        var usageCheck = new CheckBox
        {
            Content = "Send anonymous usage statistics (helps improve Black Widow)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.SendAnonymousUsageData,
            Margin = new Thickness(0, 15, 0, 5)
        };
        usageCheck.Checked += (s, e) => _currentSettings.SendAnonymousUsageData = true;
        usageCheck.Unchecked += (s, e) => _currentSettings.SendAnonymousUsageData = false;
        ContentPanel.Children.Add(usageCheck);

        // Crash reporting
        var crashCheck = new CheckBox
        {
            Content = "Enable crash reporting (helps fix bugs faster)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.EnableCrashReporting
        };
        crashCheck.Checked += (s, e) => _currentSettings.EnableCrashReporting = true;
        crashCheck.Unchecked += (s, e) => _currentSettings.EnableCrashReporting = false;
        ContentPanel.Children.Add(crashCheck);

        AddHelperText("\nWhat we collect (if enabled):");
        AddHelperText("• App version and OS version");
        AddHelperText("• Feature usage (which buttons clicked)");
        AddHelperText("• Error messages (no log content)");
        AddHelperText("• Performance metrics (parse times)");
        
        AddHelperText("\nWhat we NEVER collect:");
        AddHelperText("• Your Salesforce credentials");
        AddHelperText("• Debug log contents");
        AddHelperText("• Organization data");
        AddHelperText("• Personal information");
    }

    private void LoadAdvancedTab()
    {
        _currentTab = "Advanced";
        ContentPanel.Children.Clear();

        AddHeader("Advanced Settings");

        // Editor bridge
        var bridgeCheck = new CheckBox
        {
            Content = "Enable VSCode integration (EditorBridge)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.EnableEditorBridge
        };
        bridgeCheck.Checked += (s, e) => _currentSettings.EnableEditorBridge = true;
        bridgeCheck.Unchecked += (s, e) => _currentSettings.EnableEditorBridge = false;
        ContentPanel.Children.Add(bridgeCheck);

        AddLabel("EditorBridge Port");
        var portText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.EditorBridgePort.ToString()
        };
        portText.TextChanged += (s, e) =>
        {
            if (int.TryParse(portText.Text, out int value))
                _currentSettings.EditorBridgePort = Math.Max(1024, Math.Min(65535, value));
        };
        ContentPanel.Children.Add(portText);

        // Caching
        var cacheCheck = new CheckBox
        {
            Content = "Enable local caching for faster loading",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.EnableCaching
        };
        cacheCheck.Checked += (s, e) => _currentSettings.EnableCaching = true;
        cacheCheck.Unchecked += (s, e) => _currentSettings.EnableCaching = false;
        ContentPanel.Children.Add(cacheCheck);

        // Cache retention
        AddLabel("Cache retention (days)");
        var retentionText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.CacheRetentionDays.ToString()
        };
        retentionText.TextChanged += (s, e) =>
        {
            if (int.TryParse(retentionText.Text, out int value))
                _currentSettings.CacheRetentionDays = Math.Max(1, Math.Min(90, value));
        };
        ContentPanel.Children.Add(retentionText);

        // Verbose logging
        var verboseCheck = new CheckBox
        {
            Content = "Enable verbose logging (for troubleshooting)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.VerboseLogging
        };
        verboseCheck.Checked += (s, e) => _currentSettings.VerboseLogging = true;
        verboseCheck.Unchecked += (s, e) => _currentSettings.VerboseLogging = false;
        ContentPanel.Children.Add(verboseCheck);

        // Clear cache button
        var clearButton = new Button
        {
            Content = "Clear Cache Now",
            Style = (Style)FindResource("MaterialDesignOutlinedButton"),
            Foreground = (Brush)FindResource("Warning"),
            Margin = new Thickness(0, 20, 0, 0),
            Padding = new Thickness(15, 8, 15, 8)
        };
        clearButton.Click += ClearCache_Click;
        ContentPanel.Children.Add(clearButton);
    }

    private void LoadMonitoringTab()
    {
        _currentTab = "Monitoring";
        ContentPanel.Children.Clear();

        AddHeader("Background Monitoring");

        // Enable monitoring
        var enableCheck = new CheckBox
        {
            Content = "Enable background monitoring",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.MonitoringEnabled
        };
        enableCheck.Checked += (s, e) => _currentSettings.MonitoringEnabled = true;
        enableCheck.Unchecked += (s, e) => _currentSettings.MonitoringEnabled = false;
        ContentPanel.Children.Add(enableCheck);

        // Auto-start monitoring
        var autoStartCheck = new CheckBox
        {
            Content = "Auto-start monitoring when connected to an org",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.MonitoringAutoStart
        };
        autoStartCheck.Checked += (s, e) => _currentSettings.MonitoringAutoStart = true;
        autoStartCheck.Unchecked += (s, e) => _currentSettings.MonitoringAutoStart = false;
        ContentPanel.Children.Add(autoStartCheck);

        // Poll interval
        AddLabel("Poll interval (seconds)");
        var pollText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.MonitoringPollIntervalSeconds.ToString()
        };
        pollText.TextChanged += (s, e) =>
        {
            if (int.TryParse(pollText.Text, out int value))
                _currentSettings.MonitoringPollIntervalSeconds = Math.Max(15, Math.Min(600, value));
        };
        ContentPanel.Children.Add(pollText);
        AddHelperText("How often to check for new debug logs (15-600 seconds)");

        // Shield monitoring
        var shieldCheck = new CheckBox
        {
            Content = "Enable Shield EventLogFile monitoring (when available)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.ShieldMonitoringEnabled,
            Margin = new Thickness(0, 15, 0, 5)
        };
        shieldCheck.Checked += (s, e) => _currentSettings.ShieldMonitoringEnabled = true;
        shieldCheck.Unchecked += (s, e) => _currentSettings.ShieldMonitoringEnabled = false;
        ContentPanel.Children.Add(shieldCheck);
        AddHelperText("Shield provides Login, API, Page Performance, and Apex Exception monitoring");

        // --- System Tray Section ---
        AddSectionSeparator();
        AddHeader("System Tray");

        var trayCheck = new CheckBox
        {
            Content = "Minimize to system tray on close",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.MinimizeToTray
        };
        trayCheck.Checked += (s, e) => _currentSettings.MinimizeToTray = true;
        trayCheck.Unchecked += (s, e) => _currentSettings.MinimizeToTray = false;
        ContentPanel.Children.Add(trayCheck);

        var startMinCheck = new CheckBox
        {
            Content = "Start minimized to tray",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.StartMinimized
        };
        startMinCheck.Checked += (s, e) => _currentSettings.StartMinimized = true;
        startMinCheck.Unchecked += (s, e) => _currentSettings.StartMinimized = false;
        ContentPanel.Children.Add(startMinCheck);

        // --- Notifications Section ---
        AddSectionSeparator();
        AddHeader("Notifications");

        var toastCheck = new CheckBox
        {
            Content = "Enable desktop notifications",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.ToastNotificationsEnabled
        };
        toastCheck.Checked += (s, e) => _currentSettings.ToastNotificationsEnabled = true;
        toastCheck.Unchecked += (s, e) => _currentSettings.ToastNotificationsEnabled = false;
        ContentPanel.Children.Add(toastCheck);

        var criticalOnlyCheck = new CheckBox
        {
            Content = "Only notify for critical alerts",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.CriticalAlertsOnly
        };
        criticalOnlyCheck.Checked += (s, e) => _currentSettings.CriticalAlertsOnly = true;
        criticalOnlyCheck.Unchecked += (s, e) => _currentSettings.CriticalAlertsOnly = false;
        ContentPanel.Children.Add(criticalOnlyCheck);

        var quietCheck = new CheckBox
        {
            Content = "Enable quiet hours (suppress non-critical notifications)",
            Style = (Style)FindResource("SettingCheckBox"),
            IsChecked = _currentSettings.QuietHoursEnabled
        };
        quietCheck.Checked += (s, e) => _currentSettings.QuietHoursEnabled = true;
        quietCheck.Unchecked += (s, e) => _currentSettings.QuietHoursEnabled = false;
        ContentPanel.Children.Add(quietCheck);

        // Quiet hours range
        var quietPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        quietPanel.Children.Add(new TextBlock
        {
            Text = "Quiet hours: ",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });

        var startCombo = new ComboBox
        {
            Style = (Style)FindResource("SettingComboBox"),
            Width = 70,
            ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:D2}:00").ToArray(),
            SelectedIndex = _currentSettings.QuietHoursStart
        };
        startCombo.SelectionChanged += (s, e) => _currentSettings.QuietHoursStart = startCombo.SelectedIndex;
        quietPanel.Children.Add(startCombo);

        quietPanel.Children.Add(new TextBlock
        {
            Text = "  to  ",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });

        var endCombo = new ComboBox
        {
            Style = (Style)FindResource("SettingComboBox"),
            Width = 70,
            ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:D2}:00").ToArray(),
            SelectedIndex = _currentSettings.QuietHoursEnd
        };
        endCombo.SelectionChanged += (s, e) => _currentSettings.QuietHoursEnd = endCombo.SelectedIndex;
        quietPanel.Children.Add(endCombo);

        ContentPanel.Children.Add(quietPanel);
        AddHelperText("Critical alerts will still notify during quiet hours");

        // --- Alert Thresholds Section ---
        AddSectionSeparator();
        AddHeader("Alert Thresholds");
        AddHelperText("Adjust when alerts are triggered. Lower values = more sensitive.");

        // Governor warning
        AddLabel("Governor limit warning (% of limit)");
        var govWarnText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.GovernorWarningPct.ToString()
        };
        govWarnText.TextChanged += (s, e) =>
        {
            if (int.TryParse(govWarnText.Text, out int value))
                _currentSettings.GovernorWarningPct = Math.Max(50, Math.Min(99, value));
        };
        ContentPanel.Children.Add(govWarnText);

        // Governor critical
        AddLabel("Governor limit critical (% of limit)");
        var govCritText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.GovernorCriticalPct.ToString()
        };
        govCritText.TextChanged += (s, e) =>
        {
            if (int.TryParse(govCritText.Text, out int value))
                _currentSettings.GovernorCriticalPct = Math.Max(70, Math.Min(100, value));
        };
        ContentPanel.Children.Add(govCritText);

        // Duration warning
        AddLabel("Execution time warning (ms)");
        var durWarnText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.DurationWarningMs.ToString()
        };
        durWarnText.TextChanged += (s, e) =>
        {
            if (int.TryParse(durWarnText.Text, out int value))
                _currentSettings.DurationWarningMs = Math.Max(1000, Math.Min(60000, value));
        };
        ContentPanel.Children.Add(durWarnText);

        // Duration critical
        AddLabel("Execution time critical (ms)");
        var durCritText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.DurationCriticalMs.ToString()
        };
        durCritText.TextChanged += (s, e) =>
        {
            if (int.TryParse(durCritText.Text, out int value))
                _currentSettings.DurationCriticalMs = Math.Max(2000, Math.Min(120000, value));
        };
        ContentPanel.Children.Add(durCritText);

        // Health score
        AddLabel("Health score warning (score below)");
        var healthWarnText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.HealthScoreWarning.ToString()
        };
        healthWarnText.TextChanged += (s, e) =>
        {
            if (int.TryParse(healthWarnText.Text, out int value))
                _currentSettings.HealthScoreWarning = Math.Max(20, Math.Min(90, value));
        };
        ContentPanel.Children.Add(healthWarnText);

        AddLabel("Health score critical (score below)");
        var healthCritText = new TextBox
        {
            Style = (Style)FindResource("SettingTextBox"),
            Text = _currentSettings.HealthScoreCritical.ToString()
        };
        healthCritText.TextChanged += (s, e) =>
        {
            if (int.TryParse(healthCritText.Text, out int value))
                _currentSettings.HealthScoreCritical = Math.Max(10, Math.Min(80, value));
        };
        ContentPanel.Children.Add(healthCritText);
    }

    private void AddSectionSeparator()
    {
        ContentPanel.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderSubtle"),
            Margin = new Thickness(0, 20, 0, 10)
        });
    }

    private void LoadAboutTab()
    {
        _currentTab = "About";
        ContentPanel.Children.Clear();

        AddHeader("About Black Widow");

        // Logo/Icon
        var logoText = new TextBlock
        {
            Text = "🕷️",
            FontSize = 72,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 20)
        };
        ContentPanel.Children.Add(logoText);

        // Version
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = new TextBlock
        {
            Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}",
            FontSize = 16,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        ContentPanel.Children.Add(versionText);

        // Description
        var descText = new TextBlock
        {
            Text = "The only Salesforce debug log analyzer that groups related logs\nand shows the complete user experience journey.",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextSecondary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 30)
        };
        ContentPanel.Children.Add(descText);

        // Links
        AddLink("🌐 Website", "https://github.com/felisbinofarms/salesforce-debug-log-analyzer");
        AddLink("📖 Documentation", "https://github.com/felisbinofarms/salesforce-debug-log-analyzer/blob/main/README.md");
        AddLink("🐛 Report a Bug", "https://github.com/felisbinofarms/salesforce-debug-log-analyzer/issues");
        AddLink("💡 Request a Feature", "https://github.com/felisbinofarms/salesforce-debug-log-analyzer/issues/new");

        // Copyright
        var copyright = new TextBlock
        {
            Text = $"Copyright © {DateTime.Now.Year} Black Widow Team\nMade with ❤️ for the Salesforce community",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0)
        };
        ContentPanel.Children.Add(copyright);
    }

    private void AddHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("HeaderStyle")
        };
        ContentPanel.Children.Add(header);
    }

    private void AddLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("LabelStyle")
        };
        ContentPanel.Children.Add(label);
    }

    private void AddHelperText(string text)
    {
        var helper = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(0, 2, 0, 2),
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel.Children.Add(helper);
    }

    private void AddLink(string text, string url)
    {
        var link = new Button
        {
            Content = text,
            Style = (Style)FindResource("MaterialDesignFlatButton"),
            Foreground = (Brush)FindResource("AccentPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            Tag = url
        };
        link.Click += (s, e) =>
        {
            if (link.Tag is string linkUrl)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(linkUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to open settings link: {LinkUrl}", linkUrl);
                }
            }
        };
        ContentPanel.Children.Add(link);
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will clear all cached data including:\n\n" +
            "• Parsed log files\n" +
            "• Connection history\n" +
            "• Debug level cache\n\n" +
            "Your settings will not be affected.\n\n" +
            "Are you sure you want to continue?",
            "Clear Cache",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var cache = new CacheService();
                cache.ClearCache();
                
                // Also clear any cached log files from temp
                var tempPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlackWidow", "Cache");
                if (System.IO.Directory.Exists(tempPath))
                {
                    System.IO.Directory.Delete(tempPath, true);
                }
                
                MessageBox.Show("Cache cleared successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will reset ALL settings to their default values.\n\n" +
            "Are you sure you want to continue?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes)
        {
            _settingsService.Reset();
            _currentSettings = _settingsService.Load();

            // Reload current tab
            switch (_currentTab)
            {
                case "General": LoadGeneralTab(); break;
                case "Connections": LoadConnectionsTab(); break;
                case "Parser": LoadParserTab(); break;
                case "Privacy": LoadPrivacyTab(); break;
                case "Advanced": LoadAdvancedTab(); break;
                case "Monitoring": LoadMonitoringTab(); break;
                case "About": LoadAboutTab(); break;
            }

            MessageBox.Show("Settings reset to defaults!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsService.Save(_currentSettings);
            MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save settings:\n\n{ex.Message}\n\nPlease try again or contact support if the problem persists.",
                "Error Saving Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
