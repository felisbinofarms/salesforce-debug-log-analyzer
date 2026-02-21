# Black Widow — Implementation Roadmap

## Vision
**The first Salesforce debug log analyzer that translates technical logs into plain English, making debugging accessible to everyone — from junior admins to senior architects.**

---

## ✅ Phase 1: Foundation (COMPLETE)

### Core Infrastructure
- [x] WPF .NET 8 project with Discord-themed dark UI
- [x] MVVM architecture with CommunityToolkit.Mvvm
- [x] Connection management with saved credentials
- [x] OAuth 2.0 + PKCE flow with embedded WebView2 browser
- [x] Salesforce Tooling API integration (v60.0)

### Authentication & API
- [x] OAuth browser dialog with SSO/MFA support
- [x] Platform CLI client ID integration (no Connected App needed)
- [x] HttpListener callback handler for OAuth redirect
- [x] Connection persistence (JSON + encrypted tokens)
- [x] Org name resolution for friendly display

### Debug Log Management
- [x] Trace flag creation dialog (user, debug level, duration)
- [x] Active trace flag view with delete
- [x] Debug level creation with granular log categories
- [x] 4-step Debug Setup Wizard

---

## ✅ Phase 2: Plain-English Translation Engine (COMPLETE)

### Parser Foundation
- [x] Comprehensive log parsing — all event types:
  - CODE_UNIT_STARTED/FINISHED, METHOD_ENTRY/EXIT
  - SOQL_EXECUTE_BEGIN/END, DML_BEGIN/END
  - EXCEPTION_THROWN, VALIDATION_RULE
  - CUMULATIVE_LIMIT_USAGE, USER_DEBUG, and more
- [x] Execution tree building with stack-based hierarchy
- [x] Database operation extraction (SOQL and DML)
- [x] Governor limit tracking with snapshots
- [x] Method statistics (call count, duration, hotspots)

### Plain-English Generation (THE DIFFERENTIATOR)
- [x] Conversational summaries — "What Happened", "What Your Code Did", "Performance", "Result"
- [x] Human-readable time formatting ("2.5 seconds", not "2500ms")
- [x] Contextual performance assessment
- [x] Real-world analogies ("like making 87 phone calls instead of one")
- [x] Before/after code examples for every recommendation
- [x] Actionable fixes, not just problem descriptions

### Actionable Issue Classification
- [x] Severity scoring (Critical / High / Quick Win)
- [x] Role badges (Developer / Admin / Architect)
- [x] Difficulty badges (Easy / Medium / Hard)
- [x] Estimated fix time per issue
- [x] Health score (0–100) for the overall transaction

---

## ✅ Phase 3: Visualization & Transaction Analysis (COMPLETE)

### Five Analysis Tabs
- [x] **Summary tab** — health score, critical issues, high-priority issues, quick wins
- [x] **Explain tab** — full conversational narrative with sections and formatting
- [x] **Tree tab** — hierarchical execution tree (method calls, SOQL, DML, exceptions)
- [x] **Timeline tab** — Gantt chart visualization of execution phases
- [x] **Queries tab** — database operations grid with row counts and duration

### Transaction Chain Analysis
- [x] Log metadata extraction for fast folder scanning (first/last lines only)
- [x] Transaction grouping by user and 10-second timing window
- [x] Phase detection — Backend (triggers/flows) vs Frontend (Lightning components)
- [x] Re-entry pattern detection (trigger recursion)
- [x] Sequential vs parallel component loading detection
- [x] Aggregate metrics across all logs in a group
- [x] Smart recommendations for transaction-level optimization
- [x] Folder drag-and-drop with automatic grouping

### Governance Insights
- [x] Execution context classification (Interactive, Batch, Integration, Scheduled, Async)
- [x] Mixed context detection — flags when one user is doing multiple things
- [x] Educational governance warning banner (warm, not alarming)
- [x] Plain-English context breakdown with emoji icons
- [x] Dedicated user recommendations with concrete examples
- [x] Best practice callout ("This keeps your logs clean and troubleshooting 10x easier")

---

## 🔄 Phase 4: Advanced Features (IN PROGRESS)

