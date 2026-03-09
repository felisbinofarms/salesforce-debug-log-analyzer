using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Routes Shield and monitoring alerts to external channels: email (SMTP) and Slack webhooks.
/// Both channels are opt-in and configured via AppSettings.
/// </summary>
public class AlertRoutingService
{
    private readonly SettingsService _settingsService;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public AlertRoutingService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Routes an alert to all configured external channels.
    /// Respects the CriticalAlertsOnly setting.
    /// </summary>
    public async Task RouteAlertAsync(MonitoringAlert alert)
    {
        var settings = _settingsService.Load();

        // Respect alert routing filter
        if (settings.AlertRoutingCriticalOnly && alert.Severity != "critical")
            return;

        var tasks = new List<Task>();

        if (settings.EmailAlertsEnabled && !string.IsNullOrWhiteSpace(settings.AlertEmailTo))
            tasks.Add(TrySendEmailAsync(alert, settings));

        if (settings.SlackAlertsEnabled && !string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
            tasks.Add(TrySendSlackAsync(alert, settings));

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private async Task TrySendEmailAsync(MonitoringAlert alert, AppSettings settings)
    {
        try
        {
            using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
                smtp.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);

            var severityEmoji = alert.Severity switch
            {
                "critical" => "🔴",
                "warning" => "🟡",
                _ => "🔵"
            };

            var subject = $"{severityEmoji} Black Widow Alert: {alert.Title}";

            var body = new StringBuilder();
            body.AppendLine($"<html><body style=\"font-family: Arial, sans-serif; color: #333;\">");
            body.AppendLine($"<h2 style=\"color: {(alert.Severity == "critical" ? "#c0392b" : alert.Severity == "warning" ? "#d68910" : "#2980b9")}\">");
            body.AppendLine($"{severityEmoji} {WebUtility.HtmlEncode(alert.Title)}</h2>");
            body.AppendLine($"<p><strong>Severity:</strong> {alert.Severity.ToUpperInvariant()}</p>");
            body.AppendLine($"<p><strong>Time:</strong> {alert.CreatedAt.ToLocalTime():f}</p>");
            body.AppendLine($"<p><strong>Description:</strong></p>");
            body.AppendLine($"<p>{WebUtility.HtmlEncode(alert.Description)}</p>");

            if (!string.IsNullOrEmpty(alert.EntryPoint))
                body.AppendLine($"<p><strong>User/Entry Point:</strong> {WebUtility.HtmlEncode(alert.EntryPoint)}</p>");

            if (alert.CurrentValue.HasValue)
                body.AppendLine($"<p><strong>Metric:</strong> {alert.MetricName} = {alert.CurrentValue:F0}" +
                               (alert.ThresholdValue.HasValue ? $" (threshold: {alert.ThresholdValue:F0})" : "") + "</p>");

            body.AppendLine("<hr/><p style=\"color:#888;font-size:12px;\">Sent by Black Widow 🕷️ — Salesforce Debug Log Analyzer</p>");
            body.AppendLine("</body></html>");

            var message = new MailMessage
            {
                From = new MailAddress(
                    string.IsNullOrWhiteSpace(settings.SmtpUsername) ? "noreply@blackwidow.app" : settings.SmtpUsername,
                    "Black Widow"),
                Subject = subject,
                Body = body.ToString(),
                IsBodyHtml = true
            };

            foreach (var addr in settings.AlertEmailTo.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                message.To.Add(addr);

            await smtp.SendMailAsync(message);
            Log.Information("Alert email sent for '{Title}' to {To}", alert.Title, settings.AlertEmailTo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send alert email for '{Title}'", alert.Title);
        }
    }

    private async Task TrySendSlackAsync(MonitoringAlert alert, AppSettings settings)
    {
        try
        {
            var color = alert.Severity switch
            {
                "critical" => "#F85149",
                "warning" => "#D29922",
                _ => "#4493F8"
            };

            var severityEmoji = alert.Severity switch
            {
                "critical" => ":red_circle:",
                "warning" => ":yellow_circle:",
                _ => ":blue_circle:"
            };

            var payload = new
            {
                attachments = new[]
                {
                    new
                    {
                        color,
                        title = $"{severityEmoji} {alert.Title}",
                        text = alert.Description,
                        fields = BuildSlackFields(alert),
                        footer = "Black Widow 🕷️",
                        ts = new DateTimeOffset(alert.CreatedAt).ToUnixTimeSeconds()
                    }
                }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(settings.SlackWebhookUrl, content);
            if (!response.IsSuccessStatusCode)
                Log.Warning("Slack webhook returned {Status} for alert '{Title}'", response.StatusCode, alert.Title);
            else
                Log.Information("Slack alert sent for '{Title}'", alert.Title);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send Slack alert for '{Title}'", alert.Title);
        }
    }

    private static object[] BuildSlackFields(MonitoringAlert alert)
    {
        var fields = new List<object>
        {
            new { title = "Severity", value = alert.Severity.ToUpperInvariant(), @short = true },
            new { title = "Time", value = alert.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"), @short = true }
        };

        if (!string.IsNullOrEmpty(alert.EntryPoint))
            fields.Add(new { title = "User / Entry Point", value = alert.EntryPoint, @short = true });

        if (alert.CurrentValue.HasValue)
        {
            var metricText = $"{alert.CurrentValue:F0}";
            if (alert.ThresholdValue.HasValue)
                metricText += $" (threshold: {alert.ThresholdValue:F0})";
            fields.Add(new { title = alert.MetricName ?? "Metric", value = metricText, @short = true });
        }

        return fields.ToArray();
    }
}
