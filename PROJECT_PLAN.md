# Black Widow - Project Plan
**Last Updated:** February 2, 2026  
**Team:** 2 Senior Developers (Salesforce + .NET Architecture)  
**Target Launch:** March 15, 2026 (6 weeks)

---

## 🎯 Project Overview

**What We're Building:**
Salesforce debug log analyzer that groups related logs from a single transaction (e.g., saving a Case triggers 13 logs) and shows the complete user experience journey.

**Why It's Different:**
- Groups multi-log transactions (competitors analyze one log at a time)
- Detects backend (triggers/flows) vs frontend (component loading) phases
- Identifies recursion patterns and generates smart recommendations
- CLI streaming integration for real-time log analysis

**Current Status:**
- ✅ MVP core functionality complete (parsing, grouping, phase detection)
- ✅ Architecture validated (0 errors, 2 harmless warnings)
- ✅ Performance tested (19MB log in <3s, 100 logs in <2s)
- 🔄 **Next:** Monetization (licensing + payment integration)

---

## 📅 Timeline & Milestones

### Week 1-2: Monetization Foundation (Feb 3-16)
**Goal:** Users can purchase Pro licenses and access premium features

#### Sprint 1.1 (Feb 3-9)
- [ ] **License validation system** (#1) - 3 days
  - Local license storage (AES-256 encrypted)
  - Online validation API (check every 30 days)
  - Device fingerprinting (2 devices per license)
  - 7-day offline grace period
- [ ] **Upgrade flow UI** (#2) - 2 days
  - Feature comparison modal
  - "Upgrade" button in toolbar
  - Trial expiration warning
  - Stripe Checkout integration

#### Sprint 1.2 (Feb 10-16)
- [ ] **Stripe payment integration** (#3) - 3 days
  - Webhook handler (payment → license provisioning)
  - Customer Portal integration
  - Subscription management
- [ ] **Settings dialog** (#19) - 2 days
  - License info display
  - Connection persistence
  - Preferences (theme, auto-update)

### Week 3-4: Polish & Marketplace (Feb 17 - Mar 2)
**Goal:** Professional product ready for beta users

#### Sprint 2.1 (Feb 17-23)
- [ ] **Installer/deployment** (#6) - 3 days
  - WiX installer (Windows)
  - Auto-update mechanism
  - Custom URL scheme registration
- [ ] **Marketplace infrastructure** (#7) - 2 days
  - Extension packaging format
  - Submission wizard
  - Revenue sharing (70/30 split)

#### Sprint 2.2 (Feb 24 - Mar 2)
- [ ] **Sample extensions** (#8-10) - 3 days
  - Test coverage analyzer
  - SOQL query optimizer
  - CPU profiler
- [ ] **Documentation** (#11) - 2 days
  - User guide
  - Extension developer docs
  - Video tutorials

### Week 5-6: Beta Testing & Launch (Mar 3-15)
**Goal:** 10 beta users, collect feedback, public launch

#### Sprint 3.1 (Mar 3-9)
- [ ] **Beta program** - 3 days
  - Recruit 10 beta testers (Reddit, LinkedIn)
  - Feedback collection system
  - Bug fixing based on feedback
- [ ] **Telemetry/analytics** (#14) - 2 days
  - Error reporting (Sentry)
  - Usage analytics (PostHog)
  - Crash dump collection

#### Sprint 3.2 (Mar 10-15)
- [ ] **Marketing materials** - 2 days
  - Website landing page
  - Demo video (3 minutes)
  - Reddit/LinkedIn announcement posts
- [ ] **Public launch** - 1 day
  - Deploy v1.0
  - Send beta users upgrade codes
  - Post on r/salesforce, r/dotnet

---

## 🎯 Feature Prioritization

### Must-Have (v1.0 - Launch Blockers)
1. ✅ Core parsing & transaction grouping
2. ✅ Phase detection (backend vs frontend)
3. ✅ Architecture validation
4. 🔄 License validation (#1)
5. 🔄 Upgrade flow UI (#2)
6. 🔄 Stripe payment integration (#3)
7. 🔄 Installer/deployment (#6)

### Should-Have (v1.1 - First Update)
8. 🔄 Settings dialog (#19)
9. 🔄 Marketplace infrastructure (#7)
10. 🔄 Sample extensions (#8-10)
11. 🔄 ViewModel unit tests (#20)
12. 🔄 Telemetry/analytics (#14)

### Nice-to-Have (v2.0+ - Future)
13. ⏳ Multi-language support (#12)
14. ⏳ Team collaboration features (#15)
15. ⏳ Cloud sync (#16)
16. ⏳ Advanced visualizations (#17)

---

## 🎭 Roles & Responsibilities

### Developer #1 (You)
**Focus:** Backend, Services, Architecture
- License validation system
- Stripe integration
- Marketplace infrastructure
- Extension API design

### Developer #2 (Partner)
**Focus:** UI, ViewModels, UX
- Upgrade flow UI
- Settings dialog
- Installer/deployment
- Marketing materials

### GitHub Copilot (Project Manager)
**Focus:** Keep team on track, prevent rabbit holes
- Daily standup prompts
- Feature scope enforcement
- Timeline tracking
- Code review reminders

---

## 🚧 Risk Management

### High Risk
| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Stripe integration complexity | Medium | High | Use Stripe.NET SDK, start early |
| Installer issues (Windows Registry) | High | Medium | Test on fresh VMs, use WiX |
| Scope creep (new features) | High | High | **Copilot PM enforces scope** |

### Medium Risk
| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Beta users don't provide feedback | Medium | Medium | Incentivize with free Pro licenses |
| Performance issues with 500+ logs | Low | High | Load testing, optimize if needed |
| Partner availability (other commitments) | Medium | Medium | Async communication, clear tasks |

---

## 📊 Success Metrics

### Launch Goals (v1.0)
- [ ] 10 beta users complete testing
- [ ] <5 critical bugs reported
- [ ] 3 paying customers in first week
- [ ] <3s parse time for 20MB logs
- [ ] 95% uptime for license API

### 3-Month Goals (Post-Launch)
- [ ] 50 paying Pro users ($1,450/mo MRR)
- [ ] 5 marketplace extensions submitted
- [ ] 4.5+ rating on G2/Capterra
- [ ] 1000 Reddit post upvotes
- [ ] Featured on Salesforce Ben blog

---

## 🔧 Development Workflow

### Daily Routine
1. **Morning Standup** (async Slack message)
   - What did you complete yesterday?
   - What are you working on today?
   - Any blockers?
   - **Copilot PM:** Reviews progress, flags scope creep

2. **Focus Time** (4-6 hours)
   - Deep work on assigned feature
   - No Slack/email during focus blocks

3. **End-of-Day Sync** (15 min call or async)
   - Demo progress
   - Update GitHub project board
   - Plan tomorrow's tasks

### Code Review Process
- All PRs reviewed within 24 hours
- Run `dotnet build` and `dotnet test` before PR
- Use conventional commits: `feat:`, `fix:`, `docs:`
- **Copilot:** Review for scope creep and over-engineering

### Communication
- **Slack:** Daily updates, quick questions
- **Zoom:** Weekly sync (Mondays), ad-hoc pair programming
- **GitHub:** All feature discussions, technical decisions
- **Copilot PM Prompt:** Type `/pm standup` or `/pm review` in chat

---

## 🎯 Feature Breakdown (from ISSUES_BACKLOG.md)

### P0 - Critical (v1.0 Launch Blockers)
- #1 License validation system (3 days)
- #2 Upgrade flow UI (2 days)
- #3 Stripe payment integration (3 days)
- #6 Installer/deployment (3 days)

### P1 - High (v1.1 First Update)
- #7 Marketplace infrastructure (2 days)
- #8-10 Sample extensions (3 days)
- #14 Telemetry/analytics (2 days)
- #19 Settings dialog (2 days)

### P2 - Medium (v1.2-v2.0)
- #11 User guide & docs (2 days)
- #20 ViewModel unit tests (3 days)
- #12 Multi-language support (5 days)
- #15 Team collaboration (10 days)

### P3 - Low (Future)
- #16 Cloud sync (7 days)
- #17 Advanced visualizations (5 days)
- #18 Custom reports (5 days)
- #21-22 AI features (15 days)

---

## 📝 Archive Strategy

### Keep These Docs (Active)
- `README.md` - Quick start, setup instructions
- `PROJECT_PLAN.md` - This file (timelines, milestones)
- `ISSUES_BACKLOG.md` - Detailed feature specs (reference)
- `GITHUB_PROJECT_SETUP.md` - GitHub Projects setup
- `copilot-instructions.md` - Copilot context

### Archive These Docs (Completed/Obsolete)
Move to `.archive/` folder:
- `STATUS_REPORT.md` → Replaced by PROJECT_PLAN.md
- `ARCHITECTURE_VALIDATION.md` → Architecture reviewed, done
- `ARCHITECTURE_REVIEW_SUMMARY.md` → Covered in PROJECT_PLAN.md
- `TECH_DEBT_ELIMINATED.md` → Historical record, not needed daily
- `FOR_MY_WIFE.md` → Partner is technical, not needed
- `BUSINESS_DOCS_INDEX.md` → Outdated index
- `PARTNERSHIP_PROPOSAL.md` → Not pursuing partnership model
- `MARKETPLACE_STRATEGY.md` → Merged into PROJECT_PLAN.md
- `PRICING_STRATEGY.md` → Merged into PROJECT_PLAN.md
- `PLAIN_ENGLISH_FEATURES.md` → Technical partner, not needed
- `PROJECT_REVIEW_FEB_2_2026.md` → Superseded by PROJECT_PLAN.md
- `CODE_REVIEW.md`, `CODE_REVIEW_SUMMARY.md` → One-time reviews
- `COMPLETE_REVIEW.md`, `FIXES_COMPLETE.md` → Historical
- `APP_REVIEW.md` → Historical

### Keep for Reference (No Archive)
- `OAUTH_SETUP.md` - Technical reference
- `TESTING_GUIDE.md` - Developer reference
- `PARSING_WALKTHROUGH.md` - Technical deep-dive
- `EXAMPLE_OUTPUT.md` - Sample data
- `ROADMAP.md` - High-level vision

---

## 🤖 Copilot as Project Manager

### How to Use Copilot PM

Invoke PM mode with these prompts in GitHub Copilot Chat:

#### Daily Standup
```
/pm standup

What I completed yesterday: [your answer]
What I'm working on today: [your answer]
Blockers: [your answer]
```

**Copilot will:**
- Review your progress against PROJECT_PLAN.md timeline
- Flag if you're off-track or scope creeping
- Suggest next highest-priority task
- Warn if approaching deadline

#### Scope Check
```
/pm scope-check

I want to add [feature description]
```

**Copilot will:**
- Check if feature is in ISSUES_BACKLOG.md
- Assess priority (P0-P3)
- Recommend deferring to future milestone if not critical
- Suggest simpler MVP approach

#### Code Review
```
/pm review

[paste code or file path]
```

**Copilot will:**
- Check for over-engineering (YAGNI principle)
- Flag premature optimization
- Verify it solves the issue requirement
- Suggest simplifications

#### Timeline Check
```
/pm timeline
```

**Copilot will:**
- Show current sprint progress (X/Y tasks complete)
- Calculate days until milestone deadline
- Flag at-risk features
- Suggest reordering tasks

#### Focus Mode
```
/pm focus

[task description]
```

**Copilot will:**
- Set 2-hour focus block
- Mute low-priority suggestions
- Only flag critical issues
- Remind you of acceptance criteria

---

## 🎓 Definition of Done

A feature is "done" when:
- [ ] All acceptance criteria met (from ISSUES_BACKLOG.md)
- [ ] Unit tests pass (`dotnet test`)
- [ ] Build succeeds with 0 errors (`dotnet build`)
- [ ] Code reviewed by partner
- [ ] Documented (inline comments + README if needed)
- [ ] Tested manually (happy path + error cases)
- [ ] GitHub issue closed with demo screenshot/video

---

## 📞 Communication Protocols

### When to Slack
- Quick questions (<5 min to answer)
- Daily standup updates
- "I'm blocked on X"
- "PR ready for review"

### When to Zoom
- Design discussions (>30 min)
- Pair programming sessions
- Debugging complex issues
- Weekly sync (Mondays 10am)

### When to Use GitHub
- All feature requests
- Bug reports
- Technical decisions (document for future)
- Code reviews (PR comments)

### When to Use Copilot PM
- Before starting any new task
- When tempted by "shiny new feature"
- End of day progress check
- Stuck on implementation approach

---

## 🚀 Launch Checklist

### Week Before Launch (Mar 8-14)
- [ ] All P0 features complete and tested
- [ ] Beta feedback incorporated
- [ ] Installer tested on fresh Windows 10/11 VMs
- [ ] License API deployed to production
- [ ] Stripe webhooks tested (use Stripe CLI)
- [ ] Landing page live (blackwidow.dev)
- [ ] Demo video uploaded to YouTube
- [ ] Reddit post drafted (r/salesforce)
- [ ] LinkedIn announcement drafted

### Launch Day (Mar 15)
- [ ] Deploy v1.0 to GitHub Releases
- [ ] Update download link on website
- [ ] Post to r/salesforce (best time: 9am ET)
- [ ] Post to r/dotnet
- [ ] Post to LinkedIn
- [ ] Email beta users with 50% off code
- [ ] Monitor Sentry for crashes
- [ ] Monitor Stripe dashboard for purchases
- [ ] Respond to Reddit comments within 1 hour

### Week After Launch (Mar 16-22)
- [ ] Daily bug fixes (hotfix releases if needed)
- [ ] Collect user feedback (survey)
- [ ] Update roadmap based on feedback
- [ ] Plan v1.1 features
- [ ] Calculate MRR, churn rate
- [ ] Celebrate! 🎉🕷️

---

## 📚 Quick Reference

**Project Board:** https://github.com/felisbinofarms/salesforce-debug-log-analyzer/projects  
**Issues Backlog:** [ISSUES_BACKLOG.md](./ISSUES_BACKLOG.md)  
**Architecture:** [ARCHITECTURE_VALIDATION.md](./.archive/ARCHITECTURE_VALIDATION.md)  
**Setup Instructions:** [README.md](./README.md)

**Copilot PM Prompts:**
- `/pm standup` - Daily check-in
- `/pm scope-check` - Prevent scope creep
- `/pm timeline` - Progress check
- `/pm focus` - Deep work mode

**Quick Commands:**
```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run

# Create installer
dotnet publish -c Release -r win-x64 --self-contained
```

---

**Remember:** Ship fast, iterate based on real user feedback. Don't build features users don't need. Let Copilot PM keep you honest! 🕷️
