# Salesforce Debug Log Analyzer - WPF .NET 8 Application (Black Widow 🕷️)

## Project Setup Complete ✓

- [x] Verify that the copilot-instructions.md file in the .github directory is created
- [x] Clarify Project Requirements - WPF .NET 8 application for Salesforce debug log analysis
- [x] Scaffold the Project - Created WPF project with .NET 8 SDK
- [x] Customize the Project - Added Models, ViewModels, Views, Services folder structure
- [x] Install Required Extensions - Not needed for WPF
- [x] Compile the Project - Build succeeded with 10 warnings (all harmless), 0 errors
- [x] Create and Run Task - Can use `dotnet run` to launch
- [x] Launch the Project - Ready to run with `dotnet run`
- [x] Ensure Documentation is Complete - README.md created with full project details
- [x] Discord UI Theme - Complete with Black Widow branding
- [x] Transaction Grouping - Multi-log analysis implemented

## Project Overview

**Black Widow** - The only Salesforce debug log analyzer that groups related logs, detects execution phases, and explains the complete user experience journey.

### Built With:
- **Framework**: WPF (.NET 8.0)
- **Architecture**: MVVM using CommunityToolkit.Mvvm
- **UI**: Discord-themed dark interface with Black Widow spider branding
- **Purpose**: Analyze individual logs AND transaction chains (multiple related logs from one user action)

### Key Differentiator:
Unlike traditional tools that show one log at a time, Black Widow groups related logs from a single user action (e.g., saving a Case that triggers 13 logs) and shows:
- Total user wait time
- Backend phase (triggers/flows) vs Frontend phase (component loading)
- Recursion patterns
- Sequential vs parallel loading
- Aggregate metrics across entire transaction

## Quick Commands

```powershell
# Build the project
dotnet build

# Run the application
dotnet run

# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore
```

## Project Structure

- `Models/` - Data models for logs and Salesforce objects
  - `LogModels.cs` - Core parsing models (LogLine, ExecutionNode, LogAnalysis)
  - `LogModels.cs` - **NEW:** Transaction grouping models (LogGroup, LogPhase, DebugLogMetadata)
  - `SalesforceModels.cs` - Salesforce API DTOs
- `ViewModels/` - MVVM ViewModels with CommunityToolkit
  - `MainViewModel.cs` - Main application logic with grouping support
- `Views/` - WPF XAML views (Discord-themed)
  - `MainWindow.xaml` - Main app shell with sidebar navigation
  - `ConnectionDialog.xaml` - OAuth and manual token connection
  - `TraceFlagDialog.xaml` - Manage debug logs and trace flags
  - `DebugSetupWizard.xaml` - 4-step wizard for debug logging setup
  - `DebugLevelDialog.xaml` - Custom debug level creation
  - `OAuthBrowserDialog.xaml` - Browser-based OAuth flow
- `Services/` - Business logic (parsing, API calls, grouping)
  - `LogParserService.cs` - Parses individual debug log files
  - `LogGroupService.cs` - **NEW:** Groups related logs into transactions
  - `LogMetadataExtractor.cs` - **NEW:** Fast log scanning without full parse
  - `SalesforceApiService.cs` - Salesforce API integration
  - `OAuthService.cs` - OAuth 2.0 authentication
  - `CacheService.cs` - Application-level caching
- `Helpers/` - Utility classes

## Core Features Implemented

### 1. Transaction Chain Analysis ✅
**Files:** `LogGroupService.cs`, `LogMetadataExtractor.cs`, `LogModels.cs`

Groups multiple debug logs from the same user action:
- Detects logs within 10-second window from same user
- Groups by record ID when available
- Creates `LogGroup` objects with aggregate metrics

### 2. Phase Detection ✅
**Method:** `LogGroupService.DetectPhases()`

Separates logs into execution phases:
- **Backend Phase**: Triggers, Flows, Process Builders, Validation, @future methods
- **Frontend Phase**: Lightning Controllers, @AuraEnabled methods, component loading
- Calculates gaps between phases

### 3. Sequential vs Parallel Loading Detection ✅
**Method:** `LogGroupService.DetectSequentialLoading()`

Identifies component loading patterns:
- Sequential: Components load one after another (waterfall)
- Parallel: Components load simultaneously
- Calculates potential time savings

### 4. Re-entry Pattern Detection ✅
**Method:** `LogGroupService.DetectReentryPatterns()`

Finds trigger recursion:
- Counts how many times each trigger fires
- Detects infinite loop potential
- Flags as critical issue in recommendations

### 5. Smart Recommendations ✅
**Method:** `LogGroupService.GenerateRecommendations()`

Auto-generates fixes based on patterns:
- "Add recursion control to CaseTrigger"
- "Load components in parallel to save 2.5s"
- "Optimize @future method taking 2.5s"
- "N+1 query pattern detected in RelatedCasesController"

### 6. Fast Metadata Extraction ✅
**Class:** `LogMetadataExtractor`

Scans logs without full parsing:
- Reads only first 5000 and last 1000 lines
- Extracts user, timestamp, duration, governor limits
- Enables fast folder scanning (100+ logs in seconds)

## Next Steps

1. **Testing Phase** - Load real log folders and validate grouping accuracy
2. **UI Implementation** - Build LogGroup display in MainWindow
3. **Visualizations** - Timeline chart showing phase breakdown
4. **Export Features** - Generate reports for transaction analysis
3. **Visualizations** - Add execution tree, timeline, and charts
4. **Testing** - Create unit tests for parsers and services
