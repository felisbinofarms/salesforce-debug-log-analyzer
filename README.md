# Salesforce Debug Log Analyzer (Black Widow 🕷️)

> **The only debug log tool that explains logs like a patient mentor, not a technical manual.**

A revolutionary Windows desktop application that **translates** complex Salesforce debug logs into plain English that anyone can understand - whether you're a seasoned developer or an admin with little coding knowledge.

## 🤖 For Contributors: Copilot PM Mode

**This project uses GitHub Copilot as Project Manager** to keep the team focused and prevent scope creep.

### Quick Start for Developers

When working on this project, use these PM commands in Copilot Chat:

```
/pm standup           - Daily progress check + priority setting
/pm scope-check       - "Should I build this feature?"
/pm review           - Code review for over-engineering
/pm timeline         - Sprint progress + deadline check
/pm focus            - 2-hour deep work mode
```

**Auto-Intervention:** Copilot will automatically flag:
- ⚠️ Scope creep (adding features not in current issue)
- ⚠️ Over-engineering (unnecessary abstractions/patterns)
- ⚠️ Rabbit holes (researching instead of shipping)
- ⚠️ Analysis paralysis (comparing too many options)

**Key Documents:**
- [PROJECT_PLAN.md](./PROJECT_PLAN.md) - 6-week timeline, milestones, roles
- [ISSUES_BACKLOG.md](./ISSUES_BACKLOG.md) - Detailed feature specs (22 issues)
- [.github/copilot-pm-instructions.md](./.github/copilot-pm-instructions.md) - Full PM behavior guide

**Launch Deadline:** March 15, 2026 (6 weeks)  
**Current Sprint:** Monetization (License validation + Stripe integration)

---

## What Makes This Different?

Traditional debug log tools show you raw technical data: timestamps, event types, stack traces, and governor limits with no context.

**This tool explains what actually happened in conversational language:**

### Traditional Debug Tool:
```
Execution completed in 2500ms. Executed 5 methods. Performed 3 SOQL queries.
SOQL queries: 87/100 (87%)
⚠️ High number of SOQL queries: 87 (Consider bulkification)
```

### Our Tool:
```
📋 What Happened:
✅ This transaction completed successfully in 2.5 seconds.

What Your Code Did:
• Called 5 different methods (pieces of code)
• Talked to the database 3 times to get or save information

⚠️ Too Many Database Queries: 
You're asking the database for information too many times (87 times). 
Think of it like making 87 separate phone calls instead of one call 
with a list of questions.

💡 Recommendation: Try to combine multiple queries into one where possible.
```

**No Salesforce expertise required** - understand debug logs on your first day!

## Key Features

### �️ Transaction Chain Analysis (NEW!)
- **Automatic Log Grouping** - Detects multiple related logs from a single user action
- **Phase Detection** - Separates Backend (triggers/flows) from Frontend (component loading)
- **Recursion Detection** - Identifies when triggers fire multiple times in one transaction
- **Sequential vs Parallel Loading** - Spots component waterfall patterns and calculates savings
- **Real User Experience Metrics** - Shows total wait time from button click to page rendered
- **Transaction Timeline** - Visual representation of the entire user journey
- **Aggregate Metrics** - Combined SOQL, CPU, and DML usage across all related logs

**Why This Matters:** In poorly-governed orgs, clicking "Save" on a Case might trigger 13 separate logs (triggers, flows, component reloads). Traditional tools show 13 disconnected logs. Black Widow groups them, shows the 11.9-second user wait time, and explains exactly why it's slow.
### 🚨 Execution Context Detection (NEW!)
- **Automatic Context Classification** - Distinguishes Interactive, Batch, Integration, Scheduled, and Async operations
- **Mixed Context Warning** - Flags when one user account is doing multiple things (UI + Batch + Integration)
- **Governance Recommendations** - Suggests creating dedicated users for each execution type
- **Impact Analysis** - Shows if background jobs are interfering with interactive performance

**Real-World Scenario:** Senior Architect with system admin credentials runs batch jobs, integrations, AND UI actions. When they click "Save Case", Black Widow shows:
```
🚨 GOVERNANCE ISSUE: 15 logs detected
   • 3 logs from your Case save (Interactive)
   • 5 logs from BatchApex job (Batch)
   • 4 logs from SAP integration (Integration)
   • 3 logs from Scheduled Flow (Scheduled)
   
💡 Recommendation: Only 3 of these 15 logs are YOUR action!
   Create dedicated users:
   - IntegrationUser-SAP for API calls
   - BatchUser for scheduled jobs
   - FlowUser for time-based automation
```

### 🔌 Salesforce CLI Integration (NEW!)
- **Real-Time Log Streaming** - Watch logs appear as they happen using `sf apex tail log`
- **CLI Auto-Detection** - Finds `sf` or legacy `sfdx` automatically
- **User-Specific Streaming** - Monitor logs for specific users (like yourself!)
- **Batch Download** - Download all logs for a user within a time range
- **No API Limits** - Uses CLI, doesn't count against API limits
### 🗣️ Plain-English Translation
- **Conversational summaries** that tell the story of your transaction
- **Real-world analogies** for technical concepts (N+1 queries = "asking 'What's the weather?' 100 times")
- **Contextual explanations** - "You're using 25% of allowed processing time - plenty of room to spare!"
- **Actionable recommendations** with specific solutions, not just problems

