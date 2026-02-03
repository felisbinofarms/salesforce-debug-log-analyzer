# GitHub Project Management Setup Guide

This guide sets up a complete project tracking system for Black Widow using GitHub Issues and Projects.

## Quick Setup Checklist

- [ ] Create issue templates (DONE - see .github/ISSUE_TEMPLATE/)
- [ ] Create GitHub labels
- [ ] Set up GitHub Project (Kanban board)
- [ ] Create milestones
- [ ] Create starter issues
- [ ] Set up branch protection

---

## Step 1: Create GitHub Labels

Go to: https://github.com/YOUR_USERNAME/log_analyser/labels

Click **New label** and create the following:

### Type Labels
| Name | Color | Description |
|------|-------|-------------|
| `feature` | `#10B981` (green) | New feature or request |
| `bug` | `#EF4444` (red) | Something isn't working |
| `documentation` | `#3B82F6` (blue) | Documentation improvements |
| `enhancement` | `#FBBF24` (yellow) | Improvement to existing feature |
| `user-story` | `#8B5CF6` (purple) | User story with acceptance criteria |
| `refactor` | `#6B7280` (gray) | Code refactoring without behavior change |
| `test` | `#14B8A6` (teal) | Testing improvements |

### Priority Labels
| Name | Color | Description |
|------|-------|-------------|
| `P0-critical` | `#7F1D1D` (dark red) | Drop everything, fix now |
| `P1-high` | `#F97316` (orange) | This sprint / MVP blocker |
| `P2-medium` | `#FCD34D` (yellow) | Next sprint / Important |
| `P3-low` | `#D1D5DB` (light gray) | Backlog / Nice to have |

### Size Labels (Effort Estimation)
| Name | Color | Description |
|------|-------|-------------|
| `size:XS` | `#DBEAFE` (light blue) | 1-2 hours |
| `size:S` | `#93C5FD` (blue) | 2-4 hours |
| `size:M` | `#60A5FA` (medium blue) | 1-2 days |
| `size:L` | `#3B82F6` (darker blue) | 3-5 days |
| `size:XL` | `#1E40AF` (dark blue) | 1+ week |

### Status Labels
| Name | Color | Description |
|------|-------|-------------|
| `status:ready` | `#10B981` (green) | Ready to work on |
| `status:in-progress` | `#F59E0B` (amber) | Currently being worked on |
| `status:needs-review` | `#8B5CF6` (purple) | Needs code review |
| `status:blocked` | `#EF4444` (red) | Blocked by dependency |

### Special Labels
| Name | Color | Description |
|------|-------|-------------|
| `marketplace` | `#EC4899` (pink) | Consulting marketplace feature |
| `free-tier` | `#A78BFA` (light purple) | Free tier feature |
| `pro-tier` | `#FBBF24` (gold) | Pro tier feature |
| `enterprise` | `#6366F1` (indigo) | Enterprise tier feature |
| `breaking-change` | `#DC2626` (red) | Breaking API or UI change |
| `good-first-issue` | `#22C55E` (green) | Good for newcomers |

---

## Step 2: Create GitHub Project (Kanban Board)

1. Go to: https://github.com/YOUR_USERNAME/log_analyser/projects
2. Click **New project**
3. Choose **Board** view
4. Name it: **Black Widow Roadmap**
5. Add columns (in this order):
   - 📋 **Backlog** - "Ideas and unrefined issues"
   - ✅ **Ready** - "Ready to work on"
   - 🚧 **In Progress** - "Currently being worked on"
   - 👀 **In Review** - "Waiting for code review"
   - ✔️ **Done** - "Completed"

### Configure Automation (Optional but Recommended)

- **Backlog**: When issue is created → Add to Backlog
- **In Progress**: When assignee is set → Move to In Progress
- **In Review**: When PR is opened → Move to In Review
- **Done**: When PR is merged → Move to Done

---

## Step 3: Create Milestones

Go to: https://github.com/YOUR_USERNAME/log_analyser/milestones

Create these milestones:

### v1.0 - MVP Launch
- **Due Date:** March 1, 2026
- **Description:** Free + Pro tiers, transaction grouping, basic marketplace (manual)
- **Key Features:**
  - License validation system
  - Upgrade flow UI
  - Stripe payment integration
  - Basic partner dashboard
  - Manual marketplace matching

