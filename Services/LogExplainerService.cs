using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Generates deep, plain-English explanations of log analysis results.
/// Transforms technical data into mentoring-quality explanations with analogies,
/// before/after code examples, root cause analysis, and learning content.
/// 
/// This is the "brain" that makes Black Widow different from every other log tool.
/// </summary>
public class LogExplainerService
{
    /// <summary>
    /// Generate a complete LogExplanation from a parsed LogAnalysis.
    /// This is the main entry point — call it after parsing a log.
    /// </summary>
    public LogExplanation Explain(LogAnalysis analysis)
    {
        var explanation = new LogExplanation();

        // 1. WHAT HAPPENED — plain English summary
        explanation.WhatHappened = BuildWhatHappened(analysis);
        explanation.WhatYourCodeDid = BuildWhatYourCodeDid(analysis);
        explanation.PerformanceVerdict = BuildPerformanceVerdict(analysis);
        explanation.VerdictIcon = GetVerdictIcon(analysis);

        // 2. DETAILED ISSUES — deep explanations with analogies
        explanation.Issues = BuildDetailedIssues(analysis);

        // 3. RECOMMENDATIONS — with before/after code
        explanation.Recommendations = BuildDetailedRecommendations(analysis);

        // 4. LEARNING — what concepts to take away
        explanation.WhatYouLearned = BuildLearningItems(analysis);

        // 5. OVERALL ASSESSMENT
        explanation.OverallAssessment = BuildOverallAssessment(analysis);
        explanation.Priority = DeterminePriority(analysis);
        explanation.Effort = EstimateEffort(analysis);
        explanation.ImpactSummary = BuildImpactSummary(analysis);

        return explanation;
    }

    // ================================================================
    // SECTION 1: WHAT HAPPENED
    // ================================================================

    private string BuildWhatHappened(LogAnalysis analysis)
    {
        var user = string.IsNullOrEmpty(analysis.LogUser) ? "A user" : analysis.LogUser;

        if (analysis.IsTestExecution)
        {
            var result = analysis.TransactionFailed ? "❌ The test failed." : "✅ The test passed.";
            return $"Salesforce ran a test called \"{analysis.TestClassName}\". {result}";
        }

        if (!string.IsNullOrEmpty(analysis.EntryPoint))
        {
            var friendlyEntry = FriendlyEntryPoint(analysis.EntryPoint);
            var result = analysis.TransactionFailed
                ? "❌ The operation failed with an error."
                : "✅ The operation completed successfully.";
            return $"{user} triggered {friendlyEntry}. {result}";
        }

        return analysis.TransactionFailed
            ? $"{user} performed an action, but ❌ something went wrong."
            : $"{user} performed an action. ✅ Everything worked.";
    }

    private List<string> BuildWhatYourCodeDid(LogAnalysis analysis)
    {
        var items = new List<string>();

        // Entry point
        if (!string.IsNullOrEmpty(analysis.EntryPoint))
            items.Add($"• {analysis.OperationIcon} **{analysis.OperationType}** fired: {FriendlyEntryPoint(analysis.EntryPoint)}");

        // Methods called
        var methodCount = analysis.MethodStats?.Count ?? 0;
        if (methodCount > 0)
            items.Add($"• Called **{methodCount} method{(methodCount > 1 ? "s" : "")}** during execution");

        // Database operations
        var soqlCount = analysis.DatabaseOperations?.Count(d => d.OperationType == "SOQL") ?? 0;
        var dmlCount = analysis.DatabaseOperations?.Count(d => d.OperationType == "DML") ?? 0;
        if (soqlCount > 0)
            items.Add($"• 💾 Talked to the database **{soqlCount} time{(soqlCount > 1 ? "s" : "")}** to read data");
        if (dmlCount > 0)
            items.Add($"• 📝 Saved data **{dmlCount} time{(dmlCount > 1 ? "s" : "")}** (inserts/updates/deletes)");

        // Callouts
        if (analysis.Callouts?.Count > 0)
            items.Add($"• 🌐 Made **{analysis.Callouts.Count} external API call{(analysis.Callouts.Count > 1 ? "s" : "")}**");

        // Flows
        if (analysis.Flows?.Count > 0)
            items.Add($"• 🔄 Ran **{analysis.Flows.Count} Flow{(analysis.Flows.Count > 1 ? "s" : "")}**");

        // Trigger re-entry
        var reentries = analysis.TriggerReEntries?.Where(t => t.HasReEntry).ToList();
        if (reentries?.Count > 0)
        {
            foreach (var re in reentries)
                items.Add($"• 🔁 **{re.TriggerName}** fired **{re.TotalFireCount} times** (recursion detected!)");
        }

        // Errors
        if (analysis.Errors?.Count > 0)
            items.Add($"• ❌ Hit **{analysis.Errors.Count} error{(analysis.Errors.Count > 1 ? "s" : "")}**");

        if (items.Count == 0)
            items.Add("• Processed a Salesforce transaction");

        return items;
    }

