using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SalesforceDebugAnalyzer.Models;
using System.IO;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for exporting analysis reports to various formats (PDF, JSON, etc.)
/// </summary>
public class ReportExportService
{
    public ReportExportService()
    {
        // Set QuestPDF license (Community - free for open-source)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Export log analysis to PDF report
    /// </summary>
    public void ExportToPdf(LogAnalysis analysis, string outputPath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Black));

                page.Header().Element(ComposeHeader);
                page.Content().Element(content => ComposeContent(content, analysis));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("🕷️ Black Widow Analysis Report")
                    .FontSize(20)
                    .Bold()
                    .FontColor("#5865F2");

                column.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private void ComposeContent(IContainer container, LogAnalysis analysis)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            // Executive Summary
            column.Item().Element(c => ComposeSummarySection(c, analysis));

            // Health Score (if available)
            if (analysis.Health != null)
            {
                column.Item().Element(c => ComposeHealthSection(c, analysis));
            }

            // Actionable Issues (if available)
            if (analysis.Health != null && analysis.Health.TotalIssues > 0)
            {
                column.Item().Element(c => ComposeIssuesSection(c, analysis));
            }

            // Governor Limits
            if (analysis.LimitSnapshots.Any())
            {
                column.Item().Element(c => ComposeGovernorLimitsSection(c, analysis));
            }

            // Performance Metrics
            column.Item().Element(c => ComposePerformanceSection(c, analysis));

