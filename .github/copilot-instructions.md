# Salesforce Debug Log Analyzer - Avalonia .NET 8 Application (Black Widow 🕷️)

---

## ⚙️ ENGINEERING PROCESS RULES (Read First)

These rules were established during active development. Follow them for every session without needing to be reminded.

### 📋 Source of Truth: GitHub Issues
- **GitHub Issues are the authoritative backlog** — not `ISSUES_BACKLOG.md`.
- `ISSUES_BACKLOG.md` is a historical planning doc. Do not sync it manually.
- Before starting any session, run: `gh issue list --repo felisbinofarms/salesforce-debug-log-analyzer --state open --limit 100`
- Check issue labels and milestones to understand priority and grouping.

### 🔍 Pre-Work Evaluation (Do This Before Every Issue)
1. **Read the GitHub issue** — understand acceptance criteria and dependencies.
2. **Check file overlap** — identify which files the issue touches.
3. **Scan related issues** — if two issues touch the same files, evaluate whether to work them together to avoid double-touching and rework.
4. **Create a branch** — one branch per logical group of issues. Never work directly on `master`.
5. **Create a PR** — open a draft PR immediately so work is visible and reviewable.

### 🌿 Branch & PR Strategy
- Branch naming: `feature/issue-N-short-description` or `fix/issue-N-short-description`
- Group related issues that touch the same files into a single branch: `feature/issue-3-4-5-tab-migration`
- Open a PR for every branch before starting work — use draft PRs while in progress.
- PR title format: `[#N, #M] Short description` for grouped issues.
- PRs must reference all related issue numbers in the body: `Closes #3, Closes #4`.

