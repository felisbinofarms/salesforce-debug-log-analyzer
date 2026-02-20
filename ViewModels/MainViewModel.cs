using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using SalesforceDebugAnalyzer.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace SalesforceDebugAnalyzer.ViewModels;

/// <summary>
/// Main ViewModel for the application
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly OAuthService _oauthService;
    private readonly SalesforceApiService _apiService;
    private readonly LogParserService _parserService;
    private readonly LogMetadataExtractor _metadataExtractor;
    private readonly LogGroupService _groupService;
    private readonly SalesforceCliService _cliService;
    private readonly EditorBridgeService _editorBridge;
    private readonly ReportExportService _exportService;
    private readonly SettingsService _settingsService;
    private readonly OrgMetadataService _metadataService;
    private readonly LogExplainerService _explainerService;
    private readonly LicenseService _licenseService;
    private bool _disposed;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private ObservableCollection<LogAnalysis> _logs = new();

    [ObservableProperty]
    private ObservableCollection<LogGroup> _logGroups = new();

    [ObservableProperty]
    private LogGroup? _selectedLogGroup;

    [ObservableProperty]
    private LogAnalysis? _selectedLog;

    [ObservableProperty]
    private bool _showGroupedView = false;
    
    // Aggregate metrics displayed when a LogGroup is selected
    [ObservableProperty]
    private string _groupSummary = "";
    
    [ObservableProperty]
    private string _groupPhaseBreakdown = "";
    
    partial void OnSelectedLogGroupChanged(LogGroup? value)
    {
        if (value == null) return;
        
        // Don't change SelectedLog here — user will click individual logs in the group to see details
        // The group card shows aggregate metrics only
        
        // Generate aggregate summary
        var logCount = value.Logs.Count;
        var totalDuration = value.TotalDuration;
        var userName = value.UserName ?? "Unknown User";
        
        GroupSummary = $"Transaction Group: {userName} • {logCount} logs • {totalDuration:F0}ms total user wait time";
        
        // Generate phase breakdown text
        if (value.Phases != null && value.Phases.Any())
        {
            var phaseLines = value.Phases.Select(p => 
                $"  • {p.Name}: {p.DurationMs:F0}ms ({(p.DurationMs / totalDuration * 100):F1}%)");
            GroupPhaseBreakdown = $"Execution Phases:\n{string.Join("\n", phaseLines)}";
        }
        else
        {
            GroupPhaseBreakdown = "";
        }
    }

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private ObservableCollection<string> _issues = new();

    [ObservableProperty]
    private ObservableCollection<string> _recommendations = new();

    [ObservableProperty]
    private ObservableCollection<DatabaseOperation> _databaseOperations = new();

    // Stack Analysis display
    [ObservableProperty]
    private string _stackAnalysisSummary = "";

    [ObservableProperty]
    private bool _hasStackRisk = false;

    // ===== VISUAL DASHBOARD PROPERTIES =====
    
    // Hero Stats
    [ObservableProperty]
    private string _heroStatus = ""; // "SUCCESS", "WARNING", "FAILED"
    
    [ObservableProperty]
    private string _heroDuration = "0ms";
    
    [ObservableProperty]
    private string _heroEntryPoint = "";
    
    // Stat Cards
    [ObservableProperty]
    private int _statSoqlCount = 0;
    
    [ObservableProperty]
    private int _statSoqlLimit = 100;
    
    [ObservableProperty]
    private int _statDmlCount = 0;
    
    [ObservableProperty]
    private int _statDmlLimit = 150;
    
    [ObservableProperty]
    private int _statCpuTime = 0;
    
    [ObservableProperty]
    private int _statCpuLimit = 10000;
    
    [ObservableProperty]
    private int _statMethodCount = 0;
    
    [ObservableProperty]
    private int _statErrorCount = 0;
    
    [ObservableProperty]
    private int _statWarningCount = 0;
    
    // Governor Limit Percentages (for progress bars)
    [ObservableProperty]
    private double _soqlPercent = 0;
    
    [ObservableProperty]
    private double _dmlPercent = 0;
    
    [ObservableProperty]
    private double _cpuPercent = 0;
    
    // Timing breakdown
    [ObservableProperty]
    private double _wallClockMs = 0;
    
    [ObservableProperty]
    private double _cpuTimeMs = 0;
    
    [ObservableProperty]
    private double _overheadMs = 0;
    
    [ObservableProperty]
    private bool _showTimingBreakdown = false;

    // Timeline strip
    [ObservableProperty]
    private ObservableCollection<TimelineSegment> _timelineSegments = new();

    [ObservableProperty]
    private double _timelineTotalDurationMs = 0;

    [ObservableProperty]
    private string _timelineSummary = "";

    [ObservableProperty]
    private string _timelineSlowestLabel = "";

    [ObservableProperty]
    private bool _hasTimelineData = false;

    // Filter properties
    [ObservableProperty]
    private bool _showSoqlOperations = true;

    [ObservableProperty]
    private bool _showDmlOperations = true;

    [ObservableProperty]
    private bool _showErrorsOnly = false;

    [ObservableProperty]
    private bool _showDebugStatements = false;

    // Tab selection (0=Summary, 1=Tree, 2=Timeline, 3=Queries, 4=Explain)
    [ObservableProperty]
    private int _selectedTabIndex = 4;

    // Streaming properties
    [ObservableProperty]
    private bool _isStreaming = false;

    [ObservableProperty]
    private bool _isCliInstalled = false;

    [ObservableProperty]
    private string _streamingStatus = "";

    [ObservableProperty]
    private ObservableCollection<StreamingLogEntry> _streamingLogs = new();

    [ObservableProperty]
    private string? _streamingUsername = "";

    // ===== INTERACTION RECORDING PROPERTIES =====
    
    [ObservableProperty]
    private bool _isRecording = false;
    
    [ObservableProperty]
    private DateTime _recordingStartTime;
    
    [ObservableProperty]
    private string _recordingElapsedTime = "00:00";
    
    /// <summary>Buffer for logs captured during recording</summary>
    private List<LogAnalysis> _recordingBuffer = new();
    
    /// <summary>Timer to update elapsed time display</summary>
    private System.Windows.Threading.DispatcherTimer? _recordingTimer;
    
    /// <summary>Buffer for streaming logs before adding to UI (throttling)</summary>
    private readonly Queue<StreamingLogEntry> _streamingBuffer = new();
    
    /// <summary>Timer to batch streaming log updates (reduces UI lag)</summary>
    private System.Windows.Threading.DispatcherTimer? _streamingThrottleTimer;
    
    /// <summary>Lock for thread-safe streaming buffer access</summary>
    private readonly object _streamingLock = new();
    
    /// <summary>User-configured streaming options (filters, performance settings)</summary>
    private StreamingOptions? _streamingOptions;
    
    [ObservableProperty]
    private ObservableCollection<Interaction> _interactions = new();
    
    [ObservableProperty]
    private Interaction? _selectedInteraction;
    
    // ===== HEALTH SCORE & ACTIONABLE ISSUES =====
    
    [ObservableProperty]
    private int _healthScore = 0;
    
    [ObservableProperty]
    private string _healthGrade = "";
    
    [ObservableProperty]
    private string _healthStatus = "";
    
    [ObservableProperty]
    private string _healthStatusIcon = "";
    
    [ObservableProperty]
    private string _healthReasoning = "";
    
    [ObservableProperty]
    private ObservableCollection<ActionableIssue> _criticalIssues = new();
    
    [ObservableProperty]
    private ObservableCollection<ActionableIssue> _highPriorityIssues = new();
    
    [ObservableProperty]
    private ObservableCollection<ActionableIssue> _quickWins = new();
    
    [ObservableProperty]
    private int _totalEstimatedMinutes = 0;
    
    [ObservableProperty]
    private bool _hasHealthData = false;
    
    [ObservableProperty]
    private bool _isCleanBillOfHealth = false;
    
    // ===== FULL ANALYSIS SUMMARY =====
    
    [ObservableProperty]
    private string _fullSummaryText = "";
    
    // ===== PLAIN-ENGLISH INSIGHTS (for non-technical users) =====
    
    [ObservableProperty]
    private string _plainEnglishSummary = "";
    
    [ObservableProperty]
    private string _userWaitTimeSeconds = "0";
    
    [ObservableProperty]
    private string _userWaitExplanation = "";
    
    [ObservableProperty]
    private string _speedRating = "Fast"; // Fast, Moderate, Slow, Very Slow
    
    [ObservableProperty]
    private string _resourceUsageExplanation = "";
    
    [ObservableProperty]
    private int _soqlCount = 0;
    
    [ObservableProperty]
    private int _soqlLimit = 100;
    
    [ObservableProperty]
    private int _dmlCount = 0;
    
    [ObservableProperty]
    private int _dmlLimit = 150;
    
    [ObservableProperty]
    private int _cpuPercentValue = 0;
    
    [ObservableProperty]
    private ObservableCollection<PlainEnglishProblem> _plainEnglishProblems = new();
    
    [ObservableProperty]
    private ObservableCollection<PlainEnglishRecommendation> _plainEnglishRecommendations = new();
    
    [ObservableProperty]
    private bool _hasProblems = false;
    
    [ObservableProperty]
    private bool _hasRecommendations = false;
    
    [ObservableProperty]
    private bool _isAllClear = false;
    
    // ===== DEEP EXPLANATION (LogExplainerService output) =====
    
    [ObservableProperty]
    private LogExplanation? _currentExplanation;
    
    [ObservableProperty]
    private bool _hasExplanation = false;
    
    [ObservableProperty]
    private ObservableCollection<DetailedIssue> _detailedIssues = new();
    
    [ObservableProperty]
    private ObservableCollection<DetailedRecommendation> _detailedRecommendations = new();
    
    [ObservableProperty]
    private ObservableCollection<LearningItem> _learningItems = new();
    
    [ObservableProperty]
    private bool _hasDetailedIssues = false;
    
    [ObservableProperty]
    private bool _hasDetailedRecommendations = false;
    
    [ObservableProperty]
    private bool _hasLearningItems = false;
    
    [ObservableProperty]
    private ObservableCollection<string> _whatYourCodeDid = new();
    
    // ===== ALWAYS-USEFUL: TOP METHODS & QUERIES =====
    
    [ObservableProperty]
    private ObservableCollection<MethodDisplayItem> _topMethods = new();
    
    [ObservableProperty]
    private ObservableCollection<QueryDisplayItem> _topQueries = new();
    
    [ObservableProperty]
    private bool _hasTopMethods = false;
    
    [ObservableProperty]
    private bool _hasTopQueries = false;
    
    // ===== EXECUTION TREE =====
    
    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _executionTreeNodes = new();
    
    [ObservableProperty]
    private bool _hasExecutionTree = false;
    
    // ===== DETAILED TIMELINE =====
    
    [ObservableProperty]
    private ObservableCollection<TimelineDetailItem> _timelineDetails = new();
    
    [ObservableProperty]
    private bool _hasTimelineDetails = false;

    // ===== PLATFORM EVENTS =====

    [ObservableProperty]
    private ObservableCollection<Models.PlatformEventPublish> _platformEventPublishes = new();

    [ObservableProperty]
    private bool _hasPlatformEvents = false;

    // ===== NAMESPACE LIMITS =====

    [ObservableProperty]
    private ObservableCollection<Models.GovernorLimitSnapshot> _namespaceLimitItems = new();

    [ObservableProperty]
    private bool _hasNamespaceLimits = false;

    // ===== FLOW FAULTS =====

    [ObservableProperty]
    private ObservableCollection<Models.FlowExecution> _faultedFlows = new();

    [ObservableProperty]
    private bool _hasFlowFaults = false;

    // ===== CALLOUTS =====
    [ObservableProperty]
    private ObservableCollection<Models.CalloutOperation> _callouts = new();
    [ObservableProperty]
    private bool _hasCallouts = false;

    // ===== LOG CONTEXT FLAGS =====
    [ObservableProperty]
    private bool _isLogTruncated = false;
    [ObservableProperty]
    private bool _isAsyncExecution = false;

    // ===== BULK SAFETY =====
    [ObservableProperty]
    private string _bulkSafetyGrade = "";
    [ObservableProperty]
    private string _bulkSafetyReason = "";

    // ===== DUPLICATE QUERIES =====
    [ObservableProperty]
    private ObservableCollection<Models.DuplicateQueryInfo> _duplicateQueries = new();
    [ObservableProperty]
    private bool _hasDuplicateQueries = false;

    // ===== WORKFLOW RULES =====
    [ObservableProperty]
    private ObservableCollection<Models.WorkflowRuleEvaluation> _workflowRules = new();
    [ObservableProperty]
    private bool _hasWorkflowRules = false;

    // ===== FLOW ERRORS (DML/validation failures inside flows, distinct from fault paths) =====
    [ObservableProperty]
    private ObservableCollection<Models.FlowError> _flowErrors = new();
    [ObservableProperty]
    private bool _hasFlowErrors = false;

    // ===== HEAP & CMDT QUERY STATS =====
    [ObservableProperty]
    private long _totalHeapAllocated = 0;
    [ObservableProperty]
    private int _customMetadataQueryCount = 0;
    [ObservableProperty]
    private int _regularSoqlCount = 0;

    // ===== TESTING LIMITS (overhead from test framework vs production code) =====
    [ObservableProperty]
    private Models.GovernorLimitSnapshot? _testingLimits = null;
    [ObservableProperty]
    private bool _hasTestingLimits = false;

    // Editor Bridge (VSCode Integration)
    [ObservableProperty]
    private bool _isEditorConnected = false;
    
    [ObservableProperty]
    private string _editorConnectionStatus = "VSCode: Not Connected";

    // ===== LICENSE PROPERTIES =====

    [ObservableProperty]
    private LicenseTier _currentLicenseTier = LicenseTier.Free;

    [ObservableProperty]
    private bool _isProUser = false;

    [ObservableProperty]
    private bool _showTrialBanner = false;

    [ObservableProperty]
    private string _trialBannerText = "";

    [ObservableProperty]
    private string _tierDisplayName = "Free";

    public MainViewModel(SalesforceApiService salesforceApi, LogParserService parserService, OAuthService oauthService)
    {
        _apiService = salesforceApi;
        _parserService = parserService;
        _oauthService = oauthService;
        _metadataExtractor = new LogMetadataExtractor();
        _groupService = new LogGroupService();
        _cliService = new SalesforceCliService();
        _editorBridge = new EditorBridgeService();
        _exportService = new ReportExportService();
        _settingsService = new SettingsService();
        _metadataService = new OrgMetadataService(_apiService);
        _explainerService = new LogExplainerService();
        _licenseService   = new LicenseService();

        // Apply initial license state immediately (from local cache, no network)
        RefreshLicenseDisplay();

        // Then fire-and-forget the async revalidation (may hit network)
        _ = ValidateLicenseOnStartupAsync();

        // Check CLI installation
        IsCliInstalled = _cliService.IsInstalled;
        
        // Subscribe to CLI events
        _cliService.StatusChanged += OnCliStatusChanged;
        
        // Subscribe to Editor Bridge events
        _editorBridge.ConnectionStatusChanged += OnEditorConnectionChanged;
        _editorBridge.WorkspacePathReceived += OnWorkspacePathReceived;
        
        // Start Editor Bridge server
        _ = StartEditorBridgeAsync();
        _cliService.LogReceived += OnLogReceived;
        
        // Initialize streaming throttle timer (batches updates every 500ms to prevent UI lag)
        _streamingThrottleTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _streamingThrottleTimer.Tick += OnStreamingThrottleTick;
    }

    // ─────────────────────────────────────────────────────────────────────
    // License helpers
    // ─────────────────────────────────────────────────────────────────────

    private void RefreshLicenseDisplay()
    {
        CurrentLicenseTier = _licenseService.CurrentTier;
        IsProUser          = _licenseService.IsProOrAbove;
        TierDisplayName    = CurrentLicenseTier switch
        {
            LicenseTier.Free       => "Free",
            LicenseTier.Trial      => $"Pro Trial",
            LicenseTier.Pro        => "Pro",
            LicenseTier.Team       => "Team",
            LicenseTier.Enterprise => "Enterprise",
            _                      => "Free"
        };

        if (CurrentLicenseTier == LicenseTier.Trial)
        {
            var days = _licenseService.TrialDaysRemaining();
            ShowTrialBanner = true;
            TrialBannerText = days > 1
                ? $"⏳ Pro Trial — {days} days remaining"
                : days == 1
                    ? "⚠️ Pro Trial expires tomorrow — upgrade now!"
                    : "⚠️ Trial expired — upgrade to keep Pro features";
        }
        else
        {
            ShowTrialBanner = false;
            TrialBannerText = string.Empty;
        }
    }

    private async Task ValidateLicenseOnStartupAsync()
    {
        var result = await _licenseService.RevalidateIfNeededAsync().ConfigureAwait(false);

        // Re-apply display on the UI thread
        App.Current.Dispatcher.Invoke(RefreshLicenseDisplay);

        if (!result.IsValid && result.ErrorMessage != null)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // Non-blocking notification — don't interrupt startup with a dialog
                StatusMessage = $"⚠️ License: {result.ErrorMessage}";
            });
        }
        else if (result.IsOfflineGracePeriod)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = "ℹ️ License: offline — will revalidate when connected";
            });
        }
    }

    /// <summary>
    /// Returns true and shows the upgrade dialog if the feature requires Pro.
    /// Use as a guard at the top of Pro-gated commands.
    /// </summary>
    private bool RequiresPro(LicenseFeature feature)
    {
        if (_licenseService.IsFeatureAvailable(feature)) return false;
        ShowUpgradeDialog();
        return true;
    }

    private void ShowUpgradeDialog()
    {
        var dialog = new Views.UpgradeDialog { Owner = App.Current.MainWindow };
        dialog.ShowDialog();
        // Refresh tier in case user just started a trial
        RefreshLicenseDisplay();
    }

    // ─────────────────────────────────────────────────────────────────────

    public void OnConnected()
    {
        IsConnected = true;
        ConnectionStatus = $"Connected: {_apiService.Connection?.InstanceUrl}";
        
        // Extract org name from instance URL (e.g., "acme" from "acme.my.salesforce.com")
        var instanceUrl = _apiService.Connection?.InstanceUrl ?? "";
        var orgName = "Salesforce";
        
        if (instanceUrl.Contains(".my.salesforce.com"))
        {
            var start = instanceUrl.IndexOf("//") + 2;
            var end = instanceUrl.IndexOf(".my.salesforce.com");
            if (end > start)
            {
                orgName = instanceUrl.Substring(start, end - start);
            }
        }
        else if (instanceUrl.Contains(".sandbox.salesforce.com"))
        {
            orgName = "Sandbox";
        }
        
        ConnectionDisplayName = orgName;
        StatusMessage = $"✓ Connected to {orgName}";
    }

    partial void OnSelectedLogChanged(LogAnalysis? value)
    {
        if (value != null)
        {
            // Simple summary title
            if (value.IsTestExecution)
            {
                SummaryText = $"🧪 Test: {value.TestClassName}";
            }
            else
            {
                SummaryText = value.EntryPoint ?? "Log Analysis";
            }
            
            // FULL SUMMARY - the rich markdown-like analysis
            FullSummaryText = value.Summary ?? "";
            
            // ISSUES - Only show real problems
            var simpleIssues = new List<string>();
            
            // Real errors
            if (value.Errors?.Any() == true)
            {
                simpleIssues.Add($"❌ {value.Errors.Count} ERROR(S)");
                foreach (var error in value.Errors.Take(3))
                {
                    simpleIssues.Add($"   {error.Name}");
                }
            }
            
            // Governor limits only if close to limit
            var lastLimit = value.LimitSnapshots?.LastOrDefault();
            if (lastLimit != null)
            {
                var soqlPct = lastLimit.SoqlQueriesLimit > 0 ? (lastLimit.SoqlQueries * 100.0) / lastLimit.SoqlQueriesLimit : 0;
                var dmlPct = lastLimit.DmlStatementsLimit > 0 ? (lastLimit.DmlStatements * 100.0) / lastLimit.DmlStatementsLimit : 0;
                var cpuPct = lastLimit.CpuTimeLimit > 0 ? (lastLimit.CpuTime * 100.0) / lastLimit.CpuTimeLimit : 0;
                
                if (soqlPct > 80) simpleIssues.Add($"🔥 SOQL at {soqlPct:F0}% ({lastLimit.SoqlQueries}/{lastLimit.SoqlQueriesLimit})");
                if (dmlPct > 80) simpleIssues.Add($"🔥 DML at {dmlPct:F0}% ({lastLimit.DmlStatements}/{lastLimit.DmlStatementsLimit})");
                if (cpuPct > 80) simpleIssues.Add($"🔥 CPU at {cpuPct:F0}% ({lastLimit.CpuTime}/{lastLimit.CpuTimeLimit}ms)");
            }
            
            Issues = simpleIssues.Any() ? new ObservableCollection<string>(simpleIssues) : new ObservableCollection<string> { "✅ No issues found" };
            
            // RECOMMENDATIONS - Show only what matters
            var simpleRecs = new List<string>();
            
            // Slow operations (only if > 2 seconds)
            if (value.MethodStats?.Any() == true)
            {
                var slowMethods = value.MethodStats
                    .Where(m => m.Value.MaxDurationMs > 2000)
                    .OrderByDescending(m => m.Value.MaxDurationMs)
                    .Take(3);
                
                if (slowMethods.Any())
                {
                    simpleRecs.Add("⏱️ SLOW CODE:");
                    foreach (var method in slowMethods)
                    {
                        var name = ExtractClassName(method.Key);
                        simpleRecs.Add($"   {name} takes {method.Value.MaxDurationMs}ms");
                    }
                }
            }
            
            // High frequency methods (called > 100 times)
            if (value.MethodStats?.Any() == true)
            {
                var hotMethods = value.MethodStats
                    .Where(m => m.Value.CallCount > 100)
                    .OrderByDescending(m => m.Value.CallCount)
                    .Take(3);
                
                if (hotMethods.Any())
                {
                    if (simpleRecs.Any()) simpleRecs.Add("");
                    simpleRecs.Add("🔄 CALLED TOO OFTEN:");
                    foreach (var method in hotMethods)
                    {
                        var name = ExtractClassName(method.Key);
                        simpleRecs.Add($"   {name} called {method.Value.CallCount}x");
                    }
                }
            }
            
            // Recursion detection
            if (value.Timeline?.RecursionCount > 0)
            {
                if (simpleRecs.Any()) simpleRecs.Add("");
                simpleRecs.Add($"⚠️ RECURSION: {value.Timeline.RecursionCount} recursive calls detected");
            }
            
            Recommendations = simpleRecs.Any() ? new ObservableCollection<string>(simpleRecs) : new ObservableCollection<string> { "✅ Looks good" };
            DatabaseOperations = new ObservableCollection<DatabaseOperation>(value.DatabaseOperations ?? new List<DatabaseOperation>());
            
            // Stack analysis display
            if (value.StackAnalysis != null)
            {
                StackAnalysisSummary = value.StackAnalysis.Summary ?? "";
                HasStackRisk = value.StackAnalysis.RiskLevel >= StackRiskLevel.Warning;
            }
            else
            {
                StackAnalysisSummary = "";
                HasStackRisk = false;
            }
            
            // ===== POPULATE VISUAL DASHBOARD =====
            
            // Hero section
            HeroEntryPoint = value.EntryPoint ?? "";
            HeroDuration = FormatDuration(value.DurationMs);
            
            if (value.TransactionFailed)
                HeroStatus = "FAILED";
            else if (value.IsTestExecution)
                HeroStatus = "TEST"; // NEW: Show test badge
            else if (value.HandledExceptions?.Count > 0 || value.HasErrors)
                HeroStatus = "WARNING";
            else
                HeroStatus = "SUCCESS";
            
            // Governor limits snapshot — extracted first; authoritative source for all limit consumption data
            var lastSnapshot = value.LimitSnapshots?.LastOrDefault();

            // Stat cards — prefer snapshot values (exact governor limit slots used) over raw op counts
            StatSoqlCount = lastSnapshot?.SoqlQueries ?? value.DatabaseOperations?.Count(d => d.OperationType == "SOQL") ?? 0;
            StatDmlCount  = lastSnapshot?.DmlStatements ?? value.DatabaseOperations?.Count(d => d.OperationType == "DML") ?? 0;
            StatMethodCount = value.MethodStats?.Count ?? 0;
            StatErrorCount = value.Errors?.Count ?? 0;
            StatWarningCount = value.HandledExceptions?.Count ?? 0;

            // Governor limits display
            if (lastSnapshot != null)
            {
                StatSoqlLimit = lastSnapshot.SoqlQueriesLimit;
                StatDmlLimit = lastSnapshot.DmlStatementsLimit;
                StatCpuTime = lastSnapshot.CpuTime;
                StatCpuLimit = lastSnapshot.CpuTimeLimit;
                
                // Calculate percentages for progress bars
                SoqlPercent = lastSnapshot.SoqlQueriesLimit > 0 
                    ? (lastSnapshot.SoqlQueries * 100.0) / lastSnapshot.SoqlQueriesLimit 
                    : 0;
                DmlPercent = lastSnapshot.DmlStatementsLimit > 0 
                    ? (lastSnapshot.DmlStatements * 100.0) / lastSnapshot.DmlStatementsLimit 
                    : 0;
                CpuPercent = lastSnapshot.CpuTimeLimit > 0 
                    ? (lastSnapshot.CpuTime * 100.0) / lastSnapshot.CpuTimeLimit 
                    : 0;
            }
            else
            {
                SoqlPercent = 0;
                DmlPercent = 0;
                CpuPercent = 0;
            }
            
            // Timing breakdown
            WallClockMs = value.WallClockMs > 0 ? value.WallClockMs : value.DurationMs;
            CpuTimeMs = value.CpuTimeMs;
            OverheadMs = Math.Max(0, WallClockMs - CpuTimeMs);
            ShowTimingBreakdown = OverheadMs > 1000 && WallClockMs > 2000;

            // Timeline strip (top-level phases)
            if (value.Timeline?.Phases?.Any() == true)
            {
                var phases = value.Timeline.Phases;
                double total = Math.Max(1d, phases.Sum(p => Math.Max(0d, (double)p.DurationMs)));
                // Fall back to wall clock if phases sum is tiny
                if (total < 1 && WallClockMs > 0) total = WallClockMs;

                var slowest = phases.OrderByDescending(p => p.DurationMs).FirstOrDefault();

                TimelineSegments = new ObservableCollection<TimelineSegment>(
                    phases.Select(p => new TimelineSegment
                    {
                        Name = p.Name,
                        Type = p.Type,
                        DurationMs = Math.Max(0, p.DurationMs),
                        Percent = total > 0 ? (Math.Max(0, p.DurationMs) * 100.0) / total : 0,
                        Icon = p.Icon,
                        IsRecursive = p.IsRecursive,
                        IsSlowest = slowest != null && p.Name == slowest.Name && p.DurationMs == slowest.DurationMs
                    }));

                TimelineTotalDurationMs = total;
                TimelineSummary = value.Timeline.Summary ?? string.Empty;
                TimelineSlowestLabel = slowest != null
                    ? $"{slowest.Icon} {slowest.Type}: {slowest.Name} • {slowest.DurationMs}ms"
                    : string.Empty;
                HasTimelineData = TimelineSegments.Any();
            }
            else
            {
                HasTimelineData = false;
                TimelineSegments.Clear();
                TimelineTotalDurationMs = 0;
                TimelineSummary = string.Empty;
                TimelineSlowestLabel = string.Empty;
            }
            
            // ===== POPULATE HEALTH SCORE & ACTIONABLE ISSUES =====
            if (value.Health != null)
            {
                HasHealthData = true;
                HealthScore = value.Health.Score;
                HealthGrade = value.Health.Grade;
                HealthStatus = value.Health.Status;
                HealthStatusIcon = value.Health.StatusIcon;
                HealthReasoning = value.Health.Reasoning;
                
                CriticalIssues = new ObservableCollection<ActionableIssue>(value.Health.CriticalIssues);
                HighPriorityIssues = new ObservableCollection<ActionableIssue>(value.Health.HighPriorityIssues);
                QuickWins = new ObservableCollection<ActionableIssue>(value.Health.QuickWins);
                TotalEstimatedMinutes = value.Health.TotalEstimatedMinutes;
                
                // "Clean bill of health" when score is good and no serious issues
                IsCleanBillOfHealth = value.Health.Score >= 80 
                    && value.Health.CriticalIssues.Count == 0 
                    && value.Health.HighPriorityIssues.Count == 0;
            }
            else
            {
                HasHealthData = false;
                IsCleanBillOfHealth = false;
                HealthScore = 0;
                HealthGrade = "";
                HealthStatus = "";
                HealthStatusIcon = "";
                HealthReasoning = "";
                CriticalIssues.Clear();
                HighPriorityIssues.Clear();
                QuickWins.Clear();
                TotalEstimatedMinutes = 0;
            }
            
            // ===== ALWAYS-USEFUL: TOP METHODS (even for clean logs) =====
            PopulateTopMethods(value);
            PopulateTopQueries(value);
            PopulateExecutionTree(value);
            PopulateTimelineDetails(value);
            TranslateToPlainEnglish(value); // NEW: Generate plain-English insights

            // ===== PLATFORM EVENTS =====
            PlatformEventPublishes = new ObservableCollection<Models.PlatformEventPublish>(
                value.PlatformEventPublishes ?? new List<Models.PlatformEventPublish>());
            HasPlatformEvents = PlatformEventPublishes.Any();

            // ===== NAMESPACE LIMITS =====
            NamespaceLimitItems = new ObservableCollection<Models.GovernorLimitSnapshot>(
                value.NamespaceLimitSnapshots ?? new List<Models.GovernorLimitSnapshot>());
            HasNamespaceLimits = NamespaceLimitItems.Any();

            // ===== FLOW FAULTS =====
            FaultedFlows = new ObservableCollection<Models.FlowExecution>(
                value.Flows?.Where(f => f.HasFault).ToList() ?? new List<Models.FlowExecution>());
            HasFlowFaults = FaultedFlows.Any();

            // ===== CALLOUTS =====
            Callouts = new ObservableCollection<Models.CalloutOperation>(
                value.Callouts ?? new List<Models.CalloutOperation>());
            HasCallouts = Callouts.Any();

            // ===== LOG CONTEXT FLAGS =====
            IsLogTruncated = value.IsLogTruncated;
            IsAsyncExecution = value.IsAsyncExecution;

            // ===== BULK SAFETY =====
            BulkSafetyGrade = value.BulkSafetyGrade ?? "";
            BulkSafetyReason = value.BulkSafetyReason ?? "";

            // ===== DUPLICATE QUERIES =====
            DuplicateQueries = new ObservableCollection<Models.DuplicateQueryInfo>(
                value.DuplicateQueries ?? new List<Models.DuplicateQueryInfo>());
            HasDuplicateQueries = DuplicateQueries.Any();

            // ===== WORKFLOW RULES =====
            WorkflowRules = new ObservableCollection<Models.WorkflowRuleEvaluation>(
                value.WorkflowRules ?? new List<Models.WorkflowRuleEvaluation>());
            HasWorkflowRules = WorkflowRules.Any();

            // ===== FLOW ERRORS (DML/validation failures inside flows) =====
            FlowErrors = new ObservableCollection<Models.FlowError>(
                value.FlowErrors ?? new List<Models.FlowError>());
            HasFlowErrors = FlowErrors.Any();

            // ===== HEAP & CMDT STATS =====
            TotalHeapAllocated = value.TotalHeapAllocated;
            CustomMetadataQueryCount = value.CustomMetadataQueryCount;
            RegularSoqlCount = value.RegularSoqlCount;

            // ===== TESTING LIMITS =====
            TestingLimits = value.TestingLimits;
            HasTestingLimits = value.TestingLimits != null;
        }
        else
        {
            SummaryText = "";
            FullSummaryText = "";
            Issues.Clear();
            Recommendations.Clear();
            DatabaseOperations.Clear();
            StackAnalysisSummary = "";
            HasStackRisk = false;
            
            // Reset visual dashboard
            HeroStatus = "";
            HeroDuration = "";
            HeroEntryPoint = "";
            StatSoqlCount = 0;
            StatDmlCount = 0;
            StatMethodCount = 0;
            StatErrorCount = 0;
            StatWarningCount = 0;
            SoqlPercent = 0;
            DmlPercent = 0;
            CpuPercent = 0;
            ShowTimingBreakdown = false;
            
            // Reset health score
            HasHealthData = false;
            IsCleanBillOfHealth = false;
            HealthScore = 0;
            HealthGrade = "";
            HealthStatus = "";
            HasTimelineData = false;
            TimelineSegments.Clear();
            TimelineTotalDurationMs = 0;
            TimelineSummary = string.Empty;
            TimelineSlowestLabel = string.Empty;
            HealthStatusIcon = "";
            HealthReasoning = "";
            CriticalIssues.Clear();
            HighPriorityIssues.Clear();
            QuickWins.Clear();
            TotalEstimatedMinutes = 0;
            TopMethods.Clear();
            TopQueries.Clear();
            HasTopMethods = false;
            HasTopQueries = false;
            ExecutionTreeNodes.Clear();
            HasExecutionTree = false;
            TimelineDetails.Clear();
            HasTimelineDetails = false;
            
            // Reset deep explanation
            CurrentExplanation = null;
            HasExplanation = false;
            DetailedIssues.Clear();
            DetailedRecommendations.Clear();
            LearningItems.Clear();
            WhatYourCodeDid.Clear();
            HasDetailedIssues = false;
            HasDetailedRecommendations = false;
            HasLearningItems = false;

            // Reset new collections
            PlatformEventPublishes.Clear();
            HasPlatformEvents = false;
            NamespaceLimitItems.Clear();
            HasNamespaceLimits = false;
            FaultedFlows.Clear();
            HasFlowFaults = false;

            // Reset expanded collections
            Callouts.Clear(); HasCallouts = false;
            IsLogTruncated = false; IsAsyncExecution = false;
            BulkSafetyGrade = ""; BulkSafetyReason = "";
            DuplicateQueries.Clear(); HasDuplicateQueries = false;
            WorkflowRules.Clear(); HasWorkflowRules = false;
            FlowErrors.Clear(); HasFlowErrors = false;
            TotalHeapAllocated = 0; CustomMetadataQueryCount = 0; RegularSoqlCount = 0;
            TestingLimits = null; HasTestingLimits = false;
        }
    }
    
    /// <summary>
    /// Populate top methods by total time (always useful, even for clean logs)
    /// </summary>
    private void PopulateTopMethods(LogAnalysis analysis)
    {
        var methods = new List<MethodDisplayItem>();
        
        // Prefer cumulative profiling (more accurate)
        if (analysis.CumulativeProfiling?.TopMethods?.Any() == true)
        {
            methods = analysis.CumulativeProfiling.TopMethods
                .OrderByDescending(m => m.TotalDurationMs)
                .Take(8)
                .Select(m => new MethodDisplayItem
                {
                    Name = string.IsNullOrEmpty(m.MethodName) ? m.ClassName : $"{m.ClassName}.{m.MethodName}",
                    TotalTimeMs = m.TotalDurationMs,
                    CallCount = m.ExecutionCount,
                    AvgTimeMs = m.AverageDurationMs,
                    Location = m.Location,
                    PercentOfTotal = analysis.DurationMs > 0 ? (m.TotalDurationMs * 100.0) / analysis.DurationMs : 0
                })
                .ToList();
        }
        // Fall back to MethodStats
        else if (analysis.MethodStats?.Any() == true)
        {
            methods = analysis.MethodStats
                .OrderByDescending(m => m.Value.TotalDurationMs)
                .Take(8)
                .Select(m => new MethodDisplayItem
                {
                    Name = ExtractClassName(m.Key),
                    TotalTimeMs = m.Value.TotalDurationMs,
                    CallCount = m.Value.CallCount,
                    AvgTimeMs = m.Value.AverageDurationMs,
                    Location = m.Key,
                    PercentOfTotal = analysis.DurationMs > 0 ? (m.Value.TotalDurationMs * 100.0) / analysis.DurationMs : 0
                })
                .ToList();
        }
        
        TopMethods = new ObservableCollection<MethodDisplayItem>(methods);
        HasTopMethods = methods.Any();
    }
    
    /// <summary>
    /// Populate top queries by execution count and time
    /// </summary>
    private void PopulateTopQueries(LogAnalysis analysis)
    {
        var queries = new List<QueryDisplayItem>();
        
        if (analysis.CumulativeProfiling?.TopQueries?.Any() == true)
        {
            queries = analysis.CumulativeProfiling.TopQueries
                .OrderByDescending(q => q.ExecutionCount)
                .Take(8)
                .Select(q => new QueryDisplayItem
                {
                    Query = q.Query.Length > 120 ? q.Query.Substring(0, 120) + "..." : q.Query,
                    ExecutionCount = q.ExecutionCount,
                    TotalTimeMs = q.TotalDurationMs,
                    Location = q.Location,
                    IsNPlusOne = q.ExecutionCount > 5
                })
                .ToList();
        }
        else if (analysis.DatabaseOperations?.Any(d => d.OperationType == "SOQL") == true)
        {
            // Group by simplified query pattern
            queries = analysis.DatabaseOperations
                .Where(d => d.OperationType == "SOQL")
                .GroupBy(d => SimplifyQueryForDisplay(d.Query))
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => new QueryDisplayItem
                {
                    Query = g.Key.Length > 120 ? g.Key.Substring(0, 120) + "..." : g.Key,
                    ExecutionCount = g.Count(),
                    TotalTimeMs = (int)g.Sum(d => d.DurationMs),
                    Location = $"Line {g.First().LineNumber}",
                    IsNPlusOne = g.Count() > 5
                })
                .ToList();
        }
        
        TopQueries = new ObservableCollection<QueryDisplayItem>(queries);
        HasTopQueries = queries.Any();
    }
    
    /// <summary>
    /// Build execution tree from RootNode for the Tree tab
    /// </summary>
    private void PopulateExecutionTree(LogAnalysis analysis)
    {
        var nodes = new List<TreeNodeViewModel>();
        
        if (analysis.RootNode?.Children?.Any() == true)
        {
            foreach (var child in analysis.RootNode.Children)
            {
                nodes.Add(BuildTreeNode(child, 0));
            }
        }
        
        ExecutionTreeNodes = new ObservableCollection<TreeNodeViewModel>(nodes);
        HasExecutionTree = nodes.Any();
    }
    
    private TreeNodeViewModel BuildTreeNode(ExecutionNode node, int depth)
    {
        var vm = new TreeNodeViewModel
        {
            Name = node.Name,
            TypeName = node.Type.ToString(),
            DurationMs = node.DurationMs,
            Depth = depth,
            IsExpanded = depth < 2, // Auto-expand first 2 levels
            TypeIcon = node.Type switch
            {
                ExecutionNodeType.CodeUnit => "📦",
                ExecutionNodeType.Method => "⚡",
                ExecutionNodeType.SystemMethod => "⚙️",
                ExecutionNodeType.Soql => "💾",
                ExecutionNodeType.Dml => "📝",
                ExecutionNodeType.Exception => node.Severity == ExceptionSeverity.Handled ? "⚠️" : "❌",
                ExecutionNodeType.UserDebug => "🔍",
                ExecutionNodeType.Validation => "✅",
                ExecutionNodeType.Flow => "🌊",
                _ => "▶️"
            },
            HasChildren = node.Children?.Any() == true
        };
        
        if (node.Children?.Any() == true)
        {
            foreach (var child in node.Children)
            {
                // Skip noise: system methods under 1ms with no children
                if (child.Type == ExecutionNodeType.SystemMethod && child.DurationMs < 1 && !(child.Children?.Any() == true))
                    continue;
                vm.Children.Add(BuildTreeNode(child, depth + 1));
            }
        }
        
        return vm;
    }
    
    /// <summary>
    /// Populate detailed timeline for the Timeline tab
    /// </summary>
    private void PopulateTimelineDetails(LogAnalysis analysis)
    {
        var details = new List<TimelineDetailItem>();
        
        if (analysis.Timeline?.Phases?.Any() == true)
        {
            double cumulativeMs = 0;
            foreach (var phase in analysis.Timeline.Phases)
            {
                details.Add(new TimelineDetailItem
                {
                    Name = phase.Name,
                    Type = phase.Type,
                    DurationMs = Math.Max(0, phase.DurationMs),
                    StartOffsetMs = cumulativeMs,
                    Icon = phase.Icon,
                    IsRecursive = phase.IsRecursive,
                    Depth = phase.Depth,
                    OoePhase = phase.OoePhase,
                    PercentOfTotal = analysis.DurationMs > 0 ? (Math.Max(0, phase.DurationMs) * 100.0) / analysis.DurationMs : 0
                });
                cumulativeMs += Math.Max(0, phase.DurationMs);
            }
        }
        
        TimelineDetails = new ObservableCollection<TimelineDetailItem>(details);
        HasTimelineDetails = details.Any();
    }
    
    /// <summary>
    /// Translate technical log analysis into plain English for non-technical users
    /// </summary>
    private void TranslateToPlainEnglish(LogAnalysis analysis)
    {
        // 1. WHAT HAPPENED (Summary)
        if (analysis.IsTestExecution)
        {
            PlainEnglishSummary = $"Salesforce ran a test called \"{analysis.TestClassName}\". ";
            PlainEnglishSummary += analysis.TransactionFailed 
                ? "❌ The test failed." 
                : "✅ The test passed.";
        }
        else if (!string.IsNullOrEmpty(analysis.EntryPoint))
        {
            PlainEnglishSummary = $"A user {analysis.LogUser} triggered \"{analysis.EntryPoint}\". ";
            PlainEnglishSummary += analysis.TransactionFailed 
                ? "❌ The operation failed." 
                : "✅ The operation completed successfully.";
        }
        else
        {
            PlainEnglishSummary = "Salesforce processed a user action. ";
            PlainEnglishSummary += analysis.TransactionFailed 
                ? "❌ Something went wrong." 
                : "✅ Everything worked.";
        }
        
        // 2. WAIT TIME (User Experience)
        double seconds = analysis.DurationMs / 1000.0;
        UserWaitTimeSeconds = seconds < 1 ? seconds.ToString("0.00") : seconds.ToString("0.0");
        
        if (seconds < 0.5)
        {
            UserWaitExplanation = "This was instant! Users didn't notice any delay.";
            SpeedRating = "Fast";
        }
        else if (seconds < 2)
        {
            UserWaitExplanation = $"This took {seconds:0.0} seconds, which is within the acceptable range for most operations.";
            SpeedRating = "Moderate";
        }
        else if (seconds < 5)
        {
            UserWaitExplanation = $"This took {seconds:0.0} seconds, which users will notice. They might think the page is slow.";
            SpeedRating = "Slow";
        }
        else
        {
            UserWaitExplanation = $"This took {seconds:0.0} seconds, which is very slow. Users will complain about performance.";
            SpeedRating = "Very Slow";
        }
        
        // 3. RESOURCE USAGE
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit != null)
        {
            SoqlCount = lastLimit.SoqlQueries;
            SoqlLimit = lastLimit.SoqlQueriesLimit;
            DmlCount = lastLimit.DmlStatements;
            DmlLimit = lastLimit.DmlStatementsLimit;
            CpuPercentValue = lastLimit.CpuTimeLimit > 0 
                ? (int)((lastLimit.CpuTime * 100.0) / lastLimit.CpuTimeLimit) 
                : 0;
            
            ResourceUsageExplanation = $"Salesforce queried the database {SoqlCount} times (limit: {SoqlLimit}) and saved data {DmlCount} times (limit: {DmlLimit}). ";
            ResourceUsageExplanation += $"The code used {CpuPercentValue}% of available processing power.";
        }
        else
        {
            SoqlCount = 0;
            DmlCount = 0;
            CpuPercentValue = 0;
            ResourceUsageExplanation = "No resource usage data available.";
        }
        
        // 4. PROBLEMS FOUND
        var problems = new List<PlainEnglishProblem>();
        
        // Critical errors
        if (analysis.Errors?.Any() == true)
        {
            var error = analysis.Errors.First();
            problems.Add(new PlainEnglishProblem
            {
                Icon = "🔴",
                Title = "The code crashed",
                Description = $"There was an error: \"{error.Name}\". This stopped the entire operation from completing.",
                Severity = "High",
                ImpactLabel = "🔴 Critical - Operation failed"
            });
        }
        
        // Trigger re-entry
        if (analysis.TriggerReEntries?.Any(t => t.HasReEntry) == true)
        {
            var reentry = analysis.TriggerReEntries.First(t => t.HasReEntry);
            problems.Add(new PlainEnglishProblem
            {
                Icon = "🔄",
                Title = "A trigger ran multiple times",
                Description = $"The \"{reentry.TriggerName}\" trigger fired {reentry.TotalFireCount} times instead of once. This means your code is doing extra work unnecessarily, making everything slower.",
                Severity = "High",
                ImpactLabel = "⚠️ High - Wastes time and resources"
            });
        }
        
        // Governor limit warnings
        if (lastLimit != null)
        {
            var soqlPct = lastLimit.SoqlQueriesLimit > 0 ? (lastLimit.SoqlQueries * 100.0) / lastLimit.SoqlQueriesLimit : 0;
            var dmlPct = lastLimit.DmlStatementsLimit > 0 ? (lastLimit.DmlStatements * 100.0) / lastLimit.DmlStatementsLimit : 0;
            var cpuPct = lastLimit.CpuTimeLimit > 0 ? (lastLimit.CpuTime * 100.0) / lastLimit.CpuTimeLimit : 0;
            
            if (soqlPct > 80)
            {
                problems.Add(new PlainEnglishProblem
                {
                    Icon = "💾",
                    Title = "Too many database reads",
                    Description = $"Your code queried the database {lastLimit.SoqlQueries} times, which is {soqlPct:F0}% of the limit ({lastLimit.SoqlQueriesLimit}). If you go over, everything will fail.",
                    Severity = soqlPct > 90 ? "High" : "Medium",
                    ImpactLabel = soqlPct > 90 ? "🔴 Critical - Near limit!" : "⚠️ Warning - Getting close"
                });
            }
            
            if (dmlPct > 80)
            {
                problems.Add(new PlainEnglishProblem
                {
                    Icon = "📝",
                    Title = "Too many database saves",
                    Description = $"Your code saved data {lastLimit.DmlStatements} times, which is {dmlPct:F0}% of the limit ({lastLimit.DmlStatementsLimit}). If you go over, everything will fail.",
                    Severity = dmlPct > 90 ? "High" : "Medium",
                    ImpactLabel = dmlPct > 90 ? "🔴 Critical - Near limit!" : "⚠️ Warning - Getting close"
                });
            }
            
            if (cpuPct > 80)
            {
                problems.Add(new PlainEnglishProblem
                {
                    Icon = "⚡",
                    Title = "Using too much CPU",
                    Description = $"Your code used {cpuPct:F0}% of available processing power. If it goes over 100%, everything will fail.",
                    Severity = cpuPct > 90 ? "High" : "Medium",
                    ImpactLabel = cpuPct > 90 ? "🔴 Critical - Near limit!" : "⚠️ Warning - Getting close"
                });
            }
        }
        
        // Bulk safety grade
        if (!string.IsNullOrEmpty(analysis.BulkSafetyGrade) && (analysis.BulkSafetyGrade == "D" || analysis.BulkSafetyGrade == "F"))
        {
            problems.Add(new PlainEnglishProblem
            {
                Icon = "🚨",
                Title = "Not safe for large data volumes",
                Description = $"This code got a grade of {analysis.BulkSafetyGrade} for handling multiple records. {analysis.BulkSafetyReason}",
                Severity = "High",
                ImpactLabel = "⚠️ High - Will fail with more records"
            });
        }
        
        PlainEnglishProblems = new ObservableCollection<PlainEnglishProblem>(problems);
        HasProblems = problems.Any();
        
        // 5. RECOMMENDATIONS
        var recommendations = new List<PlainEnglishRecommendation>();
        
        // Fix trigger re-entry
        if (analysis.TriggerReEntries?.Any(t => t.HasReEntry) == true)
        {
            var reentry = analysis.TriggerReEntries.First(t => t.HasReEntry);
            recommendations.Add(new PlainEnglishRecommendation
            {
                Title = $"Add recursion control to {reentry.TriggerName}",
                Description = "Use a static variable to prevent the trigger from running more than once per transaction. This will make your code run faster and use fewer resources.",
                EstimatedMinutes = 15
            });
        }
        
        // Optimize slow methods
        if (analysis.MethodStats?.Any(m => m.Value.MaxDurationMs > 2000) == true)
        {
            var slowMethod = analysis.MethodStats.OrderByDescending(m => m.Value.MaxDurationMs).First();
            var methodName = ExtractClassName(slowMethod.Key);
            recommendations.Add(new PlainEnglishRecommendation
            {
                Title = $"Speed up {methodName}",
                Description = $"This method takes {slowMethod.Value.MaxDurationMs}ms to run. Look for database queries inside loops or unnecessary calculations.",
                EstimatedMinutes = 30
            });
        }
        
        // Reduce SOQL queries
        if (lastLimit != null && lastLimit.SoqlQueries > 50)
        {
            recommendations.Add(new PlainEnglishRecommendation
            {
                Title = "Combine database queries",
                Description = $"Your code queried the database {lastLimit.SoqlQueries} times. You can combine some of these queries to use fewer resources and run faster.",
                EstimatedMinutes = 45
            });
        }
        
        PlainEnglishRecommendations = new ObservableCollection<PlainEnglishRecommendation>(recommendations);
        HasRecommendations = recommendations.Any();
        
        // 6. ALL CLEAR?
        IsAllClear = !HasProblems && !analysis.TransactionFailed && (lastLimit?.SoqlQueries ?? 0) < 50;
        
        // 7. DEEP EXPLANATION — rich analogies, before/after code, learning
        try
        {
            var explanation = _explainerService.Explain(analysis);
            CurrentExplanation = explanation;
            HasExplanation = true;
            
            // Populate bindable collections
            WhatYourCodeDid = new ObservableCollection<string>(explanation.WhatYourCodeDid);
            DetailedIssues = new ObservableCollection<DetailedIssue>(explanation.Issues);
            DetailedRecommendations = new ObservableCollection<DetailedRecommendation>(explanation.Recommendations);
            LearningItems = new ObservableCollection<LearningItem>(explanation.WhatYouLearned);
            
            HasDetailedIssues = explanation.Issues.Any();
            HasDetailedRecommendations = explanation.Recommendations.Any();
            HasLearningItems = explanation.WhatYouLearned.Any();
            
            // Override the basic summary with the rich one
            PlainEnglishSummary = explanation.WhatHappened;
        }
        catch
        {
            // Fallback: keep the basic plain English if explainer fails
            HasExplanation = false;
            DetailedIssues.Clear();
            DetailedRecommendations.Clear();
            LearningItems.Clear();
            HasDetailedIssues = false;
            HasDetailedRecommendations = false;
            HasLearningItems = false;
        }
    }
    
    private string SimplifyQueryForDisplay(string query)
    {
        // Remove specific values to find similar queries
        query = System.Text.RegularExpressions.Regex.Replace(query, @"'[^']*'", "'?'");
        query = System.Text.RegularExpressions.Regex.Replace(query, @"\b\d{10,}\b", "?"); // IDs
        return query.Trim();
    }

    private string FormatDuration(double ms)
    {
        if (ms < 1000) return $"{ms:N0}ms";
        if (ms < 60000) return $"{ms / 1000.0:N1}s";
        return $"{ms / 60000.0:N1}m";
    }
    
    /// <summary>
    /// Extract clean class name from method signature
    /// Example: "MyClass.myMethod" -> "MyClass.myMethod"
    /// Example: "Trigger.CaseTrigger on Case (before insert)" -> "CaseTrigger (trigger)"
    /// </summary>
    private string ExtractClassName(string methodName)
    {
        if (methodName.Contains("Trigger."))
        {
            var parts = methodName.Split('.');
            if (parts.Length > 1)
            {
                var triggerName = parts[1].Split(' ')[0];
                return $"{triggerName} (trigger)";
            }
        }
        
        // Return as-is if already clean
        return methodName;
    }
    
    /// <summary>
    /// Truncate string to max length with ellipsis
    /// </summary>
    private string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLength) return str;
        return str.Substring(0, maxLength - 3) + "...";
    }

    [RelayCommand]
    private void SelectLog(LogAnalysis? log)
    {
        if (log != null)
        {
            SelectedLog = log;
            StatusMessage = $"Selected: {log.LogName}";
        }
    }

    [RelayCommand]
    private void SelectTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }
    
    /// <summary>
    /// Open file in VSCode at specific line (from actionable issue location)
    /// </summary>
    [RelayCommand]
    private async Task OpenInEditor(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            StatusMessage = "No location specified";
            return;
        }
        
        if (!_editorBridge.IsConnected)
        {
            StatusMessage = "⚠️ VSCode not connected. Install Black Widow VSCode extension.";
            return;
        }
        
        try
        {
            // Parse location: "MyClass.myMethod:154" or "MyClass:42"
            var parts = location.Split(':');
            if (parts.Length < 2)
            {
                StatusMessage = "Invalid location format";
                return;
            }
            
            var classAndMethod = parts[0];
            var lineNumber = int.Parse(parts[1]);
            
            // Extract class name (remove method if present)
            var className = classAndMethod.Split('.')[0];
            
            // Find file in workspace
            var filePath = _editorBridge.FindApexFile(className);
            
            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = $"⚠️ Could not find {className}.cls in workspace";
                return;
            }
            
            // Send command to VSCode
            var success = await _editorBridge.OpenFileInEditorAsync(filePath, lineNumber);
            
            if (success)
            {
                StatusMessage = $"✓ Opened {Path.GetFileName(filePath)}:{lineNumber} in VSCode";
            }
            else
            {
                StatusMessage = "✗ Failed to open file in VSCode";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Start Editor Bridge server on app startup
    /// </summary>
    private async Task StartEditorBridgeAsync()
    {
        try
        {
            await _editorBridge.StartAsync();
            StatusMessage = "✓ Editor Bridge ready (waiting for VSCode connection)";
            
            // Request workspace path after connection
            await Task.Delay(1000);
            if (_editorBridge.IsConnected)
            {
                await _editorBridge.RequestWorkspacePathAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠️ Editor Bridge failed: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Handle Editor Bridge connection status changes
    /// </summary>
    private void OnEditorConnectionChanged(object? sender, bool isConnected)
    {
        IsEditorConnected = isConnected;
        EditorConnectionStatus = isConnected ? "VSCode: ✓ Connected" : "VSCode: Not Connected";
        
        if (isConnected)
        {
            StatusMessage = "✓ VSCode extension connected";
            _ = _editorBridge.RequestWorkspacePathAsync();
        }
        else
        {
            StatusMessage = "VSCode extension disconnected";
        }
    }
    
    /// <summary>
    /// Handle workspace path received from VSCode
    /// </summary>
    private void OnWorkspacePathReceived(object? sender, string path)
    {
        StatusMessage = $"✓ Workspace: {path}";
    }
    
    [RelayCommand]
    private void ViewInteraction(Interaction interaction)
    {
        if (interaction == null) return;
        
        SelectedInteraction = interaction;
        
        // Show all captured logs in the main list
        Logs.Clear();
        foreach (var log in interaction.CapturedLogs)
        {
            Logs.Add(log);
        }
        
        // Select the first log for detailed view
        if (interaction.CapturedLogs.Count > 0)
        {
            SelectedLog = interaction.CapturedLogs[0];
            StatusMessage = $"Viewing interaction with {interaction.CapturedLogs.Count} log(s)";
        }
        else
        {
            StatusMessage = "⚠️ This interaction has no captured logs";
        }
        
        // Show grouped analysis if available
        if (interaction.LogGroups?.Count > 0)
        {
            LogGroups.Clear();
            foreach (var group in interaction.LogGroups)
            {
                LogGroups.Add(group);
            }
            SelectedLogGroup = interaction.LogGroups[0];
        }
        
        StatusMessage = $"🎬 Viewing interaction: {interaction.Name} ({interaction.CapturedLogs.Count} logs)";
    }

    [RelayCommand]
    private void ConnectToSalesforce()
    {
        StatusMessage = "Opening connection dialog...";

        try
        {
            // Must create and show WPF dialogs on the UI thread
            var connectionDialog = new ConnectionDialog(_oauthService, _apiService);
            var connected = connectionDialog.ShowDialog() == true;

            if (connected)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected to {_apiService.Connection?.InstanceUrl}";
                
                // Extract org name from instance URL
                var instanceUrl = _apiService.Connection?.InstanceUrl ?? "";
                var orgName = "Salesforce";
                
                if (instanceUrl.Contains(".my.salesforce.com"))
                {
                    var start = instanceUrl.IndexOf("//") + 2;
                    var end = instanceUrl.IndexOf(".my.salesforce.com");
                    if (end > start)
                    {
                        orgName = instanceUrl.Substring(start, end - start);
                    }
                }
                else if (instanceUrl.Contains(".sandbox.salesforce.com"))
                {
                    orgName = "Sandbox";
                }
                
                ConnectionDisplayName = orgName;
                StatusMessage = $"✓ Connected to {orgName}";

                // Optionally load recent logs
                // await LoadRecentLogsAsync();
            }
            else
            {
                StatusMessage = "Connection cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task UploadLog()
    {
        StatusMessage = "Opening file selector...";
        
        try
        {
            // Show drag-and-drop dialog (must be on UI thread)
            var filePath = ShowDragDropDialog();
            
            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "Drag a log file onto the window, or paste the path";
                return;
            }
            
            await LoadLogFromPath(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Loads and parses a log file from the given path (called from drag-drop or paste)
    /// </summary>
    public async Task LoadLogFromPath(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"File not found: {filePath}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Validate file extension
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".log" && ext != ".txt")
            {
                MessageBox.Show(
                    $"This file type ({ext}) isn't supported.\n\n" +
                    "Black Widow works with Salesforce debug log files (.log or .txt).\n\n" +
                    "To get a debug log:\n" +
                    "• Salesforce Setup → Debug Logs → click \"Download\"\n" +
                    "• Or use the \"Connect to Salesforce\" button above",
                    "Not a Debug Log", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // ── License gate: Free tier has a 30 MB file size cap ──────────
            if (!_licenseService.IsFeatureAvailable(LicenseFeature.UnlimitedFileSize))
            {
                var fileInfo = new FileInfo(filePath);
                var fileMB   = fileInfo.Length / (1024.0 * 1024.0);
                if (fileMB > LicenseService.FreeTierMaxFileSizeMB)
                {
                    var result = MessageBox.Show(
                        $"This log is {fileMB:F0} MB, which exceeds the {LicenseService.FreeTierMaxFileSizeMB} MB limit for the Free tier.\n\n" +
                        "Upgrade to Pro for unlimited file sizes.\n\n" +
                        "Would you like to upgrade now?",
                        "File Too Large — Upgrade to Pro",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                        ShowUpgradeDialog();

                    StatusMessage = $"⚠️ File too large for Free tier ({fileMB:F0} MB > {LicenseService.FreeTierMaxFileSizeMB} MB)";
                    IsLoading = false;
                    return;
                }
            }
            // ──────────────────────────────────────────────────────────────

            StatusMessage = "Reading log file...";
            IsLoading = true;

            var fileName = Path.GetFileName(filePath);
            
            // Read file on background thread to prevent UI freeze
            var logContent = await Task.Run(() => File.ReadAllText(filePath));

            // Validate this looks like a Salesforce debug log
            if (!LooksLikeSalesforceLog(logContent))
            {
                MessageBox.Show(
                    "This doesn't look like a Salesforce debug log.\n\n" +
                    "Salesforce debug logs typically contain lines like:\n" +
                    "  • EXECUTION_STARTED\n" +
                    "  • CODE_UNIT_STARTED\n" +
                    "  • SOQL_EXECUTE_BEGIN\n\n" +
                    "To get a debug log:\n" +
                    "  1. Go to Setup → Debug Logs\n" +
                    "  2. Click \"Download\" on any log entry\n" +
                    "  3. Drop that file here",
                    "Not a Salesforce Debug Log", MessageBoxButton.OK, MessageBoxImage.Information);
                IsLoading = false;
                return;
            }

            StatusMessage = "Parsing log...";
            var analysis = await Task.Run(() => _parserService.ParseLog(logContent, fileName));
            
            // Enrich with org metadata (user names, trigger locations, etc.)
            await EnrichLogWithMetadataAsync(analysis);
            
            Logs.Insert(0, analysis);
            SelectedLog = analysis;

            StatusMessage = $"✓ Log parsed successfully - {analysis.Summary}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load log: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadLogFolder()
    {
        StatusMessage = "Select folder containing debug logs...";

        try
        {
            // Use FolderBrowserDialog for WPF compatibility
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Folder with Debug Logs",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                StatusMessage = "Scanning folder for logs...";
                IsLoading = true;

                var folderPath = folderDialog.SelectedPath;

                // Extract metadata from all logs quickly
                var metadata = await Task.Run(() => _metadataExtractor.ExtractMetadataFromDirectory(folderPath));

                if (metadata.Count == 0)
                {
                    StatusMessage = "No log files found in folder";
                    return;
                }

                StatusMessage = $"Found {metadata.Count} logs, grouping by transaction...";

                // Group related logs
                var groups = await Task.Run(() => _groupService.GroupRelatedLogs(metadata));

                LogGroups.Clear();
                foreach (var group in groups)
                {
                    LogGroups.Add(group);
                }

                StatusMessage = $"✓ Loaded {metadata.Count} logs grouped into {groups.Count} transaction(s)";

                // Auto-select first group
                if (LogGroups.Any())
                {
                    SelectedLogGroup = LogGroups.First();
                }
            }
            else
            {
                StatusMessage = "Folder selection cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadRecentLogsAsync()
    {
        if (!_apiService.IsConnected)
        {
            StatusMessage = "Not connected to Salesforce";
            return;
        }

        StatusMessage = "Loading logs from Salesforce...";
        IsLoading = true;

        try
        {
            var apexLogs = await _apiService.QueryLogsAsync(20);
            StatusMessage = $"Found {apexLogs.Count} logs";

            // You could auto-load and parse these, but for now just notify
            StatusMessage = $"✓ Found {apexLogs.Count} logs (click to download and analyze)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        try
        {
            var dialog = new SettingsDialog();
            if (dialog.ShowDialog() == true)
            {
                StatusMessage = "✓ Settings saved successfully";
                // Reload settings if needed
                var settings = _settingsService.Load();
                // Apply settings to app (theme, etc.)
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportReport()
    {
        if (SelectedLog == null)
        {
            StatusMessage = "⚠️ Please select a log to export";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Analysis Report",
                FileName = $"BlackWidow_Report_{DateTime.Now:yyyy-MM-dd_HHmmss}",
                DefaultExt = ".pdf",
                Filter = "PDF Report (*.pdf)|*.pdf|JSON Export (*.json)|*.json|Text File (*.txt)|*.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Generating report...";

                await Task.Run(() =>
                {
                    var extension = System.IO.Path.GetExtension(dialog.FileName).ToLower();

                    switch (extension)
                    {
                        case ".pdf":
                            _exportService.ExportToPdf(SelectedLog, dialog.FileName);
                            break;
                        case ".json":
                            _exportService.ExportToJson(SelectedLog, dialog.FileName);
                            break;
                        case ".txt":
                            var textContent = _exportService.GetRecommendationsText(SelectedLog);
                            System.IO.File.WriteAllText(dialog.FileName, textContent);
                            break;
                    }
                });

                StatusMessage = $"✓ Report exported: {System.IO.Path.GetFileName(dialog.FileName)}";

                // Open file location
                var result = MessageBox.Show(
                    $"Report exported successfully!\n\nWould you like to open the file?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to export report:\n\n{ex.Message}\n\nPlease try again or contact support if the problem persists.",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            StatusMessage = "Export failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CopyRecommendations()
    {
        if (SelectedLog == null)
        {
            StatusMessage = "⚠️ Please select a log first";
            return;
        }

        try
        {
            var text = _exportService.GetRecommendationsText(SelectedLog);
            Clipboard.SetText(text);
            StatusMessage = "✓ Recommendations copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error copying to clipboard: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _apiService.Disconnect();
        IsConnected = false;
        ConnectionStatus = "Not connected";
        ConnectionDisplayName = "Not Connected";
        StatusMessage = "🔌 Disconnected from Salesforce";
    }

    [RelayCommand]
    private void ManageDebugLogs()
    {
        if (!_apiService.IsConnected)
        {
            StatusMessage = "⚠️ Please connect to Salesforce first";
            return;
        }

        try
        {
            // Open trace flag dialog to view/manage logs
            StatusMessage = "Opening logs view...";
            
            // Must create and show WPF dialogs on the UI thread
            var dialog = new TraceFlagDialog(_apiService, _parserService);
            var success = dialog.ShowDialog() == true;
            
            if (success && dialog.DownloadedLogAnalysis != null)
            {
                // Add the downloaded log to the list
                Logs.Insert(0, dialog.DownloadedLogAnalysis);
                SelectedLog = dialog.DownloadedLogAnalysis;
                StatusMessage = "✓ Log downloaded and analyzed";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleStreaming()
    {
        if (IsStreaming)
        {
            StopStreaming();
            return;
        }

        // Pro-gated feature
        if (RequiresPro(LicenseFeature.LiveStreaming)) return;

        await StartStreamingAsync();
    }
    
    [RelayCommand]
    private void StartRecording()
    {
        if (!IsStreaming)
        {
            StatusMessage = "⚠️ Start streaming first before recording an interaction";
            return;
        }
        
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        _recordingBuffer.Clear();
        RecordingElapsedTime = "00:00";
        
        StatusMessage = "🔴 RECORDING - Perform your Salesforce action now...";
        
        // Start timer to update elapsed time
        _recordingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _recordingTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - RecordingStartTime;
            RecordingElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        };
        _recordingTimer.Start();
        
        StatusMessage = "🔴 Recording interaction... Perform your action in Salesforce";
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = "🔴 Recording started - Perform your action in Salesforce, then click Stop Recording",
            IsError = false
        });
    }
    
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (!IsRecording) return;
        
        _recordingTimer?.Stop();
        _recordingTimer = null;
        IsRecording = false;
        
        var endTime = DateTime.Now;
        var interaction = new Interaction
        {
            StartTime = RecordingStartTime,
            EndTime = endTime,
            CapturedLogs = new List<LogAnalysis>(_recordingBuffer)
        };
        
        // Group the captured logs into transactions
        if (_recordingBuffer.Count > 1)
        {
            try
            {
                // Use consistent UserId for all logs so they can be grouped together
                // The grouping algorithm matches by UserId + time window
                var streamingUserId = StreamingUsername?.GetHashCode().ToString("X8") ?? "UNKNOWN";
                
                var metadata = _recordingBuffer.Select(log => new DebugLogMetadata
                {
                    LogId = log.LogId,
                    Timestamp = log.ParsedAt,
                    DurationMs = log.DurationMs,
                    UserName = StreamingUsername ?? "Unknown",
                    UserId = streamingUserId, // All logs get same UserId for grouping
                    MethodName = log.EntryPoint,
                    HasErrors = log.HasErrors,
                    SoqlQueries = log.LimitSnapshots.FirstOrDefault()?.SoqlQueries ?? 0,
                    DmlStatements = log.LimitSnapshots.FirstOrDefault()?.DmlStatements ?? 0,
                    CpuTime = log.CpuTimeMs
                }).ToList();
                
                interaction.LogGroups = await Task.Run(() => _groupService.GroupRelatedLogs(metadata));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to group interaction logs: {ex.Message}");
            }
        }
        
        // Add to interactions list
        Interactions.Insert(0, interaction);
        SelectedInteraction = interaction;
        
        _recordingBuffer.Clear();
        
        StatusMessage = $"✅ Recording stopped - Captured {interaction.CapturedLogs.Count} logs";
        
        var durationStr = interaction.UserWaitTimeMs < 1000 
            ? $"{interaction.UserWaitTimeMs:N0}ms" 
            : $"{interaction.UserWaitTimeMs / 1000.0:N1}s";
        
        StatusMessage = $"✅ Interaction recorded: {interaction.CapturedLogs.Count} logs, {durationStr} total wait time";
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = $"✅ Recording stopped - Captured {interaction.CapturedLogs.Count} logs ({durationStr})",
            IsError = false
        });
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cliService.StopStreaming();
        IsStreaming = false;
        StreamingUsername = null;
        
        StreamingLogs.Insert(0, new StreamingLogEntry
        {
            Timestamp = DateTime.Now,
            Message = "⏸️ Streaming stopped",
            IsError = false
        });
        
        StatusMessage = "Streaming stopped";
    }

    private async Task StartStreamingAsync()
    {
        if (!_apiService.IsConnected || _apiService.Connection == null)
        {
            StatusMessage = "⚠️ Please connect to Salesforce first";
            return;
        }

        // Use the connected org's username as default
        var defaultUsername = _apiService.Connection.Username;
        if (string.IsNullOrEmpty(defaultUsername))
        {
            StatusMessage = "⚠️ Unable to determine username for streaming";
            return;
        }

        // Show options dialog to configure filters (prevents "fire hose" problem)
        var dialog = new StreamingOptionsDialog(defaultUsername);
        var result = dialog.ShowDialog();
        
        if (result != true)
        {
            StatusMessage = "Streaming cancelled";
            return; // User cancelled
        }
        
        // Store options for filtering in OnLogReceived
        _streamingOptions = dialog.Options;
        
        // Determine which username to use for API queries
        var username = _streamingOptions.FilterByUser && !string.IsNullOrEmpty(_streamingOptions.Username)
            ? _streamingOptions.Username
            : defaultUsername;

        StreamingUsername = username;
        StatusMessage = $"🕷️ Starting log stream for {username}...";
        StreamingStatus = "Connecting...";

        // Pass the API service to CLI service (no CLI needed anymore!)
        var success = await _cliService.StartStreamingAsync(_apiService, username);
        
        if (success)
        {
            IsStreaming = true;
            
            // Show active filters in status
            var filterInfo = _streamingOptions.FilterByUser ? $" (user: {_streamingOptions.Username})" : "";
            StreamingStatus = $"🔴 LIVE - {username}{filterInfo}";
            StatusMessage = $"🕷️ Black Widow is watching... Waiting for logs from {username}";
        }
        else
        {
            StreamingStatus = "Failed to start";
        }
    }

    private void OnCliStatusChanged(object? sender, string status)
    {
        // Marshal to UI thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            // Add to streaming log
            StreamingLogs.Insert(0, new StreamingLogEntry
            {
                Timestamp = DateTime.Now,
                Message = status,
                IsError = status.Contains("⚠️") || status.Contains("Failed")
            });

            // Keep only last 100 entries
            while (StreamingLogs.Count > 100)
            {
                StreamingLogs.RemoveAt(StreamingLogs.Count - 1);
            }
        });
    }

    /// <summary>
    /// Generate a plain English insight from log analysis for non-experts
    /// </summary>
    private (string insight, string icon) GenerateLogInsight(LogAnalysis? analysis)
    {
        if (analysis == null) return ("No analysis available", "❓");
        
        // Priority 1: Errors (most important)
        if (analysis.Errors?.Count > 0)
        {
            var firstError = analysis.Errors[0].Name;
            return (firstError, "❌");
        }
        
        // Priority 2: Issues detected
        if (analysis.Issues?.Count > 0)
        {
            var firstIssue = analysis.Issues[0];
            
            // Simplify technical jargon
            if (firstIssue.Contains("SOQL", StringComparison.OrdinalIgnoreCase))
                return ("⚠️ Database query issue detected", "⚠️");
            if (firstIssue.Contains("DML", StringComparison.OrdinalIgnoreCase))
                return ("⚠️ Database update issue detected", "⚠️");
            if (firstIssue.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                return ("⚠️ Slow processing detected", "⚠️");
            if (firstIssue.Contains("recursion", StringComparison.OrdinalIgnoreCase))
                return ("⚠️ Code ran multiple times (recursion)", "⚠️");
            
            return (firstIssue, "⚠️");
        }
        
        // Priority 3: Recommendations (potential improvements)
        if (analysis.Recommendations?.Count > 0)
        {
            var firstRec = analysis.Recommendations[0];
            return ($"💡 {firstRec}", "💡");
        }
        
        // Priority 4: Show database activity if significant
        var lastSnapshot = analysis.LimitSnapshots?.LastOrDefault();
        if (lastSnapshot != null)
        {
            if (lastSnapshot.SoqlQueries > 50)
                return ($"Ran {lastSnapshot.SoqlQueries} database queries", "📊");
            if (lastSnapshot.DmlStatements > 50)
                return ($"Modified {lastSnapshot.DmlStatements} records", "📊");
            if (analysis.CpuTimeMs > 5000)
                return ($"Heavy processing: {analysis.CpuTimeMs}ms CPU time", "🔥");
        }
        
        // Priority 5: All good!
        if (analysis.DurationMs < 1000)
            return ("Executed successfully, no issues", "✓");
        else if (analysis.DurationMs < 5000)
            return ($"Completed in {analysis.DurationMs}ms", "✓");
        else
            return ($"⏱️ Slow execution: {analysis.DurationMs}ms", "⏱️");
    }
    
    private void OnLogReceived(object? sender, LogReceivedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            try
            {
                StatusMessage = "🕷️ New log detected! Parsing...";

                // Parse the log content
                var logId = !string.IsNullOrEmpty(e.LogId) ? e.LogId.Substring(0, 8) : $"Stream_{e.Timestamp:HHmmss}";
                
                // PERFORMANCE: Skip slow logs if user opted out (prevents UI freeze)
                if (_streamingOptions?.SkipSlowLogs == true)
                {
                    var lineCount = e.LogContent.Split('\n').Length;
                    if (lineCount > 50000) // ~10 seconds parse time
                    {
                        StatusMessage = $"⏭️ Skipped very large log ({lineCount:N0} lines) - enable in streaming options to parse";
                        return;
                    }
                }
                
                var analysis = await Task.Run(() => _parserService.ParseLog(e.LogContent, logId));
                
                // Enrich with org metadata
                await EnrichLogWithMetadataAsync(analysis);
                
                // FILTER: Only errors (if enabled)
                if (_streamingOptions?.OnlyErrors == true)
                {
                    if (analysis.Errors?.Count == 0 && !analysis.TransactionFailed)
                    {
                        return; // Skip successful logs
                    }
                }
                
                // FILTER: User filter (if specified)
                if (_streamingOptions?.FilterByUser == true && !string.IsNullOrEmpty(_streamingOptions.Username))
                {
                    // Case-insensitive comparison
                    if (!e.Username.Equals(_streamingOptions.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Skip logs from other users
                    }
                }
                
                // Extract operation type from entry point or root node
                var operationType = "Unknown";
                if (!string.IsNullOrEmpty(analysis.EntryPoint))
                {
                    operationType = analysis.EntryPoint;
                }
                else if (analysis.RootNode != null)
                {
                    operationType = analysis.RootNode.Type switch
                    {
                        ExecutionNodeType.CodeUnit => analysis.RootNode.Name,
                        ExecutionNodeType.Flow => "Flow Execution",
                        ExecutionNodeType.Validation => "Validation Rule",
                        ExecutionNodeType.Method => "Apex Method",
                        _ => analysis.RootNode.Type.ToString()
                    };
                    
                    // Check if it's Execute Anonymous
                    if (analysis.Summary?.Contains("Execute Anonymous", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        operationType = "Execute Anonymous";
                    }
                }
                
                // FILTER: Operation type filters
                if (_streamingOptions != null)
                {
                    var shouldSkip = false;
                    
                    // Check against operation type filters
                    if (!_streamingOptions.IncludeApex && 
                        (operationType.Contains("Trigger") || operationType.Contains("Class") || 
                         operationType.Contains("Execute Anonymous") || analysis.RootNode?.Type == ExecutionNodeType.CodeUnit))
                    {
                        shouldSkip = true;
                    }
                    
                    if (!_streamingOptions.IncludeFlows && 
                        (operationType.Contains("Flow") || analysis.RootNode?.Type == ExecutionNodeType.Flow))
                    {
                        shouldSkip = true;
                    }
                    
                    if (!_streamingOptions.IncludeValidation && 
                        (operationType.Contains("Validation") || analysis.RootNode?.Type == ExecutionNodeType.Validation))
                    {
                        shouldSkip = true;
                    }
                    
                    if (!_streamingOptions.IncludeLightning && 
                        (operationType.Contains("Aura") || operationType.Contains("LWC") || operationType.Contains("Lightning")))
                    {
                        shouldSkip = true;
                    }
                    
                    if (!_streamingOptions.IncludeApi && 
                        (operationType.Contains("API") || operationType.Contains("REST") || operationType.Contains("SOAP")))
                    {
                        shouldSkip = true;
                    }
                    
                    if (!_streamingOptions.IncludeBatch && 
                        (operationType.Contains("Batch") || operationType.Contains("Scheduled") || operationType.Contains("Queueable")))
                    {
                        shouldSkip = true;
                    }
                    
                    if (shouldSkip)
                    {
                        StatusMessage = $"⏭️ Filtered out: {operationType}";
                        return; // Skip this log based on operation type filter
                    }
                }
                
                // Add to logs list
                Logs.Insert(0, analysis);
                
                // Only auto-switch if user enabled it (prevents crash when viewing different log)
                if (_streamingOptions?.AutoSwitchToNewest == true)
                {
                    SelectedLog = analysis;
                }
                
                // Generate plain English insight for non-experts
                var (insight, icon) = GenerateLogInsight(analysis);
                
                // Create rich streaming log entry
                var streamingEntry = new StreamingLogEntry
                {
                    Timestamp = e.Timestamp,
                    IsError = analysis.Errors?.Count > 0 || analysis.TransactionFailed,
                    Analysis = analysis,
                    LogId = logId,
                    User = e.Username,
                    OperationType = operationType,
                    Duration = analysis.DurationMs > 0 ? $"{analysis.DurationMs}ms" : "0ms",
                    LineCount = e.LogContent.Split('\n').Length,
                    Status = (analysis.Errors?.Count > 0 || analysis.TransactionFailed) ? "ERROR" : "SUCCESS",
                    Message = $"{operationType} by {e.Username}",
                    Insight = insight,
                    InsightIcon = icon
                };
                
                // If recording, add to buffer
                if (IsRecording)
                {
                    _recordingBuffer.Add(analysis);
                    streamingEntry.Message = $"🔴 [Recording #{_recordingBuffer.Count}] {operationType}";
                }
                
                // Queue to buffer instead of inserting directly (prevents UI lag from rapid updates)
                lock (_streamingLock)
                {
                    _streamingBuffer.Enqueue(streamingEntry);
                }
                
                // Start timer if not already running
                if (_streamingThrottleTimer != null && !_streamingThrottleTimer.IsEnabled)
                {
                    _streamingThrottleTimer.Start();
                }

                StatusMessage = IsRecording 
                    ? $"🔴 Recording... Log #{_recordingBuffer.Count} captured" 
                    : $"🕷️ Caught a log! {analysis.Summary}";
            }
            catch (Exception ex)
            {
                // Queue error entry to buffer
                lock (_streamingLock)
                {
                    _streamingBuffer.Enqueue(new StreamingLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"❌ Failed to parse log: {ex.Message}",
                        IsError = true
                    });
                }
            }
        });
    }
    
    /// <summary>
    /// Timer tick handler that processes queued streaming logs in batches.
    /// This prevents UI lag when many logs arrive rapidly (batches updates every 500ms).
    /// </summary>
    private void OnStreamingThrottleTick(object? sender, EventArgs e)
    {
        var logsToAdd = new List<StreamingLogEntry>();
        
        lock (_streamingLock)
        {
            // Process up to 10 logs per tick (500ms)
            var batchSize = Math.Min(10, _streamingBuffer.Count);
            for (int i = 0; i < batchSize; i++)
            {
                if (_streamingBuffer.Count > 0)
                {
                    logsToAdd.Add(_streamingBuffer.Dequeue());
                }
            }
            
            // Stop timer if buffer is empty
            if (_streamingBuffer.Count == 0)
            {
                _streamingThrottleTimer?.Stop();
            }
        }
        
        // Add to UI on dispatcher thread
        foreach (var entry in logsToAdd)
        {
            StreamingLogs.Insert(0, entry);
            
            // Maintain 100-log limit (FIFO removal)
            while (StreamingLogs.Count > 100)
            {
                StreamingLogs.RemoveAt(StreamingLogs.Count - 1);
            }
        }
    }
    
    /// <summary>
    /// Quick validation: does this file content look like a Salesforce debug log?
    /// Checks the first 50 lines for well-known Salesforce log markers.
    /// </summary>
    private static bool LooksLikeSalesforceLog(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        
        // Check first ~4000 chars (first 50 lines or so)
        var snippet = content.Length > 4000 ? content[..4000] : content;
        
        // Salesforce debug logs always contain at least one of these markers
        string[] markers = [
            "EXECUTION_STARTED",
            "CODE_UNIT_STARTED",
            "APEX_CODE",
            "SOQL_EXECUTE_BEGIN",
            "USER_INFO",
            "SYSTEM_METHOD_ENTRY",
            "DML_BEGIN",
            "VALIDATION_RULE",
            "FLOW_START",
            "LIMIT_USAGE_FOR_NS"
        ];
        
        return markers.Any(marker => snippet.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Enriches log analysis with org metadata to make it human-readable.
    /// Replaces cryptic IDs and API names with actual labels, user names, and context.
    /// </summary>
    private async Task EnrichLogWithMetadataAsync(LogAnalysis log)
    {
        if (!IsConnected) return; // Need SF connection for metadata
        
        try
        {
            // Fetch org metadata (cached, so fast after first call)
            var metadata = await _metadataService.GetOrgMetadataAsync();
            
            // Enrich entry point (e.g., "CaseTrigger" → "CaseTrigger.apxt on Case")
            if (!string.IsNullOrEmpty(log.EntryPoint))
            {
                log.EntryPoint = _metadataService.EnrichEntryPoint(log.EntryPoint, metadata);
            }
            
            // Enrich user (e.g., "005xx" → "John Smith (john@example.com)")
            if (!string.IsNullOrEmpty(log.LogUser))
            {
                log.LogUser = _metadataService.EnrichUserId(log.LogUser, metadata);
            }
            
            // Enrich execution tree nodes (add context to triggers, classes, methods)
            if (log.RootNode?.Children != null && log.RootNode.Children.Count > 0)
            {
                await EnrichExecutionTreeAsync(log.RootNode.Children, metadata);
            }
        }
        catch (Exception ex)
        {
            // Silent fail - enrichment is nice-to-have, not critical
            Console.WriteLine($"Failed to enrich log with metadata: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Recursively enriches execution tree nodes with metadata
    /// </summary>
    private async Task EnrichExecutionTreeAsync(List<ExecutionNode> nodes, OrgMetadata metadata)
    {
        foreach (var node in nodes)
        {
            // Enrich trigger names
            if (node.Type == ExecutionNodeType.CodeUnit && node.Name.Contains("Trigger", StringComparison.OrdinalIgnoreCase))
            {
                var triggerName = node.Name.Split(' ').FirstOrDefault();
                if (triggerName != null && metadata.ApexTriggers.TryGetValue(triggerName, out var trigger))
                {
                    node.Name = $"{triggerName}.apxt on {trigger.ObjectName}";
                }
            }
            
            // Enrich class/method names
            if (node.Type == ExecutionNodeType.Method && node.Name.Contains('.'))
            {
                var className = node.Name.Split('.').FirstOrDefault();
                if (className != null && metadata.ApexClasses.TryGetValue(className, out var cls))
                {
                    node.Name = $"{cls.FullName}.{node.Name.Split('.').LastOrDefault()}";
                }
            }
            
            // Recursively enrich children
            if (node.Children?.Count > 0)
            {
                await EnrichExecutionTreeAsync(node.Children, metadata);
            }
        }
    }
    
    /// <summary>
    /// Shows a simple dialog to paste a file path
    /// </summary>
    private string? ShowDragDropDialog()
    {
        string? resultPath = null;
        
        var dialog = new Window
        {
            Title = "Open Debug Log",
            Width = 550,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 49, 54))
        };
        
        var mainStack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        
        // Instructions
        var label = new System.Windows.Controls.TextBlock
        {
            Text = "Paste log file path (right-click file → Copy as path):",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        
        // Path input row
        var inputRow = new System.Windows.Controls.Grid();
        inputRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        
        var textBox = new System.Windows.Controls.TextBox
        {
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 68, 75)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        System.Windows.Controls.Grid.SetColumn(textBox, 0);
        
        var openButton = new System.Windows.Controls.Button
        {
            Content = "Open",
            Width = 80,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 101, 242)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        System.Windows.Controls.Grid.SetColumn(openButton, 1);
        
        openButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                resultPath = textBox.Text.Trim().Trim('"');
                dialog.DialogResult = true;
                dialog.Close();
            }
        };
        
        // Allow Enter key to submit
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                resultPath = textBox.Text.Trim().Trim('"');
                dialog.DialogResult = true;
                dialog.Close();
            }
        };
        
        inputRow.Children.Add(textBox);
        inputRow.Children.Add(openButton);
        
        mainStack.Children.Add(label);
        mainStack.Children.Add(inputRow);
        
        dialog.Content = mainStack;
        
        // Focus the textbox after dialog loads
        dialog.Loaded += (s, e) => textBox.Focus();
        
        dialog.ShowDialog();
        return resultPath;
    }

    // ===== SALESFORCE CONNECTION MANAGEMENT =====
    
    [ObservableProperty]
    private string _connectionDisplayName = "Not Connected";
    
    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "⚠️ Connect to Salesforce first";
            return;
        }
        
        try
        {
            IsLoading = true;
            StatusMessage = "🔄 Refreshing org metadata...";
            
            // Force refresh by clearing cache
            var metadata = await _metadataService.GetOrgMetadataAsync(forceRefresh: true);
            
            if (metadata != null)
            {
                // Re-enrich all currently loaded logs with fresh metadata
                var enrichmentTasks = Logs.Select(log => EnrichLogWithMetadataAsync(log));
                await Task.WhenAll(enrichmentTasks);
                
                StatusMessage = $"✓ Metadata refreshed - {metadata.Users.Count} users, {metadata.Objects.Count} objects, " +
                               $"{metadata.ApexClasses.Count} classes, {metadata.ApexTriggers.Count} triggers";
            }
            else
            {
                StatusMessage = "⚠️ Failed to refresh metadata";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error refreshing metadata: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private void ManageConnections()
    {
        // TODO: Open ConnectionDialog to manage connections
        StatusMessage = "⚙️ Connection management coming soon...";
        
        // For now, just open the connection dialog
        var dialog = new ConnectionDialog(_oauthService, _apiService)
        {
            Owner = Application.Current.MainWindow
        };
        
        dialog.ShowDialog();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _editorBridge.Dispose(); } catch { }
        try { _apiService.Dispose(); } catch { }
    }
}

public class TimelineSegment
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public double Percent { get; set; }
    public string Icon { get; set; } = string.Empty;
    public bool IsRecursive { get; set; }
    public bool IsSlowest { get; set; }
}

/// <summary>
/// Display item for "Top Methods" always-useful section
/// </summary>
public class MethodDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public long TotalTimeMs { get; set; }
    public int CallCount { get; set; }
    public long AvgTimeMs { get; set; }
    public string Location { get; set; } = string.Empty;
    public double PercentOfTotal { get; set; }
    
    public string TimeDisplay => TotalTimeMs >= 1000 ? $"{TotalTimeMs / 1000.0:N1}s" : $"{TotalTimeMs}ms";
    public string AvgDisplay => AvgTimeMs >= 1000 ? $"{AvgTimeMs / 1000.0:N1}s" : $"{AvgTimeMs}ms";
    public string BarWidth => $"{Math.Max(5, Math.Min(100, PercentOfTotal))}";
}

/// <summary>
/// Display item for "Top Queries" always-useful section
/// </summary>
public class QueryDisplayItem
{
    public string Query { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public int TotalTimeMs { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsNPlusOne { get; set; }
    
    public string TimeDisplay => TotalTimeMs >= 1000 ? $"{TotalTimeMs / 1000.0:N1}s" : $"{TotalTimeMs}ms";
    public string CountBadge => ExecutionCount > 1 ? $"{ExecutionCount}x" : "1x";
    public string NPlusOneBadge => IsNPlusOne ? "⚠️ N+1" : "";
}

/// <summary>
/// ViewModel node for the execution tree display
/// </summary>
public class TreeNodeViewModel
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string TypeIcon { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int Depth { get; set; }
    public bool IsExpanded { get; set; }
    public bool HasChildren { get; set; }
    public ObservableCollection<TreeNodeViewModel> Children { get; set; } = new();
    
    public string DurationDisplay => DurationMs >= 1000 ? $"{DurationMs / 1000.0:N1}s" : $"{DurationMs}ms";
    public Thickness IndentMargin => new Thickness(Depth * 24, 0, 0, 0);
    public string ExpanderIcon => HasChildren ? (IsExpanded ? "▾" : "▸") : "  ";
}

/// <summary>
/// Display item for the detailed timeline tab
/// </summary>
public class TimelineDetailItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public double StartOffsetMs { get; set; }
    public string Icon { get; set; } = string.Empty;
    public bool IsRecursive { get; set; }
    public int Depth { get; set; }
    public double PercentOfTotal { get; set; }

    /// <summary>Salesforce OOE phase label — empty when not determinable.</summary>
    public Models.OoePhase? OoePhase { get; set; }

    /// <summary>Short label shown as a badge in the timeline UI. Empty when OoePhase is null or Other.</summary>
    public string OoePhaseLabel => OoePhase switch
    {
        Models.OoePhase.BeforeTrigger    => "Before Trigger",
        Models.OoePhase.SystemValidation => "Validation",
        Models.OoePhase.AfterTrigger     => "After Trigger",
        Models.OoePhase.Workflow         => "Workflow",
        Models.OoePhase.AfterSaveFlow    => "After-Save Flow",
        Models.OoePhase.Process          => "Process",
        Models.OoePhase.FieldUpdate      => "Field Update",
        Models.OoePhase.ReEntry          => "Re-Entry ⚠",
        Models.OoePhase.Async            => "Async",
        _                                => string.Empty
    };

    public bool HasOoePhaseLabel => !string.IsNullOrEmpty(OoePhaseLabel);
    
    public string DurationDisplay => DurationMs >= 1000 ? $"{DurationMs / 1000.0:N1}s" : $"{DurationMs}ms";
    public string PercentDisplay => $"{PercentOfTotal:N1}%";
    public Thickness IndentMargin => new Thickness(Depth * 16, 0, 0, 0);
    
    public string TypeColor => Type switch
    {
        "Trigger" => "#8B5CF6",
        "Flow" => "#22D3EE",
        "Validation" => "#F59E0B",
        "DML" => "#F97316",
        "Async" => "#10B981",
        _ => "#5865F2"
    };
}

/// <summary>
/// Entry in the streaming log panel
/// </summary>
public class StreamingLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
    public bool IsError { get; set; }
    public LogAnalysis? Analysis { get; set; }
    
    // Enhanced details for better UX
    public string LogId { get; set; } = "";
    public string User { get; set; } = "";
    public string OperationType { get; set; } = "";
    public string Duration { get; set; } = "";
    public int LineCount { get; set; }
    public string Status { get; set; } = "";
    public string Insight { get; set; } = ""; // Plain English summary for non-experts
    public string InsightIcon { get; set; } = "✓"; // Icon representing the insight type
}
