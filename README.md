# Black Widow 🕷️ — Salesforce Debug Log Analyzer

> **The only debug log tool that explains logs like a patient mentor, not a technical manual.**

A Windows desktop application that **translates** complex Salesforce debug logs into plain English — from single-file analysis to full transaction chain investigation with governance insights.

---

## What Makes This Different?

Traditional debug log tools dump raw technical data at you: timestamps, event types, stack traces, and governor limits with zero context.

**Black Widow explains what actually happened:**

| Traditional Tool | Black Widow |
|---|---|
| `SOQL queries: 87/100 (87%) — Consider bulkification` | *"You're asking the database for information 87 separate times. Think of it like making 87 phone calls instead of one call with a list. Here's how to combine them."* |
| `Execution completed in 2500ms` | *"✅ This transaction completed successfully in 2.5 seconds — plenty of room before Salesforce's limit."* |
| `EXCEPTION_THROWN: System.NullPointerException` | *"Your code tried to read a field on a record that doesn't exist yet. It's like asking for the price tag on an item that hasn't been put on the shelf."* |

**No Salesforce expertise required** — understand your logs on day one.

---

## Key Features

### 🔗 Transaction Chain Analysis
One user action (e.g. saving a Case) can generate 10–15 logs from triggers, flows, and component reloads. Black Widow groups them automatically and shows you the full picture:

- **Automatic Log Grouping** — groups related logs by user and timing window
- **Phase Detection** — separates Backend (triggers/flows) from Frontend (Lightning components)
- **Re-entry Detection** — identifies when triggers fire multiple times in one transaction
- **Sequential vs Parallel Loading** — spots waterfall component patterns and calculates time savings
- **Aggregate Metrics** — combined SOQL, CPU, and DML usage across all related logs
- **Total User Wait Time** — the real number: from button click to page rendered

### 🔍 Governance Insights
- **Execution Context Detection** — classifies each log as Interactive, Batch, Integration, Scheduled, or Async
- **Mixed Context Warning** — friendly educational banner when one user account is handling multiple things at once
- **Dedicated User Recommendations** — explains *why* to create `IntegrationUser-SAP` and `BatchUser-Nightly`, not just that you should

### 📋 Five Analysis Tabs
1. **Summary** — health score, critical/high-priority issues, quick wins with severity badges
2. **Explain** — full conversational narrative: what happened, what your code did, performance, result
3. **Tree** — hierarchical execution tree with method calls, SOQL, DML, and exceptions
4. **Timeline** — Gantt chart visualization of the transaction phases
5. **Queries** — database operations grid (SOQL and DML) with row counts and duration

### 🖥️ Salesforce CLI Integration
- **Real-Time Log Streaming** — watch logs appear as they happen via `sf apex tail log`
- **Auto-Detection** — finds `sf` or legacy `sfdx` automatically
- **User-Specific Streaming** — monitor logs for any user in your org

### 🔐 Salesforce API Integration
- **OAuth 2.0 + PKCE** — secure browser-based authentication, no Connected App required
- **Tooling API** — query and retrieve debug logs directly
- **Trace Flag Management** — create/delete trace flags and configure debug levels per user
- **Batch Folder Import** — drag a folder of log files, get a grouped transaction analysis instantly

### 🗣️ Plain-English Translation Engine
- Conversational summaries with "What Happened", "What Your Code Did", "Performance", "Result" sections
- Real-world analogies for every technical concept
- Actionable recommendations with before/after code examples
- Governor limit explanations in plain terms ("You're using 45% of allowed CPU — comfortable range")

---

## Who Is This For?

| User | How They Use It |
|---|---|
| **Salesforce Administrators** | Understand workflow and flow execution without needing to read code |
| **Junior Developers** | Learn best practices through clear explanations and analogies |
| **Business Analysts** | Read what automation is actually doing and communicate it to stakeholders |
| **Senior Developers** | Quick plain-English summaries plus full technical detail on demand |
| **Architects** | Diagnose governance issues — mixed execution contexts, integration user patterns |
| **Consultants** | Quickly assess org health and identify performance bottlenecks |
| **Technical Leads** | Prove the need for dedicated integration/batch users with evidence |

---

## Getting Started

### Prerequisites
- Windows 10 or 11 (x64)
- .NET 8.0 SDK (for building from source)