### Done
- [x] Salesforce CLI integration — real-time streaming via `sf apex tail log`
- [x] CLI auto-detection (sf and sfdx)
- [x] Streaming options dialog (user, org alias, filters)
- [x] Settings dialog (6 tabs: General, Connection, Appearance, Analysis, Privacy, About)
- [x] Settings persistence (JSON, encrypted sensitive fields)
- [x] InsightsPanel — governance recommendations sidebar
- [x] LicenseService — AES-256 encrypted local license storage, device fingerprinting

### In Progress
- [ ] Raw log viewer with syntax highlighting (AvalonEdit)
- [ ] Export analysis to PDF (QuestPDF)
- [ ] Export to HTML with embedded charts

### Planned for This Phase
- [ ] Side-by-side log comparison (before/after optimization)
- [ ] Bulk log analysis report (summarize 50+ logs at once)
- [ ] Jump-to-line from Tree/Timeline to raw log

---

## 📋 Phase 5: Distribution & Monetization (PLANNED)

### Distribution
- [ ] Windows installer (WiX or NSIS)
- [ ] Auto-update mechanism
- [ ] Custom URL scheme registration (`blackwidow://`)
- [ ] GitHub Releases pipeline

### Monetization
- [ ] Stripe payment integration (subscription checkout)
- [ ] Upgrade flow UI — feature comparison modal, trial CTA
- [ ] Backend license validation API (30-day check, device fingerprinting)
- [ ] Trial enforcement and expiration warnings
- [ ] Feature gating (Free vs Pro tier)
- [ ] Stripe webhook handler (payment → license provisioning)

> **Note:** LicenseService and UpgradeDialog scaffolding already exist.
> Next step is wiring Stripe Checkout and deploying the validation API.

---

## 🔮 Phase 6: AI & Learning Enhancement (FUTURE)

### Translation Modes
- [ ] Beginner mode — maximum explanation, no jargon
- [ ] Expert mode — concise summaries, technical detail on demand
- [ ] Mode toggle per session

### Interactive Explanations
- [ ] Click any technical term for a plain-English definition
- [ ] "Why is this slow?" contextual button per issue
- [ ] "How do I fix this?" with org-specific code suggestions
- [ ] Tooltips with real-world analogies

### AI-Powered Insights
- [ ] LLM integration (OpenAI or local model) for dynamic explanations
- [ ] "Ask AI about this log" chat panel
- [ ] Root cause analysis — not just *what* failed, but *why*
- [ ] Pattern learning from repeated org issues
- [ ] Azure OpenAI option for Enterprise (data stays in tenant)

### Learning Mode
- [ ] Every analysis becomes a teaching moment
- [ ] Expandable "What is an N+1 query?" sections
- [ ] Links to relevant Trailhead modules
- [ ] Before/after performance improvement tracking

---

## Release History & Plan

| Version | Status | Highlights |
|---|---|---|
| v0.1-alpha | ✅ Released | Core parser, plain-English summaries, OAuth |
| v0.5-beta | ✅ Released | Transaction grouping, phase detection, 5 tabs |
| **v0.9-beta** | **🟢 Current** | Governance insights, CLI streaming, all features stable |
| v1.0 | 📋 Planned | Windows installer, Stripe payments, Pro tier |
| v1.1 | 📋 Planned | PDF export, side-by-side comparison, bulk analysis |
| v2.0 | 🔮 Future | AI explanations, learning mode, translation levels |

---

## Competitive Differentiation

| Feature | Black Widow | Traditional Tools |
|---|---|---|
| Plain-English summaries | ✅ First-class | ❌ Not available |
| Real-world analogies | ✅ Throughout | ❌ Technical jargon only |
| Transaction chain grouping | ✅ Automatic | ❌ One log at a time |
| Governance context detection | ✅ Built-in | ❌ Not available |
| Before/after code examples | ✅ Per issue | ⚠️ Generic suggestions |
| Target audience | Everyone | Developers only |
| Setup complexity | ✅ Zero (Platform CLI) | ⚠️ Connected App required |

---

**Last Updated:** February 20, 2026
**Current Version:** v0.9-beta
**Next Milestone:** v1.0 — Windows installer + Pro tier payments (Phase 5)