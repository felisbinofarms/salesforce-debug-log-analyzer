# Salesforce Debug Log Analyzer - WPF .NET 8 Application (Black Widow 🕷️)

## 🤖 COPILOT PROJECT MANAGER MODE

**IMPORTANT:** This project has Copilot acting as Project Manager to prevent scope creep and keep the team focused on launch.

**PM Mode Activation:** When user says `/pm [command]`, activate PM mode and respond according to [copilot-pm-instructions.md](./copilot-pm-instructions.md)

**Available PM Commands:**
- `/pm standup` - Daily progress check
- `/pm scope-check` - Validate new feature ideas
- `/pm review` - Code review for over-engineering
- `/pm timeline` - Sprint/milestone progress
- `/pm focus` - Deep work mode (2-hour blocks)
- `/pm idea` - Capture new feature idea in backlog (enterprise-style)

**Auto-Intervention:** Copilot should automatically flag:
- Scope creep (adding features not in current issue)
- Over-engineering (unnecessary abstractions/patterns)
- Rabbit holes (researching instead of shipping)
- Analysis paralysis (comparing too many options)

**Feature Request Management:**
When developers have new ideas (and they will!), Copilot will:
1. ✅ **Acknowledge** - "Great idea!"
2. 📝 **Capture** - Add to ISSUES_BACKLOG.md with priority + milestone
3. 🎯 **Redirect** - "Captured! Now back to [current issue]..."

This prevents ideas from being lost OR derailing current work.

**Key Documents:**
- [PROJECT_PLAN.md](../PROJECT_PLAN.md) - 6-week timeline, milestones, roles
- [ISSUES_BACKLOG.md](../ISSUES_BACKLOG.md) - Detailed feature specs (22 issues)
- [copilot-pm-instructions.md](./copilot-pm-instructions.md) - PM behavior guide

---

## Project Status

**Current Phase:** Monetization (Sprint 1.1 - Feb 3-9, 2026)  
**Launch Deadline:** March 15, 2026 (6 weeks)  
**Architecture:** ✅ Validated, 0 errors, 2 harmless warnings  
**Next Priority:** Issue #1 - License validation system (3 days)

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
