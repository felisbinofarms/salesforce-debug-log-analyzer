# Contributing to Black Widow 🕷️

Thanks for your interest in contributing! This project uses **Copilot as Project Manager** to keep everyone focused on shipping.

## 🚀 Quick Start (5 minutes)

### 1. Clone & Build
```bash
git clone https://github.com/felisbinofarms/salesforce-debug-log-analyzer.git
cd salesforce-debug-log-analyzer
dotnet restore
dotnet build
dotnet run
```

### 2. Enable Copilot PM Mode
In GitHub Copilot Chat, type:
```
/pm timeline
```

You'll see current sprint progress and what to work on next.

### 3. Pick an Issue
- Browse [ISSUES_BACKLOG.md](./ISSUES_BACKLOG.md)
- Look for issues tagged with your skill level:
  - `good first issue` - New contributors
  - `P0-critical` - Launch blockers
  - `P1-high` - Next sprint
  - `P2-medium` - Future work

### 4. Start Working
```bash
git checkout -b feature/issue-X-description
/pm focus
# Work for 2 hours in deep work mode
```

## 🤖 Working with Copilot PM

### Daily Commands

**Morning Standup:**
```
/pm standup

What I completed yesterday: [your work]
What I'm working on today: Issue #X
Blockers: None
```

**Before Adding Features:**
```
/pm scope-check

I want to add [feature description]
```

**Got a New Idea?**
```
/pm idea

[Describe your feature idea]
```

**Code Review:**
```
/pm review

[Paste your code or mention file path]
```

**Check Progress:**
```
/pm timeline
```

### Copilot Will Auto-Intervene If:
- ⚠️ You're adding features not in the current issue (scope creep)
- ⚠️ You're over-engineering (unnecessary abstractions)
- ⚠️ You're going down rabbit holes (researching instead of coding)
- ⚠️ You're comparing too many options (analysis paralysis)

**Response:** Follow Copilot's guidance to stay on track!

## 📋 Development Workflow

### 1. Pick an Issue
- Check [PROJECT_PLAN.md](./PROJECT_PLAN.md) for current sprint
- Assign yourself in GitHub Issues
- Comment: "Working on this"

### 2. Create Feature Branch
```bash
git checkout -b feature/issue-X-short-description
```

**Branch Naming:**
- `feature/issue-X-description` - New features
- `fix/issue-X-description` - Bug fixes
- `docs/description` - Documentation
- `refactor/description` - Code cleanup

### 3. Code with Copilot PM
```
/pm focus

Issue #X - [Task name]
```

This sets a 2-hour deep work block.

### 4. Write Tests
- Unit tests in `Tests/` folder
- Run: `dotnet test`
- Minimum: Happy path + 2 error cases

### 5. Commit with Conventional Commits
```bash
git add .
git commit -m "feat: Add license validation service

- Implemented AES-256 encryption for local storage
- Added device fingerprinting
- 7-day offline grace period
- Resolves #1"
```

**Commit Types:**
- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation
- `test:` - Adding tests
- `refactor:` - Code cleanup (no behavior change)
- `perf:` - Performance improvement
- `chore:` - Maintenance tasks

### 6. Push & Create PR
```bash
git push origin feature/issue-X-description
```

Then create Pull Request on GitHub with:
- Title: `feat: Add license validation (#1)`
- Description: Link to issue, explain changes
- Screenshots/video if UI changes

### 7. Code Review
- PR reviewed within 24 hours
- Address feedback
- Copilot PM will check for over-engineering
- Merge when approved

## 🎯 Definition of Done

A feature is "done" when:
- [ ] All acceptance criteria met (from ISSUES_BACKLOG.md)
- [ ] Unit tests pass (`dotnet test`)
- [ ] Build succeeds (`dotnet build`)
- [ ] Code reviewed and approved
- [ ] Documentation updated (if needed)
- [ ] Manual testing completed (happy path + errors)
- [ ] GitHub issue closed with demo screenshot/video

## 🏗️ Architecture Overview

```
Black Widow (MVVM Architecture)
├── Models/          - Data structures (POCOs)
├── ViewModels/      - Presentation logic (CommunityToolkit.Mvvm)
├── Views/           - XAML UI (WPF, Discord theme)
├── Services/        - Business logic (parsing, API, grouping)
├── Helpers/         - Utilities and converters
└── Tests/           - Unit tests (xUnit)
```

