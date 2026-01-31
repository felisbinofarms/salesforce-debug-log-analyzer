# Salesforce Debug Log Analyzer

> **The only debug log tool that explains logs like a patient mentor, not a technical manual.**

A revolutionary Windows desktop application that **translates** complex Salesforce debug logs into plain English that anyone can understand - whether you're a seasoned developer or an admin with little coding knowledge.

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

### 🗣️ Plain-English Translation
- **Conversational summaries** that tell the story of your transaction
- **Real-world analogies** for technical concepts (N+1 queries = "asking 'What's the weather?' 100 times")
- **Contextual explanations** - "You're using 25% of allowed processing time - plenty of room to spare!"
- **Actionable recommendations** with specific solutions, not just problems

### 🔌 Salesforce Integration
- **OAuth 2.0 Authentication** - Connect securely to any Salesforce org
- **API Integration** - Query and retrieve debug logs via Tooling API
- **Trace Flag Management** - Set debug levels and configure logging for users
- **Respects Permissions** - Works within your Salesforce security model

### 📊 Intelligent Analysis
- **Execution Tree** - Hierarchical view of method calls and operations
- **Timeline Visualization** - Gantt chart showing execution duration
- **Database Operations** - Dedicated view for SOQL queries and DML operations
- **Performance Dashboard** - Governor limits, CPU time, heap usage metrics
- **Smart Issue Detection** - Identifies N+1 queries, recursive triggers, slow operations

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
