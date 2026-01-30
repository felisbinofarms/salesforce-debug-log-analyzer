# Salesforce Debug Log Analyzer

A modern Windows desktop application that transforms complex Salesforce debug logs into intuitive visualizations, helping both junior admins and experienced developers understand execution flow, performance bottlenecks, and errors.

## Features

### Salesforce Integration
- **OAuth 2.0 Authentication** - Connect securely to any Salesforce org
- **API Integration** - Query and retrieve debug logs via Tooling API
- **Trace Flag Management** - Set debug levels and configure logging for users
- **Respects Permissions** - Works within your Salesforce security model

### Log Analysis
- **Intelligent Parsing** - Processes logs up to 20MB with advanced tokenization
- **Execution Tree** - Hierarchical view of method calls and operations
- **Timeline Visualization** - Gantt chart showing execution duration
- **Database Operations** - Dedicated view for SOQL queries and DML operations
- **Performance Dashboard** - Governor limits, CPU time, heap usage metrics
- **Error Detection** - Automatic identification and highlighting of exceptions

### Insights & Recommendations
- **Plain English Summaries** - Understand what happened without technical jargon
- **Issue Detection** - Identifies N+1 queries, recursive triggers, slow operations
- **Performance Hotspots** - Highlights methods and queries consuming most time
- **Actionable Recommendations** - Suggestions for code optimization

## Technology Stack

- **.NET 8.0** - Modern C# with latest features
- **WPF** - Rich Windows desktop UI framework
- **Material Design** - Modern, intuitive user interface
- **MVVM Pattern** - Clean separation of concerns
- **AvalonEdit** - Syntax-highlighted log viewer
- **SQLite** - Local log caching

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
│   ├── LogModels.cs     # Debug log structures
│   └── SalesforceModels.cs  # Salesforce API objects
├── ViewModels/          # MVVM ViewModels
│   └── MainViewModel.cs
├── Views/               # WPF Views (XAML)
│   └── MainWindow.xaml
├── Services/            # Business logic and API services
│   ├── LogParserService.cs
│   └── SalesforceApiService.cs
└── Helpers/             # Utility classes
```

## Roadmap

### Phase 1: Foundation ✅
- [x] Project structure and dependencies
- [x] Material Design UI implementation
- [x] Complete log parsing engine with all event types
- [x] File upload and local log analysis
- [x] Intelligent issue detection and recommendations
- [x] ViewModel integration with services

### Phase 2: Core Features (In Progress)
- [x] Salesforce OAuth authentication framework
- [x] Salesforce API service for log retrieval
- [ ] Execution tree TreeView visualization
- [ ] Database operations DataGrid
- [ ] Performance dashboard with charts
- [ ] Governor limits visualization

### Phase 3: Advanced Analysis
- [ ] Timeline/Gantt chart visualization
- [ ] Flowchart generation with MSAGL
- [ ] Raw log viewer with AvalonEdit
- [ ] Enhanced error stack trace display
- [ ] Export analysis to PDF/HTML

### Phase 4: Power Features
- [ ] Real-time log streaming from Salesforce
- [ ] Batch log comparison
- [ ] Custom rules engine
- [ ] Trace flag management UI
- [ ] Debug level configuration
- [ ] Integration with CI/CD pipelines

## Contributing

Contributions are welcome! This project aims to bridge the gap between experienced Salesforce developers and junior admins by making debug logs accessible to everyone.

## License

[Add your chosen license]

## Acknowledgments

- Inspired by the VSCode Salesforce Extensions debugger
- Built for the Salesforce developer community