    private string BuildPerformanceVerdict(LogAnalysis analysis)
    {
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit == null) return "No performance data available.";

        var soqlPct = SafePercent(lastLimit.SoqlQueries, lastLimit.SoqlQueriesLimit);
        var dmlPct = SafePercent(lastLimit.DmlStatements, lastLimit.DmlStatementsLimit);
        var cpuPct = SafePercent(lastLimit.CpuTime, lastLimit.CpuTimeLimit);
        var maxPct = Math.Max(soqlPct, Math.Max(dmlPct, cpuPct));

        if (analysis.TransactionFailed)
            return "❌ This transaction failed. The code hit an error before completing.";
        if (maxPct >= 95)
            return $"🔥 Your code barely survived! It used {maxPct:F0}% of at least one Salesforce limit. One more record and it will crash.";
        if (maxPct >= 80)
            return $"⚠️ Your code is using {maxPct:F0}% of Salesforce limits — dangerously close. It works now but won't scale.";
        if (maxPct >= 50)
            return $"⚡ Your code used {maxPct:F0}% of limits. It works fine but has room for optimization.";

        return "✅ Your code is efficient and well within Salesforce limits. Nice work!";
    }

    private string GetVerdictIcon(LogAnalysis analysis)
    {
        if (analysis.TransactionFailed) return "❌";
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit == null) return "✅";
        var maxPct = MaxLimitPercent(lastLimit);
        if (maxPct >= 95) return "🔥";
        if (maxPct >= 80) return "⚠️";
        if (maxPct >= 50) return "⚡";
        return "✅";
    }

    // ================================================================
    // SECTION 2: DETAILED ISSUES
    // ================================================================

    private List<DetailedIssue> BuildDetailedIssues(LogAnalysis analysis)
    {
        var issues = new List<DetailedIssue>();

        // --- ERRORS ---
        if (analysis.Errors?.Any() == true)
        {
            foreach (var error in analysis.Errors.Take(3))
            {
                issues.Add(new DetailedIssue
                {
                    Icon = "🔴",
                    Title = "Fatal Error: " + TruncateText(error.Name, 80),
                    WhatThisMeans = "Your code threw an exception that stopped the entire transaction. Whatever the user was trying to do didn't work — they saw an error message.",
                    WhyThisHappened = $"The error occurred in: {error.Name}",
                    Analogy = "Think of it like a recipe where one step says \"add 2 cups of sugar\" but there's no sugar in the kitchen. The chef has to stop — they can't just skip the step.",
                    WhyItsBad = "The user's action completely failed. If this is a trigger or flow, the record they were saving didn't get saved.",
                    Severity = "Critical"
                });
            }
        }

        // --- N+1 QUERY PATTERN ---
        var duplicateQueries = analysis.DuplicateQueries?.Where(d => d.ExecutionCount > 3).ToList();
        if (duplicateQueries?.Any() == true)
        {
            var worst = duplicateQueries.OrderByDescending(d => d.ExecutionCount).First();
            issues.Add(new DetailedIssue
            {
                Icon = "🔁",
                Title = $"N+1 Query Pattern ({worst.ExecutionCount}x repeated queries)",
                WhatThisMeans = $"Your code is asking the database the same question {worst.ExecutionCount} separate times instead of once. Each query takes time, and Salesforce limits you to {(analysis.IsAsyncExecution ? 200 : 100)} queries per transaction.",
                WhyThisHappened = "There's likely a SOQL query inside a loop (for/while). Every time the loop runs, it fires another query.",
                Analogy = $"This is like going to the grocery store {worst.ExecutionCount} times to buy one item each trip, instead of making a list and going once. You'd waste hours driving back and forth!",
                WhyItsBad = $"You used {worst.ExecutionCount} of your {(analysis.IsAsyncExecution ? 200 : 100)} allowed queries on the same thing. With more records, this WILL hit the limit and crash.",
                Severity = worst.ExecutionCount > 20 ? "Critical" : "High"
            });
        }

        // --- GOVERNOR LIMIT WARNINGS ---
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit != null)
        {
            var soqlPct = SafePercent(lastLimit.SoqlQueries, lastLimit.SoqlQueriesLimit);
            if (soqlPct >= 80)
            {
                issues.Add(new DetailedIssue
                {
                    Icon = "💾",
                    Title = $"SOQL Query Limit at {soqlPct:F0}% ({lastLimit.SoqlQueries}/{lastLimit.SoqlQueriesLimit})",
                    WhatThisMeans = $"Salesforce limits how many times you can query the database to {lastLimit.SoqlQueriesLimit} per transaction. You've used {soqlPct:F0}%!",
                    WhyThisHappened = "Your code is making too many individual database reads. This usually means queries inside loops or multiple redundant queries.",
                    Analogy = "Imagine a library that lets you check out 100 books per visit. You've checked out " + lastLimit.SoqlQueries + " — if you need even one more, the librarian will refuse and kick you out.",
                    WhyItsBad = soqlPct >= 95
                        ? "You're essentially at the limit. Adding one more record will crash your code."
                        : "You're in the danger zone. This code won't scale to handle more records.",
                    Severity = soqlPct >= 95 ? "Critical" : "High"
                });
            }

            var cpuPct = SafePercent(lastLimit.CpuTime, lastLimit.CpuTimeLimit);
            if (cpuPct >= 80)
            {
                issues.Add(new DetailedIssue
                {
                    Icon = "⏱️",
                    Title = $"CPU Time at {cpuPct:F0}% ({lastLimit.CpuTime}ms/{lastLimit.CpuTimeLimit}ms)",
                    WhatThisMeans = $"Salesforce gives your code {lastLimit.CpuTimeLimit / 1000} seconds of processing time. You've used {cpuPct:F0}%.",
                    WhyThisHappened = "Your code is doing too much computation — possibly processing records one at a time, running complex logic, or calling slow methods.",
                    Analogy = $"Think of it like a timed exam. You have {lastLimit.CpuTimeLimit / 1000} seconds to finish, and you've used {lastLimit.CpuTime / 1000.0:F1} seconds. If you run out of time, your answer gets thrown away.",
                    WhyItsBad = "If CPU time exceeds the limit, Salesforce kills the transaction immediately. The user sees a confusing timeout error.",
                    Severity = cpuPct >= 95 ? "Critical" : "High"
                });
            }

            var dmlPct = SafePercent(lastLimit.DmlStatements, lastLimit.DmlStatementsLimit);
            if (dmlPct >= 80)
            {
                issues.Add(new DetailedIssue
                {
                    Icon = "📝",
                    Title = $"DML Limit at {dmlPct:F0}% ({lastLimit.DmlStatements}/{lastLimit.DmlStatementsLimit})",
                    WhatThisMeans = $"Salesforce limits database writes (inserts, updates, deletes) to {lastLimit.DmlStatementsLimit} per transaction. You've used {dmlPct:F0}%.",
                    WhyThisHappened = "Your code is doing individual inserts/updates inside a loop instead of collecting records and saving them all at once.",
                    Analogy = "Instead of writing one email to 50 people (BCC), you're writing 50 individual emails saying the same thing. It takes 50x longer!",
                    WhyItsBad = "Every DML statement has overhead. Batching them into one call is dramatically faster and safer.",
                    Severity = dmlPct >= 95 ? "Critical" : "High"
                });
            }
        }

        // --- TRIGGER RECURSION ---
        var reentries = analysis.TriggerReEntries?.Where(t => t.HasReEntry).ToList();
        if (reentries?.Any() == true)
        {
            foreach (var re in reentries)
            {
                issues.Add(new DetailedIssue
                {
                    Icon = "🔄",
                    Title = $"{re.TriggerName} fired {re.TotalFireCount} times (recursion)",
                    WhatThisMeans = $"The trigger \"{re.TriggerName}\" ran {re.TotalFireCount} times in a single transaction instead of once. This wastes resources and can cause infinite loops.",
                    WhyThisHappened = "Your trigger updates the same object it's triggered on, which fires the trigger again. This creates a loop.",
                    Analogy = "It's like a smoke detector that's so sensitive it goes off from the steam of cooking — which makes you fan the air — which triggers it again. A never-ending cycle!",
                    WhyItsBad = $"Each re-entry burns through governor limits. With {re.TotalFireCount} runs, you're using {re.TotalFireCount}x the SOQL/DML you should be. In worst case, Salesforce kills the transaction.",
                    Severity = re.TotalFireCount > 3 ? "Critical" : "High"
                });
            }
        }

        // --- BULK SAFETY ---
        if (!string.IsNullOrEmpty(analysis.BulkSafetyGrade) && (analysis.BulkSafetyGrade == "D" || analysis.BulkSafetyGrade == "F"))
        {
            issues.Add(new DetailedIssue
            {
                Icon = "🚨",
                Title = $"Bulk Safety Grade: {analysis.BulkSafetyGrade} — Not Safe for Multiple Records",
                WhatThisMeans = $"This code got a '{analysis.BulkSafetyGrade}' for handling multiple records at once. It works with 1 record but will fail with many.",
                WhyThisHappened = analysis.BulkSafetyReason,
                Analogy = "Imagine a cashier who can ring up 1 customer fine, but if 10 customers arrive at once, they try to ring up each one from scratch — scanning every item individually across 10 separate transactions. The line would be out the door!",
                WhyItsBad = "Salesforce processes records in batches of up to 200. Data loads, imports, and even some UI actions trigger multiple records. Your code WILL fail during these operations.",
                Severity = analysis.BulkSafetyGrade == "F" ? "Critical" : "High"
            });
        }

        // --- SLOW EXECUTION ---
        if (analysis.DurationMs > 5000 && issues.All(i => i.Title.Contains("CPU") == false))
        {
            issues.Add(new DetailedIssue
            {
                Icon = "🐌",
                Title = $"Slow Execution: {FormatDuration(analysis.DurationMs)}",
                WhatThisMeans = $"This transaction took {FormatDuration(analysis.DurationMs)}, which users will definitely notice. Anything over 3 seconds feels \"slow\" to users.",
                WhyThisHappened = "Common causes: too many database queries, unoptimized loops, external API callouts, or complex Flow logic.",
                Analogy = "When a webpage takes more than 3 seconds to load, 53% of mobile users leave. Your users are waiting this long every time they do this action.",
                WhyItsBad = "Slow operations frustrate users, reduce adoption, and can cascade into timeout errors during peak usage.",
                Severity = analysis.DurationMs > 10000 ? "High" : "Medium"
            });
        }

        // --- FLOW ERRORS ---
        if (analysis.FlowErrors?.Any() == true)
        {
            var flowErr = analysis.FlowErrors.First();
            issues.Add(new DetailedIssue
            {
                Icon = "🌊",
                Title = $"Flow Error: {flowErr.ElementName}",
                WhatThisMeans = $"A Flow element failed with error: {flowErr.ErrorCode}. The Flow couldn't complete the action it was trying to do.",
                WhyThisHappened = $"Error details: {flowErr.ErrorMessage}",
                Analogy = "Think of a Flow like a conveyor belt in a factory. One station broke down, so everything behind it stopped moving.",
                WhyItsBad = "Flow errors can silently fail or cascade, causing data inconsistencies that are hard to debug later.",
                Severity = "High"
            });
        }

        return issues;
    }

    // ================================================================
    // SECTION 3: RECOMMENDATIONS WITH CODE
    // ================================================================

    private List<DetailedRecommendation> BuildDetailedRecommendations(LogAnalysis analysis)
    {
        var recommendations = new List<DetailedRecommendation>();

        // --- FIX N+1 QUERIES ---
        var duplicateQueries = analysis.DuplicateQueries?.Where(d => d.ExecutionCount > 3).ToList();
        if (duplicateQueries?.Any() == true)
        {
            var worst = duplicateQueries.OrderByDescending(d => d.ExecutionCount).First();
            var queryObj = ExtractObjectFromQuery(worst.ExampleQuery);

            recommendations.Add(new DetailedRecommendation
            {
                Icon = "🎯",
                Title = "Fix the N+1 Query Pattern (CRITICAL — Do This First!)",
                Explanation = $"Your code queries {queryObj} inside a loop, {worst.ExecutionCount} separate times. Move the query BEFORE the loop and query all records at once.",
                CodeBefore = $@"// ❌ BAD: Query inside a loop — runs {worst.ExecutionCount} times!
for (Account acc : Trigger.new) {{
    List<{queryObj}> records = [
        {TruncateText(worst.ExampleQuery, 120)}
    ];
    // Process records...
}}",
                CodeBeforeLabel = $"❌ Current — {worst.ExecutionCount} queries",
                CodeAfter = $@"// ✅ GOOD: Query ONCE before the loop
Set<Id> parentIds = new Set<Id>();
for (Account acc : Trigger.new) {{
    parentIds.add(acc.Id);
}}

// One query gets ALL the data
Map<Id, List<{queryObj}>> recordsByParent = new Map<Id, List<{queryObj}>>();
for ({queryObj} rec : [{TruncateText(worst.ExampleQuery, 80)} WHERE Id IN :parentIds]) {{
    if (!recordsByParent.containsKey(rec.Id)) {{
        recordsByParent.put(rec.Id, new List<{queryObj}>());
    }}
    recordsByParent.get(rec.Id).add(rec);
}}

// Now loop and use the Map — no queries!
for (Account acc : Trigger.new) {{
    List<{queryObj}> records = recordsByParent.get(acc.Id);
    // Process records...
}}",
                CodeAfterLabel = "✅ Fixed — 1 query total",
                ImpactBefore = $"{worst.ExecutionCount} database queries, {worst.ExecutionCount * 50}-{worst.ExecutionCount * 100}ms wasted",
                ImpactAfter = "1 database query, ~50ms total",
                SpeedImprovement = $"{worst.ExecutionCount}x faster",
                EstimatedMinutes = 30
            });
        }

        // --- FIX TRIGGER RECURSION ---
        var reentries = analysis.TriggerReEntries?.Where(t => t.HasReEntry).ToList();
        if (reentries?.Any() == true)
        {
            var re = reentries.First();
            recommendations.Add(new DetailedRecommendation
            {
                Icon = "🔄",
                Title = $"Add Recursion Control to {re.TriggerName}",
                Explanation = $"Your trigger fires {re.TotalFireCount} times because it updates the same record type it triggers on. Add a static variable to prevent re-entry.",
                CodeBefore = $@"// ❌ BAD: No recursion guard — fires {re.TotalFireCount} times!
trigger {re.TriggerName} on {re.ObjectType} (after update) {{
    // This update fires the trigger AGAIN...
    update Trigger.new;
}}",
                CodeBeforeLabel = $"❌ Current — fires {re.TotalFireCount}x",
                CodeAfter = $@"// ✅ GOOD: Static variable prevents recursion
public class {re.TriggerName.Replace("Trigger", "")}TriggerHandler {{
    private static Boolean isRunning = false;
    
    public static void handleAfterUpdate(List<{re.ObjectType}> records) {{
        if (isRunning) return; // Stop recursion!
        isRunning = true;
        
        try {{
            // Your trigger logic here
            update records;
        }} finally {{
            isRunning = false; // Reset for next transaction
        }}
    }}
}}",
                CodeAfterLabel = "✅ Fixed — runs once only",
                ImpactBefore = $"Trigger runs {re.TotalFireCount}x, wastes {re.TotalFireCount}x resources",
                ImpactAfter = "Trigger runs exactly 1 time",
                SpeedImprovement = $"{re.TotalFireCount}x fewer executions",
                EstimatedMinutes = 15
            });
        }

        // --- OPTIMIZE SLOW METHODS ---
        if (analysis.MethodStats?.Any() == true)
        {
            var slowMethod = analysis.MethodStats
                .Where(m => m.Value.MaxDurationMs > 2000)
                .OrderByDescending(m => m.Value.MaxDurationMs)
                .FirstOrDefault();

            if (slowMethod.Key != null)
            {
                var methodName = ExtractClassName(slowMethod.Key);
                recommendations.Add(new DetailedRecommendation
                {
                    Icon = "⚡",
                    Title = $"Speed Up {methodName} ({FormatDuration(slowMethod.Value.MaxDurationMs)})",
                    Explanation = $"This method takes {FormatDuration(slowMethod.Value.MaxDurationMs)} to execute. Look for database queries inside it, complex loops, or unnecessary processing.",
                    CodeBefore = $@"// ❌ Slow method — {FormatDuration(slowMethod.Value.MaxDurationMs)} execution
// Common causes:
// 1. SOQL queries inside loops
// 2. Processing records one at a time
// 3. Redundant calculations
// 4. Large data volumes without LIMIT",
                    CodeBeforeLabel = $"⏱️ Current: {FormatDuration(slowMethod.Value.MaxDurationMs)}",
                    CodeAfter = @"// ✅ Optimization strategies:
// 1. Move queries outside of loops
// 2. Use Maps for O(1) lookups instead of nested loops
// 3. Add LIMIT clauses to queries
// 4. Use Database.queryLocator for large datasets
// 5. Consider @future or Queueable for heavy processing",
                    CodeAfterLabel = "💡 Optimization strategies",
                    ImpactBefore = $"{FormatDuration(slowMethod.Value.MaxDurationMs)} execution time",
                    ImpactAfter = "Target: under 1 second",
                    SpeedImprovement = $"{slowMethod.Value.MaxDurationMs / 1000.0:F0}x improvement possible",
                    EstimatedMinutes = 45
                });
            }
        }

        // --- REDUCE SOQL QUERIES ---
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit != null && lastLimit.SoqlQueries > 50 && duplicateQueries?.Any() != true)
        {
            recommendations.Add(new DetailedRecommendation
            {
                Icon = "💾",
                Title = $"Combine Database Queries ({lastLimit.SoqlQueries} queries → fewer)",
                Explanation = $"Your code made {lastLimit.SoqlQueries} database queries. Many of these might be fetching similar data and can be combined.",
                CodeBefore = $@"// ❌ Multiple separate queries
List<Account> accounts = [SELECT Id FROM Account WHERE Id = :accId1];
List<Account> accounts2 = [SELECT Id FROM Account WHERE Id = :accId2];
List<Contact> contacts = [SELECT Id FROM Contact WHERE AccountId = :accId1];",
                CodeBeforeLabel = $"❌ Current: {lastLimit.SoqlQueries} queries",
                CodeAfter = @"// ✅ Combine into fewer queries using collections
Set<Id> accountIds = new Set<Id>{accId1, accId2};
List<Account> allAccounts = [SELECT Id FROM Account WHERE Id IN :accountIds];
List<Contact> allContacts = [SELECT Id FROM Contact WHERE AccountId IN :accountIds];",
                CodeAfterLabel = "✅ Combined: 2 queries",
                ImpactBefore = $"{lastLimit.SoqlQueries} queries used, {SafePercent(lastLimit.SoqlQueries, lastLimit.SoqlQueriesLimit):F0}% of limit",
                ImpactAfter = "Significantly fewer queries, more headroom",
                SpeedImprovement = "Fewer queries = faster + safer",
                EstimatedMinutes = 30
            });
        }

        // --- ADD SAFETY MEASURES ---
        if (analysis.BulkSafetyGrade == "D" || analysis.BulkSafetyGrade == "F")
        {
            recommendations.Add(new DetailedRecommendation
            {
                Icon = "🛡️",
                Title = "Add Bulkification Safety Measures",
                Explanation = $"Your code scored a '{analysis.BulkSafetyGrade}' for bulk safety. Add defensive checks to prevent failures with multiple records.",
                CodeBefore = @"// ❌ No safety checks — will fail with 200 records
trigger MyTrigger on Account (after update) {
    for (Account acc : Trigger.new) {
        // Dangerous: query + DML inside loop
        Contact c = [SELECT Id FROM Contact WHERE AccountId = :acc.Id LIMIT 1];
        c.LastName = 'Updated';
        update c;
    }
}",
                CodeBeforeLabel = "❌ Not bulk-safe",
                CodeAfter = @"// ✅ Bulk-safe pattern
trigger MyTrigger on Account (after update) {
    // 1. Collect all IDs first
    Set<Id> accountIds = Trigger.newMap.keySet();
    
    // 2. Query ONCE outside the loop
    Map<Id, Contact> contactsByAccount = new Map<Id, Contact>();
    for (Contact c : [SELECT Id, AccountId, LastName FROM Contact 
                      WHERE AccountId IN :accountIds]) {
        contactsByAccount.put(c.AccountId, c);
    }
    
    // 3. Collect changes in a list
    List<Contact> toUpdate = new List<Contact>();
    for (Account acc : Trigger.new) {
        Contact c = contactsByAccount.get(acc.Id);
        if (c != null) {
            c.LastName = 'Updated';
            toUpdate.add(c);
        }
    }
    
    // 4. One DML operation
    if (!toUpdate.isEmpty()) {
        update toUpdate;
    }
}",
                CodeAfterLabel = "✅ Bulk-safe — handles 200+ records",
                ImpactBefore = "Fails with 2+ records in production",
                ImpactAfter = "Handles up to 200 records safely",
                SpeedImprovement = "200x more reliable",
                EstimatedMinutes = 45
            });
        }

        return recommendations;
    }

    // ================================================================
    // SECTION 4: LEARNING ITEMS
    // ================================================================

    private List<LearningItem> BuildLearningItems(LogAnalysis analysis)
    {
        var items = new List<LearningItem>();
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();

        // Governor limits — always teach this
        if (lastLimit != null)
        {
            items.Add(new LearningItem
            {
                Concept = "Governor Limits",
                Explanation = "Salesforce is multi-tenant — your org shares servers with others. Governor limits prevent any one org from hogging resources. Think of it like living in an apartment: you share water/electricity, so there are usage limits.",
                ResourceUrl = "https://developer.salesforce.com/docs/atlas.en-us.salesforce_app_limits_cheatsheet.meta/salesforce_app_limits_cheatsheet/salesforce_app_limits_overview.htm",
                ResourceLabel = "Salesforce Governor Limits Cheat Sheet"
            });
        }

        // N+1 pattern
        if (analysis.DuplicateQueries?.Any(d => d.ExecutionCount > 3) == true)
        {
            items.Add(new LearningItem
            {
                Concept = "N+1 Query Pattern",
                Explanation = "A common anti-pattern where your code makes N database queries (one per record) instead of 1 query for all records. The fix is always the same: collect IDs first, query once, use a Map.",
                ResourceUrl = "https://trailhead.salesforce.com/content/learn/modules/apex_database/apex_database_soql",
                ResourceLabel = "Trailhead: SOQL & SOSL"
            });
        }

        // Bulkification
        if (!string.IsNullOrEmpty(analysis.BulkSafetyGrade) && (analysis.BulkSafetyGrade == "C" || analysis.BulkSafetyGrade == "D" || analysis.BulkSafetyGrade == "F"))
        {
            items.Add(new LearningItem
            {
                Concept = "Bulkification",
                Explanation = "\"Bulkification\" means writing code that handles 1 record OR 200 records equally well. Never put SOQL or DML inside loops. Collect data, process in bulk, save once.",
                ResourceUrl = "https://trailhead.salesforce.com/content/learn/modules/apex_triggers/apex_triggers_bulk",
                ResourceLabel = "Trailhead: Bulk Apex Triggers"
            });
        }

        // Trigger patterns
        if (analysis.TriggerReEntries?.Any(t => t.HasReEntry) == true)
        {
            items.Add(new LearningItem
            {
                Concept = "Trigger Recursion",
                Explanation = "When a trigger updates the same object type it's attached to, it fires again — creating a loop. Always use a static Boolean or Set<Id> to track processed records and prevent re-entry.",
                ResourceUrl = "https://developer.salesforce.com/docs/atlas.en-us.apexcode.meta/apexcode/apex_triggers_order_of_execution.htm",
                ResourceLabel = "Salesforce: Order of Execution"
            });
        }

        // General best practice
        if (items.Count == 0)
        {
            items.Add(new LearningItem
            {
                Concept = "Salesforce Best Practices",
                Explanation = "Your code follows good patterns! Keep using bulk-safe queries, avoid DML/SOQL in loops, and test with 200 records to ensure scalability.",
                ResourceUrl = "https://developer.salesforce.com/docs/atlas.en-us.apexcode.meta/apexcode/apex_dev_guide.htm",
                ResourceLabel = "Apex Developer Guide"
            });
        }

        return items;
    }

    // ================================================================
    // SECTION 5: OVERALL ASSESSMENT
    // ================================================================

    private string BuildOverallAssessment(LogAnalysis analysis)
    {
        if (analysis.TransactionFailed)
            return "Your code crashed. Fix the error first, then address any performance issues.";

        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit == null) return "Transaction completed successfully.";

        var maxPct = MaxLimitPercent(lastLimit);
        var grade = analysis.BulkSafetyGrade;

        if (maxPct >= 95 || grade == "F")
            return "This code barely survived. It's at the breaking point and WILL fail with more data. Fix the critical issues immediately.";
        if (maxPct >= 80 || grade == "D")
            return "This code works but is heading for trouble. Prioritize the high-severity issues before users start seeing errors.";
        if (maxPct >= 50 || grade == "C")
            return "This code is functional but has room for improvement. The recommendations below will make it faster and more reliable.";

        return "Great work! This code is efficient and well-structured. The quick wins below are optional optimizations.";
    }

    private string DeterminePriority(LogAnalysis analysis)
    {
        if (analysis.TransactionFailed) return "Critical";
        var lastLimit = analysis.LimitSnapshots?.LastOrDefault();
        if (lastLimit == null) return "Low";
        var maxPct = MaxLimitPercent(lastLimit);
        if (maxPct >= 95) return "Critical";
        if (maxPct >= 80) return "High";
        if (maxPct >= 50) return "Medium";
        return "Low";
    }

    private string EstimateEffort(LogAnalysis analysis)
    {
        var totalMinutes = 0;
        if (analysis.DuplicateQueries?.Any(d => d.ExecutionCount > 3) == true) totalMinutes += 30;
        if (analysis.TriggerReEntries?.Any(t => t.HasReEntry) == true) totalMinutes += 15;
        if (analysis.Errors?.Any() == true) totalMinutes += 60;
        if (analysis.BulkSafetyGrade is "D" or "F") totalMinutes += 45;

        if (totalMinutes == 0) return "No action needed — your code looks great!";
        if (totalMinutes <= 30) return $"🛠️ Quick fix — about {totalMinutes} minutes of work";
        if (totalMinutes <= 60) return $"🛠️ Medium effort — about {totalMinutes} minutes ({totalMinutes / 60.0:F1} hours)";
        return $"🛠️ Significant refactor — about {totalMinutes} minutes ({totalMinutes / 60.0:F1} hours)";
    }

    private string BuildImpactSummary(LogAnalysis analysis)
    {
        var improvements = new List<string>();
        if (analysis.DuplicateQueries?.Any(d => d.ExecutionCount > 3) == true)
        {
            var worst = analysis.DuplicateQueries.Max(d => d.ExecutionCount);
            improvements.Add($"{worst}x fewer queries");
        }
        if (analysis.TriggerReEntries?.Any(t => t.HasReEntry) == true)
        {
            var worst = analysis.TriggerReEntries.Max(t => t.TotalFireCount);
            improvements.Add($"{worst}x fewer trigger runs");
        }
        if (analysis.DurationMs > 5000)
            improvements.Add("significantly faster execution");

        if (improvements.Count == 0)
            return "Your code is already performing well.";

        return "After fixes: " + string.Join(", ", improvements);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static double SafePercent(int value, int limit)
        => limit > 0 ? (value * 100.0) / limit : 0;

    private static double MaxLimitPercent(GovernorLimitSnapshot limit)
    {
        var soql = SafePercent(limit.SoqlQueries, limit.SoqlQueriesLimit);
        var dml = SafePercent(limit.DmlStatements, limit.DmlStatementsLimit);
        var cpu = SafePercent(limit.CpuTime, limit.CpuTimeLimit);
        return Math.Max(soql, Math.Max(dml, cpu));
    }

    private static string FriendlyEntryPoint(string entryPoint)
    {
        if (string.IsNullOrEmpty(entryPoint)) return "a Salesforce action";
        // Clean up common patterns
        var clean = entryPoint
            .Replace("trigger event", "")
            .Replace("on Account", "on Account records")
            .Replace("on Contact", "on Contact records")
            .Replace("on Case", "on Case records")
            .Replace("on Opportunity", "on Opportunity records");
        return $"\"{clean.Trim()}\"";
    }

    private static string ExtractClassName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return "Unknown";
        var parts = fullName.Split('.');
        return parts.Length > 1 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
    }

    private static string ExtractObjectFromQuery(string query)
    {
        if (string.IsNullOrEmpty(query)) return "SObject";
        // Try to extract FROM <Object> from SOQL
        var fromIdx = query.IndexOf("FROM ", StringComparison.OrdinalIgnoreCase);
        if (fromIdx >= 0)
        {
            var after = query.Substring(fromIdx + 5).TrimStart();
            var endIdx = after.IndexOfAny(new[] { ' ', '\n', '\r', '(' });
            return endIdx > 0 ? after.Substring(0, endIdx) : after;
        }
        return "SObject";
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
    }

    private static string FormatDuration(double ms)
    {
        if (ms < 1) return "<1ms";
        if (ms < 1000) return $"{ms:F0}ms";
        if (ms < 60000) return $"{ms / 1000.0:F1}s";
        return $"{ms / 60000.0:F1}min";
    }
}
