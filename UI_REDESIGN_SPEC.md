# Black Widow — Complete UI/UX Redesign Specification

> **"The only Salesforce debug log analyzer that groups related logs, detects execution phases, and explains the complete user experience journey."**

---

## 1. Competitive Landscape & Design Inspiration

### Direct Competitor: Certinia/LANA (Apex Log Analyzer)
- **What it is**: VS Code extension, 1.1M installs, 101 GitHub stars
- **Strengths**: Blazing-fast flame charts, interactive call tree, database analysis (SOQL/DML), raw log navigation with bidirectional jumping, 19 color themes, minimap navigation, governor limits strip, global search across all views
- **Weaknesses**: VS Code only (not standalone), single-log analysis only (no grouping), no Shield/security coverage, no PII scanning, no plain-English explanations, no transaction chain analysis, no monitoring/alerting, no folder scanning, no live streaming
- **UI Patterns to Borrow**: Flame chart with minimap + governor limits strip, color-coded operation types in timeline, breadcrumb zoom navigation, context menu actions on chart elements

### Adjacent Tool Inspirations

| Tool | Borrowed Concept | Applied To |
|------|-----------------|------------|
| **Chrome DevTools Performance** | Insights sidebar + flame chart + summary tab layout | Our Timeline + Summary tabs |
| **Datadog Trace Explorer** | Faceted sidebar filtering + live/indexed toggle + Watchdog anomaly detection | Our log list + Shield monitoring |
| **Sentry Issues** | Issue grouping with frequency bars + triage states + error level icons | Our log groups + critical issues |
| **Grafana Explore** | Query builder + correlations + split view | Our Queries tab + cross-log correlation |
| **Speedscope** | Clean flamegraph with left/right/sandwich views | Our execution tree visualization |
| **Linear** | Minimal, keyboard-first, fast transitions | Overall navigation + command palette |
| **Vercel** | Clean dark dashboard with status indicators + deployment timeline | Our dashboard overview |

---

## 2. Design Philosophy: "Calm Authority"

Black Widow is a tool for **2 AM production incidents**. Every design decision serves:

