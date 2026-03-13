# Development Workflow - Black Widow 🕷️

> Multi-developer collaboration guide. All work tracked via [GitHub Issues](https://github.com/felisbinofarms/salesforce-debug-log-analyzer/issues) and [Milestones](https://github.com/felisbinofarms/salesforce-debug-log-analyzer/milestones).

## Branch Strategy

```
master               ← production-ready, always builds clean
  ├── feature/XX-*   ← new features (XX = issue number)
  ├── fix/XX-*       ← bug fixes
  └── chore/XX-*     ← refactors, docs, infra
```

### Rules

1. **Never push directly to `master`** — all changes go through PRs
2. **One issue per branch** — keeps PRs focused and reviewable
3. **Branch naming**: `feature/42-port-summary-tab`, `fix/15-null-ref-on-connect`
4. **Delete branch after merge**

### Workflow

```bash
# 1. Pick an issue from GitHub, assign yourself
# 2. Create branch from latest master
git checkout master
git pull origin master
git checkout -b feature/42-port-summary-tab

# 3. Do work, commit often with conventional commits
git commit -m "feat(ui): port SummaryTab to Avalonia axaml"
git commit -m "fix(ui): replace WPF SolidColorBrush with Avalonia IBrush"

# 4. Push and open PR
git push -u origin feature/42-port-summary-tab
gh pr create --title "feat: Port SummaryTab to Avalonia" --body "Resolves #42"

# 5. Request review, iterate, merge
```

## Commit Convention

Format: `type(scope): description`

| Type       | When                                    |
|------------|-----------------------------------------|
| `feat`     | New feature or functionality            |
| `fix`      | Bug fix                                 |
| `refactor` | Code restructuring, no behavior change  |
| `test`     | Adding or updating tests                |
| `docs`     | Documentation only                      |
| `chore`    | Build, deps, CI/CD, tooling             |
| `perf`     | Performance improvement                 |

**Scopes**: `ui`, `services`, `vm` (viewmodel), `models`, `infra`, `tests`

**Examples**:
```
feat(ui): port TreeTab to Avalonia with execution node rendering
fix(services): handle null log body in parser
refactor(vm): extract tab navigation to helper method
chore(infra): add GitHub Actions CI workflow
```

## Pull Request Process

1. **Create PR** referencing the issue: `Resolves #42`
2. **Fill out the PR template** (auto-populates from `.github/pull_request_template.md`)
3. **Verify build passes**: `dotnet build` must show 0 errors
4. **Self-review** the diff before requesting review
5. **Request review** from at least 1 team member
6. **Address feedback** with fixup commits (squash on merge)
7. **Merge via GitHub** (squash merge preferred for clean history)

### PR Size Guidelines

| Size       | Lines Changed | Review Time |
|------------|--------------|-------------|
| Small      | < 200        | Same day    |
| Medium     | 200-500      | 1-2 days    |
| Large      | 500+         | Break it up |

## Issue Management

### Labels We Use

| Label              | Meaning                                    |
|--------------------|--------------------------------------------|
| `P0-critical`      | Launch blocker, drop everything             |
| `P1-high`          | Needed for current milestone                |
| `P2-medium`        | Planned for upcoming milestone              |
| `P3-low`           | Backlog / nice to have                      |
| `area:ui`          | Views, XAML, visual components              |
| `area:services`    | Backend services, parsing, API              |
| `area:infra`       | Build, CI/CD, packaging                     |
| `area:testing`     | Test coverage                               |
| `area:monetization`| Licensing, payments                         |
| `avalonia-migration`| WPF → Avalonia porting                     |
| `v1.0-launch`      | Required for March 29 launch                |

### Issue Lifecycle

```
Open → Assigned → In Progress → In Review → Merged → Closed
```

1. **Open**: Created with labels + milestone
2. **Assigned**: Someone claims it (assign yourself on GitHub)
3. **In Progress**: Branch created, work started
4. **In Review**: PR opened
5. **Merged/Closed**: PR merged, issue auto-closes via `Resolves #XX`

## Avoiding Conflicts

### Daily Sync
- Check GitHub Issues board before starting work
- Comment on your issue when you start/stop working
- Pull `master` frequently: `git pull origin master`

### Rebase Before PR
```bash
git checkout master
git pull origin master
git checkout feature/42-port-summary-tab
git rebase master
# Resolve any conflicts
git push --force-with-lease
```

### Area Ownership (Loose)
To minimize conflicts, loosely claim areas:
- If you're working on tabs, communicate which tabs
- Shared files (MainViewModel.cs, MainWindow.axaml) — coordinate before touching
- Services are generally independent and safe to work on in parallel

## Build Verification

Before opening a PR, always run:

```powershell
dotnet build          # Must be 0 errors
dotnet test           # All tests must pass
```

## Quick Reference

```bash
# See all open issues
gh issue list

# See issues assigned to you
gh issue list --assignee @me

# Create a PR
gh pr create --title "feat: description" --body "Resolves #XX"

# Check PR status
gh pr status

# View project board (web)
# https://github.com/felisbinofarms/salesforce-debug-log-analyzer/issues
```