### 🔌 Salesforce Integration
- **OAuth 2.0 Authentication** - Connect securely to any Salesforce org
- **API Integration** - Query and retrieve debug logs via Tooling API
- **Trace Flag Management** - Set debug levels and configure logging for users
- **Batch Folder Import** - Load entire folders of logs for transaction analysis
- **Respects Permissions** - Works within your Salesforce security model

### 📊 Intelligent Analysis
- **Execution Tree** - Hierarchical view of method calls and operations
- **Timeline Visualization** - Gantt chart showing execution duration
- **Database Operations** - Dedicated view for SOQL queries and DML operations
- **Performance Dashboard** - Governor limits, CPU time, heap usage metrics
- **Smart Issue Detection** - Identifies N+1 queries, recursive triggers, slow operations
- **Component Loading Patterns** - Detects sequential vs parallel Lightning component loading
- **Bottleneck Identification** - Pinpoints the slowest operation across entire transaction chain

### 💡 Learning Tool
- Perfect for **junior developers** learning Salesforce best practices
- Helps **admins** understand what automation is doing without code knowledge
- Enables **business analysts** to review system behavior
- Gives **experienced developers** quick insights without wading through logs

## Who Is This For?

✅ **Salesforce Administrators** - Understand workflow and Process Builder execution without coding knowledge  
✅ **Junior Developers** - Learn best practices through clear explanations  
✅ **Business Analysts** - Read what code is doing and communicate issues  
✅ **Senior Developers** - Get quick plain-English summaries plus detailed technical data when needed  
✅ **Architects** - Diagnose governance issues (mixed execution contexts, integration user patterns)  
✅ **Consultants** - Quickly assess org health and identify performance bottlenecks  
✅ **Technical Leads** - Prove the need for dedicated integration/batch users to management

## Technology Stack

- **.NET 8.0** - Modern C# with latest features
- **WPF** - Rich Windows desktop UI framework
- **Material Design** - Modern, intuitive user interface
- **MVVM Pattern** - Clean separation of concerns
- **AvalonEdit** - Syntax-highlighted log viewer (coming soon)

## Getting Started

### Prerequisites

- Windows 10/11
- .NET 8.0 SDK or higher
- Salesforce Developer/Admin account with appropriate permissions (for API features)

### Building the Project

```powershell
# Clone or download the repository
cd log_analyser

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

### Quick Start - Analyzing a Local Log

1. **Launch the application**: Run `dotnet run` or double-click the compiled .exe
2. **Click "Upload Log"** in the top toolbar
3. **Select a log file**: Choose a `.log` or `.txt` file from your computer
4. **View the analysis**: The app will parse and display:
   - **Summary**: English explanation of what happened
   - **Issues**: Detected problems (N+1 queries, slow operations, errors)
   - **Recommendations**: Suggestions for optimization
   - **Tabs**: Browse execution tree, timeline, database operations, and more

### Quick Start - Analyzing Transaction Chains (NEW!)

For investigating slow page loads or complex automation chains:

1. **Download multiple logs** from the same user action (or use Salesforce Developer Console to export a series)
2. **Save all logs to one folder** (e.g., `C:\Logs\CaseSaveIssue\`)
3. **Click "Load Folder"** in Black Widow
4. **View grouped analysis**:
   - **Transaction Groups**: Logs automatically grouped by user and timing
   - **Phase Breakdown**: Backend (triggers/flows) vs Frontend (components)
   - **Total User Wait Time**: Complete duration from button click to page render
   - **Re-entry Detection**: Which triggers/flows fired multiple times
   - **Context Detection**: Which logs are UI vs Batch vs Integration
   - **Recommendations**: Specific fixes for performance bottlenecks

**Example Use Case:**
```
User reports: "Saving a Case takes forever!"

Your Investigation:
1. Enable debug logs for that user
2. Ask them to save a Case
3. Download all logs from that time period (might be 8-15 logs)
4. Load folder into Black Widow
5. See: "CaseTrigger fired 3 times, Flow took 2.5s, 
   Components loaded sequentially adding 3.7s - Total: 11.9 seconds"