            // Recommendations (legacy format if Health is not available)
            if (analysis.Health == null && analysis.Recommendations.Any())
            {
                column.Item().Element(c => ComposeLegacyRecommendations(c, analysis));
            }
        });
    }

    private void ComposeSummarySection(IContainer container, LogAnalysis analysis)
    {
        container.Column(column =>
        {
            column.Item().Text("Executive Summary")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
            {
                inner.Item().Text($"Log: {analysis.LogName}")
                    .FontSize(10);
                inner.Item().Text($"Duration: {analysis.DurationMs:F2} ms ({analysis.DurationMs / 1000:F2} seconds)")
                    .FontSize(10);
                inner.Item().Text($"Lines: {analysis.LineCount:N0}")
                    .FontSize(10);
                inner.Item().Text($"Status: {(analysis.TransactionFailed ? "FAILED" : "SUCCESS")}")
                    .FontSize(10)
                    .Bold()
                    .FontColor(analysis.TransactionFailed ? "#F04747" : "#43B581");

                if (!string.IsNullOrWhiteSpace(analysis.Summary))
                {
                    inner.Item().PaddingTop(5).Text(analysis.Summary)
                        .FontSize(10)
                        .Italic();
                }
            });
        });
    }

    private void ComposeHealthSection(IContainer container, LogAnalysis analysis)
    {
        if (analysis.Health == null) return;

        container.Column(column =>
        {
            column.Item().Text("Health Score")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            var scoreColor = analysis.Health.Score >= 80 ? "#43B581" : // Green
                            analysis.Health.Score >= 50 ? "#FAA61A" : // Orange
                            "#F04747"; // Red

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
            {
                row.RelativeItem().Text($"{analysis.Health.StatusIcon} {analysis.Health.Score}/100")
                    .FontSize(24)
                    .Bold()
                    .FontColor(scoreColor);

                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text($"{analysis.Health.Status} (Grade: {analysis.Health.Grade})")
                        .FontSize(12)
                        .Bold();
                    if (analysis.Health.TotalIssues > 0)
                    {
                        inner.Item().Text($"Total Issues: {analysis.Health.TotalIssues}")
                            .FontSize(10)
                            .FontColor("#F04747");
                        inner.Item().Text($"Estimated Fix Time: {analysis.Health.TotalEstimatedMinutes} minutes")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken1);
                    }
                });
            });

            if (!string.IsNullOrWhiteSpace(analysis.Health.Reasoning))
            {
                column.Item().PaddingTop(5).Text(analysis.Health.Reasoning)
                    .FontSize(10)
                    .Italic()
                    .FontColor(Colors.Grey.Darken1);
            }
        });
    }

    private void ComposeIssuesSection(IContainer container, LogAnalysis analysis)
    {
        if (analysis.Health == null) return;

        container.Column(column =>
        {
            column.Item().Text("Actionable Issues & Recommendations")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
            {
                // Critical Issues
                if (analysis.Health.CriticalIssues.Any())
                {
                    inner.Item().Text("🔴 Critical Priority")
                        .FontSize(12)
                        .Bold()
                        .FontColor("#F04747");
                    
                    foreach (var issue in analysis.Health.CriticalIssues)
                    {
                        AddIssueItem(inner, issue);
                    }
                }

                // High Priority Issues
                if (analysis.Health.HighPriorityIssues.Any())
                {
                    inner.Item().PaddingTop(10).Text("🟠 High Priority")
                        .FontSize(12)
                        .Bold()
                        .FontColor("#FAA61A");
                    
                    foreach (var issue in analysis.Health.HighPriorityIssues)
                    {
                        AddIssueItem(inner, issue);
                    }
                }

                // Quick Wins
                if (analysis.Health.QuickWins.Any())
                {
                    inner.Item().PaddingTop(10).Text("⚡ Quick Wins")
                        .FontSize(12)
                        .Bold()
                        .FontColor("#43B581");
                    
                    foreach (var issue in analysis.Health.QuickWins)
                    {
                        AddIssueItem(inner, issue);
                    }
                }
            });
        });
    }

    private void AddIssueItem(ColumnDescriptor column, ActionableIssue issue)
    {
        column.Item().PaddingLeft(15).PaddingTop(5).Column(issueColumn =>
        {
            issueColumn.Item().Row(row =>
            {
                row.ConstantItem(10).Text("•");
                row.RelativeItem().Text(issue.Title)
                    .FontSize(10)
                    .Bold();
            });

            if (!string.IsNullOrWhiteSpace(issue.Problem))
            {
                issueColumn.Item().PaddingLeft(10).Text(issue.Problem)
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken1);
            }

            if (!string.IsNullOrWhiteSpace(issue.Impact))
            {
                issueColumn.Item().PaddingLeft(10).Text($"💡 Impact: {issue.Impact}")
                    .FontSize(9)
                    .Italic()
                    .FontColor("#5865F2");
            }

            if (!string.IsNullOrWhiteSpace(issue.Fix))
            {
                issueColumn.Item().PaddingLeft(10).Text($"✅ Fix: {issue.Fix}")
                    .FontSize(9)
                    .FontColor("#43B581");
            }

            if (!string.IsNullOrWhiteSpace(issue.Location))
            {
                issueColumn.Item().PaddingLeft(10).Text($"📍 {issue.Location}")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeGovernorLimitsSection(IContainer container, LogAnalysis analysis)
    {
        if (!analysis.LimitSnapshots.Any()) return;

        // Get the last (final) snapshot
        var finalLimits = analysis.LimitSnapshots.Last();

        container.Column(column =>
        {
            column.Item().Text("Governor Limits")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
            {
                AddLimitRow(inner, "SOQL Queries", finalLimits.SoqlQueries, finalLimits.SoqlQueriesLimit);
                AddLimitRow(inner, "DML Statements", finalLimits.DmlStatements, finalLimits.DmlStatementsLimit);
                AddLimitRow(inner, "CPU Time", finalLimits.CpuTime, finalLimits.CpuTimeLimit, "ms");
                AddLimitRow(inner, "Heap Size", finalLimits.HeapSize / 1024, finalLimits.HeapSizeLimit / 1024, "KB");
                AddLimitRow(inner, "Query Rows", finalLimits.QueryRows, finalLimits.QueryRowsLimit);
                AddLimitRow(inner, "DML Rows", finalLimits.DmlRows, finalLimits.DmlRowsLimit);
            });
        });
    }

    private void AddLimitRow(ColumnDescriptor column, string name, double used, double limit, string unit = "")
    {
        var percentage = limit > 0 ? (used / limit) * 100 : 0;
        var color = percentage >= 80 ? "#F04747" : 
                   percentage >= 50 ? "#FAA61A" : 
                   "#43B581";

        column.Item().PaddingTop(3).Row(row =>
        {
            row.RelativeItem(3).Text(name).FontSize(10);
            row.RelativeItem(2).Text($"{used:F0} / {limit:F0} {unit}").FontSize(10);
            row.RelativeItem(2).Text($"{percentage:F1}%")
                .FontSize(10)
                .Bold()
                .FontColor(color);
        });
    }

    private void ComposePerformanceSection(IContainer container, LogAnalysis analysis)
    {
        container.Column(column =>
        {
            column.Item().Text("Performance Metrics")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
            {
                inner.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Wall Clock Time").FontSize(10).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"{analysis.WallClockMs:F2} ms").FontSize(12).Bold();
                    });
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("CPU Time").FontSize(10).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"{analysis.CpuTimeMs:F2} ms").FontSize(12).Bold();
                    });
                });

                inner.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Database Operations").FontSize(10).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"{analysis.DatabaseOperations.Count}").FontSize(12).Bold();
                    });
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Errors").FontSize(10).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"{analysis.Errors.Count}")
                            .FontSize(12)
                            .Bold()
                            .FontColor(analysis.Errors.Count > 0 ? "#F04747" : "#43B581");
                    });
                });

                // Top slow methods (if available)
                if (analysis.CumulativeProfiling?.TopMethods?.Any() == true)
                {
                    inner.Item().PaddingTop(10).Text("Top Slow Methods:")
                        .FontSize(11)
                        .Bold();

                    foreach (var method in analysis.CumulativeProfiling.TopMethods.OrderByDescending(m => m.TotalDurationMs).Take(5))
                    {
                        inner.Item().PaddingLeft(10).PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem(4).Text(method.Location).FontSize(9);
                            row.RelativeItem(1).Text($"{method.TotalDurationMs:F2} ms")
                                .FontSize(9)
                                .FontColor(method.TotalDurationMs > 1000 ? "#F04747" : Colors.Grey.Darken1);
                        });
                    }
                }
            });
        });
    }

    private void ComposeLegacyRecommendations(IContainer container, LogAnalysis analysis)
    {
        container.Column(column =>
        {
            column.Item().Text("Recommendations")
                .FontSize(16)
                .Bold()
                .FontColor("#5865F2");

            column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
            {
                foreach (var rec in analysis.Recommendations)
                {
                    inner.Item().PaddingTop(3).Row(row =>
                    {
                        row.ConstantItem(10).Text("•");
                        row.RelativeItem().Text(rec).FontSize(10);
                    });
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("Generated by ").FontSize(8).FontColor(Colors.Grey.Medium);
            text.Span("Black Widow").FontSize(8).Bold().FontColor("#5865F2");
            text.Span(" - Salesforce Debug Log Analyzer").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    /// <summary>
    /// Export analysis to JSON format
    /// </summary>
    public void ExportToJson(LogAnalysis analysis, string outputPath)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(analysis, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Copy recommendations to clipboard
    /// </summary>
    public string GetRecommendationsText(LogAnalysis analysis)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Black Widow Analysis - {DateTime.Now:yyyy-MM-dd}");
        text.AppendLine($"Log: {analysis.LogName}");
        
        if (analysis.Health != null)
        {
            text.AppendLine($"Health Score: {analysis.Health.Score}/100 ({analysis.Health.Grade})");
            text.AppendLine();
            text.AppendLine("ACTIONABLE ISSUES:");
            text.AppendLine(new string('=', 50));

            if (analysis.Health.CriticalIssues.Any())
            {
                text.AppendLine();
                text.AppendLine("🔴 CRITICAL:");
                foreach (var issue in analysis.Health.CriticalIssues)
                {
                    text.AppendLine($"  • {issue.Title}");
                    if (!string.IsNullOrWhiteSpace(issue.Problem))
                        text.AppendLine($"    Problem: {issue.Problem}");
                    if (!string.IsNullOrWhiteSpace(issue.Fix))
                        text.AppendLine($"    Fix: {issue.Fix}");
                }
            }

            if (analysis.Health.HighPriorityIssues.Any())
            {
                text.AppendLine();
                text.AppendLine("🟠 HIGH PRIORITY:");
                foreach (var issue in analysis.Health.HighPriorityIssues)
                {
                    text.AppendLine($"  • {issue.Title}");
                    if (!string.IsNullOrWhiteSpace(issue.Fix))
                        text.AppendLine($"    Fix: {issue.Fix}");
                }
            }

            if (analysis.Health.QuickWins.Any())
            {
                text.AppendLine();
                text.AppendLine("⚡ QUICK WINS:");
                foreach (var issue in analysis.Health.QuickWins)
                {
                    text.AppendLine($"  • {issue.Title} (Est: {issue.EstimatedFixTimeMinutes} min)");
                }
            }
        }
        else if (analysis.Recommendations.Any())
        {
            text.AppendLine();
            text.AppendLine("RECOMMENDATIONS:");
            text.AppendLine(new string('=', 50));
            foreach (var rec in analysis.Recommendations)
            {
                text.AppendLine($"  • {rec}");
            }
        }
        else
        {
            text.AppendLine();
            text.AppendLine("No recommendations available.");
        }

        return text.ToString();
    }
}
