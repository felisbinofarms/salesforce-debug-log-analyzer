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
- [x] Export analysis to PDF/JSON/TXT (QuestPDF via ReportExportService)
- [x] PII scanner — detects sensitive data in debug logs
- [x] Shield anomaly detection — security event monitoring
- [x] Shield event log CSV parsing
- [x] Trend analysis service — historical pattern detection
- [x] Alert routing and center UI
- [x] Background monitoring service
- [x] System tray integration (NotifyIcon)
- [x] Toast notification service
- [x] Monitoring database (SQLite)
- [x] LogCompare QA tool — scripted vs AI comparison workflow

### In Progress
- [ ] Raw log viewer with syntax highlighting (AvalonEdit package installed, not wired to UI)
- [ ] Export to HTML with embedded charts

### Planned for This Phase
- [ ] Side-by-side log comparison (before/after optimization)
- [ ] Bulk log analysis report (summarize 50+ logs at once)
- [ ] Jump-to-line from Tree/Timeline to raw log

---

## ✅ Phase 5: Distribution & Monetization (MOSTLY COMPLETE)

### Distribution
- [ ] Windows installer (WiX or NSIS)
- [ ] Auto-update mechanism
- [ ] Custom URL scheme registration (`blackwidow://`)
- [ ] GitHub Releases pipeline

### Monetization ✅
- [x] LemonSqueezy payment integration (replaced Stripe — checkout URLs configured)
- [x] Upgrade flow UI — feature comparison modal, trial start, license activation (UpgradeDialog.xaml)
- [x] License validation API — LemonSqueezy activate/validate/deactivate endpoints
- [x] Trial enforcement — 14-day local Pro trial, no credit card required
- [x] Feature gating — Free vs Pro tier checks across all commands (LicenseFeature enum)
- [x] Device fingerprinting — SHA256-based, 2 devices per license
- [x] 7-day offline grace period
- [ ] LemonSqueezy account setup + product variant creation (store-side config)
- [ ] Webhook handler for payment → license provisioning (server-side)

> **Note:** LicenseService (623 lines) and UpgradeDialog are fully implemented.
> Payment provider is **LemonSqueezy** (not Stripe). Client-side integration is complete.
> Remaining work is store-side account configuration and server-side webhook.

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

## 🎨 Phase 7: Complete UI Redesign (HIGH PRIORITY)

**Problem:** The current Discord-themed dark UI does not feel modern, intuitive, or professional enough to represent a paid product. Users who already understand Salesforce logs find the layout does not match how they think about debugging. The output structure does not match the mental model of experienced developers and architects.

**Goal:** Rebuild the UI from scratch with a clean, modern design that works for both beginners and senior architects — without feeling like a gaming app.

### Design Principles
- Clean minimal aesthetic — think Linear, Vercel, or Apple design language
- Dense information for power users, progressive disclosure for beginners
- No Discord theme — neutral professional palette (dark mode optional)
- Every pixel earns its place — remove anything decorative that does not aid comprehension

### New Layout Structure
- [ ] Full UI redesign — replace Discord theme with modern neutral design system
- [ ] Responsive layout — panels resize gracefully, no fixed-width columns
- [ ] Consistent typography hierarchy — clear distinction between labels, values, and actions
- [ ] Color used only for signal (red = error, amber = warning, green = healthy) — not decoration
- [ ] Subtle micro-animations on load (150ms) — not distracting
- [ ] Keyboard-first navigation — power users should never need the mouse

### Navigation Overhaul
- [ ] Replace tab bar with a left sidebar navigation (icon + label, collapsible)
- [ ] Breadcrumb trail showing current log → current view
- [ ] Recent logs quick-access panel (last 10 analyzed)
- [ ] Global search across all open logs (Ctrl+K command palette)

### Component Library
- [ ] Define a reusable WPF component set (cards, badges, stat tiles, progress bars)
- [ ] Consistent spacing tokens (4px grid system)
- [ ] Accessible contrast ratios — WCAG AA minimum
- [ ] Icon set refresh — replace emoji with a consistent vector icon library

---

## 🏗️ Phase 8: Architect View (HIGH PRIORITY)

**Problem:** Black Widow was designed to explain logs to beginners. Senior developers and architects — the people most likely to pay — open it, find the output does not match how they think, and go back to the Developer Console. The Summary and Explain tabs are too verbose for someone who already knows what a SOQL query is.