### Option A: Pre-Built Executable
Download the latest release from [GitHub Releases](https://github.com/felisbinofarms/salesforce-debug-log-analyzer/releases). No installation required — just run `BlackWidow.exe`.

### Option B: Build From Source
```powershell
git clone https://github.com/felisbinofarms/salesforce-debug-log-analyzer.git
cd salesforce-debug-log-analyzer

dotnet restore
dotnet build
dotnet run
```

### Analyzing a Single Log File
1. Launch the application
2. Click **Upload Log** in the toolbar
3. Select a `.log` or `.txt` file
4. Browse the five analysis tabs

### Analyzing a Transaction Chain
For investigating slow page loads or complex automation:

1. Enable debug logs for the affected user in Salesforce Setup
2. Ask them to reproduce the issue (or reproduce it yourself)
3. Download all logs from that time window — you may have 8–15 files
4. Save them all to one folder
5. **Drag the folder** into Black Widow (or click **Load Folder**)
6. Black Widow groups related logs automatically
7. Click any group to see the full transaction breakdown

**Example:**
```
User reports: "Saving a Case takes forever!"

After loading folder:
→ CaseTrigger fired 3x (recursion) .............. 1.2s wasted
→ CaseValidation Flow ........................... 0.8s
→ SendEmailNotification (@future) ............... 1.5s
→ CaseDetails component (sequential load) ....... 1.7s
─────────────────────────────────────────────────────
Total user wait: 5.2 seconds

Recommendations:
• Add recursion guard to CaseTrigger — saves ~0.8s
• Load Lightning components in parallel — saves ~0.9s
```

### Real-Time Streaming
1. Install Salesforce CLI: `winget install Salesforce.CLI`
2. Authenticate: `sf org login web`
3. In Black Widow, click **Stream Logs**
4. Enter your org username or alias
5. Logs appear live as you perform actions

### Connecting via OAuth
1. Click **Connect to Salesforce** in the toolbar
2. Log in through the embedded browser window
3. Grant access — Black Widow uses the Platform CLI client ID (no Connected App setup needed)
4. Browse and download logs directly from your org

### Sample Logs
The `SampleLogs/` directory includes 231 sample log files for testing and exploration.

---

## Project Structure

```
salesforce-debug-log-analyzer/
├── Models/
│   ├── LogModels.cs              # All data models (LogAnalysis, LogGroup, ActionableIssue, etc.)
│   └── SalesforceModels.cs       # Salesforce API DTOs
├── ViewModels/
│   └── MainViewModel.cs          # Main application logic (2,600+ lines, 19 commands)
├── Views/
│   ├── MainWindow.xaml           # Main UI — 5-tab analysis dashboard
│   ├── ConnectionDialog.xaml     # Salesforce connection management
│   ├── ConnectionsView.xaml      # Saved connections list
│   ├── TraceFlagDialog.xaml      # Trace flag creation and management
│   ├── DebugSetupWizard.xaml     # 4-step debug logging setup wizard
│   ├── DebugLevelDialog.xaml     # Custom debug level configuration
│   ├── OAuthBrowserDialog.xaml   # Embedded OAuth browser
│   ├── InsightsPanel.xaml        # Governance insights panel
│   ├── SettingsDialog.xaml       # App settings (6 tabs)
│   ├── StreamingOptionsDialog.xaml # CLI streaming configuration
│   └── UpgradeDialog.xaml        # Pro tier feature comparison
├── Services/
│   ├── LogParserService.cs       # Core log parser (3,500+ lines, all event types)
│   ├── LogExplainerService.cs    # Plain-English generation engine (660+ lines)
│   ├── LogGroupService.cs        # Transaction grouping and phase detection
│   ├── LogMetadataExtractor.cs   # Fast log scanning without full parse
│   ├── SalesforceApiService.cs   # Tooling API integration
│   ├── SalesforceCliService.cs   # CLI streaming and batch download
│   ├── OAuthService.cs           # OAuth 2.0 + PKCE authentication
│   ├── OrgMetadataService.cs     # User and org enrichment
│   ├── EditorBridgeService.cs    # VS Code editor integration
│   ├── ReportExportService.cs    # Analysis export
│   ├── LicenseService.cs         # License management (AES-256 encrypted)
│   ├── SettingsService.cs        # Persistent application settings
│   └── CacheService.cs           # Application-level caching
├── Helpers/
│   └── Converters.cs             # WPF value converters
├── Themes/                       # Discord-themed resource dictionaries
├── SampleLogs/                   # 231 sample log files for testing
└── Tests/                        # xUnit test suite (9/9 passing)
```

---

## Current Status

**Build:** ✅ 0 errors &nbsp;|&nbsp; **Tests:** ✅ 9/9 passing &nbsp;|&nbsp; **Version:** v0.9-beta

### What Works Today
- ✅ Single log file analysis with all 5 tabs
- ✅ Folder drag-and-drop transaction chain analysis
- ✅ Plain-English explanations with code examples
- ✅ Governance / mixed-context warning and recommendations
- ✅ CLI real-time log streaming
- ✅ OAuth Salesforce connection and log download
- ✅ Trace flag and debug level management
- ✅ Persistent settings

### Coming Next
- 🔄 Windows installer (.exe setup)
- 🔄 Pro tier payment processing
- 🔄 Raw log viewer with syntax highlighting
- 🔄 Side-by-side log comparison
- 🔄 Export to PDF / HTML

See [ROADMAP.md](./ROADMAP.md) for the full plan.

---

## Technology Stack

| Component | Technology |
|---|---|
| Framework | .NET 8.0 + WPF |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| UI Theme | Discord-inspired dark theme |
| Testing | xUnit |
| Auth | OAuth 2.0 + PKCE (System.Net.Http) |
| Encryption | AES-256 (System.Security.Cryptography) |
| CLI Bridge | System.Diagnostics.Process |

---

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](./CONTRIBUTING.md) for the full development guide — branch naming, commit conventions, and working with the Copilot PM workflow.

**Quick start:**
```powershell
git clone https://github.com/felisbinofarms/salesforce-debug-log-analyzer.git
cd salesforce-debug-log-analyzer
dotnet build    # 0 errors expected
dotnet test     # 9/9 passing
dotnet run
```

Type `/pm timeline` in GitHub Copilot Chat to see the current sprint and pick an issue.

---

## License

[Add your chosen license]

## Acknowledgments

Built for the Salesforce developer community. Inspired by the frustration of explaining what a debug log *actually means* to every new team member, every single time. 🕷️