1. **Signal over decoration** — Every pixel earns its place. No gradients, no glows, no unnecessary motion.
2. **Progressive disclosure** — Surface the "what's wrong" immediately, let users drill into "why" on demand.
3. **Context preservation** — Never lose the user's position. Breadcrumbs, back navigation, and state persistence everywhere.
4. **Keyboard-first, mouse-friendly** — Power users should never touch the mouse. Every action has a shortcut.
5. **Warm dark, not cold dark** — Neutral charcoals (#0B0E13 → #141820 → #1C2128), not blue-tinted or purple-tinted.

### Design Tokens (Already Defined, Validated)
Our Obsidian Design System in `Themes/DiscordTheme.xaml` is solid. Keep:
- Backgrounds: `#0B0E13` / `#141820` / `#1C2128` / `#272D36`
- Accent: `#4493F8` (professional blue, trustworthy)
- Semantic: Green `#3FB950`, Yellow `#D29922`, Red `#F85149`, Info `#79C0FF`
- Typography: Segoe UI body, Cascadia Code for code/data
- Spacing: 4px base grid
- Animation: 100-300ms, ease-out curves

---

## 3. Application Layout Architecture

### 3.1 Global Shell (MainWindow.xaml — the container)

```
┌──────────────────────────────────────────────────────────────────────┐
│ TITLE BAR: App icon + "Black Widow" + breadcrumb trail + ⌘K + ─□✕  │
├────┬─────────────────────────────────────────────────────────────────┤
│    │  CONTENT AREA                                                  │
│ S  │  ┌────────────────────────────┬──────────────────────────────┐ │
│ I  │  │                            │                              │ │
│ D  │  │   LEFT: Log Navigator      │   RIGHT: Analysis Workspace  │ │
│ E  │  │   (collapsible, 300px)     │   (fills remaining space)    │ │
│ B  │  │                            │                              │ │
│ A  │  │                            │                              │ │
│ R  │  └────────────────────────────┴──────────────────────────────┘ │
│    │                                                                │
├────┴────────────────────────────────────────────────────────────────┤
│ STATUS BAR: Connection status · Log count · Parse time · Shortcuts  │
└─────────────────────────────────────────────────────────────────────┘
```

#### Sidebar (56px collapsed → 200px expanded, icon-first)
**Inspiration**: Linear's sidebar — icon rail that expands on hover or pin

| Icon | Label | Target |
|------|-------|--------|
| 🏠 | Dashboard | Overview/landing when no log selected |
| 📋 | Logs | Log Navigator panel (the left panel) |
| 🔍 | Search | Global search (filters across everything) |
| 🛡️ | Shield | Shield Event Monitoring dashboard |
| 📊 | Reports | Export/report generation |
| ⚙️ | Settings | Settings dialog |
| 🔌 | Connect | Salesforce org connection |

The sidebar is NOT tabs for analysis views. Analysis views are controlled by the workspace area.

#### Title Bar
- Left: App icon (spider) + current context breadcrumb: `Logs > Case_Trigger_Log > Summary`
- Center: Nothing (clean)
- Right: Command Palette trigger (`Ctrl+K`), minimize, maximize, close
- **Key change**: Remove branding bloat, add breadcrumb navigation

#### Status Bar (24px, subtle)
- Left: Connection indicator (green dot + org name, or "Not connected")
- Center: Log context (e.g., "3 logs selected · 2.4s total parse time")
- Right: Keyboard shortcut hints (contextual), version number

---

## 4. Screen-by-Screen Redesign

### 4.0 Launch / Loading Screen
**Current**: Straight to empty main window
**Redesign**: 

```
┌─────────────────────────────────────────┐
│                                         │
│         🕷️                              │
│       BLACK WIDOW                       │
│    Debug Log Analyzer                   │
│                                         │
│    ┌─────────────────────────────┐      │
│    │  📂  Open Log File          │      │
│    │  📁  Scan Folder            │      │
│    │  ☁️  Connect to Salesforce   │      │
│    │  🕐  Recent: Case_Debug.log  │      │
│    └─────────────────────────────┘      │
│                                         │
│    ⌨️ Ctrl+K to open command palette     │
│                                         │
└─────────────────────────────────────────┘
```

**Inspiration**: VS Code's "Get Started" welcome tab, Vercel's empty state
- Show when no logs are loaded
- Replaces the empty content area
- Recent files list (last 5-10 logs/folders)
- Quick action cards with keyboard shortcuts
- Subtle spider branding in center
- Fade transition to main workspace when content loads

---

### 4.1 Log Navigator (Left Panel — replaces current log list)
**Inspiration**: Sentry Issues list + Datadog faceted filtering

**Current problems**: Cluttered, too many inline filters, group toggle confusing

**Redesign**:

```
┌──────────────────────────────┐
│ 🔍 Search logs...     ⌘F    │
├──────────────────────────────┤
│ FILTERS (collapsible)        │
│  Source: [All ▾]             │
│  Time:   [Last hour ▾]      │
│  Status: ● Critical ● Warn  │
├──────────────────────────────┤
│ GROUPS or INDIVIDUAL         │
│ ○ Individual  ● Grouped     │
├──────────────────────────────┤
│                              │
│ ▼ Case Save Transaction      │
│   ├─ 🔴 CaseTrigger (3.2s)  │
│   ├─ 🟡 FlowRunner (1.1s)   │
│   └─ 🟢 AuraCtrl (0.4s)     │
│   ─────────────────────      │
│   Total: 4.7s · 3 logs      │
│                              │
│ ▼ Account Merge              │
│   ├─ 🔴 MergeTrigger (5.1s) │
│   └─ 🟡 Validation (0.8s)   │
│   ─────────────────────      │
│   Total: 5.9s · 2 logs      │
│                              │
│ ─ Single_Log_Debug.log       │
│   🟢 0.3s · No issues       │
│                              │
└──────────────────────────────┘
```

**Key changes**:
1. **Search at top, always visible** — type to filter instantly
2. **Faceted filters** — collapsible section with quick toggles (borrowed from Datadog)
3. **Group view as default** — shows transaction chains with total timing
4. **Color-coded severity dots** — Red (critical issues), Yellow (warnings), Green (clean)
5. **Inline summary** — each log shows duration + issue count without clicking
6. **Expand/collapse groups** — click group header to see individual logs
7. **Right-click context menu** — Delete, Copy path, Open in explorer, Export
8. **Drag-and-drop** — drop `.log` files directly onto the list

---

### 4.2 Analysis Workspace (Right Panel — the main content area)

When a log or group is selected, the workspace shows analysis. This is the core of the app.

**Current**: 8 tabs in a horizontal strip
**Redesign**: **Contextual workspace with a top toolbar**

**Inspiration**: Chrome DevTools Performance panel layout

```
┌──────────────────────────────────────────────────────────────────┐
│ WORKSPACE TOOLBAR                                                │
│ [Overview] [Timeline] [Tree] [Queries] [Explain] [Security]     │
│                                               🔍 Search · ⤓ Export│
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  CONTENT (varies by selected tab)                                │
│                                                                  │
│                                                                  │
│                                                                  │
│                                                                  │
│                                                                  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

#### Tab Consolidation (8 → 6 tabs)
| Old Tab | New Tab | Why |
|---------|---------|-----|
| Summary | **Overview** | Renamed for clarity. Dashboard-style layout. |
| Tree | **Tree** | Keep — execution tree is core |
| Timeline | **Timeline** | Keep — chronological view is core |
| Queries | **Queries** | Keep — SOQL/DML analysis |
| Explain | **Explain** | Keep — plain English is our differentiator |
| Debug | *(removed as tab)* | Move to a footer/drawer, only visible when debug mode is on |
| Shield | **Security** | Renamed. Only shows when Shield data exists |
| PII | Merged into **Security** | PII scanning belongs in Security tab as a sub-section |

**Debug tab** becomes a slide-up drawer at the bottom (like Chrome DevTools Console), toggled with a keyboard shortcut. Not a peer tab — it's a dev/diagnostic tool.

**Security tab** merges Shield monitoring + PII scanning. Both are compliance/security concerns. Two sections within one tab.

---

### 4.3 Overview Tab (formerly Summary)
**Inspiration**: Vercel dashboard + Sentry issue detail + Chrome DevTools Summary

**Current problems**: Too much crammed in (hero stats, governor cards, health arc, issues list, timing, stack analysis), visually overwhelming

**Redesign**: Information hierarchy with clear sections

```
┌──────────────────────────────────────────────────────────────────┐
│ HEALTH SCORE BAR                                                 │
│ ████████████░░░░░░░  72/100 — Warning: 2 critical issues         │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  KEY METRICS (4 cards in a row)                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ Duration │ │  SOQL    │ │   DML    │ │ CPU Time │           │
│  │  3.24s   │ │ 42/100   │ │  12/150  │ │ 8,200ms  │           │
│  │ ██████░░ │ │ ████░░░░ │ │ █░░░░░░░ │ │ ████████ │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
│                                                                  │
│  CRITICAL ISSUES (if any — red banner)                           │
│  ┌──────────────────────────────────────────────────────┐       │
│  │ 🔴 SOQL in FOR loop — AccountTrigger.cls:42          │       │
│  │    150 queries from 1 statement. Fix: Bulkify query.  │       │
│  ├──────────────────────────────────────────────────────┤       │
│  │ 🟡 Trigger recursion — CaseTrigger fired 3x           │       │
│  │    Add static Boolean guard. See: Explain tab.        │       │
│  └──────────────────────────────────────────────────────┘       │
│                                                                  │
│  EXECUTION BREAKDOWN (horizontal stacked bar)                    │
│  ┌──────────────────────────────────────────────────────┐       │
│  │ ████ Triggers ████ Flows ██ Validation █ Callouts    │       │
│  │ 1.8s (55%)     0.9s (28%) 0.3s (9%)    0.2s (6%)   │       │
│  └──────────────────────────────────────────────────────┘       │
│                                                                  │
│  GOVERNOR LIMITS (compact grid, only show approaching/exceeded)  │
│  SOQL ████████░░ 82/100  ←  Only show limits > 50%              │
│  DML  █████░░░░░ 48/150                                         │
│  CPU  ██████████ 9800/10000ms  ⚠️ 98%                           │
│                                                                  │
│  RECOMMENDATIONS (collapsed by default, expand on click)         │
│  ▶ 3 recommendations available                                   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Key changes**:
1. **Health Score as horizontal bar** (not arc) — faster to parse, less visual noise
2. **4-card metric row** — inspired by Vercel/Datadog hero metrics
3. **Critical Issues promoted to top** — if there are problems, show them FIRST (Sentry-style)
4. **Execution Breakdown as stacked bar** — shows where time was spent at a glance
5. **Governor Limits only show approaching/exceeded** — hide healthy limits, reduce noise
6. **Recommendations collapsed** — don't overwhelm, let user expand when ready
7. **Remove**: Health arc animation, intent navigator, stack analysis section (move to Tree tab)

---

### 4.4 Timeline Tab
**Inspiration**: Chrome DevTools flame chart + Certinia LANA timeline + Datadog trace view

**Current problems**: Basic chronological bars, no interactivity, no zoom

**Redesign**: Interactive flame chart with governor strip

```
┌──────────────────────────────────────────────────────────────────┐
│ MINIMAP (overview of entire execution, click to jump)            │
│ ▓▓▓▓▓▓▓▓▓▓░░░▓▓▓▓▓▓░░▓▓▓▓▓▓▓▓▓▓▓▓░░░░░▓▓▓▓▓▓▓▓▓▓▓▓         │
│        [====viewport====]                                        │
├──────────────────────────────────────────────────────────────────┤
│ GOVERNOR LIMITS STRIP (traffic-light coloring over time)         │
│ SOQL: ░░░░█░░░░░░░██░░░░░░░░░░░░░░░░░                          │
│ DML:  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░                           │
│ CPU:  ░░░░░░░░░░░░░░░░░░░░█████████░                           │
├──────────────────────────────────────────────────────────────────┤
│ FLAME CHART (layered execution)                                  │
│                                                                  │
│ ┌────────────── BeforeInsert ────────────────────┐              │
│ │ ┌──── CaseTrigger ─────┐ ┌── Validation ──┐  │              │
│ │ │ ┌─ SOQL ─┐ ┌─ DML ─┐│ │ ┌─ Rule1 ─┐   │  │              │
│ │ │ └────────┘ └───────┘│ │ └──────────┘   │  │              │
│ │ └──────────────────────┘ └────────────────┘  │              │
│ └───────────────────────────────────────────────┘              │
│                                                                  │
│ 0ms        500ms        1000ms       1500ms       2000ms        │
├──────────────────────────────────────────────────────────────────┤
│ DETAIL PANEL (shows on click)                                    │
│ CaseTrigger.cls — Self: 120ms · Total: 890ms · SOQL: 3 · DML: 1│
└──────────────────────────────────────────────────────────────────┘
```

**Key features**:
1. **Minimap** at top (LANA-style) — bird's-eye view, click to teleport
2. **Governor limits strip** — thin horizontal lanes showing limit usage over time (LANA)
3. **Flame chart** — nested bars showing execution depth (Chrome DevTools + LANA)
4. **Color-coded by operation type**: Triggers (red), Flows (blue), LWC (green), Async (purple), Callouts (yellow), Validation (orange)
5. **Click any frame → detail panel** at bottom (Chrome DevTools Summary tab pattern)
6. **Zoom**: Scroll to zoom, drag to pan, Shift+drag to measure duration
7. **Breadcrumb zoom** — select a range, click to zoom in, breadcrumb appears to go back (DevTools)
8. **Hover tooltips** — duration, self-time, SOQL/DML counts

**Implementation note**: This is a major rendering task. Consider using a `WriteableBitmap` or `SkiaSharp` canvas for the flame chart, not hundreds of WPF `Border` elements. Start with a simpler version (stacked bars with horizontal scroll) for v1, upgrade to full flame chart for v1.1.

---

### 4.5 Tree Tab (Execution Tree)
**Inspiration**: Chrome DevTools Call Tree + LANA Call Tree

**Current**: Basic `TreeView` with `HierarchicalDataTemplate`
**Redesign**: Add metrics columns and filtering

```
┌──────────────────────────────────────────────────────────────────┐
│ TOOLBAR: [Filter: ▾ All Types] [Min Duration: ▾ None]  🔍       │
├──────────┬──────────┬──────────┬───────┬───────┬────────────────┤
│ Method   │ Self Time│ Total    │ SOQL  │ DML   │ Rows           │
├──────────┼──────────┼──────────┼───────┼───────┼────────────────┤
│ ▼ Execute│ 2ms      │ 3240ms   │ 42    │ 12    │ 1,450          │
│  ▼ Case  │ 120ms    │ 890ms    │ 3     │ 1     │ 150            │
│    SOQL  │ 340ms    │ 340ms    │ 1     │ -     │ 100            │
│    DML   │ 45ms     │ 45ms     │ -     │ 1     │ 1              │
│  ▼ Flow  │ 15ms     │ 1100ms   │ 5     │ 2     │ 300            │
│    ...   │          │          │       │       │                │
│  Validat │ 300ms    │ 300ms    │ 0     │ 0     │ 0              │
└──────────┴──────────┴──────────┴───────┴───────┴────────────────┘
```

**Key changes**:
1. **Column headers with sorting** — click any column to sort (LANA-style)
2. **Self Time vs Total Time** — distinguish where time is actually spent
3. **SOQL/DML/Rows columns** — resource counts at each node
4. **Filter toolbar** — filter by namespace, type, or minimum duration
5. **Highlight on hover** — cross-reference with Timeline tab
6. **Keyboard navigation** — up/down to navigate, left/right to expand/collapse
7. **Color-coded type indicator** — small colored dot by each node matching Timeline colors

---

### 4.6 Queries Tab (Database Analysis)
**Inspiration**: LANA Database Analysis + Datadog query analysis

**Current**: Table with SOQL/DML operations
**Redesign**: Richer table with optimization tips

```
┌──────────────────────────────────────────────────────────────────┐
│ TOOLBAR: [SOQL | DML | All]  Sort: [Duration ▾]  🔍 Search      │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│ SOQL QUERIES (38 total, 42/100 limit)                           │
│ ┌────────────────────────────────────────────────────────┐      │
│ │ ⚠️ SELECT Id, Name FROM Account WHERE...   340ms  100  │      │
│ │    Called 15x from CaseTrigger.cls:42                   │      │
│ │    💡 Non-selective. Add index on Account.Type          │      │
│ ├────────────────────────────────────────────────────────┤      │
│ │ ✅ SELECT Id FROM Case WHERE Id = :caseId    12ms   1  │      │
│ │    Called 1x from CaseService.cls:88                    │      │
│ └────────────────────────────────────────────────────────┘      │
│                                                                  │
│ ⚠️ DUPLICATE QUERIES (3 identical queries detected)              │
│    SELECT Id, Name FROM Account WHERE Type = 'Customer'          │
│    Called 3x — could be 1x with caching                          │
│                                                                  │
│ DML OPERATIONS (12 total, 12/150 limit)                         │
│ ┌────────────────────────────────────────────────────────┐      │
│ │ INSERT Case — 45ms — 1 row                             │      │
│ │    From: CaseTrigger.cls:55                             │      │
│ └────────────────────────────────────────────────────────┘      │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Key changes**:
1. **Inline optimization tips** (💡) — LANA-style, shows actionable advice per query
2. **Duplicate query detection** promoted — highlighted as a dedicated section
3. **Call source** shown for each query — where in code this originated
4. **SOQL/DML toggle** in toolbar — switch between query types
5. **Expandable rows** — click to see full query text, explain plan, and call stack
6. **Governor limit context** — show "42/100" next to the section header

---

### 4.7 Explain Tab (Plain English — Our Differentiator)
**Inspiration**: GitHub Copilot explanations + Sentry's "What happened" summaries

This tab is Black Widow's **secret weapon**. No competitor does this. Keep it front and center.

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  📖 WHAT HAPPENED                                                │
│  ─────────────────                                               │
│  A user saved a Case record, which triggered a chain of 3       │
│  automated processes taking 4.7 seconds total.                   │
│                                                                  │
│  1. CaseTrigger fired first (3.2s) — ran SOQL queries in a     │
│     loop, executing 42 queries where 1 would suffice.            │
│  2. Case Assignment Flow ran next (1.1s) — assigned the case    │
│     to a queue and sent 2 email notifications.                   │
│  3. AuraEnabled controller loaded (0.4s) — refreshed the       │
│     Lightning page with updated case data.                       │
│                                                                  │
│  ⚡ PERFORMANCE VERDICT                                          │
│  ─────────────────────                                            │
│  🔴 Poor — User waited 4.7s. Industry benchmark: < 2s.          │
│                                                                  │
│  The main bottleneck is the CaseTrigger's N+1 query pattern.    │
│  Fixing this single issue would reduce wait time to ~1.5s.       │
│                                                                  │
│  🔧 TOP FIX                                                      │
│  ──────────                                                       │
│  Move the SOQL query outside the FOR loop in                      │
│  CaseTrigger.cls line 42. Query all records at once              │
│  using a Set<Id> collection pattern.                              │
│                                                                  │
│  [Copy to Clipboard]  [Export as Report]                          │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Key changes**:
1. **Clean prose layout** — no cards, no grids, just readable text
2. **Three clear sections**: What happened → Performance verdict → Top fix
3. **Numbered narrative** — tells a story, not a data dump
4. **Analogy system** maintained — "like a checkout line with 42 people instead of 1"
5. **Direct file references** — clickable links to code locations
6. **Copy/Export actions** — share the explanation with teammates
7. **Remove**: Performance verdict percentile badges, issue analogies section (merge into narrative)

---

### 4.8 Security Tab (Shield + PII Merged)
**Inspiration**: Microsoft Sentinel dashboard + CrowdStrike overview

Only visible when Shield data or PII scan results exist.

```
┌──────────────────────────────────────────────────────────────────┐
│ SECURITY  [Shield Events | PII Scanner]                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  SHIELD EVENTS (when Shield tab selected)                        │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ Logins   │ │ API Calls│ │ Exports  │ │ Anomalies│           │
│  │ 1,247    │ │ 8,432    │ │ 23       │ │ 3 ⚠️     │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
│                                                                  │
│  ANOMALIES                                                       │
│  🔴 Unusual login: admin@org.com from 🇷🇺 Russia, 3:42 AM      │
│  🟡 Bulk export: user@org.com exported 50,000 Contacts          │
│  🟡 API spike: 3x normal rate from integration user             │
│                                                                  │
│  ACTIVITY TIMELINE (sparkline chart)                             │
│  ▁▂▃▅▇█▅▃▂▁▁▂▅▇▇▅▃▁                                           │
│  Mon  Tue  Wed  Thu  Fri  Sat  Sun                               │
│                                                                  │
│  PII SCAN (when PII tab selected)                                │
│  ┌──────────────────────────────────────────────────────┐       │
│  │ Risk Level: 🔴 HIGH — 12 PII findings in 3 logs      │       │
│  ├──────────────────────────────────────────────────────┤       │
│  │ 📧 Email addresses (5 found)                          │       │
│  │ 📞 Phone numbers (3 found)                            │       │
│  │ 🆔 SSN patterns (2 found) — CRITICAL                  │       │
│  │ 🏠 Address matches (2 found)                          │       │
│  └──────────────────────────────────────────────────────┘       │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

### 4.9 Debug Drawer (Bottom Panel — replaces Debug Tab)
**Inspiration**: Chrome DevTools Console drawer, VS Code Terminal panel

```
┌──────────────────────────────────────────────────────────────────┐
│ 🐛 Parser Debug  [Lines ▾] [Warnings Only]     ━ (collapse)     │
├──────────────────────────────────────────────────────────────────┤
│ 14:32:01.234  INFO   Parsed 12,847 lines in 1.2s               │
│ 14:32:01.234  WARN   Truncated log detected — last 300 lines   │
│ 14:32:01.235  INFO   Built execution tree: 847 nodes            │
│ 14:32:01.236  DEBUG  Governor extraction: SOQL=42, DML=12       │
└──────────────────────────────────────────────────────────────────┘
```

- Only visible when enabled (Settings → Developer → Show debug drawer)
- Slides up from bottom, resizable
- Shows parser audit trail, warnings, performance metrics
- Not a peer to analysis tabs — it's infrastructure

---

### 4.10 Transaction Group View (Multi-Log Analysis)
**Inspiration**: Datadog's distributed tracing view

When a **group** is selected (not a single log), the workspace shows a specialized view:

```
┌──────────────────────────────────────────────────────────────────┐
│ 📋 TRANSACTION: Case Save — 3 logs, 4.7s total wait time        │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│ WATERFALL (logs as a sequence)                                   │
│                                                                  │
│ t=0ms    t=1000ms   t=2000ms   t=3000ms   t=4000ms   t=4700ms  │
│ │         │          │          │          │          │          │
│ ████████████████████████████ CaseTrigger (3.2s)                  │
│                              ██████████████ Flow (1.1s)          │
│                                             ████ Aura (0.4s)    │
│                                                                  │
│ PHASE BREAKDOWN                                                  │
│ ┌─────────────────────────────────────────┐                     │
│ │ Backend: 4.3s (92%)  │ Frontend: 0.4s  │                     │
│ │ ████████████████████ │ ██              │                     │
│ └─────────────────────────────────────────┘                     │
│                                                                  │
│ ISSUES ACROSS CHAIN                                              │
│ 🔴 N+1 queries span CaseTrigger → Flow handoff                  │
│ 🟡 Sequential loading — could parallelize 2 API calls           │
│ 🔴 CaseTrigger fired 3x (recursion detected)                    │
│                                                                  │
│ RECOMMENDATIONS                                                  │
│ Fix the N+1 pattern and recursion guard to reduce               │
│ total wait from 4.7s → estimated 1.5s (68% improvement)         │
│                                                                  │
│ [View Individual Logs ▾]                                         │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**This is our #1 differentiator**. No other tool shows this. The waterfall view of related logs in a transaction chain is unique.

---

### 4.11 Empty States
Every screen needs a purposeful empty state, not a blank void.

| Screen | Empty State |
|--------|-------------|
| Log Navigator (no logs) | "Drop .log files here or connect to Salesforce" + icon |
| Overview (no log selected) | Welcome/landing page (see 4.0) |
| Timeline (no data) | "Select a log to see its execution timeline" |
| Security (no Shield data) | "No Shield Event Logs detected. Connect to a Shield-enabled org to see security monitoring." |
| Search (no results) | "No logs match your search. Try broader terms." |

---

### 4.12 Error States
**Inspiration**: Sentry's error grouping, GitHub's error pages

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  ⚠️  PARSE ERROR                                                 │
│                                                                  │
│  Could not parse "Case_Debug_20260301.log"                      │
│                                                                  │
│  Reason: Log file appears truncated (ended mid-line at           │
│  position 1,247,832). This usually happens when the debug       │
│  log limit was reached (20MB max in Salesforce).                 │
│                                                                  │
│  What you can do:                                                │
│  • Reduce debug log levels (set SYSTEM to NONE)                 │
│  • Add more specific trace flags to capture less data            │
│  • Try re-downloading the log from Salesforce                    │
│                                                                  │
│  [Retry Parse]  [Open Raw File]  [Report Bug]                   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Principles for all errors**:
1. **What happened** — plain English, no stack trace
2. **Why it happened** — explain the likely cause
3. **What to do** — actionable steps
4. **Actions** — retry, open raw, report bug

---

### 4.13 Connection Dialog
**Current**: Functional but dense
**Redesign**: Two-step flow with visual feedback

**Step 1: Choose method**
```
┌──────────────────────────────────────────────────┐
│  Connect to Salesforce                     ✕    │
├──────────────────────────────────────────────────┤
│                                                  │
│  ┌─────────────────────┐ ┌────────────────────┐ │
│  │  🌐 OAuth Login     │ │  🔑 Access Token   │ │
│  │  (Recommended)      │ │  (Manual)          │ │
│  │                     │ │                    │ │
│  │  Opens browser for  │ │  Paste a session   │ │
│  │  secure login.      │ │  ID and instance.  │ │
│  │  No password stored.│ │                    │ │
│  └─────────────────────┘ └────────────────────┘ │
│                                                  │
│  Recent connections:                             │
│  • dev-ed@salesforce.com (Production) — 2h ago  │
│  • sandbox@test.com (Sandbox) — Yesterday       │
│                                                  │
└──────────────────────────────────────────────────┘
```

**Step 2: OAuth flow** — shows inline browser or waiting animation
**Step 2 alt: Token entry** — clean two-field form

---

### 4.14 Settings Dialog
**Current**: 6-tab dialog
**Redesign**: Keep but clean up to:

| Tab | Content |
|-----|---------|
| General | Theme (dark only for now), font size, startup behavior |
| Connection | Saved orgs, default org, API timeout |
| Analysis | Auto-group threshold, parse depth, max log size |
| Security | PII scan patterns, Shield polling interval |
| Keyboard | Full shortcut reference + customization |
| Developer | Debug drawer toggle, verbose logging, cache clear |

---

## 5. Interaction Patterns

### 5.1 Command Palette (Ctrl+K)
**Inspiration**: Linear, VS Code, Raycast

Already partially implemented. Expand:
- Log-specific actions: "Parse log", "Export as PDF", "Copy summary"
- Navigation: "Go to Queries", "Open Settings", "Connect to Salesforce"
- Search: "Search for CaseTrigger", "Find SOQL queries > 1s"
- Recent: Show last 5 commands

### 5.2 Keyboard Shortcuts
| Shortcut | Action |
|----------|--------|
| `Ctrl+K` | Command palette |
| `Ctrl+O` | Open file |
| `Ctrl+Shift+O` | Open folder |
| `1-6` | Switch tabs (Overview, Timeline, Tree, Queries, Explain, Security) |
| `Ctrl+F` | Search within current view |
| `Ctrl+E` | Export current view |
| `Ctrl+,` | Open settings |
| `Escape` | Close dialogs / deselect |
| `Ctrl+D` | Toggle debug drawer |

### 5.3 Context Menus
Every data element should have a right-click menu:
- Log in navigator: Delete, Copy path, Open folder, Export, Properties
- Tree node: Copy name, Jump to Timeline, Filter queries from here
- Query row: Copy SOQL, Show call stack, Go to source
- Timeline frame: Zoom to fit, Show in Tree, Copy duration

### 5.4 Drag and Drop
- Drop `.log` files on window → add to navigator
- Drop folder on window → scan folder
- Drag column headers in tables → reorder

### 5.5 Transitions & Animation
- Tab switches: Instant (no animation — speed > polish)
- Panel resize: Smooth (200ms ease-out)
- Dialog open/close: 150ms fade + 8px slide up
- Toast notifications: 100ms slide in from bottom-right, 3s display, 200ms fade out
- Loading states: Skeleton screens (gray placeholder shapes), not spinners

---

## 6. Visual Design Details

### 6.1 Cards
- Background: `BgSecondary` (#141820)
- Border: 1px `BorderDefault` (#2D333B)
- Corner radius: 8px (`RadiusMedium`)
- Padding: 16px
- No shadows on cards (flat design, clean)
- Hover: Border transitions to `BorderFocus` (#4493F8) with 100ms ease

### 6.2 Tables/DataGrids
- Header: `BgTertiary` background, `TextSecondary` color, `FontWeight.SemiBold`
- Rows: Alternating `BgPrimary` / `BgSecondary`
- Hover: `BgHover` (#1E242C)
- Selected: `AccentSubtle` background + `AccentPrimary` left border (2px)
- Sort indicators: ▲/▼ in header, accent color

### 6.3 Buttons
- Primary: `AccentPrimary` bg, white text, 8px radius, 100ms hover transition
- Secondary: Transparent bg, `BorderDefault` border, `TextSecondary` text
- Danger: `Danger` bg on hover, `Danger` text normally
- Ghost: No border, no bg, text only, hover shows `BgHover`

### 6.4 Severity Indicators
Consistent across entire app:
| Level | Color | Icon | Usage |
|-------|-------|------|-------|
| Critical | `#F85149` | 🔴 filled circle | Fatal errors, limit exceeded |
| Warning | `#D29922` | 🟡 filled circle | Approaching limits, performance issues |
| Info | `#4493F8` | 🔵 filled circle | Informational notices |
| Success | `#3FB950` | 🟢 filled circle | Clean, no issues |

### 6.5 Typography Hierarchy
| Element | Size | Weight | Color |
|---------|------|--------|-------|
| Page title | 24px (FontSizeXL) | SemiBold | TextPrimary |
| Section header | 15px (FontSizeMD) | SemiBold | TextPrimary |
| Body text | 14px (FontBaseline) | Normal | TextPrimary |
| Label/caption | 13px (FontSizeSM) | Normal | TextSecondary |
| Code/data | 13px (FontSizeSM) | Normal (mono) | TextPrimary |
| Muted hint | 11px (FontSizeXS) | Normal | TextMuted |

---

## 7. Implementation Priority

### Phase 1: Foundation (Essential — do first)
1. **Welcome/Empty state** — landing page when no logs loaded
2. **Overview tab redesign** — health bar, 4-card metrics, critical issues first
3. **Log Navigator cleanup** — search, severity dots, inline summary
4. **Tab consolidation** — merge Shield+PII into Security, remove Debug tab (drawer)
5. **Sidebar simplification** — icon rail, clean separation from workspace

### Phase 2: Interactivity (High value)
6. **Command palette expansion** — more actions, recent commands
7. **Table improvements** — sortable columns, filters in Tree and Queries tabs
8. **Error states** — purposeful messages for every failure mode
9. **Explain tab polish** — narrative format, copy/export
10. **Transaction Group view** — waterfall visualization for multi-log groups

### Phase 3: Advanced Visualization (Polish)
11. **Timeline flame chart** — minimap, zoom, governor strip
12. **Cross-tab linking** — click tree node → highlight in timeline
13. **Keyboard shortcuts** — full keyboard navigation
14. **Transition animations** — skeleton loading, toast improvements
15. **Settings cleanup** — organized tabs, keyboard shortcut customization

---

## 8. What Makes Black Widow Unique

After researching every competitor, here's what no one else does:

| Feature | LANA | Datadog | Sentry | **Black Widow** |
|---------|------|---------|--------|-----------------|
| Transaction chain grouping | ❌ | ✅ (distributed) | ❌ | ✅ (Salesforce-specific) |
| Plain English explanation | ❌ | ❌ | Partial (AI) | ✅ (rule-based, deterministic) |
| Shield event monitoring | ❌ | ❌ | ❌ | ✅ |
| PII scanning in logs | ❌ | ❌ | Partial | ✅ |
| Standalone desktop app | ❌ (VS Code) | ❌ (SaaS) | ❌ (SaaS) | ✅ |
| Folder batch scanning | ❌ | ❌ | ❌ | ✅ |
| Live Salesforce streaming | ❌ | ❌ | ❌ | ✅ |
| Recommendations with code fixes | ❌ | ❌ | Partial | ✅ |
| User wait time calculation | ❌ | ❌ | ❌ | ✅ |
| Phase detection (BE vs FE) | ❌ | ❌ | ❌ | ✅ |
| Recursion detection | ❌ | ❌ | ❌ | ✅ |

**Our positioning**: *"LANA shows you one log at a time. Black Widow shows you the whole story."*

---

## 9. Technical Notes

### Files to modify (in priority order)
1. [Views/MainWindow.xaml](Views/MainWindow.xaml) — Shell layout, sidebar, title bar
2. [Views/Tabs/SummaryTab.xaml](Views/Tabs/SummaryTab.xaml) → rename to OverviewTab.xaml
3. [Views/Tabs/TimelineTab.xaml](Views/Tabs/TimelineTab.xaml) — Flame chart implementation
4. [Views/Tabs/TreeTab.xaml](Views/Tabs/TreeTab.xaml) — Column headers, sorting
5. [Views/Tabs/QueriesTab.xaml](Views/Tabs/QueriesTab.xaml) — Inline tips, call source
6. [Views/Tabs/ExplainTab.xaml](Views/Tabs/ExplainTab.xaml) — Narrative layout
7. [Views/Tabs/ShieldTab.xaml](Views/Tabs/ShieldTab.xaml) + PiiTab.xaml → merge into SecurityTab.xaml
8. [Views/ConnectionDialog.xaml](Views/ConnectionDialog.xaml) — Two-step flow
9. [Themes/DiscordTheme.xaml](Themes/DiscordTheme.xaml) — Design tokens (mostly keep)

### New files to create
- `Views/WelcomePage.xaml` — Landing/empty state
- `Views/DebugDrawer.xaml` — Bottom debug panel
- `Views/Tabs/SecurityTab.xaml` — Merged Shield + PII
- `Views/TransactionGroupView.xaml` — Multi-log waterfall view (optional, embed in workspace)

### ViewModel changes
- No major ViewModel restructuring needed — the data binding layer is solid
- Add `SelectedWorkspaceTab` property for the 6-tab system
- Add `IsDebugDrawerVisible` property
- Add `RecentFiles` collection for welcome page

---

*This document is the single source of truth for the Black Widow UI/UX redesign. Every visual change should reference a section above.*