### v1.1 - Marketplace Automation
- **Due Date:** April 1, 2026
- **Description:** Automated bids, escrow, validation system
- **Key Features:**
  - Automated bid system
  - Stripe escrow payments
  - Post-fix validation
  - Partner reputation system
  - Technical specification generator

### v1.2 - Scale & Growth
- **Due Date:** June 1, 2026
- **Description:** Team tier, CLI streaming, governance detection
- **Key Features:**
  - Team tier ($99/month for 5 users)
  - Real-time CLI streaming
  - Mixed context detection UI
  - Export governance reports
  - Vocational school partnerships

### v2.0 - Enterprise & AppExchange
- **Due Date:** September 1, 2026
- **Description:** Enterprise tier, AppExchange listing, SSO
- **Key Features:**
  - SSO (SAML/OAuth)
  - On-premise deployment
  - White-label option
  - AppExchange security review
  - Multi-language support

---

## Step 4: Create Starter Issues

Copy these into GitHub Issues (one at a time):

### MVP Critical Path Issues (P0-P1)

#### Issue 1: License Validation System
```markdown
**Title:** [FEATURE] Implement license validation system

**Labels:** feature, P1-high, size:L, pro-tier, v1.0

**Description:**
Implement online license validation to enforce Free vs Pro tier limits.

**User Story:**
As a Pro user, I want my license validated automatically so that I can access premium features without manual intervention.

**Acceptance Criteria:**
- [ ] Check license status every 30 days (not every launch)
- [ ] Allow 2 devices per Pro license
- [ ] 7-day grace period if offline
- [ ] Clear error messages for expired/invalid licenses
- [ ] Store last validation timestamp in local settings
- [ ] Handle network errors gracefully

**Implementation Notes:**
- Create Services/LicenseService.cs
- Store license in encrypted local file
- Use HTTPS API endpoint (build after Stripe integration)
- Consider using JWT for license tokens
```

#### Issue 2: Upgrade Flow UI
```markdown
**Title:** [FEATURE] Build upgrade flow from Free to Pro

**Labels:** feature, P1-high, size:M, pro-tier, v1.0

**Description:**
One-click upgrade flow with 14-day free trial (no credit card required upfront).

**User Story:**
As a Free tier user, I want to upgrade to Pro in <5 clicks so that I can access transaction grouping immediately.

**Acceptance Criteria:**
- [ ] Upgrade button visible in main window
- [ ] Modal shows feature comparison (Free vs Pro)
- [ ] "Start 14-day trial" button (no credit card)
- [ ] "Buy now" button ($29/month or $249/year)
- [ ] Redirect to Stripe Checkout
- [ ] Handle success/cancel callbacks
- [ ] Display trial days remaining

**Implementation Notes:**
- Create Views/UpgradeDialog.xaml
- Integrate with LicenseService
- Show trial expiration in status bar
```

#### Issue 3: Stripe Payment Integration
```markdown
**Title:** [FEATURE] Integrate Stripe for subscription payments

**Labels:** feature, P1-high, size:L, pro-tier, v1.0

**Description:**
Accept payments via Stripe Checkout for Pro/Team subscriptions.

**Acceptance Criteria:**
- [ ] Create Stripe account (production + test mode)
- [ ] Set up subscription products ($29/month, $249/year, $99/month Team)
- [ ] Implement Stripe Checkout (hosted page)
- [ ] Handle webhooks (payment.succeeded, subscription.updated, subscription.cancelled)
- [ ] Generate and deliver license keys
- [ ] Send email receipts
- [ ] Build billing portal link (manage subscription)

**Implementation Notes:**
- Create Services/StripeService.cs
- Use Stripe.NET NuGet package
- Store webhook secret in configuration
- Consider Stripe Connect for marketplace escrow (v1.1)
```

#### Issue 4: Partner Dashboard UI
```markdown
**Title:** [FEATURE] Build consultant partner dashboard

**Labels:** feature, P1-high, size:L, marketplace, v1.0

**Description:**
Web-based dashboard for consulting partners to view leads and submit bids.

**User Story:**
As a consulting partner, I want to see available projects so that I can bid on ones matching my expertise.

**Acceptance Criteria:**
- [ ] Authentication (email + password or SSO)
- [ ] Lead inbox (shows problem summary, user tier, budget estimate)
- [ ] Submit bid (price, timeline, approach)
- [ ] Project status tracking
- [ ] Payment history
- [ ] Reputation score display

**Implementation Notes:**
- Separate ASP.NET Core web app (new project)
- Share data models with WPF app
- Use Entity Framework Core + SQL Server
- Deploy to Azure App Service
```