6. Follow recommendations to fix recursion and parallel loading
7. Retest: Now takes 3.3 seconds! ✅
```

### Quick Start - Governance Analysis (NEW!)

For Architects investigating mixed user contexts:

1. **Load logs from your system admin account** (especially if you run integrations/batch jobs)
2. **Black Widow detects execution contexts automatically**
3. **Review Mixed Context Warning**:
   ```
   🚨 15 logs in 10 seconds:
      • 3 Interactive (your button clicks)
      • 5 Batch (scheduled apex)
      • 4 Integration (SAP API calls)
      • 3 Scheduled (time-based flows)
   
   💡 Recommendation: Create dedicated users:
      - IntegrationUser-SAP (for external API calls)
      - BatchUser (for all scheduled apex)
      - FlowUser (for scheduled flows)
   ```
4. **Export governance report** to justify dedicated users to management
5. **Follow Salesforce best practices** for user segmentation

### Quick Start - Real-Time Monitoring with CLI (NEW!)

For live debugging sessions:

1. **Install Salesforce CLI** if not already installed:
   ```powershell
   winget install Salesforce.CLI
   ```

2. **Authenticate to your org**:
   ```powershell
   sf org login web
   ```

3. **In Black Widow, click "Stream Logs"**
4. **Enter your username or org alias**
5. **Watch logs appear in real-time** as you perform actions
6. **Black Widow auto-groups** related logs as they come in

**Pro Tip:** Stream logs for a specific user while they reproduce an issue, then immediately analyze the grouped transaction!

### Sample Logs

Sample logs are included in the `SampleLogs/` directory:
- `simple_account_insert.log` - Basic DML operation with validation
- `simple_query_loop.log` - SOQL query with loop processing
- `error_validation_failure.log` - Validation rule failure example

Use these to test the parser and understand the analysis features!

### Connecting to Salesforce (Optional)

To retrieve logs directly from your Salesforce org:

1. **Create a Connected App** in Salesforce:
   - Setup → App Manager → New Connected App
   - Enable OAuth Settings
   - Callback URL: `http://localhost:8080/callback`
   - Scopes: `api`, `refresh_token`
   - Copy the Consumer Key and Consumer Secret

2. **Update OAuth Configuration**:
   - Open `Services/OAuthService.cs`
   - Replace `ClientId` and `ClientSecret` with your values

3. **Connect**:
   - Click "Connect to Salesforce"
   - Log in through your browser
   - Grant access
   - View and download logs from your org

## Project Structure

```
SalesforceDebugAnalyzer/
├── Models/              # Data models and entities
│   ├── LogModels.cs     # Debug log structures (includes LogGroup, LogPhase)
│   └── SalesforceModels.cs  # Salesforce API objects
├── ViewModels/          # MVVM ViewModels
│   └── MainViewModel.cs # Main app logic with grouping support
├── Views/               # WPF Views (XAML)
│   ├── MainWindow.xaml
│   ├── ConnectionDialog.xaml
│   ├── TraceFlagDialog.xaml
│   └── DebugSetupWizard.xaml
├── Services/            # Business logic and API services
│   ├── LogParserService.cs       # Parses individual logs
│   ├── LogGroupService.cs        # Groups related logs (NEW!)
│   ├── LogMetadataExtractor.cs   # Fast log scanning (NEW!)
│   ├── SalesforceApiService.cs   # Salesforce API integration
│   └── OAuthService.cs           # OAuth authentication
└── Helpers/             # Utility classes
```

## Roadmap

### Phase 1: Foundation ✅
- [x] Project structure and dependencies
- [x] Material Design UI implementation (now Discord-themed!)
- [x] Complete log parsing engine with all event types
- [x] File upload and local log analysis
- [x] Intelligent issue detection and recommendations
- [x] ViewModel integration with services

### Phase 2: Transaction Analysis ✅ (COMPLETED!)
- [x] Log metadata extraction for fast scanning
- [x] Transaction grouping by user and timing
- [x] Phase detection (Backend vs Frontend)
- [x] Re-entry pattern detection (recursion)
- [x] Sequential vs parallel component loading detection
- [x] Aggregate metrics across log groups
- [x] Smart recommendations for transaction-level optimization
- [x] Folder-based batch import

### Phase 3: Salesforce Integration (In Progress)
- [x] Salesforce OAuth authentication framework
- [x] Salesforce API service for log retrieval
- [x] Trace flag management UI
- [x] Debug level configuration wizard
- [ ] Real-time log streaming from Salesforce
- [ ] Automated log download by date range

### Phase 4: Visualizations
- [ ] Execution tree TreeView visualization
- [ ] Database operations DataGrid with filtering
- [ ] Performance dashboard with charts
- [ ] Governor limits progress bars and gauges
- [ ] Timeline/Gantt chart for transaction phases
- [ ] Flowchart generation with MSAGL
- [ ] Raw log viewer with syntax highlighting (AvalonEdit)

### Phase 5: Advanced Analysis
- [ ] N+1 query pattern detection with code location
- [ ] SOQL query optimization suggestions
- [ ] Comparative analysis (before/after)
- [ ] Batch log comparison across multiple transactions
- [ ] Custom rules engine for org-specific patterns
- [ ] Export analysis to PDF/HTML/JSON
- [ ] Integration with CI/CD pipelines

## Contributing

Contributions are welcome! This project aims to bridge the gap between experienced Salesforce developers and junior admins by making debug logs accessible to everyone.

## License

[Add your chosen license]

## Acknowledgments

- Inspired by the VSCode Salesforce Extensions debugger
- Built for the Salesforce developer community
