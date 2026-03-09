using System.Text.RegularExpressions;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Scans debug log lines for potential PII (Personally Identifiable Information).
/// Uses compiled regex patterns for performance; does not store raw PII values.
///
/// IMPORTANT: This scanner flags POTENTIAL PII. Debug logs routinely contain
/// Salesforce record IDs, user names, and field values. Developers should review
/// findings in context rather than treating every match as a data leak.
/// </summary>
public class PiiScannerService
{
    // ── Compiled regex patterns ──────────────────────────────────────────────

    // Email — RFC 5321 simplified. Excludes @salesforce.com and @force.com
    // (those are Salesforce system addresses, not customer PII)
    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+\-]+@(?!salesforce\.com|force\.com)[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // US Social Security Numbers — dashes required to reduce false positives
    private static readonly Regex SsnRegex = new(
        @"\b(?!000|666|9\d{2})\d{3}-(?!00)\d{2}-(?!0000)\d{4}\b",
        RegexOptions.Compiled);

    // Credit card numbers:
    //   Visa    — 16 digits starting with 4
    //   MC      — 16 digits starting with 51-55
    //   Amex    — 15 digits starting with 34/37 (grouped 4-6-5 or unformatted)
    //   Discover — 16 digits starting with 6011
    // Uses non-digit lookbehind/ahead to avoid matching inside larger numbers.
    private static readonly Regex CreditCardRegex = new(
        @"(?<![\d\-])(?:4\d{3}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}|5[1-5]\d{2}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}|3[47]\d{2}[\s\-]?\d{6}[\s\-]?\d{5}|6011[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4})(?![\d\-])",
        RegexOptions.Compiled);

    // US/Canada phone numbers — (nnn) nnn-nnnn, nnn-nnn-nnnn, +1 nnn nnn nnnn
    // Note: no leading \b because (555) starts with a non-word character.
    private static readonly Regex PhoneRegex = new(
        @"(?<!\d)(?:\+1[\s\-]?)?(?:\((?!000)\d{3}\)[\s\-\.]|(?!000)\d{3}[\s\-\.])\d{3}[\s\-\.]\d{4}(?!\d)",
        RegexOptions.Compiled);

    // IPv4 addresses — skip private ranges (127.x, 192.168.x, 10.x, 172.16-31.x)
    // to avoid excessive noise from internal Salesforce infra
    private static readonly Regex IpAddressRegex = new(
        @"\b(?!(?:127\.|192\.168\.|10\.|172\.(?:1[6-9]|2\d|3[01])\.))(?:\d{1,3}\.){3}\d{1,3}\b",
        RegexOptions.Compiled);

    // ── Scan entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Scans all raw log lines for PII patterns.
    /// Lines are 1-indexed; line 1 = first line of the file.
    /// </summary>
    public PiiScanResult Scan(IReadOnlyList<string> lines)
    {
        var result = new PiiScanResult { ScannedAt = DateTime.UtcNow };

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            ScanLine(line, i + 1, result.Matches);
        }

        return result;
    }

    /// <summary>
    /// Convenience overload for scanning a single raw log string split on newlines.
    /// </summary>
    public PiiScanResult Scan(string rawLog)
    {
        var lines = rawLog.Split('\n');
        return Scan(lines);
    }

    // ── Per-line scanning ────────────────────────────────────────────────────

    private static void ScanLine(string line, int lineNumber, List<PiiMatch> matches)
    {
        CheckPattern(line, lineNumber, EmailRegex,      "Email",       "Medium", MaskEmail,      matches);
        CheckPattern(line, lineNumber, SsnRegex,        "SSN",         "High",   MaskSsn,        matches);
        CheckPattern(line, lineNumber, CreditCardRegex, "Credit Card", "High",   MaskCard,       matches);
        CheckPattern(line, lineNumber, PhoneRegex,      "Phone",       "Medium", MaskPhone,      matches);
        CheckPattern(line, lineNumber, IpAddressRegex,  "IP Address",  "Low",    MaskIpAddress,  matches);
    }

    private static void CheckPattern(
        string line,
        int lineNumber,
        Regex pattern,
        string piiType,
        string severity,
        Func<string, string> masker,
        List<PiiMatch> matches)
    {
        foreach (Match m in pattern.Matches(line))
        {
            matches.Add(new PiiMatch
            {
                LineNumber  = lineNumber,
                PiiType     = piiType,
                Severity    = severity,
                MaskedValue = masker(m.Value),
                Context     = BuildContext(line, m.Index, m.Length)
            });
        }
    }

    // ── Context snippet ──────────────────────────────────────────────────────

    private static string BuildContext(string line, int matchStart, int matchLength)
    {
        const int ContextRadius = 30;

        int start = Math.Max(0, matchStart - ContextRadius);
        int end   = Math.Min(line.Length, matchStart + matchLength + ContextRadius);

        var prefix = line[start..matchStart];
        var suffix = line[(matchStart + matchLength)..end];

        return (start > 0 ? "…" : "") + prefix + "[REDACTED]" + suffix + (end < line.Length ? "…" : "");
    }

    // ── Masking helpers (never store raw PII) ────────────────────────────────

    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***@***";
        var local   = email[..at];
        var domain  = email[(at + 1)..];
        var maskedLocal = local.Length <= 3
            ? new string('*', local.Length)
            : local[..2] + new string('*', local.Length - 2);
        return $"{maskedLocal}@{domain}";
    }

    internal static string MaskSsn(string ssn)
    {
        // Keep last 4 digits
        var digits = ssn.Replace("-", "");
        return $"***-**-{digits[^4..]}";
    }

    internal static string MaskCard(string card)
    {
        var digits = card.Replace(" ", "").Replace("-", "");
        return "****-****-****-" + (digits.Length >= 4 ? digits[^4..] : "****");
    }

    internal static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4)
            return "***-***-" + digits[^4..];
        return "***-***-****";
    }

    internal static string MaskIpAddress(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length == 4)
            return $"{parts[0]}.{parts[1]}.***,***";
        return "***.***.***.***";
    }
}
