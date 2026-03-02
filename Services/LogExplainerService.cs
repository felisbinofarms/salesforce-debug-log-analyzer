using SalesforceDebugAnalyzer.Models;
using System.Text;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service that translates technical log analysis into plain English explanations
/// with code examples, analogies, and actionable advice
/// </summary>
public class LogExplainerService
{
    /// <summary>
    /// Generate a comprehensive, educational summary from log analysis
    /// </summary>
    public string GenerateDetailedSummary(LogAnalysis analysis)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# 📋 What Happened\n");
        
        // Success/failure with friendly context
        if (analysis.TransactionFailed)
        {
            sb.AppendLine($"❌ **Your code crashed** and took {FormatDuration((long)analysis.DurationMs)}.\n");
            sb.AppendLine("**What this means:** The user saw an error message, and their changes were NOT saved. " +
                "Salesforce automatically rolled back everything to prevent partial data corruption.\n");
        }
        else if (analysis.HandledExceptions.Count > 0)
        {
            sb.AppendLine($"⚠️ **Your code encountered {analysis.HandledExceptions.Count} error{(analysis.HandledExceptions.Count > 1 ? "s" : "")} but recovered** (took {FormatDuration((long)analysis.DurationMs)}).\n");
            sb.AppendLine("**What this means:** Your code used `try/catch` blocks to handle errors gracefully. " +
                "The user's action completed successfully, but some operations may have been skipped.\n");
        }
        else if (analysis.DurationMs > 5000)
        {
            sb.AppendLine($"✅ **Your code worked, but it was slow** - took {FormatDuration((long)analysis.DurationMs)}.\n");
            sb.AppendLine("**What this means:** Everything saved correctly, but the user had to wait. " +
                "If this happens often, users will complain about performance. " +
                "5+ seconds feels sluggish - aim for under 2 seconds.\n");
        }
        else if (analysis.DurationMs > 2000)
        {
            sb.AppendLine($"✅ **Your code worked fine** - took {FormatDuration((long)analysis.DurationMs)}.\n");
            sb.AppendLine("**What this means:** This is normal speed. Users won't complain, but there's room for optimization.\n");
        }
        else
        {
            sb.AppendLine($"✅ **Your code was fast and efficient** - only took {FormatDuration((long)analysis.DurationMs)}!\n");
            sb.AppendLine("**What this means:** Users experienced instant responsiveness. This is ideal performance. Keep it up! 🎉\n");
        }
        
        // Entry point explanation
        if (!string.IsNullOrEmpty(analysis.EntryPoint))
        {
            sb.AppendLine($"## 🎯 What Triggered This\n");
            sb.AppendLine($"**{analysis.EntryPoint}**\n");
            sb.AppendLine(ExplainEntryPoint(analysis.EntryPoint, analysis.OperationType));
            sb.AppendLine();
        }
        
