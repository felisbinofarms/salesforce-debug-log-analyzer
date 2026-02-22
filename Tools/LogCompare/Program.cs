using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;

namespace LogCompare;

/// <summary>
/// Black Widow — Scripted vs AI Comparison Tool
///
/// Usage:
///   dotnet run -- "path\to\log.log"
///   dotnet run -- "path\to\logs-folder\"
///   dotnet run -- "file1.log" "file2.log" "path\to\folder\"
///
/// For each log it produces two files in ./output/:
///   [name]-scripted.txt  — the full output from LogExplainerService (what the app says today)
///   [name]-ai-prompt.txt — a ready-to-paste prompt for any AI chat (Claude, ChatGPT, Copilot)
///
/// Workflow:
///   1. Run the tool on a real log
///   2. Paste the -ai-prompt.txt content into your AI chat of choice
///   3. Compare the AI response against -scripted.txt
///   4. Note gaps, stale analogies, missed issues
///   5. Update LogExplainerService.cs accordingly
///   6. Re-run and repeat until outputs converge
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Black Widow — Log Compare Tool");
            Console.WriteLine("================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- \"path\\to\\log.log\"");
            Console.WriteLine("  dotnet run -- \"path\\to\\logs-folder\\\"");
            Console.WriteLine();
            Console.WriteLine("Outputs two files per log into ./output/:");
            Console.WriteLine("  [name]-scripted.txt   → what the app currently says");
            Console.WriteLine("  [name]-ai-prompt.txt  → paste this into any AI chat for comparison");
            return 1;
        }

        var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        var logFiles = new List<string>();

        foreach (var arg in args)
        {
            var path = arg.Trim('"');
            if (File.Exists(path))
            {
                logFiles.Add(path);
            }
            else if (Directory.Exists(path))
            {
                logFiles.AddRange(Directory.GetFiles(path, "*.log", SearchOption.TopDirectoryOnly));
                logFiles.AddRange(Directory.GetFiles(path, "*.txt", SearchOption.TopDirectoryOnly));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠ Not found, skipping: {path}");
                Console.ResetColor();
            }
        }

        if (logFiles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No .log or .txt files found in the specified path(s).");
            Console.ResetColor();
            return 1;
        }

        var parser = new LogParserService();
        var explainer = new LogExplainerService();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n🕷️  Black Widow Log Compare Tool");
        Console.WriteLine($"   Processing {logFiles.Count} log file(s)...\n");
        Console.ResetColor();

        int processed = 0, failed = 0;

        foreach (var logFile in logFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(logFile);
            Console.Write($"  → {fileName}... ");

            try
            {
                var content = File.ReadAllText(logFile);
                var analysis = parser.ParseLog(content, fileName);
                var explanation = explainer.Explain(analysis);

                var scriptedPath = Path.Combine(outputDir, $"{fileName}-scripted.txt");
                var promptPath   = Path.Combine(outputDir, $"{fileName}-ai-prompt.txt");

                File.WriteAllText(scriptedPath, FormatScriptedOutput(analysis, explanation, logFile));
                File.WriteAllText(promptPath,   BuildAiPrompt(analysis, explanation, logFile));

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();
                processed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED — {ex.Message}");
                Console.ResetColor();
                failed++;
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Done — {processed} processed, {failed} failed");
        Console.WriteLine($"📂 Output folder: {outputDir}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Open the -ai-prompt.txt file");
        Console.WriteLine("  2. Paste its contents into Claude / ChatGPT / Copilot Chat");
        Console.WriteLine("  3. Compare the AI response to -scripted.txt");
        Console.WriteLine("  4. Note gaps and improvements");
        Console.WriteLine("  5. Update Services/LogExplainerService.cs");
        Console.WriteLine();

        return failed > 0 ? 1 : 0;
    }

    // =====================================================================
    // SCRIPTED OUTPUT FORMATTER
    // Renders exactly what the app displays, as readable plain text.
    // =====================================================================

    static string FormatScriptedOutput(LogAnalysis analysis, LogExplanation explanation, string logFile)
    {
        var sb = new System.Text.StringBuilder();
        var divider = new string('═', 70);
        var minorDivider = new string('─', 70);

        sb.AppendLine(divider);
        sb.AppendLine("BLACK WIDOW — SCRIPTED ANALYSIS");
        sb.AppendLine(divider);
        sb.AppendLine($"Log File : {Path.GetFileName(logFile)}");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Duration : {analysis.WallClockMs:F0}ms ({analysis.WallClockMs / 1000.0:F1}s)");
        sb.AppendLine($"User     : {(string.IsNullOrEmpty(analysis.LogUser) ? "(unknown)" : analysis.LogUser)}");
        sb.AppendLine($"Entry    : {(string.IsNullOrEmpty(analysis.EntryPoint) ? "(unknown)" : analysis.EntryPoint)}");
        sb.AppendLine($"Result   : {(analysis.TransactionFailed ? "❌ FAILED" : "✅ SUCCESS")}");

        // Governor limits summary
        var limits = analysis.LimitSnapshots?.LastOrDefault();
        if (limits != null)
        {
            sb.AppendLine();
            sb.AppendLine("GOVERNOR LIMITS");
            sb.AppendLine(minorDivider);
            AppendLimit(sb, "SOQL Queries",   limits.SoqlQueries,    limits.SoqlQueriesLimit);
            AppendLimit(sb, "DML Statements", limits.DmlStatements,  limits.DmlStatementsLimit);
            AppendLimit(sb, "CPU Time (ms)",  limits.CpuTime,        limits.CpuTimeLimit);
            AppendLimit(sb, "Heap Size (b)",  limits.HeapSize,       limits.HeapSizeLimit);
        }

        sb.AppendLine();
        sb.AppendLine(divider);
        sb.AppendLine("📋  WHAT HAPPENED");
        sb.AppendLine(divider);
        sb.AppendLine(explanation.WhatHappened);

        if (explanation.WhatYourCodeDid?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("What your code did:");
            foreach (var bullet in explanation.WhatYourCodeDid)
                sb.AppendLine($"  \u2022 {StripMarkdown(bullet).TrimStart('\u2022', ' ')}");
        }

        sb.AppendLine();
        sb.AppendLine($"{explanation.VerdictIcon}  PERFORMANCE: {explanation.PerformanceVerdict}");

        // Issues
        sb.AppendLine();
        sb.AppendLine(divider);
        sb.AppendLine($"🔍  ISSUES FOUND ({explanation.Issues?.Count ?? 0})");
        sb.AppendLine(divider);

        if (explanation.Issues?.Count > 0)
        {
            foreach (var issue in explanation.Issues)
            {
                sb.AppendLine();
                sb.AppendLine($"{issue.Icon}  [{issue.Severity.ToUpper()}]  {issue.Title}");
                sb.AppendLine(minorDivider);
                sb.AppendLine($"What this means:");
                sb.AppendLine($"  {issue.WhatThisMeans}");
                if (!string.IsNullOrWhiteSpace(issue.WhyThisHappened))
                {
                    sb.AppendLine($"Why this happened:");
                    sb.AppendLine($"  {issue.WhyThisHappened}");
                }
                if (!string.IsNullOrWhiteSpace(issue.Analogy))
                {
                    sb.AppendLine($"💬 Analogy:");
                    sb.AppendLine($"  {issue.Analogy}");
                }
                if (!string.IsNullOrWhiteSpace(issue.WhyItsBad))
                {
                    sb.AppendLine($"⚠️  Impact:");
                    sb.AppendLine($"  {issue.WhyItsBad}");
                }
            }
        }
        else
        {
            sb.AppendLine("  ✅ No issues detected.");
        }

        // Recommendations
        sb.AppendLine();
        sb.AppendLine(divider);
        sb.AppendLine($"💡  RECOMMENDATIONS ({explanation.Recommendations?.Count ?? 0})");
        sb.AppendLine(divider);

        if (explanation.Recommendations?.Count > 0)
        {
            foreach (var rec in explanation.Recommendations)
            {
                sb.AppendLine();
                sb.AppendLine($"{rec.Icon}  {rec.Title}  (~{rec.EstimatedMinutes} min)");
                sb.AppendLine(minorDivider);
                sb.AppendLine(rec.Explanation);

                if (rec.HasCodeExample)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  {rec.CodeBeforeLabel}");
                    foreach (var line in (rec.CodeBefore ?? "").Split('\n'))
                        sb.AppendLine($"  | {line}");

                    sb.AppendLine();
                    sb.AppendLine($"  {rec.CodeAfterLabel}");
                    foreach (var line in (rec.CodeAfter ?? "").Split('\n'))
                        sb.AppendLine($"  | {line}");
                }

                if (!string.IsNullOrWhiteSpace(rec.SpeedImprovement))
                    sb.AppendLine($"  ⚡ {rec.ImpactBefore} → {rec.ImpactAfter}  {rec.SpeedImprovement}");
            }
        }
        else
        {
            sb.AppendLine("  No recommendations.");
        }

        // Learning
        if (explanation.WhatYouLearned?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(divider);
            sb.AppendLine("🎓  WHAT YOU LEARNED");
            sb.AppendLine(divider);
            foreach (var item in explanation.WhatYouLearned)
            {
                sb.AppendLine();
                sb.AppendLine($"  📚 {item.Concept}");
                sb.AppendLine($"     {item.Explanation}");
                if (!string.IsNullOrWhiteSpace(item.ResourceUrl))
                    sb.AppendLine($"     → {item.ResourceLabel}: {item.ResourceUrl}");
            }
        }

        // Overall assessment
        sb.AppendLine();
        sb.AppendLine(divider);
        sb.AppendLine("📊  OVERALL ASSESSMENT");
        sb.AppendLine(divider);
        sb.AppendLine(explanation.OverallAssessment);
        sb.AppendLine();
        sb.AppendLine($"Priority : {explanation.Priority}");
        sb.AppendLine($"Effort   : {explanation.Effort}");
        sb.AppendLine($"Impact   : {explanation.ImpactSummary}");

        return sb.ToString();
    }

    static void AppendLimit(System.Text.StringBuilder sb, string label, int current, int max)
    {
        if (max <= 0) return;
        double pct = current * 100.0 / max;
        var flag = pct >= 95 ? "🔴" : pct >= 80 ? "🟡" : "🟢";
        sb.AppendLine($"  {flag}  {label,-20} {current,6} / {max,6}  ({pct:F0}%)");
    }

    // =====================================================================
    // AI PROMPT BUILDER
    // Produces a structured, paste-ready prompt for any AI chat.
    // The goal: get a response we can compare field-by-field with the scripted output.
    // =====================================================================

    static string BuildAiPrompt(LogAnalysis analysis, LogExplanation scriptedExplanation, string logFile)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Black Widow — Scripted vs AI Comparison");
        sb.AppendLine();
        sb.AppendLine("## Context: What We Need From You");
        sb.AppendLine();
        sb.AppendLine("We are building **Black Widow**, a Salesforce debug log analyzer that explains");
        sb.AppendLine("logs in plain English for non-experts (junior admins, business analysts, new devs).");
        sb.AppendLine();
        sb.AppendLine("Our app currently uses **hardcoded template scripts** to generate explanations.");
        sb.AppendLine("The problem: the same analogies and phrases appear every time, which gets stale.");
        sb.AppendLine();
        sb.AppendLine("We've pasted below:");
        sb.AppendLine("  1. A **structured summary** of what we parsed from a real Salesforce debug log");
        sb.AppendLine("  2. The **scripted explanation** our app currently produces for this log");
        sb.AppendLine();
        sb.AppendLine("We need you to:");
        sb.AppendLine("  A. Give us YOUR plain-English explanation of this log (same audience: non-expert)");
        sb.AppendLine("  B. Tell us what our scripts got RIGHT");
        sb.AppendLine("  C. Tell us what our scripts MISSED or got WRONG");
        sb.AppendLine("  D. Point out any analogies that are weak, stale, or confusing");
        sb.AppendLine("  E. Suggest better phrasings or analogies for specific issues");
        sb.AppendLine("  F. Flag any problems in the log that our scripts completely ignored");
        sb.AppendLine();
        sb.AppendLine("Be direct and specific. We're going to use your feedback to update our code.");
        sb.AppendLine();

        // --- PARSED LOG SUMMARY ---
        sb.AppendLine("---");
        sb.AppendLine("## 1. Parsed Log Data (Structured Summary)");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine($"Log file  : {Path.GetFileName(logFile)}");
        sb.AppendLine($"Duration  : {analysis.WallClockMs:F0}ms ({analysis.WallClockMs / 1000.0:F1}s)");
        sb.AppendLine($"User      : {(string.IsNullOrEmpty(analysis.LogUser) ? "(unknown)" : analysis.LogUser)}");
        sb.AppendLine($"Entry     : {(string.IsNullOrEmpty(analysis.EntryPoint) ? "(unknown)" : analysis.EntryPoint)}");
        sb.AppendLine($"Result    : {(analysis.TransactionFailed ? "FAILED" : "SUCCESS")}");
        sb.AppendLine($"Test run  : {analysis.IsTestExecution}");
        sb.AppendLine($"Async     : {analysis.IsAsyncExecution}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Governor limits
        var limits = analysis.LimitSnapshots?.LastOrDefault();
        if (limits != null)
        {
            sb.AppendLine("**Governor Limits (final snapshot):**");
            sb.AppendLine("```");
            sb.AppendLine($"SOQL Queries   : {limits.SoqlQueries} / {limits.SoqlQueriesLimit}  ({SafePct(limits.SoqlQueries, limits.SoqlQueriesLimit):F0}%)");
            sb.AppendLine($"DML Statements : {limits.DmlStatements} / {limits.DmlStatementsLimit}  ({SafePct(limits.DmlStatements, limits.DmlStatementsLimit):F0}%)");
            sb.AppendLine($"CPU Time       : {limits.CpuTime}ms / {limits.CpuTimeLimit}ms  ({SafePct(limits.CpuTime, limits.CpuTimeLimit):F0}%)");
            sb.AppendLine($"Heap Size      : {limits.HeapSize} / {limits.HeapSizeLimit} bytes  ({SafePct(limits.HeapSize, limits.HeapSizeLimit):F0}%)");
            sb.AppendLine($"Query Rows     : {limits.QueryRows} / {limits.QueryRowsLimit}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Errors
        if (analysis.Errors?.Count > 0)
        {
            sb.AppendLine("**Errors / Exceptions:**");
            sb.AppendLine("```");
            foreach (var err in analysis.Errors.Take(5))
                sb.AppendLine($"  [{err.Severity}] {err.Name}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Duplicate queries (N+1)
        var dupQueries = analysis.DuplicateQueries?.Where(d => d.ExecutionCount > 2).OrderByDescending(d => d.ExecutionCount).Take(5).ToList();
        if (dupQueries?.Count > 0)
        {
            sb.AppendLine("**Repeated SOQL Queries (N+1 pattern):**");
            sb.AppendLine("```");
            foreach (var dq in dupQueries)
                sb.AppendLine($"  {dq.ExecutionCount}x — {TruncateText(dq.NormalizedQuery, 100)}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Top 5 queries by duration
        var topQueries = analysis.DatabaseOperations?
            .Where(d => d.OperationType == "SOQL" && d.DurationMs > 0)
            .OrderByDescending(d => d.DurationMs)
            .Take(5)
            .ToList();
        if (topQueries?.Count > 0)
        {
            sb.AppendLine("**Slowest SOQL Queries:**");
            sb.AppendLine("```");
            foreach (var q in topQueries)
                sb.AppendLine($"  {q.DurationMs}ms, {q.RowsAffected} rows — {TruncateText(q.Query, 120)}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Top DML
        var topDml = analysis.DatabaseOperations?
            .Where(d => d.OperationType == "DML" && d.DurationMs > 0)
            .OrderByDescending(d => d.DurationMs)
            .Take(5)
            .ToList();
        if (topDml?.Count > 0)
        {
            sb.AppendLine("**DML Operations (slowest):**");
            sb.AppendLine("```");
            foreach (var d in topDml)
                sb.AppendLine($"  {d.DurationMs}ms — {d.DmlOperation} {d.ObjectType} ({d.RowsAffected} rows)");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Trigger re-entries
        var reentries = analysis.TriggerReEntries?.Where(r => r.ReEntryCount > 1).ToList();
        if (reentries?.Count > 0)
        {
            sb.AppendLine("**Trigger Re-Entry (Recursion):**");
            sb.AppendLine("```");
            foreach (var re in reentries)
                sb.AppendLine($"  {re.TriggerName} fired {re.ReEntryCount}x  (object: {re.ObjectType}, events: {string.Join(", ", re.Events)})");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Top slow methods
        var slowMethods = analysis.MethodStats?
            .OrderByDescending(m => m.Value.TotalDurationMs)
            .Take(8)
            .ToList();
        if (slowMethods?.Count > 0)
        {
            sb.AppendLine("**Slowest Methods:**");
            sb.AppendLine("```");
            foreach (var m in slowMethods)
                sb.AppendLine($"  {m.Value.TotalDurationMs}ms  ({m.Value.CallCount}x)  {TruncateText(m.Key, 80)}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Callouts
        if (analysis.Callouts?.Count > 0)
        {
            sb.AppendLine("**External Callouts:**");
            sb.AppendLine("```");
            foreach (var c in analysis.Callouts.Take(10))
                sb.AppendLine($"  {c.DurationMs}ms  HTTP {c.StatusCode}  {c.HttpMethod}  {TruncateText(c.Endpoint, 100)}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Flow errors
        if (analysis.FlowErrors?.Count > 0)
        {
            sb.AppendLine("**Flow Errors:**");
            sb.AppendLine("```");
            foreach (var fe in analysis.FlowErrors.Take(5))
                sb.AppendLine($"  [{fe.ErrorCode}] {TruncateText(fe.ErrorMessage, 120)} (Flow: {fe.FlowName})");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Handled exceptions
        if (analysis.HandledExceptions?.Count > 0)
        {
            sb.AppendLine($"**Handled Exceptions (try/catch, {analysis.HandledExceptions.Count} total — these did NOT crash the transaction):**");
            sb.AppendLine("```");
            foreach (var he in analysis.HandledExceptions.Take(5))
                sb.AppendLine($"  {he.Name}");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // --- SCRIPTED EXPLANATION ---
        sb.AppendLine("---");
        sb.AppendLine("## 2. Our Current Scripted Explanation (What the App Says Today)");
        sb.AppendLine();
        sb.AppendLine("This is the verbatim output of our template engine for this log:");
        sb.AppendLine();

        sb.AppendLine("### What Happened");
        sb.AppendLine(scriptedExplanation.WhatHappened);
        sb.AppendLine();

        if (scriptedExplanation.WhatYourCodeDid?.Count > 0)
        {
            sb.AppendLine("### What Your Code Did");
            foreach (var b in scriptedExplanation.WhatYourCodeDid)
                sb.AppendLine($"• {b}");
            sb.AppendLine();
        }

        sb.AppendLine("### Performance Verdict");
        sb.AppendLine($"{scriptedExplanation.VerdictIcon} {scriptedExplanation.PerformanceVerdict}");
        sb.AppendLine();

        if (scriptedExplanation.Issues?.Count > 0)
        {
            sb.AppendLine("### Issues Found");
            foreach (var issue in scriptedExplanation.Issues)
            {
                sb.AppendLine();
                sb.AppendLine($"**{issue.Icon} [{issue.Severity}] {issue.Title}**");
                sb.AppendLine($"- What this means: {issue.WhatThisMeans}");
                if (!string.IsNullOrWhiteSpace(issue.WhyThisHappened))
                    sb.AppendLine($"- Why this happened: {issue.WhyThisHappened}");
                if (!string.IsNullOrWhiteSpace(issue.Analogy))
                    sb.AppendLine($"- 💬 Analogy: *\"{issue.Analogy}\"*");
                if (!string.IsNullOrWhiteSpace(issue.WhyItsBad))
                    sb.AppendLine($"- Impact: {issue.WhyItsBad}");
            }
            sb.AppendLine();
        }

        if (scriptedExplanation.Recommendations?.Count > 0)
        {
            sb.AppendLine("### Recommendations");
            foreach (var rec in scriptedExplanation.Recommendations)
            {
                sb.AppendLine();
                sb.AppendLine($"**{rec.Icon} {rec.Title}** (~{rec.EstimatedMinutes} min)");
                sb.AppendLine(rec.Explanation);
                if (!string.IsNullOrWhiteSpace(rec.SpeedImprovement))
                    sb.AppendLine($"Impact: {rec.ImpactBefore} → {rec.ImpactAfter}  {rec.SpeedImprovement}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Overall Assessment");
        sb.AppendLine(scriptedExplanation.OverallAssessment);
        sb.AppendLine($"Priority: {scriptedExplanation.Priority} | Effort: {scriptedExplanation.Effort}");
        sb.AppendLine($"Impact: {scriptedExplanation.ImpactSummary}");

        // --- THE ASK ---
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## 3. What We Need From You");
        sb.AppendLine();
        sb.AppendLine("Please respond with the following sections:");
        sb.AppendLine();
        sb.AppendLine("### A. Your Explanation");
        sb.AppendLine("Write your own plain-English explanation of this log, targeting the same audience");
        sb.AppendLine("(someone who knows Salesforce basics but isn't a developer). Use our section");
        sb.AppendLine("structure: What Happened, Issues Found, Recommendations. Be conversational");
        sb.AppendLine("and mentoring — not clinical. Don't hold back on analogies.");
        sb.AppendLine();
        sb.AppendLine("### B. What We Got Right");
        sb.AppendLine("Where our scripted explanation is accurate, helpful, and well-phrased.");
        sb.AppendLine();
        sb.AppendLine("### C. What We Missed or Got Wrong");
        sb.AppendLine("Issues in the parsed data that our scripts didn't catch, or things we described");
        sb.AppendLine("inaccurately. Be specific — reference field names/values from the parsed data above.");
        sb.AppendLine();
        sb.AppendLine("### D. Stale or Weak Analogies");
        sb.AppendLine("Flag any analogies that are confusing, overused, or just not very good.");
        sb.AppendLine("Suggest a better one for each.");
        sb.AppendLine();
        sb.AppendLine("### E. Better Phrasings");
        sb.AppendLine("Specific sentences or paragraphs from our scripted output that you'd rewrite.");
        sb.AppendLine("Show the before and after.");
        sb.AppendLine();
        sb.AppendLine("### F. Priority Changes");
        sb.AppendLine("Would you change the severity of any issue? E.g., we said Critical but it's");
        sb.AppendLine("actually Medium, or we flagged something minor but missed something severe.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"*Generated by Black Widow LogCompare tool — {DateTime.Now:yyyy-MM-dd HH:mm}*");

        return sb.ToString();
    }

    static double SafePct(int current, int max) => max > 0 ? current * 100.0 / max : 0;

    static string TruncateText(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "…";
    }
    // Strip Markdown bold/italic/code markers for plain-text output
    static string StripMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text
            .Replace("**", "")
            .Replace("__", "")
            .Replace("`", "");
    }}