### ✅ Definition of Done (Every Issue, No Exceptions)
- `dotnet build` → **0 errors**
- `dotnet test` → **all tests pass** (currently baseline 289/289)
- New code has **full unit test coverage** — no untested public methods
- No regressions in existing tests
- Build warnings from this issue are resolved (don't add new ones)
- PR description explains what changed and why

### 🚨 When You Spot a Problem Not In Scope
- **Do NOT fix it inline** — that's scope creep and creates untraceable changes.
- **Add a TODO comment** in the code at the exact location:
  ```csharp
  // TODO: GH#XX - brief description (link to GitHub issue)
  ```
- **Create a GitHub issue** with: clear title, which file/line, what the problem is, and suggested fix.
- Then **continue with the current issue** — don't get distracted.

### 🤖 Subagent Quality Gate
- Subagents can be used to parallelize implementation work.
- **Copilot is the quality gate** — review all subagent output before it's committed or a PR is created.
- Quality checklist for subagent output:
  - Builds with 0 errors
  - Tests still pass
  - Code follows existing patterns in the codebase (naming, structure, style)
  - No new warnings introduced
  - Acceptance criteria from the GitHub issue are met

### 🏗️ Tech Stack (Current — Avalonia, not WPF)
- **UI Framework**: Avalonia (`.axaml` files) — the WPF `.xaml` files in `Views/Tabs/` are legacy and being migrated
- **Architecture**: MVVM via CommunityToolkit.Mvvm
- **Theme**: ObsidianTheme (dark Discord-style)
- **Icons**: Material.Icons.Avalonia (`mi:MaterialIcon`)
- Do NOT add WPF patterns (`Trigger`, `DataTrigger`, `Style.Triggers`) — use Avalonia equivalents (`Classes`, `DataTrigger` in XAML styles)
- New view files go in `Views/Tabs/` as `.axaml` UserControls

### 📝 Updating These Instructions
- When a new rule is established during a session that applies to all future sessions, **add it here immediately** — don't wait.
- Keep the rules concise and actionable. No fluff.

---

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

**Current Phase:** Pre-launch (Distribution & Final Polish)  
**Launch Deadline:** March 29, 2026  
**Next Priority:** LemonSqueezy store setup + Windows installer

**Read [PROJECT_STATUS.md](../PROJECT_STATUS.md) for full context — monetization code is complete.**

**Last Major Update:** March 14, 2026
- ⚠️ Build: 0 errors, **37 warnings** (tracked in GH#12 — fix/build-warnings branch in progress)
- ✅ 289/289 tests passing
- ✅ 24 services, 13 views, 4 model files fully implemented
- ✅ Monetization code complete: LicenseService (623 lines) + UpgradeDialog + LemonSqueezy integration
- ✅ Feature gating, trial system, device fingerprinting all working
- 🔄 Avalonia tab migration in progress (GH#3–#11) — all tabs still inlined in MainWindow.axaml (1,979 lines)
- Remaining launch blockers: LemonSqueezy store setup, webhook handler, Windows installer

## Project Overview

**Black Widow** - The only Salesforce debug log analyzer that groups related logs, detects execution phases, and explains the complete user experience journey.

### Built With:
- **Framework**: Avalonia (.NET 8.0) — migrated from WPF (legacy `.xaml` files still exist in `Views/Tabs/`, being ported issue by issue)
- **Architecture**: MVVM using CommunityToolkit.Mvvm
- **UI**: ObsidianTheme dark interface (Discord-style) with Black Widow spider branding
- **Icons**: Material.Icons.Avalonia
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

## ✅ What's Been Completed

### Architecture (Feb 2, 2026)
- ✅ **Build:** 0 errors, 2 harmless warnings (WizardCancelled event)
- ✅ **Tests:** 7/7 passing (100% success rate)
- ✅ **Performance:** 19MB log parsed in <3 seconds
- ✅ **Services:** All 6 services validated and working
- ✅ **ViewModels:** MainViewModel fully wired (10 RelayCommands)
- ✅ **Views:** All 7 XAML views connected correctly

### Project Management System (Feb 2, 2026)
- ✅ **Copilot PM:** 6 commands implemented (`/pm standup`, `/pm scope-check`, `/pm review`, `/pm timeline`, `/pm focus`, `/pm idea`)
- ✅ **Auto-Intervention:** Prevents scope creep, over-engineering, rabbit holes
- ✅ **Timeline:** 6-week plan (Feb 3 - Mar 15, 2026)
- ✅ **Documentation:** PROJECT_PLAN.md, ISSUES_BACKLOG.md, copilot-pm-instructions.md

### Contributor Onboarding (Feb 2, 2026)
- ✅ **CONTRIBUTING.md:** 500-line guide with 5-minute quick start
- ✅ **Issue Templates:** Feature request + bug report (enterprise format)
- ✅ **PR Template:** Definition of Done checklist
- ✅ **Recognition:** Contributors get free Pro tier license

## 🎯 Where to Start (New Contributors)

### Option 1: Quick Start (5 minutes)
1. **Clone & Build:**
   ```powershell
   git clone https://github.com/YOUR_USERNAME/log_analyser.git
   cd log_analyser
   dotnet build
   dotnet run
   ```

2. **Read Documentation:**
   - [CONTRIBUTING.md](../CONTRIBUTING.md) - Complete onboarding guide
   - [PROJECT_PLAN.md](../PROJECT_PLAN.md) - Timeline and milestones
   - [ISSUES_BACKLOG.md](../ISSUES_BACKLOG.md) - All 22 feature specs

3. **Check Current Work:**
   - Type `/pm timeline` in Copilot Chat to see sprint progress
   - View [GitHub Issues](https://github.com/YOUR_USERNAME/log_analyser/issues) marked "good first issue"

### Option 2: Pick an Issue
**Good First Issues (P2-P3):**
- Issue #20: ViewModel unit tests (3 days, testing experience)
- Issue #11: User guide & docs (2 days, technical writing)
- Issue #21: Context detection accuracy (ML/testing, 2 days)

**High Priority (P0-P1) - Requires Approval:**
- Issue #6: Windows installer (3 days, WiX/NSIS)
- Issue #11: User guide & docs (2 days, technical writing)

### Option 3: Have a New Idea?
Type `/pm idea` in Copilot Chat:
```
/pm idea

I want to add [describe your feature]
```
Copilot will:
1. Acknowledge your idea
2. Add it to ISSUES_BACKLOG.md with priority
3. Assign to correct milestone (v1.0/v1.1/v2.0)
4. Keep you focused on current work

## 🚀 Next Steps (Development Team)

### Immediate (Launch Blockers)
- [ ] LemonSqueezy store setup (account, product variants, checkout URLs)
- [ ] Webhook handler (server-side payment → license provisioning)
- [ ] Windows installer (WiX or NSIS)

### Completed (Issues #1-3)
- [x] **Issue #1:** License validation system — LicenseService.cs (623 lines)
- [x] **Issue #2:** Upgrade flow UI — UpgradeDialog.xaml + code-behind
- [x] **Issue #3:** LemonSqueezy payment integration (replaced Stripe)
- [x] **Issue #19:** Settings dialog (6 tabs)

### Launch (March 29, 2026)
10 beta users → Public launch → 50 paying customers by June
