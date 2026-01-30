# Salesforce Debug Log Analyzer - WPF .NET 8 Application

## Project Setup Complete ✓

- [x] Verify that the copilot-instructions.md file in the .github directory is created
- [x] Clarify Project Requirements - WPF .NET 8 application for Salesforce debug log analysis
- [x] Scaffold the Project - Created WPF project with .NET 8 SDK
- [x] Customize the Project - Added Models, ViewModels, Views, Services folder structure
- [x] Install Required Extensions - Not needed for WPF
- [x] Compile the Project - Build succeeded with 0 warnings, 0 errors
- [x] Create and Run Task - Can use `dotnet run` to launch
- [ ] Launch the Project - Ready to run with `dotnet run`
- [x] Ensure Documentation is Complete - README.md created with full project details

## Project Overview

This is a Windows desktop application built with:
- **Framework**: WPF (.NET 8.0)
- **Architecture**: MVVM using CommunityToolkit.Mvvm
- **UI**: Material Design themes
- **Purpose**: Analyze Salesforce debug logs with visualizations and insights

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
- `ViewModels/` - MVVM ViewModels with CommunityToolkit
- `Views/` - WPF XAML views
- `Services/` - Business logic (parsing, API calls)
- `Helpers/` - Utility classes

## Next Steps

1. **OAuth Implementation** - Complete Salesforce OAuth 2.0 flow
2. **Log Parser** - Implement full debug log parsing engine
3. **Visualizations** - Add execution tree, timeline, and charts
4. **Testing** - Create unit tests for parsers and services