**Goal:** Add a dedicated Architect View that is the default tab for power users — dense, no analogies, all critical signals visible in under 3 seconds without scrolling.

### Data Model Changes
- [ ] Add `List<DebugStatement>` to `LogAnalysis` — extracted from `USER_DEBUG` nodes already in the execution tree (data is already parsed, just not surfaced as a dedicated collection)
- [ ] `DebugStatement` model: `{ LineNumber, ApexLine, LogLevel, Message, Timestamp, NanosecondCounter }`
- [ ] Populate during `BuildExecutionTree()` walk — no re-parsing needed

### Architect View Layout
Single screen, four quadrants, no scrolling required on load:

```
┌──────────────────────────────────────────────────────────────┐
│  [Entry Point] · [Context] · [Duration]          [Score]    │
├─────────────────┬──────────────┬────────────┬───────────────┤
│  EXCEPTIONS     │  LIMITS      │  TIMING    │  DEBUG LINES  │
│  ─────────────  │  ──────────  │  ────────  │  ───────────  │
│  Unhandled (N)  │  SOQL  87/100│  Total Xms │  [L.23] msg   │
│  Handled  (N)   │  CPU  4200ms │  Top 3     │  [L.67] msg   │
│  Fatal    (N)   │  DML   12/150│  methods   │  [L.89] msg   │
│  ─────────────  │  Rows  4800  │  with ms   │  [L.142] msg  │
│  Click to drill │  ──────────  │  each      │  ───────────  │
└─────────────────┴──────────────┴────────────┴───────────────┘
│  EXCEPTION DETAIL (expands inline on click)                  │
│  NullPointerException at CaseTrigger.cls:142 — UNHANDLED    │
│  → CaseTrigger.execute() → CaseHandler.process() → ...      │
└──────────────────────────────────────────────────────────────┘
```

- [ ] Exceptions panel — unhandled first, then handled, then fatal; click to expand full stack trace inline
- [ ] Limits panel — all governor limits as compact progress bars (% used / limit); color coded by severity
- [ ] Timing panel — total execution time + top 5 slowest methods with individual durations
- [ ] Debug Lines panel — chronological list of all `USER_DEBUG` statements with line number, log level, and message; filterable by level (DEBUG, INFO, WARN, ERROR)
- [ ] All four panels update instantly when a different log is selected
- [ ] Copy to clipboard on any panel for quick sharing

### Architect View Behavior
- [ ] Make Architect View the default first tab (configurable in settings)
- [ ] Existing Summary / Explain / Tree / Timeline / Queries tabs remain — nothing removed
- [ ] User preference persisted — if they switch to Summary as default, remember it
- [ ] Architect View respects transaction chain mode — aggregates across all logs in the group

### Shield Log Integration (Planned Add-on)
- [ ] When Salesforce Shield is present, add a fifth panel: **Shield Events**
- [ ] Shows field-level encryption reads, platform events, event monitoring entries alongside debug log signals
- [ ] Correlates Shield timestamps to debug log execution timeline
- [ ] Flags PII exposure in debug output (already has `PiiScanResult` model in `LogModels.cs`)

---

## Release History & Plan

| Version | Status | Highlights |
|---|---|---|
| v0.1-alpha | ✅ Released | Core parser, plain-English summaries, OAuth |
| v0.5-beta | ✅ Released | Transaction grouping, phase detection, 5 tabs |
| **v0.9-beta** | **🟢 Current** | Governance insights, CLI streaming, Shield monitoring, PII scanner, licensing/trial system |
| v1.0 | 📋 Planned | Windows installer, LemonSqueezy store setup, webhook handler |
| v1.1 | 📋 Planned | **UI redesign (Phase 7) + Architect View (Phase 8)** |
| v1.2 | 📋 Planned | Side-by-side comparison, bulk analysis, raw log viewer |
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
| Architect View (power user) | ✅ Planned v1.1 | ❌ Not available |
| Shield log correlation | ✅ Implemented | ❌ Not available |
| Target audience | Everyone | Developers only |
| Setup complexity | ✅ Zero (Platform CLI) | ⚠️ Connected App required |

---

**Last Updated:** March 11, 2026
**Current Version:** v0.9-beta
**Next Milestone:** v1.0 — Windows installer + LemonSqueezy store setup (Phase 5 remaining)
**Known Gap:** UI does not match mental model of Sr. Architects — Phases 7 & 8 address this directly