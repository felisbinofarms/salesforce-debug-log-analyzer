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
- .NET 8.0 SDK
- Salesforce Developer/Admin account with appropriate permissions

### Building the Project

```powershell
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

### Connecting to Salesforce

1. Click "Connect to Salesforce" in the toolbar
2. Log in through the OAuth flow
3. Grant access to your org
4. Start analyzing debug logs!

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

### Phase 1: Foundation (Current)
- [x] Project structure and dependencies
- [x] Material Design UI implementation
- [ ] Basic log parsing engine
- [ ] Salesforce OAuth authentication

### Phase 2: Core Features
- [ ] Complete log parser with all event types
- [ ] Execution tree visualization
- [ ] Database operations grid
- [ ] Performance dashboard

### Phase 3: Advanced Analysis
- [ ] Timeline/Gantt chart visualization
- [ ] Flowchart generation
- [ ] AI-powered issue detection
- [ ] Export to PDF/HTML

### Phase 4: Power Features
- [ ] Real-time log streaming
- [ ] Batch log comparison
- [ ] Custom rules engine
- [ ] Integration with CI/CD pipelines

## Contributing

Contributions are welcome! This project aims to bridge the gap between experienced Salesforce developers and junior admins by making debug logs accessible to everyone.

## License

[Add your chosen license]

## Acknowledgments

- Inspired by the VSCode Salesforce Extensions debugger
- Built for the Salesforce developer community