#### Issue 5: Transaction Grouping UI
```markdown
**Title:** [FEATURE] Display transaction groups in main window

**Labels:** feature, P1-high, size:M, pro-tier, v1.0

**Description:**
Show LogGroup objects (multiple related logs) in main UI with aggregate metrics.

**User Story:**
As a Senior Architect, I want to see all logs from one user action grouped together so that I can understand the full transaction.

**Acceptance Criteria:**
- [ ] Group header shows total duration, user, timestamp
- [ ] Expandable list shows individual logs
- [ ] Phase breakdown (Backend vs Frontend)
- [ ] Recommendations displayed prominently
- [ ] Export transaction report (PDF)

**Implementation Notes:**
- Update MainWindow.xaml with GroupBox
- Use LogGroupService (already implemented)
- Add timeline visualization (optional v1.1)
```

### Important But Not MVP (P2)

#### Issue 6: Context Badge UI
```markdown
**Title:** [ENHANCEMENT] Add execution context badges to log list

**Labels:** enhancement, P2-medium, size:S, v1.1

**Description:**
Show color-coded badges (Interactive/Batch/Integration/Scheduled/Async) next to each log.

**User Story:**
As a user, I want to quickly identify my UI actions vs background processes so that I can focus on the right logs.

**Acceptance Criteria:**
- [ ] 5 badge colors (green=Interactive, blue=Batch, orange=Integration, purple=Scheduled, red=Async)
- [ ] Tooltip explains context type
- [ ] Filter by context (checkbox or dropdown)

**Implementation Notes:**
- ExecutionContext enum already exists
- Update MainWindow.xaml with badge control
- Use FontAwesome icons (optional)
```

#### Issue 7: Export Governance Report
```markdown
**Title:** [FEATURE] Export governance violation report as PDF

**Labels:** feature, P2-medium, size:M, v1.2

**Description:**
Generate PDF report showing mixed context warnings and recommendations.

**User Story:**
As a team lead, I want to export a report so that I can share findings with my development team.

**Acceptance Criteria:**
- [ ] Export button in toolbar
- [ ] PDF includes: transaction summary, phase breakdown, recommendations, governor limit usage
- [ ] Professional formatting (company logo placeholder)
- [ ] Save dialog (choose location)

**Implementation Notes:**
- Use QuestPDF or iTextSharp NuGet package
- Template for consistent formatting
```

### Future Enhancements (P3)

#### Issue 8: Voice Command Interface
```markdown
**Title:** [FEATURE] Add voice commands for hands-free navigation

**Labels:** feature, P3-low, size:XL, v2.0

**Description:**
"Show me trigger recursion" or "Find logs longer than 5 seconds"

**User Story:**
As a user presenting findings to stakeholders, I want to navigate with voice commands so that I can keep focus on the screen.

**Acceptance Criteria:**
- [ ] Windows Speech Recognition integration
- [ ] 10-15 common commands
- [ ] Visual feedback (mic icon)
- [ ] Settings to enable/disable

**Implementation Notes:**
- System.Speech.Recognition namespace
- Train custom grammar
```

#### Issue 9: Multi-Language Support
```markdown
**Title:** [FEATURE] Add Spanish, French, German translations

**Labels:** feature, P3-low, size:XL, enterprise, v2.0

**Description:**
Internationalize UI for global Salesforce customers.

**User Story:**
As a French-speaking admin, I want the UI in French so that I can use the tool more efficiently.

**Acceptance Criteria:**
- [ ] Resource files for 4 languages
- [ ] Language selector in settings
- [ ] All UI strings translated (not code comments)

**Implementation Notes:**
- Use .resx files
- Consider Crowdin for translation management
```

---

## Step 5: Set Up Branch Protection (Do on GitHub Web UI)

1. Go to: https://github.com/YOUR_USERNAME/log_analyser/settings/branches
2. Click **Add branch protection rule**
3. Branch name pattern: `master`
4. Enable these settings:
   - ✅ **Require a pull request before merging**
     - Required approvals: 1
     - ✅ Dismiss stale pull request approvals when new commits are pushed
   - ✅ **Require conversation resolution before merging**
   - ✅ **Require linear history** (optional - keeps clean git log)
   - ✅ **Do not allow bypassing the above settings** (even for admins)

