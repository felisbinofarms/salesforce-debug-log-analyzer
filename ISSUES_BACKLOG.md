# Black Widow - Issues Backlog
**Generated:** February 2, 2026

This document contains all features, user stories, and issues for Black Widow. Copy these into GitHub Issues to populate your project board.

---

## 🚨 MVP Critical Path (v1.0) - Launch by March 1, 2026

### Issue #1: License Validation System
**Type:** Feature | **Priority:** P0-critical | **Size:** L | **Milestone:** v1.0

**User Story:**
As a Pro user, I want my license validated automatically so that I can access premium features without manual intervention.

**Problem Statement:**
Desktop apps can't enforce usage limits like web apps. Need online validation to prevent piracy while staying user-friendly (no DRM hell).

**Acceptance Criteria:**
- [ ] Check license status every 30 days (not every launch - don't annoy users)
- [ ] Allow 2 devices per Pro license (laptop + desktop is common)
- [ ] 7-day grace period if offline (travel, airplane mode)
- [ ] Clear error messages: "License expired. Click to renew." (not "ERROR 4012")
- [ ] Store last validation timestamp in encrypted local file
- [ ] Handle network errors gracefully (retry 3x with exponential backoff)
- [ ] Show license info in Help → About dialog (expires, devices used, upgrade link)

**Implementation Notes:**
```csharp
// Services/LicenseService.cs
public class LicenseService {
    // Store license in %APPDATA%/BlackWidow/license.enc (AES-256)
    // API endpoint: https://api.blackwidow.dev/v1/licenses/validate
    // Use JWT for license tokens (signed with RSA private key)
    // Schema: { userId, tier: "pro"|"team"|"enterprise", expires, maxDevices }
}
```

**Technical Considerations:**
- Use `System.Security.Cryptography` for AES encryption
- Device fingerprint: `Environment.MachineName + Environment.UserName` hashed
- Store in `ApplicationData.Current.LocalFolder` (WPF equivalent)
- Build webhook handler for Stripe → License API (auto-provision on payment)

**Out of Scope:**
- Offline activation codes (v2.0 enterprise feature)
- License transfer between devices (contact support for now)

---

### Issue #2: Upgrade Flow UI
**Type:** Feature | **Priority:** P0-critical | **Size:** M | **Milestone:** v1.0

**User Story:**
As a Free tier user, I want to upgrade to Pro in under 5 clicks so that I can access transaction grouping immediately.

**Problem Statement:**
Users hit Free tier limits (30MB log, single log analysis). Need frictionless upgrade to capture impulse purchases. Every extra click = 20% drop-off.

**Acceptance Criteria:**
- [ ] "Upgrade" button in toolbar (always visible, but grayed out for Pro users)
- [ ] Modal shows feature comparison table:
  ```
  Feature              | Free    | Pro      | Team
  ---------------------|---------|----------|----------
  Single log analysis  | ✅      | ✅       | ✅
  Transaction grouping | ❌      | ✅       | ✅
  File size limit      | 30MB    | Unlimited| Unlimited
  CLI integration      | ❌      | ✅       | ✅
  Marketplace access   | View    | Submit   | Submit
  Price                | $0      | $29/mo   | $99/mo (5 users)
  ```
- [ ] "Start 14-day free trial" button (no credit card - build trust)
- [ ] "Buy now" button redirects to Stripe Checkout
- [ ] After purchase, auto-download license (no email roundtrip)
- [ ] Show trial expiration in status bar: "Pro trial: 7 days left"
- [ ] "View billing" link opens Stripe Customer Portal

**Design Mockup:**
```
┌─────────────────────────────────────────────┐
│  🕷️ Upgrade to Black Widow Pro              │
│                                              │
│  [Feature Comparison Table]                 │
│                                              │
│  💎 Try Pro Free for 14 Days                │
│  No credit card required. Cancel anytime.   │
│                                              │
│  [Start Free Trial]    [Buy Now $29/mo]     │
│                                              │
│  Or save 28%: [Buy Yearly $249/year]        │
└─────────────────────────────────────────────┘
```

**Implementation Notes:**
- Views/UpgradeDialog.xaml (WPF Window, centered on parent)
- Use `System.Diagnostics.Process.Start()` to open browser for Stripe
- Return URL: `blackwidow://upgrade-success?session_id={CHECKOUT_SESSION_ID}`
- Register custom URL scheme in Windows Registry (installer task)

---

### Issue #3: Stripe Payment Integration
**Type:** Feature | **Priority:** P0-critical | **Size:** L | **Milestone:** v1.0

**User Story:**
As Black Widow, I want to accept payments via Stripe so that I can generate revenue from Pro/Team subscriptions.

**Technical Requirements:**
- [ ] Create Stripe account (production + test mode)
- [ ] Set up subscription products:
  - `prod_pro_monthly`: $29/month (price_xxx)
  - `prod_pro_yearly`: $249/year (price_yyy) - 28% discount
  - `prod_team_monthly`: $99/month for 5 users (price_zzz)
- [ ] Implement Stripe Checkout (redirect to hosted page)
- [ ] Build webhook endpoint: `https://api.blackwidow.dev/webhooks/stripe`
  - Handle `checkout.session.completed` → Provision license
  - Handle `customer.subscription.updated` → Update license tier
  - Handle `customer.subscription.deleted` → Revoke license
  - Handle `invoice.payment_succeeded` → Send receipt email
  - Handle `invoice.payment_failed` → Grace period warning
- [ ] Store Stripe customer ID in user profile (for portal link)
- [ ] Generate license keys (UUID v4, signed JWT)
- [ ] Send confirmation email with license key + download link

**Implementation Notes:**
```csharp
// Services/StripeService.cs
public class StripeService {
    private readonly string _secretKey = Configuration["Stripe:SecretKey"];
    
    public async Task<string> CreateCheckoutSession(string tier, string email) {
        var options = new SessionCreateOptions {
            SuccessUrl = "blackwidow://upgrade-success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = "blackwidow://upgrade-cancel",
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions> {
                new() { Price = GetPriceId(tier), Quantity = 1 }
            },
            Mode = "subscription",
            CustomerEmail = email,
            SubscriptionData = new SessionSubscriptionDataOptions {
                TrialPeriodDays = 14
            }
        };
        var session = await new SessionService().CreateAsync(options);
        return session.Url;
    }
}
```

**Webhook Security:**
- Verify signature: `Stripe.EventUtility.ConstructEvent(body, signature, webhookSecret)`
- Store webhook secret in Azure Key Vault (not appsettings.json)
- Idempotency: Check `event.id` hasn't been processed (store in DB)

**Dependencies:**
- NuGet: `Stripe.net` (latest)
- Backend API: ASP.NET Core Web API (new project)
- Database: SQL Server or PostgreSQL (store users, subscriptions, license keys)

---

### Issue #4: Transaction Grouping UI Enhancement
**Type:** Feature | **Priority:** P1-high | **Size:** M | **Milestone:** v1.0

**User Story:**
As a Senior Architect, I want to see all logs from one user action grouped together with aggregate metrics so that I can understand the complete transaction flow.

**Current State:**
- LogGroupService exists (groups logs by 10-second window + same user)
- Phase detection works (Backend vs Frontend)
- Recommendations generated
- **Missing:** UI to display this data

**Acceptance Criteria:**
- [ ] Group header shows:
  - Total duration (e.g., "5.2s")
  - User name (e.g., "John Smith")
  - Timestamp (e.g., "2026-02-02 14:35:21")
  - Record ID if available (e.g., "Case-12345")
  - Phase breakdown: "Backend: 3.5s (67%) | Frontend: 1.7s (33%)"
- [ ] Expandable tree view:
  ```
  📦 Transaction Group (5.2s) - John Smith
  ├─ 🔧 Backend Phase (3.5s)
  │  ├─ CaseTrigger.apxt (1.2s) ⚠️ Fired 3x
  │  ├─ CaseValidation.flow (0.8s)
  │  └─ SendEmailNotification.future (1.5s)
  └─ 🎨 Frontend Phase (1.7s)
     ├─ CaseDetailsController.aura (0.9s)
     └─ RelatedCasesController.aura (0.8s)
  ```
- [ ] Recommendations panel (right sidebar):
  - "⚠️ Add recursion control to CaseTrigger (fired 3x)"
  - "💡 Load components in parallel to save 0.8s"
  - "🔥 Optimize SendEmailNotification (@future taking 1.5s)"
- [ ] Export button: "Export Transaction Report" (PDF)
- [ ] Filter: Show only groups with issues (has recommendations)

**Design Inspiration:**
- Chrome DevTools Network tab (waterfall view)
- Postman request history (expandable groups)
- Discord threads (grouped messages)

**Implementation Notes:**
- Update MainWindow.xaml:
  - Add `TreeView` for log hierarchy
  - Use `ItemTemplateSelector` for different node types
  - Add context menu: "Export", "Copy to Clipboard", "View Raw Log"
- Bind to `ObservableCollection<LogGroup>` in MainViewModel
- Use FontAwesome icons: 📦 (fa-box), 🔧 (fa-wrench), 🎨 (fa-palette)

---

### Issue #5: Connection Dialog - OAuth Flow
**Type:** Bug Fix | **Priority:** P1-high | **Size:** M | **Milestone:** v1.0

**Problem:**
Current ConnectionDialog has placeholder OAuth. Need real Salesforce OAuth 2.0 Web Server Flow.

**User Story:**
As a user, I want to connect to Salesforce with OAuth so that I don't have to manually copy tokens.

**Current Behavior:**
- User must manually generate session token from browser
- Copy-paste into Black Widow
- Error-prone (spaces, expires quickly)

**Expected Behavior:**
- Click "Connect with Salesforce"
- Browser opens Salesforce login
- User authorizes Black Widow
- Browser redirects to `blackwidow://oauth-callback?code=xxx`
- App exchanges code for access token + refresh token
- Connection saved, user sees org info

**Acceptance Criteria:**
- [ ] Connected App created in Salesforce (one-time setup):
  - Callback URL: `blackwidow://oauth-callback`
  - Scopes: `api`, `refresh_token`, `id`
  - Consumer Key/Secret stored in app config
- [ ] OAuth flow implementation:
  - Generate state parameter (CSRF protection)
  - Open browser: `https://login.salesforce.com/services/oauth2/authorize?...`
  - Listen for `blackwidow://oauth-callback` (custom URL scheme)
  - Exchange code for tokens: POST `/services/oauth2/token`
  - Store refresh token encrypted in local settings
- [ ] Token refresh logic:
  - Access tokens expire in 2 hours
  - Auto-refresh using refresh token (transparent to user)
  - Handle revoked refresh token (prompt re-auth)
- [ ] Connection list:
  - Show org name, username, environment (Prod/Sandbox)
  - "Default" checkbox (use for CLI commands)
  - "Remove" button (delete from local storage)

**Implementation Notes:**
- Services/OAuthService.cs (already exists, needs enhancement)
- Use `System.Net.Http.HttpListener` for callback (or custom URL scheme)
- Store tokens in Windows Credential Manager (`CredentialManagement` NuGet)

**Security:**
- Never log access tokens or refresh tokens
- Use PKCE (Proof Key for Code Exchange) for extra security
- Validate `state` parameter matches (prevent CSRF)

---

## 🏪 Marketplace Features (v1.1) - Launch by April 1, 2026

### Issue #6: Partner Dashboard Web App
**Type:** Feature | **Priority:** P1-high | **Size:** XL | **Milestone:** v1.1

**User Story:**
As a consulting partner, I want a web dashboard to view leads and submit bids so that I can grow my Salesforce consulting business.

**Scope:**
Separate ASP.NET Core web app (not WPF). URL: `https://partners.blackwidow.dev`

**Features:**
- [ ] Authentication:
  - Email + password signup
  - Google/LinkedIn SSO (optional v1.2)
  - Email verification (SendGrid)
  - Password reset flow
- [ ] Partner Profile:
  - Name, company, certifications (Admin, Dev, Architect)
  - Hourly rate ($50-300/hr)
  - Specialties (Industries, Technologies)
  - Bio (500 chars)
  - Portfolio (3-5 past projects)
  - LinkedIn profile link
- [ ] Lead Inbox:
  - Table view: Client, Problem, Budget, Posted Date, Status
  - Filter by: Budget range, Technology, Industry
  - Sort by: Newest, Highest Budget, Expiring Soon
- [ ] Lead Detail Page:
  - Problem summary (generated by Black Widow)
  - Detected issues (trigger recursion, N+1 queries, etc.)
  - Log file download (sanitized - no sensitive data)
  - Technical specification PDF
  - User tier (Free/Pro/Team/Enterprise)
  - Budget estimate ($500-5K, based on complexity)
  - Timeline (Urgent: <3 days, Normal: 1-2 weeks, Flexible: 1+ month)
- [ ] Submit Bid Form:
  - Fixed price OR hourly rate
  - Estimated hours (if hourly)
  - Timeline (delivery date)
  - Approach (textarea, 500 chars)
  - Questions for client (optional)
- [ ] Active Projects:
  - Status: Awaiting Acceptance, In Progress, Awaiting Validation, Completed
  - Payment status: Escrowed, Released, Disputed
  - Message thread (partner ↔ client)
  - "Request Validation" button (run Black Widow again)
- [ ] Payment History:
  - Date, Client, Amount, Commission, Net Payout
  - Export to CSV (for taxes)
- [ ] Reputation Score:
  - ⭐⭐⭐⭐⭐ (1-5 stars from clients)
  - Completed projects count
  - Average response time
  - On-time delivery rate

**Tech Stack:**
- ASP.NET Core 8.0 MVC
- Entity Framework Core (SQL Server)
- Razor Pages (or Blazor for interactivity)
- Bootstrap 5 + custom Discord theme CSS
- SignalR (real-time bid notifications)

**Deployment:**
- Azure App Service (Standard tier)
- Azure SQL Database
- Azure Blob Storage (profile pictures, portfolios)
- Cost: ~$100/month initially

---

### Issue #7: Automated Bid System
**Type:** Feature | **Priority:** P1-high | **Size:** XL | **Milestone:** v1.1

**User Story:**
As Black Widow, I want to automatically match clients with partners so that the marketplace operates without manual intervention.

**Current State:**
MVP (v1.0) uses manual matching:
1. User submits problem via Black Widow desktop app
2. Admin reviews, creates lead
3. Notifies partners via email
4. Partners bid manually
5. Admin selects winner

**Target State:**
Fully automated:
1. User clicks "Find Consultant" in Black Widow
2. AI analyzes logs, generates technical spec
3. Leads posted to Partner Dashboard automatically
4. Partners bid (3-day bidding window)
5. Client reviews bids, selects winner (or Black Widow recommends based on score)
6. Stripe escrows payment automatically
7. Project starts

**Acceptance Criteria:**
- [ ] Technical Specification Generator:
  - Input: LogGroup with issues
  - Output: PDF with:
    - Executive summary (what's broken, impact)
    - Technical details (trigger recursion, SOQL queries, etc.)
    - Salesforce metadata (org limits, installed packages)
    - Recommended approach (best practices)
    - Effort estimate (hours)
  - Use GPT-4 for natural language summary (100 tokens max)
- [ ] Lead Routing Algorithm:
  - Match partners by:
    - Specialties (e.g., Trigger Optimization)
    - Availability (not over-booked)
    - Tier (Certified for $5K+ projects, Emerging for <$2K)
    - Location (optional: same timezone)
  - Notify top 10 partners (email + in-app notification)
- [ ] Bidding Window:
  - 3 days for partners to submit bids
  - Countdown timer in Lead Detail
  - Reminder emails at 24h and 6h before close
- [ ] Bid Ranking (show to client):
  - Sort by: Reputation Score, Price, Timeline
  - Show "Black Widow Recommends" badge (top scorer)
  - Score = `(reputation * 0.5) + (1 / price * 0.3) + (1 / timeline * 0.2)`
- [ ] Client Selection:
  - Client clicks "Accept Bid" in desktop app
  - Stripe charges card (Checkout redirect)
  - Escrow held until validation
  - Partner notified: "Project awarded! Payment escrowed."
  - Losing bidders notified: "Client selected another partner."

**Implementation Notes:**
```csharp
// Services/MarketplaceBidService.cs
public class MarketplaceBidService {
    public async Task<string> GenerateTechnicalSpec(LogGroup group) {
        var prompt = $@"
            Generate a technical specification for a Salesforce consultant.
            Issues detected: {string.Join(", ", group.Recommendations)}
            Governor limits: SOQL {group.TotalSoqlQueries}/100, DML {group.TotalDmlStatements}/150
            Duration: {group.TotalDuration}ms
            
            Format:
            1. Executive Summary (2-3 sentences)
            2. Technical Details (bullet points)
            3. Recommended Approach (best practices)
            4. Estimated Effort (hours)
        ";
        var response = await OpenAI.ChatCompletion(prompt, model: "gpt-4");
        return response.Content;
    }
}
```

**AI Integration:**
- Use OpenAI API (GPT-4) for spec generation
- Cost: ~$0.10 per spec (100 tokens @ $0.001/token)
- Cache specs (same issue pattern → reuse spec)
- Fallback: Template-based if API fails

**Dependencies:**
- Issue #6 (Partner Dashboard must exist)
- Issue #3 (Stripe integration for escrow)

---

### Issue #8: Stripe Escrow System
**Type:** Feature | **Priority:** P1-high | **Size:** L | **Milestone:** v1.1

**User Story:**
As a client, I want my payment held in escrow until the consultant fixes my issue so that I'm protected from bad work.

**User Story (Partner):**
As a partner, I want escrow protection so that I'm guaranteed payment after completing quality work.

**How Escrow Works:**
1. Client accepts bid → Stripe charges card → Money held (not paid to partner yet)
2. Partner completes work → Requests validation
3. Black Widow re-runs analysis on new logs → Verifies issues fixed
4. If fixed → Release payment to partner (minus 15% commission)
5. If not fixed → Partner has 3 days to fix again
6. If still broken → Client requests refund (dispute resolution)

**Acceptance Criteria:**
- [ ] Stripe Connect integration:
  - Create Stripe Connect accounts for partners (Standard type)
  - Use `transfer_data` to route payment to partner
  - Hold funds in platform account (escrow period: 7-14 days)
- [ ] Payment flow:
  - Charge client: `amount = bidPrice`
  - Hold in platform account (not auto-payout)
  - On validation success:
    - Transfer to partner: `amount * 0.85` (15% commission)
    - Platform keeps: `amount * 0.15`
- [ ] Validation System:
  - "Request Validation" button (partner clicks when done)
  - Client uploads new debug logs to Black Widow
  - Black Widow re-runs LogGroupService
  - Compare before/after:
    - Recursion fixed? (check `DetectReentryPatterns`)
    - Duration improved? (20%+ faster)
    - Recommendations resolved? (list decreased)
  - Auto-approve if all checks pass
  - Manual review if ambiguous (support ticket)
- [ ] Dispute Resolution:
  - Client: "Request Refund" button (with reason)
  - Partner: 3 days to respond or fix
  - Black Widow Support reviews (ticket system)
  - Outcome: Full refund, Partial refund, or Dismiss claim
- [ ] Payout Schedule:
  - Weekly payouts to partners (every Friday)
  - Minimum balance: $100 (accumulate if below)
  - Payout method: Bank transfer (ACH/SEPA)

**Implementation Notes:**
```csharp
// Services/EscrowService.cs
public class EscrowService {
    public async Task ChargeAndHold(string clientId, decimal amount, string partnerId) {
        // Create PaymentIntent with on_behalf_of (Stripe Connect)
        var options = new PaymentIntentCreateOptions {
            Amount = (long)(amount * 100), // Convert to cents
            Currency = "usd",
            Customer = clientId,
            TransferData = new PaymentIntentTransferDataOptions {
                Destination = partnerId, // Partner's Stripe Connect account
                Amount = (long)(amount * 0.85 * 100) // 85% to partner, 15% commission
            },
            CaptureMethod = "manual" // Don't auto-capture, hold for validation
        };
        var intent = await new PaymentIntentService().CreateAsync(options);
    }
    
    public async Task ReleasePayment(string paymentIntentId) {
        // Capture the PaymentIntent (release from escrow)
        await new PaymentIntentService().CaptureAsync(paymentIntentId);
    }
}
```

**Legal Requirements:**
- Terms of Service: Escrow period (14 days max)
- Refund policy (full refund if not fixed)
- Partner payout terms (weekly, $100 minimum)
- Consult lawyer for compliance (payment processor regulations)

---

## 📈 Scale & Growth (v1.2) - Launch by June 1, 2026

### Issue #9: Team Tier - Multi-User Collaboration
**Type:** Feature | **Priority:** P2-medium | **Size:** L | **Milestone:** v1.2

**User Story:**
As a Salesforce team lead, I want to share Black Widow with my team (5 people) so that we can collaborate on performance issues.

**Business Case:**
- Pro tier: $29/month per user = $145/month for 5 users
- Team tier: $99/month for 5 users = $20/user (66% cheaper)
- Increases ARPU (Average Revenue Per User)
- Teams are stickier (lower churn)

**Features:**
- [ ] Team Management:
  - Team admin creates team (name, logo)
  - Invite members via email
  - Members accept invite, download Black Widow
  - License key shared (team-level, not per-user)
  - Max 5 users per Team license ($19/month per additional user)
- [ ] Shared Projects:
  - Create project: "Q1 Performance Audit"
  - Upload logs to cloud (Azure Blob Storage)
  - All team members see project in sidebar
  - Click to sync: "Download 15 new logs"
- [ ] Comments & Annotations:
  - Right-click log → "Add Comment"
  - @mention team members: "@john can you look at this trigger?"
  - Comments sync to cloud (SignalR for real-time)
  - Email notification if @mentioned
- [ ] Assignments:
  - Assign log to team member: "John, fix this recursion"
  - Status: To Do, In Progress, Review, Done (mini Kanban)
  - Due dates (optional)
- [ ] Activity Feed:
  - "Sarah uploaded 5 logs to Q1 Performance Audit"
  - "John commented on CaseTrigger.apxt"
  - "Mike resolved trigger recursion issue"
- [ ] Access Control:
  - Admin: Full access (invite, remove, billing)
  - Member: Can view/comment/upload
  - Viewer: Read-only (view logs, can't comment)

**Implementation Notes:**
- Backend API: Add Teams table, TeamMembers, Projects, Comments
- Desktop app: Add "Switch to Team" dropdown (if user belongs to multiple teams)
- Sync engine: Poll API every 30 seconds for new logs/comments
- Conflict resolution: Last-write-wins (simple, no CRDT needed)

**Pricing:**
- $99/month for 5 users
- $19/month per additional user (6-20 users)
- 21+ users → Contact Sales (Enterprise tier)

---

### Issue #10: CLI Streaming - Real-Time Monitoring UI
**Type:** Feature | **Priority:** P2-medium | **Size:** M | **Milestone:** v1.2

**User Story:**
As a developer debugging in real-time, I want to see logs streaming live in Black Widow so that I don't have to switch between terminal and app.

**Current State:**
- SalesforceCliService exists (can start/stop streaming)
- Logs captured from `sf apex tail log`
- **Missing:** UI to display streaming logs

**Acceptance Criteria:**
- [ ] "Start Streaming" button in toolbar
  - Shows connection dialog (select org)
  - Starts `sf apex tail log --target-org {username}`
- [ ] Streaming panel (bottom half of window, resizable splitter):
  ```
  ┌─────────────────────────────────────────┐
  │ 🔴 Live Streaming - user@example.com    │
  │ [Stop] [Clear] [Auto-scroll ✓] [⚙️]     │
  ├─────────────────────────────────────────┤
  │ 14:35:21 | USER_INFO | [User: John]     │
  │ 14:35:21 | CODE_UNIT_STARTED | Trigger  │
  │ 14:35:21 | SOQL_EXECUTE | SELECT Id...  │
  │ 14:35:22 | CODE_UNIT_FINISHED | 1.2s    │
  │ ▼ (auto-scroll to bottom)                │
  └─────────────────────────────────────────┘
  ```
- [ ] Color-coded log levels:
  - DEBUG: Gray
  - INFO: White
  - WARN: Yellow
  - ERROR: Red
  - FATAL: Dark red (bold)
- [ ] Filters:
  - Show only: SOQL, DML, Exceptions, User Actions
  - Hide system logs (e.g., Visualforce noise)
- [ ] Context menu:
  - Right-click line → "Copy", "Filter by this", "Highlight keyword"
- [ ] Performance:
  - Buffer 10,000 lines max (auto-trim older)
  - Virtualized scrolling (only render visible lines)
- [ ] Save to file:
  - "Save Session" button → Export all buffered logs to .log file

**Implementation Notes:**
- Views/StreamingPanel.xaml (UserControl, dockable)
- Use `TextBlock` with `Run` elements for color-coding
- Subscribe to `SalesforceCliService.LogReceived` event
- Use `ObservableCollection<LogLine>` with `CollectionChanged` throttling

**Edge Cases:**
- Handle CLI disconnect (org logout, network loss)
- Handle rate limits (Salesforce limits log streaming)
- Show warning if logs > 10K/minute (too noisy)

---

### Issue #11: Governance Report Export (PDF)
**Type:** Feature | **Priority:** P2-medium | **Size:** M | **Milestone:** v1.2

**User Story:**
As a team lead, I want to export a governance report so that I can share findings with my development team in a meeting.

**Use Case:**
"Here's the Q1 performance audit. We have 5 critical issues and 12 recommendations. Let's prioritize the top 3."

**Report Sections:**
1. **Executive Summary:**
   - Total logs analyzed: 47
   - Total duration: 3m 15s
   - Critical issues: 5
   - High priority: 12
   - Medium priority: 8
   - Governance violations: 3 (mixed contexts)
2. **Top Issues:**
   - Trigger recursion in CaseTrigger (fired 5x, wasting 2.5s)
   - N+1 query pattern in RelatedCasesController (45 SOQL queries)
   - @future method taking 3.8s (SendEmailNotification)
3. **Recommendations:**
   - [P0] Add recursion control to CaseTrigger
   - [P1] Batch SOQL queries in RelatedCasesController
   - [P1] Investigate SendEmailNotification performance
   - [P2] Load Lightning components in parallel (save 1.2s)
4. **Governor Limits:**
   - SOQL Queries: 78/100 (⚠️ 78% used)
   - DML Statements: 45/150 (✅ 30% used)
   - CPU Time: 4500ms/10000ms (⚠️ 45% used)
   - Heap Size: 3.2MB/6MB (✅ 53% used)
5. **Timeline Visualization:**
   - Waterfall chart showing transaction phases
   - Color-coded by context (Interactive/Batch/etc.)
6. **Appendix:**
   - Full log file names
   - Metadata (users, timestamps, org info)

**Acceptance Criteria:**
- [ ] "Export Report" button in toolbar (and right-click context menu)
- [ ] File save dialog (default: `BlackWidow_Report_2026-02-02.pdf`)
- [ ] Professional design:
  - Black Widow logo (top-left)
  - Discord purple theme (#5865F2 headers)
  - Tables with alternating row colors
  - Page numbers (bottom-right)
  - Generated timestamp (footer)
- [ ] Charts (optional v2.0):
  - Governor limit usage (donut chart)
  - Timeline (Gantt chart)

**Implementation Notes:**
```csharp
// Services/ReportService.cs
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

public class ReportService {
    public void GeneratePDF(LogGroup group, string filePath) {
        Document.Create(container => {
            container.Page(page => {
                page.Header().Text("Black Widow Governance Report");
                page.Content().Column(column => {
                    column.Item().Text("Executive Summary").Bold();
                    column.Item().Text($"Total Logs: {group.Logs.Count}");
                    // ... more sections
                });
                page.Footer().AlignRight().Text($"Generated {DateTime.Now:yyyy-MM-dd}");
            });
        }).GeneratePdf(filePath);
    }
}
```

**Dependencies:**
- NuGet: `QuestPDF` (v2023.12.0 or later)
- License: QuestPDF is free for open-source (Community license)

---

### Issue #12: Vocational School Partnership Program
**Type:** Feature | **Priority:** P2-medium | **Size:** XL | **Milestone:** v1.2

**User Story:**
As Black Widow, I want to partner with vocational schools to create a pipeline of Tier 2 (Emerging) consultants so that we have enough supply to meet marketplace demand.

**Business Case:**
- Year 2 projection: 500 marketplace projects/month
- Need 100+ partners to handle volume
- Recruiting individual freelancers is slow
- Schools graduate 20-50 Salesforce students per cohort
- One partnership = 20 new partners every 3 months

**Partnership Model:**
1. **Identify Schools:**
   - Salesforce Trailhead Academy partners
   - Code bootcamps (General Assembly, Flatiron School)
   - Community colleges with Salesforce programs
   - Target: 5 partnerships Year 1
2. **Apprenticeship Program:**
   - Students work on real Black Widow marketplace projects
   - Mentored by Tier 1 (Certified) partners
   - Paid $25-40/hr (vs $150+ for Certified)
   - School gets 20% commission (vs 15% for individuals)
   - Students build portfolio, earn reputation
3. **Graduation Path:**
   - Complete 10 projects → Earn "Emerging Partner" badge
   - Maintain 4+ star rating → Unlock higher-value projects
   - After 6 months + certification → Upgrade to Tier 1 (Certified)

**Implementation (Marketplace Enhancement):**
- [ ] "Apprentice" tier in Partner Dashboard
  - Flag: `isApprentice: true`, `schoolId: "ga-2026-q1"`
  - Lower visibility (not shown to Enterprise clients)
  - Paired with mentor (Certified partner required to review work)
- [ ] Mentor Assignment:
  - When apprentice bids, auto-assign Certified mentor
  - Mentor reviews code before delivery (extra QA step)
  - Mentor earns 10% bonus if apprentice succeeds
- [ ] School Dashboard:
  - Show all apprentices from school
  - Track performance (projects completed, ratings)
  - Leaderboard (gamification)
- [ ] Commission Split:
  - Client pays: $500
  - Apprentice earns: $400 (80%)
  - School earns: $100 (20%)
  - Black Widow earns: $0 (reinvest in partnership)
- [ ] Marketing Materials:
  - Create pitch deck for schools
  - Case study: "How General Assembly increased job placement by 30%"
  - ROI calculator: "Your students can earn $5K/month while learning"

**Legal Considerations:**
- Schools are partners (revenue share agreement)
- Students are independent contractors (not employees)
- Insurance: Schools require general liability insurance ($1M coverage)
- Background checks: Required for Enterprise projects

**Timeline:**
- Month 1: Identify 10 target schools
- Month 2: Cold outreach (email + LinkedIn)
- Month 3: Pitch 3 schools (video call, demo Black Widow)
- Month 4: Pilot with 1 school (10 students)
- Month 6: Expand to 3 schools (50 students total)

---

## 🚀 Future Enhancements (v2.0+) - September 2026+

### Issue #13: Voice Command Interface
**Type:** Feature | **Priority:** P3-low | **Size:** XL | **Milestone:** v2.0

**User Story:**
As a presenter showing log analysis to stakeholders, I want to navigate with voice commands so that I can keep focus on the screen and engage the audience.

**Example Commands:**
- "Show me trigger recursion"
- "Find logs longer than 5 seconds"
- "Filter by Case trigger"
- "Export this transaction"
- "Switch to streaming mode"
- "Connect to production org"

**Acceptance Criteria:**
- [ ] Microphone button in toolbar (toggle on/off)
- [ ] Visual feedback:
  - Listening: 🎤 (red pulsing)
  - Processing: ⏳ (loading spinner)
  - Recognized: ✅ (green checkmark + show command)
- [ ] 15-20 common commands
- [ ] Fuzzy matching (handle variations):
  - "Show recursion" = "Show me trigger recursion"
  - "Find slow logs" = "Find logs longer than 5 seconds"
- [ ] Voice feedback (optional):
  - Text-to-speech: "Showing 3 logs with trigger recursion"
- [ ] Settings:
  - Enable/disable voice
  - Choose microphone (dropdown)
  - Adjust sensitivity
  - Language (English only v2.0)

**Implementation Notes:**
```csharp
// Services/VoiceCommandService.cs
using System.Speech.Recognition;

public class VoiceCommandService {
    private SpeechRecognitionEngine _recognizer;
    
    public void Initialize() {
        _recognizer = new SpeechRecognitionEngine();
        
        var grammar = new GrammarBuilder();
        grammar.Append(new Choices("show", "find", "filter", "export"));
        grammar.Append(new Choices("recursion", "logs", "trigger", "transaction"));
        
        _recognizer.LoadGrammar(new Grammar(grammar));
        _recognizer.SpeechRecognized += OnSpeechRecognized;
        _recognizer.SetInputToDefaultAudioDevice();
        _recognizer.RecognizeAsync(RecognizeMode.Multiple);
    }
    
    private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
        var command = e.Result.Text;
        // Parse command and execute action
    }
}
```

**Dependencies:**
- Windows Speech Recognition (built-in)
- System.Speech.Recognition namespace
- Microphone permission (user consent)

**Future Enhancement (v2.1):**
- OpenAI Whisper API for better accuracy
- Natural language processing: "Show me the slowest log from yesterday"

---

### Issue #14: SSO (Single Sign-On) for Enterprise
**Type:** Feature | **Priority:** P3-low | **Size:** L | **Milestone:** v2.0

**User Story:**
As an Enterprise IT admin, I want to enforce SSO (SAML/OAuth) so that my team doesn't have to manage separate passwords for Black Widow.

**Supported Providers:**
- Okta
- Azure AD (Microsoft Entra)
- Google Workspace
- OneLogin
- Ping Identity

**Implementation:**
- [ ] SAML 2.0 support (industry standard for Enterprise SSO)
- [ ] Configuration in desktop app:
  - Settings → SSO → "Configure SSO"
  - Input: Identity Provider metadata URL
  - Test connection button
- [ ] Login flow:
  - User clicks "Login with SSO"
  - Browser opens: `https://idp.example.com/saml/login`
  - User authenticates with corporate credentials
  - Redirect: `blackwidow://sso-callback?SAMLResponse=xxx`
  - App validates signature, extracts user info
  - User logged in (license tied to email domain)
- [ ] Group mapping:
  - IT admin maps Azure AD groups → Black Widow roles
  - Example: "Salesforce-Admins" → Team Admin
  - Example: "Salesforce-Devs" → Team Member

**Pricing:**
- SSO is Enterprise-only feature
- Enterprise tier: $499/month for 25 users + SSO + on-premise

**Dependencies:**
- NuGet: `Sustainsys.Saml2` (SAML library)
- Backend API: Validate SAML responses server-side (security)

---

### Issue #15: On-Premise Deployment (Enterprise)
**Type:** Feature | **Priority:** P3-low | **Size:** XL | **Milestone:** v2.0

**User Story:**
As an Enterprise security officer, I want to deploy Black Widow on-premise so that debug logs never leave our network (compliance requirement).

**Use Cases:**
- Financial services (PCI-DSS, SOX)
- Healthcare (HIPAA)
- Government (FedRAMP, ITAR)

**Architecture:**
- Desktop app (WPF) runs locally (no change)
- Backend API deployed on customer's infrastructure:
  - Docker container (Linux)
  - Windows Server (IIS)
  - Kubernetes (Helm chart)
- Database: Customer-managed (SQL Server, PostgreSQL)
- License server: Callback to Black Widow cloud (check license validity)

**Deployment Options:**
1. **Docker Compose** (simplest):
   ```yaml
   services:
     blackwidow-api:
       image: blackwidow/api:latest
       environment:
         - LICENSE_KEY=${LICENSE_KEY}
         - DATABASE_URL=${DATABASE_URL}
     database:
       image: postgres:15
   ```
2. **Kubernetes** (scalable):
   - Helm chart: `helm install blackwidow blackwidow/api`
   - Ingress: HTTPS with customer's SSL cert
3. **Windows Server** (traditional):
   - MSI installer (Web Deploy package)
   - IIS site configuration

**Challenges:**
- Reverse ETL (if marketplace is used): How do partners access logs?
  - Solution: Sanitized export (PII removed, anonymized)
- Updates: How to push updates to on-premise?
  - Solution: Customers opt-in to update checks (phone home once/week)
- Support: SSH access for troubleshooting?
  - Solution: Log shipping to Black Widow support (opt-in)

**Pricing:**
- $5,000/year license + $2,000 setup fee
- Includes: Docker image, documentation, email support (24h SLA)

---

### Issue #16: Multi-Language Support (i18n)
**Type:** Feature | **Priority:** P3-low | **Size:** XL | **Milestone:** v2.0

**User Story:**
As a French-speaking Salesforce admin, I want Black Widow in French so that I can use it more efficiently.

**Target Languages (Priority Order):**
1. English (US) - Default, already done
2. Spanish (ES) - 22% of Salesforce customers
3. French (FR) - 15% (Canada, France, Africa)
4. German (DE) - 10% (Germany, Austria, Switzerland)
5. Portuguese (BR) - 8% (Brazil is huge Salesforce market)
6. Japanese (JP) - 6% (high-value Enterprise customers)

**Implementation:**
- [ ] Resource files (.resx) for each language:
  - Resources.en-US.resx
  - Resources.es-ES.resx
  - Resources.fr-FR.resx
- [ ] Translate all UI strings (buttons, labels, tooltips)
- [ ] Do NOT translate:
  - Code snippets (SOQL, Apex)
  - Log file contents (raw Salesforce output)
  - Technical terms (SOQL, DML, CPU Time)
- [ ] Language selector:
  - Settings → Language → Dropdown (flags + names)
  - Restart required (or reload UI dynamically)
- [ ] Fallback logic:
  - If translation missing → Show English
  - Log warning: "Missing translation: {key}"

**Translation Strategy:**
1. **Phase 1:** Machine translation (DeepL API)
   - Cost: $0.02 per 1,000 chars (~$50 for all languages)
   - Fast, good enough for v2.0
2. **Phase 2:** Community translation (v2.1)
   - Crowdin.com (free for open-source)
   - Invite community contributors
   - Review by native speakers (bounty: $50/language)

**Right-to-Left (RTL) Support:**
- Arabic (AR) - 2% of customers
- Hebrew (HE) - <1%
- Defer to v3.0 (requires UI layout changes)

---

### Issue #17: Browser Extension (Quick Log Upload)
**Type:** Feature | **Priority:** P3-low | **Size:** M | **Milestone:** v2.0

**User Story:**
As a Salesforce admin, I want to send a log from Salesforce Setup directly to Black Widow so that I don't have to download and manually upload.

**How It Works:**
1. Install browser extension (Chrome, Edge, Firefox)
2. Navigate to Salesforce Setup → Debug Logs
3. Right-click log → "Send to Black Widow"
4. Extension downloads log via Salesforce API
5. Sends to local Black Widow app (WebSocket or HTTP POST to localhost:7777)
6. Black Widow parses and displays immediately

**Acceptance Criteria:**
- [ ] Chrome extension manifest v3
- [ ] Content script injected on `*.salesforce.com`
- [ ] Context menu: "Send to Black Widow"
- [ ] Detect if Black Widow app is running (ping localhost:7777)
- [ ] If not running → Show "Install Black Widow" link
- [ ] Support multi-select (send 5 logs at once)

**Implementation Notes:**
```javascript
// background.js
chrome.contextMenus.create({
  id: "send-to-blackwidow",
  title: "Send to Black Widow",
  contexts: ["link"],
  targetUrlPatterns: ["*://*/apex/apexdebug?*"]
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  // Download log from Salesforce
  const logUrl = info.linkUrl;
  const logContent = await fetch(logUrl).then(r => r.text());
  
  // Send to Black Widow local server
  await fetch('http://localhost:7777/api/logs/import', {
    method: 'POST',
    body: JSON.stringify({ content: logContent })
  });
});
```

**Desktop App Enhancement:**
- Add HTTP server (listen on localhost:7777)
- Endpoint: POST `/api/logs/import` (accept log content)
- CORS: Allow `https://*.salesforce.com`

---

### Issue #18: AI-Powered Root Cause Analysis
**Type:** Feature | **Priority:** P3-low | **Size:** XL | **Milestone:** v3.0

**User Story:**
As a junior admin, I want Black Widow to explain WHY the trigger recursion happened (not just that it happened) so that I can learn and fix it correctly.

**Current State:**
- Black Widow detects problems (recursion, N+1 queries)
- Shows recommendations ("Add recursion control")
- **Doesn't explain root cause or teach**

**Enhanced AI Analysis:**
- [ ] GPT-4 integration:
  - Input: LogGroup + detected issues
  - Output: Plain English explanation
  - Example:
    ```
    🤖 AI Analysis: Trigger Recursion
    
    Your CaseTrigger fired 3 times because:
    1. You update the Case in the trigger
    2. The update fires the trigger again
    3. No static variable to prevent re-entry
    
    Why it's bad:
    - Wastes 2.5 seconds (CPU limit risk)
    - If it fires 10x, you'll hit the 10-second timeout
    
    How to fix:
    1. Add a static Set<Id> to track processed records
    2. Check the Set before processing
    3. Alternative: Use a "Process Builder bypass" custom field
    
    Learn more: [Link to Salesforce best practices]
    ```
- [ ] "Explain this to me" button (next to each issue)
- [ ] Adjustable complexity:
  - Beginner: ELI5 (Explain Like I'm 5)
  - Intermediate: Standard explanation
  - Expert: Technical details + code snippets
- [ ] Learning mode:
  - Toggle in settings: "Show explanations automatically"
  - Collapsible panel (don't overwhelm)

**Cost Analysis:**
- GPT-4 Turbo: $0.01 per 1K tokens
- Average explanation: 300 tokens = $0.003 per issue
- If 1,000 users analyze 10 issues/month = $30/month
- Bundle into Pro tier (no extra charge)

**Privacy:**
- Sanitize logs before sending to OpenAI:
  - Remove usernames, emails, record IDs
  - Keep technical patterns (SOQL, DML)
- Add setting: "Enable AI analysis" (opt-in)
- Enterprise: Use Azure OpenAI (data never leaves tenant)

---

## 🐛 Known Bugs & Technical Debt

### Issue #19: Fix TODO at MainViewModel.cs:192
**Type:** Bug | **Priority:** P2-medium | **Size:** XS | **Milestone:** v1.0

**Description:**
There's a TODO comment at line 192 in MainViewModel.cs:
```csharp
// TODO: Implement settings dialog
```

**What's Needed:**
- [ ] Create Views/SettingsDialog.xaml
- [ ] Settings to implement:
  - Default Salesforce org (dropdown)
  - Theme (Light/Dark/Auto)
  - Log parser settings (show debug vs release)
  - Privacy (enable AI analysis, send anonymized usage data)
  - Updates (auto-check for updates, channel: Stable/Beta)
  - About (version, license, links)
- [ ] Save settings to local file (JSON)
- [ ] Apply settings without restart (where possible)

---

### Issue #20: Performance Testing with 1000+ Logs
**Type:** Test | **Priority:** P2-medium | **Size:** M | **Milestone:** v1.0

**Description:**
Need to test Black Widow with large log folders (1000+ logs) to ensure performance.

**Test Cases:**
- [ ] Load folder with 1,000 logs (100MB total)
- [ ] Load folder with 10,000 logs (1GB total)
- [ ] Measure:
  - Time to scan metadata (target: <30s for 1,000 logs)
  - Time to group transactions (target: <5s for 1,000 logs)
  - Memory usage (target: <500MB for 1,000 logs)
  - UI responsiveness (target: <100ms click latency)
- [ ] Optimize if needed:
  - Parallel processing (use all CPU cores)
  - Lazy loading (load metadata, parse on-demand)
  - Caching (store parsed logs in SQLite)

---

### Issue #21: Context Detection Accuracy Tuning
**Type:** Enhancement | **Priority:** P2-medium | **Size:** S | **Milestone:** v1.1

**Description:**
LogMetadataExtractor.DetectExecutionContext() currently uses keyword matching. Accuracy is ~95% (estimated). Need to measure and improve.

**Test Plan:**
- [ ] Collect 100 real debug logs (labeled manually):
  - 20 Interactive (UI actions)
  - 20 Batch (Apex Batch, Scheduled)
  - 20 Integration (Inbound API, Outbound callouts)
  - 20 Scheduled (Process Builder, Flows)
  - 20 Async (@future, Queueable)
- [ ] Run DetectExecutionContext() on all 100
- [ ] Calculate accuracy: `correctPredictions / 100`
- [ ] If <98% → Add more keywords or use ML model

**Improvement Ideas:**
- Add more keywords (scan 1,000 logs for patterns)
- Use log structure (e.g., Batch logs have `BATCH_START`)
- Train simple ML model (Naive Bayes) if keywords fail

---

## 📊 Metrics & Analytics (Future)

### Issue #22: Usage Analytics Dashboard
**Type:** Feature | **Priority:** P3-low | **Size:** L | **Milestone:** v2.0

**User Story:**
As Black Widow, I want to track how users are using the app so that I can prioritize features and improve UX.

**Metrics to Track:**
- [ ] User engagement:
  - Daily Active Users (DAU)
  - Weekly Active Users (WAU)
  - Monthly Active Users (MAU)
  - DAU/MAU ratio (stickiness)
- [ ] Feature usage:
  - % of users who use transaction grouping
  - % of users who use CLI streaming
  - % of users who export reports
- [ ] Conversion funnel:
  - Free users → Trial started → Trial converted → Paid
  - Free users → Marketplace lead submitted
- [ ] Performance:
  - Average log parse time
  - Average logs per session
  - App crash rate
- [ ] Marketplace:
  - Leads generated per week
  - Bid acceptance rate
  - Average project value
  - Partner satisfaction (NPS)

**Implementation:**
- Use Mixpanel or Amplitude (free tier: 10M events/month)
- Send events from desktop app (opt-in, anonymized)
- Build internal dashboard (visualize metrics)

---

## 🎯 Summary: Priority Roadmap

### Critical Path to Launch (v1.0 - March 1, 2026):
1. ✅ Issue #1: License validation (P0-critical, size:L)
2. ✅ Issue #2: Upgrade flow UI (P0-critical, size:M)
3. ✅ Issue #3: Stripe integration (P0-critical, size:L)
4. ✅ Issue #4: Transaction grouping UI (P1-high, size:M)
5. ✅ Issue #5: OAuth flow (P1-high, size:M)

**Total effort: ~3 weeks** (if working 10-15 hrs/week)

### Marketplace Launch (v1.1 - April 1, 2026):
6. ✅ Issue #6: Partner dashboard (P1-high, size:XL)
7. ✅ Issue #7: Automated bid system (P1-high, size:XL)
8. ✅ Issue #8: Escrow system (P1-high, size:L)

**Total effort: ~6 weeks**

### Scale & Growth (v1.2 - June 1, 2026):
9. ✅ Issue #9: Team tier (P2-medium, size:L)
10. ✅ Issue #10: CLI streaming UI (P2-medium, size:M)
11. ✅ Issue #11: Governance report PDF (P2-medium, size:M)
12. ✅ Issue #12: Vocational school program (P2-medium, size:XL)

**Total effort: ~8 weeks**

### Future (v2.0+ - September 2026+):
13. ✅ Issue #13: Voice commands (P3-low, size:XL)
14. ✅ Issue #14: SSO (P3-low, size:L)
15. ✅ Issue #15: On-premise (P3-low, size:XL)
16. ✅ Issue #16: Multi-language (P3-low, size:XL)
17. ✅ Issue #17: Browser extension (P3-low, size:M)
18. ✅ Issue #18: AI root cause analysis (P3-low, size:XL)

---

## 🚀 Next Steps

1. **Copy issues into GitHub** (15 minutes):
   - Go to: https://github.com/YOUR_USERNAME/log_analyser/issues/new
   - Choose template (Feature, Bug, or User Story)
   - Paste issue content from this document
   - Add labels (type, priority, size, milestone)
   - Assign to yourself

2. **Prioritize top 3** (5 minutes):
   - Move to "Ready" column in Project board
   - These are your focus for this week

3. **Start coding!** 🎉
   - Create feature branch: `git checkout -b feature/issue-1-license-validation`
   - Work on issue
   - Commit regularly
   - Open PR when done

---

**Total Issues Created: 22** (5 critical, 8 high, 6 medium, 3 low)
**Estimated Effort: 6-9 months to v2.0** (working 10-15 hrs/week)
**Revenue Potential: $2.2M by Year 3** (conservative)

Let's build this! 🕷️💪