**Key Services:**
- `LogParserService` - Parse debug logs (1359 lines)
- `LogGroupService` - Transaction grouping (408 lines)
- `SalesforceApiService` - REST API wrapper (338 lines)
- `LogMetadataExtractor` - Fast scanning (323 lines)

**Read Before Coding:**
- [ARCHITECTURE_VALIDATION.md](./.archive/ARCHITECTURE_VALIDATION.md) - Full architecture review

## 🚫 What NOT to Do

### ❌ Don't Add Features Without Approval
```
/pm scope-check

I want to add voice commands
```

Wait for Copilot PM to prioritize and assign milestone.

### ❌ Don't Over-Engineer
**Bad:** Create abstract factory for license validation  
**Good:** Simple `LicenseService.cs` with clear methods

**Bad:** Research 5 encryption libraries for 2 hours  
**Good:** Use built-in `System.Security.Cryptography` (ships with .NET)

### ❌ Don't Skip Tests
All services must have unit tests. No exceptions.

### ❌ Don't Break the Build
Run `dotnet build` and `dotnet test` before pushing.

## 💡 How to Propose New Ideas

### Option 1: Use Copilot PM (Recommended)
```
/pm idea

I think we should add [feature description]
```

Copilot will:
1. Acknowledge your idea
2. Add to ISSUES_BACKLOG.md with priority
3. Assign milestone (v1.0/v1.1/v2.0)
4. Redirect you back to current work

### Option 2: Create GitHub Issue
Use the **Feature Request** template:
- Clear user story ("As a [user], I want [feature] so that [benefit]")
- Acceptance criteria (what "done" looks like)
- Why it's valuable (business case)

**Note:** Ideas go into backlog, not current sprint (unless P0-critical).

## 📅 Release Schedule

**Current Phase:** Monetization (Sprint 1.1)  
**Launch Date:** March 15, 2026 (6 weeks)

**Milestones:**
- v1.0 (Mar 15) - MVP with licensing
- v1.1 (Apr 1) - Marketplace + extensions
- v1.2 (Jun 1) - Team collaboration
- v2.0 (Sep 1) - Advanced features

See [PROJECT_PLAN.md](./PROJECT_PLAN.md) for detailed timeline.

## 🎓 Learning Resources

**New to WPF?**
- [Microsoft WPF Tutorial](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [CommunityToolkit.Mvvm Docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

**New to Salesforce?**
- [Salesforce Developer Docs](https://developer.salesforce.com/)
- [Debug Log Basics](https://developer.salesforce.com/docs/atlas.en-us.apexcode.meta/apexcode/apex_debugging_debug_log.htm)

**Our Docs:**
- [OAUTH_SETUP.md](./OAUTH_SETUP.md) - Connect to Salesforce
- [PARSING_WALKTHROUGH.md](./PARSING_WALKTHROUGH.md) - How log parsing works
- [TESTING_GUIDE.md](./TESTING_GUIDE.md) - Writing tests

## 🏆 Recognition

Contributors will be:
- Listed in README.md
- Credited in release notes
- Given early access to Pro tier (free)
- Invited to beta testing

Top contributors may be invited to join core team.

## 📞 Getting Help

**Stuck on something?**
1. Check [ISSUES_BACKLOG.md](./ISSUES_BACKLOG.md) for implementation notes
2. Read [ARCHITECTURE_VALIDATION.md](./.archive/ARCHITECTURE_VALIDATION.md)
3. Ask Copilot: `/pm review [your question]`
4. Comment on the GitHub issue
5. Join our Discord (link in README.md)

**Found a bug?**
Create issue with **Bug Report** template.

## 🤝 Code of Conduct

**Be respectful.** We're all here to ship great software.

**Be focused.** Use Copilot PM to stay on track.

**Be collaborative.** Code reviews are learning opportunities, not criticisms.

**Be honest.** Blocked? Say so. Don't know? Ask.

---

**Welcome to the team! Let's ship Black Widow! 🕷️**

**First PR?** Type `/pm standup` in Copilot Chat to get started!