        // What the code did
        sb.AppendLine("## 🔧 What Your Code Did\n");
        
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        var dmlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "DML");
        var methodCount = analysis.MethodStats.Count;
        
        if (methodCount > 0)
        {
            sb.AppendLine($"• **Called {methodCount} different methods** (functions/pieces of code)");
            if (methodCount > 50)
            {
                sb.AppendLine($"  - *That's a lot!* Your code is doing many different things. " +
                    "This is fine, but watch out for 'spaghetti code' that's hard to debug.");
            }
        }
        
        if (soqlCount > 0 || dmlCount > 0)
        {
            sb.AppendLine($"• **Talked to the database {soqlCount + dmlCount} times:**");
            
            if (soqlCount > 0)
            {
                sb.AppendLine($"  - {soqlCount} **read** operation{(soqlCount > 1 ? "s" : "")} (SOQL queries) - asking for data");
                if (analysis.CustomMetadataQueryCount > 0)
                {
                    sb.AppendLine($"    ({analysis.CustomMetadataQueryCount} were free Custom Metadata queries that don't count against limits)");
                }
            }
            if (dmlCount > 0)
            {
                sb.AppendLine($"  - {dmlCount} **write** operation{(dmlCount > 1 ? "s" : "")} (DML statements) - saving/updating/deleting records");
            }
        }
        
        if (analysis.Flows.Count > 0)
        {
            sb.AppendLine($"• **Triggered {analysis.Flows.Count} Flow{(analysis.Flows.Count > 1 ? "s" : "")} or Process Builder{(analysis.Flows.Count > 1 ? "s" : "")}**");
            var faultedFlows = analysis.Flows.Where(f => f.HasFault).ToList();
            if (faultedFlows.Any())
            {
                sb.AppendLine($"  - ⚠️ {faultedFlows.Count} of them had errors!");
            }
        }
        
        sb.AppendLine();
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Generate comprehensive, educational issue explanations with code examples
    /// </summary>
    public List<DetailedIssue> GenerateDetailedIssues(LogAnalysis analysis)
    {
        var issues = new List<DetailedIssue>();
        var lastLimitSnapshot = analysis.LimitSnapshots.LastOrDefault();
        if (lastLimitSnapshot == null) return issues;
        
        // Issue 1: Governor limit dangers
        var soqlPercent = SafePercent(lastLimitSnapshot.SoqlQueries, lastLimitSnapshot.SoqlQueriesLimit);
        var cpuPercent = SafePercent(lastLimitSnapshot.CpuTime, lastLimitSnapshot.CpuTimeLimit);
        var dmlPercent = SafePercent(lastLimitSnapshot.DmlStatements, lastLimitSnapshot.DmlStatementsLimit);
        
        if (soqlPercent >= 90 || cpuPercent >= 90 || dmlPercent >= 90)
        {
            issues.Add(GenerateCriticalLimitIssue(analysis, lastLimitSnapshot, soqlPercent, cpuPercent, dmlPercent));
        }
        else if (soqlPercent >= 70 || cpuPercent >= 70 || dmlPercent >= 70)
        {
            // Warning level - not critical yet but getting close
            issues.Add(GenerateCriticalLimitIssue(analysis, lastLimitSnapshot, soqlPercent, cpuPercent, dmlPercent));
        }
        
        // Issue 2: N+1 query pattern
        var soqlCount = analysis.DatabaseOperations.Count(d => d.OperationType == "SOQL");
        if (soqlCount > 10)
        {
            var repeatedQueries = analysis.DatabaseOperations
                .Where(d => d.OperationType == "SOQL")
                .GroupBy(d => SimplifyQuery(d.Query))
                .Where(g => g.Count() >= 3)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            
            if (repeatedQueries != null && repeatedQueries.Count() >= 5)
            {
                issues.Add(GenerateNPlusOneIssue(repeatedQueries, analysis));
            }
        }
        
        // Issue 3: Stack overflow risk
        if (analysis.StackAnalysis.RiskLevel == StackRiskLevel.Critical || 
            analysis.StackAnalysis.RiskLevel == StackRiskLevel.Warning)
        {
            issues.Add(GenerateStackOverflowIssue(analysis));
        }
        
        // Issue 4: Slow queries
        var slowQueries = analysis.DatabaseOperations
            .Where(d => d.DurationMs > 1000)
            .OrderByDescending(d => d.DurationMs)
            .Take(3)
            .ToList();
        
        if (slowQueries.Any())
        {
            issues.Add(GenerateSlowQueryIssue(slowQueries));
        }
        
        // Issue 5: Trigger re-entry
        var reEntries = analysis.TriggerReEntries.Where(t => t.HasReEntry).ToList();
        if (reEntries.Any())
        {
            issues.Add(GenerateTriggerReEntryIssue(reEntries.First(), analysis));
        }
        
        return issues;
    }
    
    private DetailedIssue GenerateCriticalLimitIssue(LogAnalysis analysis, GovernorLimitSnapshot limits, double soqlPercent, double cpuPercent, double dmlPercent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 🚨 CRITICAL: You're At The Absolute Maximum!\n");
        
        var criticalLimits = new List<string>();
        if (soqlPercent >= 90) criticalLimits.Add($"**Database queries:** {limits.SoqlQueries}/{limits.SoqlQueriesLimit} ({soqlPercent:F0}%)");
        if (cpuPercent >= 90) criticalLimits.Add($"**Processing time:** {limits.CpuTime}ms/{limits.CpuTimeLimit}ms ({cpuPercent:F0}%)");
        if (dmlPercent >= 90) criticalLimits.Add($"**Database writes:** {limits.DmlStatements}/{limits.DmlStatementsLimit} ({dmlPercent:F0}%)");
        
        sb.AppendLine("**What's wrong:**");
        foreach (var limit in criticalLimits)
        {
            sb.AppendLine($"• {limit}");
        }
        sb.AppendLine();
        
        sb.AppendLine("**What this means in plain English:**");
        sb.AppendLine("Imagine Salesforce giving you 100 tokens to query the database. Your code used 95+ tokens. " +
            "If you need even ONE more query, Salesforce will throw an error and roll back everything. " +
            "Your users will see a scary red error message saying *\"Too many SOQL queries: 101\"*.\n");
        
        sb.AppendLine("**Why Salesforce has these limits:**");
        sb.AppendLine("Salesforce is a shared platform (like an apartment building). If one tenant blasts music at full volume, " +
            "everyone suffers. Governor limits = noise complaints that force you to turn it down. These limits protect ALL customers.\n");
        
        sb.AppendLine("**What happens if you hit the limit:**");
        sb.AppendLine("1. Salesforce throws an exception: `System.LimitException: Too many SOQL queries: 101`");
        sb.AppendLine("2. Everything your code did gets UNDONE (rolled back)");
        sb.AppendLine("3. The user sees an error and their changes aren't saved");
        sb.AppendLine("4. You get angry emails/tickets from users 😱\n");
        
        return new DetailedIssue
        {
            Severity = IssueSeverity.Critical.ToString(),
            Title = "🚨 CRITICAL: Governor Limits at Maximum",
            Description = sb.ToString(),
            Impact = "Code will fail if processing one more record or making one more query",
            Effort = "High (2-4 hours to refactor)",
            Priority = 1
        };
    }
    
    private DetailedIssue GenerateNPlusOneIssue(IGrouping<string, DatabaseOperation> repeatedQueries, LogAnalysis analysis)
    {
        var sb = new StringBuilder();
        var queryCount = repeatedQueries.Count();
        var exampleQuery = repeatedQueries.First().Query;
        
        sb.AppendLine("### 🔁 You're Asking The Same Question Over and Over (N+1 Pattern)\n");
        
        sb.AppendLine("**What's happening:**");
        sb.AppendLine($"Your code is running this query **{queryCount} times:**");
        sb.AppendLine($"```sql\n{TruncateQuery(exampleQuery)}\n```\n");
        
        sb.AppendLine("**The Analogy:**");
        sb.AppendLine("This is like calling a friend 100 times asking \"What's the weather in New York?\" " +
            "Instead of asking once and writing it down. Each call takes time - even if they answer instantly, " +
            "you waste time dialing, waiting for them to pick up, etc.\n");
        
        sb.AppendLine("Or imagine going to the grocery store 100 times to buy one item each time, " +
            "instead of making ONE trip with a shopping list. That's what your code is doing!\n");
        
        sb.AppendLine("**Why it's slow:**");
        sb.AppendLine($"• Each database query takes 50-100ms on average");
        sb.AppendLine($"• {queryCount} queries × 75ms = **{queryCount * 75}ms wasted** just waiting");
        sb.AppendLine("• Plus you're burning through your 100-query limit!\n");
        
        sb.AppendLine("**How to fix it (Bulkification):**");
        sb.AppendLine("Instead of querying inside a loop, collect ALL the IDs first, then query ONCE:\n");
        
        sb.AppendLine("**❌ BAD (Current Code):**");
        sb.AppendLine("```apex");
        sb.AppendLine("for (Account acc : Trigger.new) {");
        sb.AppendLine("    // 🚫 Query INSIDE loop = 100 database calls!");
        sb.AppendLine("    List<Contact> contacts = [SELECT Id, Name FROM Contact WHERE AccountId = :acc.Id];");
        sb.AppendLine("    // Do something with contacts...");
        sb.AppendLine("}");
        sb.AppendLine("```\n");
        
        sb.AppendLine("**✅ GOOD (Fixed Code):**");
        sb.AppendLine("```apex");
        sb.AppendLine("// Step 1: Collect all Account IDs first");
        sb.AppendLine("Set<Id> accountIds = new Set<Id>();");
        sb.AppendLine("for (Account acc : Trigger.new) {");
        sb.AppendLine("    accountIds.add(acc.Id);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Step 2: Query ONCE for ALL Contacts (1 database call)");
        sb.AppendLine("Map<Id, List<Contact>> contactsByAccount = new Map<Id, List<Contact>>();");
        sb.AppendLine("for (Contact con : [SELECT Id, Name, AccountId FROM Contact WHERE AccountId IN :accountIds]) {");
        sb.AppendLine("    if (!contactsByAccount.containsKey(con.AccountId)) {");
        sb.AppendLine("        contactsByAccount.put(con.AccountId, new List<Contact>());");
        sb.AppendLine("    }");
        sb.AppendLine("    contactsByAccount.get(con.AccountId).add(con);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Step 3: Now loop and use the Map (fast lookup in memory)");
        sb.AppendLine("for (Account acc : Trigger.new) {");
        sb.AppendLine("    List<Contact> contacts = contactsByAccount.get(acc.Id);");
        sb.AppendLine("    if (contacts != null) {");
        sb.AppendLine("        // Do something with contacts...");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("```\n");
        
        sb.AppendLine("**Impact of this change:**");
        sb.AppendLine($"• **Before:** {queryCount} queries, {FormatDuration(queryCount * 75)} wasted");
        sb.AppendLine($"• **After:** 1 query, ~75ms total");
        sb.AppendLine($"• **Speedup:** {queryCount}x faster! ⚡");
        sb.AppendLine($"• **Governor limits:** Uses only 1 out of 100 queries instead of {queryCount}\n");
        
        return new DetailedIssue
        {
            Severity = IssueSeverity.High.ToString(),
            Title = $"🔁 N+1 Query Pattern ({queryCount} duplicate queries)",
            Description = sb.ToString(),
            Impact = $"{queryCount}x slower than necessary, wastes {queryCount - 1} of your 100-query limit",
            Effort = "Medium (1-2 hours to refactor - use code example above)",
            Priority = 2
        };
    }
    
    private DetailedIssue GenerateStackOverflowIssue(LogAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 🚨 Stack Overflow Risk Detected\n");
        
        sb.AppendLine("**What's happening:**");
        sb.AppendLine($"Your code is calling methods nested {analysis.StackAnalysis.MaxDepth} levels deep. " +
            $"Salesforce's limit is 1,000 stack frames - you're at {SafePercent(analysis.StackAnalysis.MaxDepth, 1000):F0}%.\n");
        
        sb.AppendLine("**The Analogy:**");
        sb.AppendLine("Imagine a tower of blocks. Each time a method calls another method, you add a block. " +
            "If the tower gets too tall (1,000 blocks), it falls over (stack overflow). " +
            "Your tower is currently " + analysis.StackAnalysis.MaxDepth + " blocks tall.\n");
        
        sb.AppendLine("**What causes this:**");
        sb.AppendLine($"The main culprit is: `{analysis.StackAnalysis.MaxDepthMethod}`\n");
        
        var topLoopPattern = analysis.StackAnalysis.LoopPatterns
            .OrderByDescending(p => p.TotalFrames)
            .FirstOrDefault();
        
        if (topLoopPattern != null)
        {
            sb.AppendLine($"This method is being called **{topLoopPattern.CallCount} times inside a loop**, " +
                $"creating {topLoopPattern.TotalFrames} stack frames.\n");
        }
        
        sb.AppendLine("**How to fix it:**");
        sb.AppendLine("1. **Cache before the loop** - If you're calling a method 100+ times, call it ONCE and store the result");
        sb.AppendLine("2. **Flatten method chains** - If method A → B → C → D, consider combining them");
        sb.AppendLine("3. **Process in batches** - Instead of 281 trigger configs, process 50 at a time\n");
        
        sb.AppendLine("**Example fix:**");
        sb.AppendLine("```apex");
        sb.AppendLine("// ❌ BAD: Calling method in loop");
        sb.AppendLine("for (Trigger_Detail__c td : triggerDetails) {");
        sb.AppendLine("    RecordType rt = Util.getRecordType(td.Object__c, td.Record_Type_Name__c); // Called 281x!");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// ✅ GOOD: Cache ALL record types first");
        sb.AppendLine("Map<String, RecordType> rtMap = Util.getAllRecordTypes(objectNames); // Called 1x");
        sb.AppendLine("for (Trigger_Detail__c td : triggerDetails) {");
        sb.AppendLine("    RecordType rt = rtMap.get(td.Object__c + '.' + td.Record_Type_Name__c); // Fast Map lookup");
        sb.AppendLine("}");
        sb.AppendLine("```\n");
        
        return new DetailedIssue
        {
            Severity = IssueSeverity.Critical.ToString(),
            Title = "🚨 Stack Overflow Risk",
            Description = sb.ToString(),
            Impact = "Code may crash with 'Maximum stack depth exceeded' error",
            Effort = "Medium (2-3 hours to refactor and cache method results)",
            Priority = 1
        };
    }
    
    private DetailedIssue GenerateSlowQueryIssue(List<DatabaseOperation> slowQueries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 🐌 Slow Database Queries Detected\n");
        
        sb.AppendLine($"**What's happening:**");
        sb.AppendLine($"You have {slowQueries.Count} database quer{(slowQueries.Count > 1 ? "ies" : "y")} that took over 1 second each:\n");
        
        foreach (var query in slowQueries)
        {
            sb.AppendLine($"• {FormatDuration((long)query.DurationMs)} - `{TruncateQuery(query.Query)}`");
        }
        sb.AppendLine();
        
        sb.AppendLine("**Why this matters:**");
        sb.AppendLine("A good SOQL query should take 50-200ms. Anything over 1 second means:");
        sb.AppendLine("1. You're searching through too much data (millions of records)");
        sb.AppendLine("2. The database doesn't have proper indexes (like a book without a table of contents)");
        sb.AppendLine("3. Your WHERE clause is too complex or not using indexed fields\n");
        
        sb.AppendLine("**How to fix it:**");
        sb.AppendLine("1. **Add filters (WHERE clauses)** to narrow down the search");
        sb.AppendLine("2. **Use indexed fields** - Id, Name, RecordTypeId, CreatedDate, etc.");
        sb.AppendLine("3. **Add custom indexes** - Ask a Salesforce admin to index frequently-queried fields");
        sb.AppendLine("4. **Reduce SELECT fields** - Only get the fields you actually need\n");
        
        sb.AppendLine("**Example improvement:**");
        sb.AppendLine("```apex");
        sb.AppendLine("// ❌ SLOW: No filters, searching millions of records");
        sb.AppendLine("List<Case> cases = [SELECT Id, Subject, Description... FROM Case];");
        sb.AppendLine();
        sb.AppendLine("// ✅ FAST: Add WHERE clause to filter");
        sb.AppendLine("List<Case> cases = [SELECT Id, Subject FROM Case ");
        sb.AppendLine("                     WHERE CreatedDate = LAST_N_DAYS:30 ");
        sb.AppendLine("                     AND Status != 'Closed' LIMIT 1000];");
        sb.AppendLine("```\n");
        
        return new DetailedIssue
        {
            Severity = IssueSeverity.Medium.ToString(),
            Title = $"🐌 {slowQueries.Count} Slow Database Quer{(slowQueries.Count > 1 ? "ies" : "y")}",
            Description = sb.ToString(),
            Impact = $"Users wait {FormatDuration((long)slowQueries.Sum(q => q.DurationMs))} unnecessarily",
            Effort = "Low to Medium (add WHERE clauses or request custom indexes)",
            Priority = 3
        };
    }
    
    private DetailedIssue GenerateTriggerReEntryIssue(TriggerReEntry reEntry, LogAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 🔄 Trigger Fired Multiple Times (Recursion)\n");
        
        sb.AppendLine("**What's happening:**");
        sb.AppendLine($"Your trigger `{reEntry.TriggerName}` on {reEntry.ObjectType} fired **{reEntry.TotalFireCount} times** " +
            $"when it should have only fired once.\n");
        
        sb.AppendLine("**Why this is a problem:**");
        sb.AppendLine("This is called **trigger recursion** - your trigger is triggering itself. Here's the cycle:");
        sb.AppendLine("1. User updates a Case");
        sb.AppendLine($"2. {reEntry.TriggerName} fires and updates the Case");
        sb.AppendLine("3. Updating the Case triggers the trigger AGAIN");
        sb.AppendLine("4. This repeats until you hit governor limits or max depth\n");
        
        sb.AppendLine("**The Analogy:**");
        sb.AppendLine("Imagine a microphone next to a speaker. The mic picks up sound → sends it to the speaker → " +
            "speaker plays it → mic picks it up again → FEEDBACK LOOP! That's trigger recursion.\n");
        
        sb.AppendLine("**How to fix it (Add Recursion Control):**");
        sb.AppendLine("```apex");
        sb.AppendLine("// Step 1: Create a static variable to track if trigger already ran");
        sb.AppendLine("public class TriggerControl {");
        sb.AppendLine("    private static Set<String> executedTriggers = new Set<String>();");
        sb.AppendLine("    ");
        sb.AppendLine("    public static Boolean isFirstRun(String triggerName) {");
        sb.AppendLine("        if (executedTriggers.contains(triggerName)) {");
        sb.AppendLine("            return false; // Already ran");
        sb.AppendLine("        }");
        sb.AppendLine("        executedTriggers.add(triggerName);");
        sb.AppendLine("        return true; // First time running");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"// Step 2: Use it in your trigger");
        sb.AppendLine($"trigger {reEntry.TriggerName} on {reEntry.ObjectType} (after update) {{");
        sb.AppendLine($"    if (!TriggerControl.isFirstRun('{reEntry.TriggerName}')) {{");
        sb.AppendLine("        return; // Exit early to prevent recursion");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    // Your trigger logic here...");
        sb.AppendLine("}");
        sb.AppendLine("```\n");
        
        return new DetailedIssue
        {
            Severity = IssueSeverity.High.ToString(),
            Title = $"🔄 Trigger Recursion ({reEntry.TriggerName} fired {reEntry.TotalFireCount}x)",
            Description = sb.ToString(),
            Impact = "Wastes governor limits, risk of infinite loops and stack overflow",
            Effort = "Low (30 minutes to add recursion control)",
            Priority = 2
        };
    }
    
    private string ExplainEntryPoint(string entryPoint, string operationType)
    {
        return operationType.ToLower() switch
        {
            "apex trigger" => "**What this means:** A user created/updated/deleted a record, which automatically ran this trigger code. " +
                "Triggers are like motion sensors - they detect changes and react automatically.",
            
            "flow" => "**What this means:** Salesforce ran an automated Flow or Process Builder. This could have been triggered by a record change, " +
                "a scheduled time, or a user clicking a button. Flows are like recipes - step-by-step instructions for Salesforce to follow.",
            
            "lightning" => "**What this means:** A user interacted with a Lightning Component on a Salesforce page. They might have clicked a button, " +
                "loaded a page, or filled out a form. This code runs on the server to fetch or save data.",
            
            "async apex" => "**What this means:** This is asynchronous (background) code that runs separately from the user's action. " +
                "Think of it like sending an email in the background while you continue working. The user didn't wait for this - it runs later.",
            
            "batch apex" => "**What this means:** This is a scheduled job that processes many records in chunks (batches). " +
                "Like processing 1 million records 200 at a time to avoid hitting limits. This runs in the background.",
            
            _ => "**What this means:** This code ran in response to some action or event in Salesforce."
        };
    }
    
    private string FormatDuration(long ms)
    {
        if (ms < 1000)
            return $"{ms}ms";
        else if (ms < 60 * 1000)
            return $"{ms / 1000.0:F1} seconds";
        else
            return $"{ms / 60000:F1} minutes";
    }
    
    private string TruncateQuery(string query)
    {
        if (query.Length <= 100)
            return query;
        return query.Substring(0, 97) + "...";
    }
    
    private string SimplifyQuery(string query)
    {
        // Remove WHERE clause values to group similar queries
        var simplified = System.Text.RegularExpressions.Regex.Replace(query, @"=\s*'[^']*'", "= ?");
        simplified = System.Text.RegularExpressions.Regex.Replace(simplified, @"=\s*\d+", "= ?");
        return simplified;
    }
    
    private double SafePercent(int value, int max)
    {
        return max == 0 ? 0 : (value * 100.0 / max);
    }

    /// <summary>
    /// Convenience wrapper: generates a full LogExplanation from a LogAnalysis.
    /// </summary>
    public LogExplanation Explain(LogAnalysis analysis)
    {
        return new LogExplanation
        {
            WhatHappened = GenerateDetailedSummary(analysis),
            Issues = GenerateDetailedIssues(analysis),
            WhatYourCodeDid = new List<string>(),
            Recommendations = new List<DetailedRecommendation>(),
            WhatYouLearned = new List<LearningItem>()
        };
    }
}