5. Repeat for `develop` branch (optional - lighter rules):
   - ✅ Require a pull request before merging
   - Required approvals: 0 (allow self-merge for solo work)

---

## Step 6: Start Using the System

### Daily Workflow:
1. Check **Black Widow Roadmap** project board
2. Move next Ready issue to In Progress
3. Create feature branch: `git checkout -b feature/issue-1-license-validation`
4. Work on issue, commit regularly
5. Push branch: `git push origin feature/issue-1-license-validation`
6. Open PR: https://github.com/YOUR_USERNAME/log_analyser/compare
7. Add description, link to issue (#1), request review
8. Merge after approval
9. Move card to Done
10. Delete feature branch

### Idea Capture Workflow (For "Inspired Nights"):
1. Open GitHub Issues on phone/laptop
2. Create new issue (use templates)
3. Add labels (priority, size)
4. Assign to milestone (or leave in backlog)
5. Go to sleep knowing it's captured
6. Review backlog weekly, prioritize top 3

---

## Tips for Success

### Keep Issues Small
- One issue = One PR (1-200 lines changed)
- Large features → Break into 3-5 smaller issues
- Label large issues with `size:XL` and create sub-issues

### Update Status Frequently
- Move cards as you work
- Comment on issues with progress updates
- Close issues when truly done (tests pass, documented)

### Use Labels Consistently
- Every issue needs: Type (feature/bug), Priority (P0-P3), Size (XS-XL)
- Add tier labels (free-tier, pro-tier, enterprise) for monetization clarity
- Use `marketplace` label for consulting features

### Review Backlog Weekly
- Sunday evening: Review new issues
- Prioritize top 3 for coming week
- Move to Ready column
- Archive P3 issues older than 6 months (if no traction)

### Milestone Management
- Don't overfill milestones (better to under-promise)
- Review milestone progress every 2 weeks
- Move issues to next milestone if slipping
- Celebrate milestone completion! 🎉

---

## Quick Copy-Paste Commands

### Create all labels at once (GitHub CLI):
```bash
# Install GitHub CLI first: https://cli.github.com/

# Type labels
gh label create "feature" --color 10B981 --description "New feature or request"
gh label create "bug" --color EF4444 --description "Something isn't working"
gh label create "documentation" --color 3B82F6 --description "Documentation improvements"
gh label create "enhancement" --color FBBF24 --description "Improvement to existing feature"
gh label create "user-story" --color 8B5CF6 --description "User story with acceptance criteria"

# Priority labels
gh label create "P0-critical" --color 7F1D1D --description "Drop everything, fix now"
gh label create "P1-high" --color F97316 --description "This sprint / MVP blocker"
gh label create "P2-medium" --color FCD34D --description "Next sprint / Important"
gh label create "P3-low" --color D1D5DB --description "Backlog / Nice to have"

# Size labels
gh label create "size:XS" --color DBEAFE --description "1-2 hours"
gh label create "size:S" --color 93C5FD --description "2-4 hours"
gh label create "size:M" --color 60A5FA --description "1-2 days"
gh label create "size:L" --color 3B82F6 --description "3-5 days"
gh label create "size:XL" --color 1E40AF --description "1+ week"

# Status labels
gh label create "status:ready" --color 10B981 --description "Ready to work on"
gh label create "status:in-progress" --color F59E0B --description "Currently being worked on"
gh label create "status:needs-review" --color 8B5CF6 --description "Needs code review"
gh label create "status:blocked" --color EF4444 --description "Blocked by dependency"

# Special labels
gh label create "marketplace" --color EC4899 --description "Consulting marketplace feature"
gh label create "free-tier" --color A78BFA --description "Free tier feature"
gh label create "pro-tier" --color FBBF24 --description "Pro tier feature"
gh label create "enterprise" --color 6366F1 --description "Enterprise tier feature"
```

---

## Done! 🎉

You now have a complete project management system for Black Widow. Use it to:
- ✅ Capture all ideas (even at 2am)
- ✅ Prioritize realistically
- ✅ Track progress visibly
- ✅ Stay focused on deliverables
- ✅ Ship features consistently

**Next:** Create your first issue and move it to "Ready"! 🚀
