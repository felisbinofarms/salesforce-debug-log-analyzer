using SalesforceDebugAnalyzer.Models;
using FluentAssertions;
using Xunit;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests for MonitoringModels: computed properties, defaults, and factory methods.
/// </summary>
public class MonitoringModelsTests
{
    // ================================================================
    //  LogSnapshot Defaults
    // ================================================================

    [Fact]
    public void LogSnapshot_DefaultSource_IsDebugLog()
    {
        var snapshot = new LogSnapshot();
        snapshot.Source.Should().Be("debug_log");
    }

    [Fact]
    public void LogSnapshot_DefaultStrings_AreEmpty()
    {
        var snapshot = new LogSnapshot();
        snapshot.LogId.Should().BeEmpty();
        snapshot.OrgId.Should().BeEmpty();
        snapshot.EntryPoint.Should().BeEmpty();
        snapshot.OperationType.Should().BeEmpty();
        snapshot.LogUser.Should().BeEmpty();
        snapshot.HealthGrade.Should().BeEmpty();
        snapshot.BulkSafetyGrade.Should().BeEmpty();
    }

    [Fact]
    public void LogSnapshot_DefaultBooleans_AreFalse()
    {
        var snapshot = new LogSnapshot();
        snapshot.HasErrors.Should().BeFalse();
        snapshot.TransactionFailed.Should().BeFalse();
        snapshot.IsAsync.Should().BeFalse();
        snapshot.IsTruncated.Should().BeFalse();
    }

    // ================================================================
    //  MonitoringAlert.SeverityColor
    // ================================================================

    [Theory]
    [InlineData("critical", "#F85149")]
    [InlineData("warning", "#D29922")]
    [InlineData("info", "#4493F8")]
    [InlineData("unknown", "#7D8590")]
    [InlineData("", "#7D8590")]
    public void MonitoringAlert_SeverityColor_ReturnsCorrectColor(string severity, string expectedColor)
    {
        var alert = new MonitoringAlert { Severity = severity };
        alert.SeverityColor.Should().Be(expectedColor);
    }

    // ================================================================
    //  MonitoringAlert.TimeAgo
    // ================================================================

    [Fact]
    public void MonitoringAlert_TimeAgo_JustNow()
    {
        var alert = new MonitoringAlert { CreatedAt = DateTime.UtcNow.AddSeconds(-30) };
        alert.TimeAgo.Should().Be("Just now");
    }

    [Fact]
    public void MonitoringAlert_TimeAgo_MinutesAgo()
    {
        var alert = new MonitoringAlert { CreatedAt = DateTime.UtcNow.AddMinutes(-15) };
        alert.TimeAgo.Should().Be("15m ago");
    }

    [Fact]
    public void MonitoringAlert_TimeAgo_HoursAgo()
    {
        var alert = new MonitoringAlert { CreatedAt = DateTime.UtcNow.AddHours(-3) };
        alert.TimeAgo.Should().Be("3h ago");
    }

    [Fact]
    public void MonitoringAlert_TimeAgo_DaysAgo()
    {
        var alert = new MonitoringAlert { CreatedAt = DateTime.UtcNow.AddDays(-2) };
        alert.TimeAgo.Should().Be("2d ago");
    }

    [Fact]
    public void MonitoringAlert_TimeAgo_OverOneWeek_ShowsDate()
    {
        var pastDate = DateTime.UtcNow.AddDays(-10);
        var alert = new MonitoringAlert { CreatedAt = pastDate };
        alert.TimeAgo.Should().Be(pastDate.ToString("MMM dd"));
    }

    // ================================================================
    //  MonitoringAlert.ChangeDescription
    // ================================================================

    [Fact]
    public void MonitoringAlert_ChangeDescription_BothValues()
    {
        var alert = new MonitoringAlert { CurrentValue = 85, BaselineValue = 40 };
        alert.ChangeDescription.Should().Be("85 (was 40)");
    }

    [Fact]
    public void MonitoringAlert_ChangeDescription_OnlyCurrentValue()
    {
        var alert = new MonitoringAlert { CurrentValue = 85, BaselineValue = null };
        alert.ChangeDescription.Should().Be("85");
    }

    [Fact]
    public void MonitoringAlert_ChangeDescription_NoValues()
    {
        var alert = new MonitoringAlert { CurrentValue = null, BaselineValue = null };
        alert.ChangeDescription.Should().BeEmpty();
    }

    [Fact]
    public void MonitoringAlert_ChangeDescription_OnlyBaseline_ReturnsEmpty()
    {
        var alert = new MonitoringAlert { CurrentValue = null, BaselineValue = 40 };
        alert.ChangeDescription.Should().BeEmpty();
    }

    // ================================================================
    //  MonitoringAlert Defaults
    // ================================================================

    [Fact]
    public void MonitoringAlert_DefaultSeverity_IsInfo()
    {
        var alert = new MonitoringAlert();
        alert.Severity.Should().Be("info");
    }

    [Fact]
    public void MonitoringAlert_DefaultBooleans_AreFalse()
    {
        var alert = new MonitoringAlert();
        alert.IsRead.Should().BeFalse();
        alert.IsDismissed.Should().BeFalse();
    }

    // ================================================================
    //  ShieldEvent Defaults
    // ================================================================

    [Fact]
    public void ShieldEvent_DefaultIsSuccess_IsTrue()
    {
        var ev = new ShieldEvent();
        ev.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ShieldEvent_DefaultIsAnomaly_IsFalse()
    {
        var ev = new ShieldEvent();
        ev.IsAnomaly.Should().BeFalse();
    }

    [Fact]
    public void ShieldEvent_NullableFields_AreNull()
    {
        var ev = new ShieldEvent();
        ev.UserId.Should().BeNull();
        ev.Uri.Should().BeNull();
        ev.DurationMs.Should().BeNull();
        ev.CpuTimeMs.Should().BeNull();
        ev.RowCount.Should().BeNull();
        ev.StatusCode.Should().BeNull();
        ev.ClientIp.Should().BeNull();
        ev.ExtraJson.Should().BeNull();
        ev.AnomalyReason.Should().BeNull();
    }

    // ================================================================
    //  MetricAggregate Defaults
    // ================================================================

    [Fact]
    public void MetricAggregate_DefaultPeriodType_IsHourly()
    {
        var agg = new MetricAggregate();
        agg.PeriodType.Should().Be("hourly");
    }

    // ================================================================
    //  ShieldLogFileRecord Defaults
    // ================================================================

    [Fact]
    public void ShieldLogFileRecord_DefaultIntervalType_IsHourly()
    {
        var record = new ShieldLogFileRecord();
        record.IntervalType.Should().Be("Hourly");
    }

    // ================================================================
    //  EventLogFileRecord Defaults
    // ================================================================

    [Fact]
    public void EventLogFileRecord_DefaultInterval_IsHourly()
    {
        var record = new EventLogFileRecord();
        record.Interval.Should().Be("Hourly");
    }

    // ================================================================
    //  Baseline Defaults
    // ================================================================

    [Fact]
    public void Baseline_DefaultWindowDays_Is14()
    {
        var baseline = new Baseline();
        baseline.WindowDays.Should().Be(14);
    }
}
