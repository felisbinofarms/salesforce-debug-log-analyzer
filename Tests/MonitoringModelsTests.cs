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

    // ================================================================
    //  MonitoringAlert — AffectedUserCount display properties
    // ================================================================

    [Fact]
    public void MonitoringAlert_AffectedUsersDisplay_SingleUser()
    {
        var alert = new MonitoringAlert { AffectedUserCount = 1 };
        alert.AffectedUsersDisplay.Should().Be("1 user");
        alert.HasAffectedUsers.Should().BeTrue();
    }

    [Fact]
    public void MonitoringAlert_AffectedUsersDisplay_PluralUsers()
    {
        var alert = new MonitoringAlert { AffectedUserCount = 5 };
        alert.AffectedUsersDisplay.Should().Be("5 users");
    }

    [Fact]
    public void MonitoringAlert_AffectedUsersDisplay_NullCount_ReturnsEmpty()
    {
        var alert = new MonitoringAlert { AffectedUserCount = null };
        alert.AffectedUsersDisplay.Should().BeEmpty();
        alert.HasAffectedUsers.Should().BeFalse();
    }

    [Fact]
    public void MonitoringAlert_AffectedUsersDisplay_ZeroCount_ReturnsEmpty()
    {
        var alert = new MonitoringAlert { AffectedUserCount = 0 };
        alert.AffectedUsersDisplay.Should().BeEmpty();
        alert.HasAffectedUsers.Should().BeFalse();
    }

    // ================================================================
    //  ShieldDashboardData — AnomalyAffectedUsers display properties
    // ================================================================

    [Fact]
    public void ShieldDashboardData_AnomalyAffectedUsersDisplay_ZeroUsers()
    {
        var data = new ShieldDashboardData { AnomalyAffectedUsers = 0 };
        data.AnomalyAffectedUsersDisplay.Should().Be("No users affected");
        data.HasAnomalyAffectedUsers.Should().BeFalse();
    }

    [Fact]
    public void ShieldDashboardData_AnomalyAffectedUsersDisplay_MultipleUsers()
    {
        var data = new ShieldDashboardData { AnomalyAffectedUsers = 3 };
        data.AnomalyAffectedUsersDisplay.Should().Be("3 users affected");
        data.HasAnomalyAffectedUsers.Should().BeTrue();
    }

    [Fact]
    public void ShieldDashboardData_AnomalyAffectedUsersDisplay_OneUser()
    {
        var data = new ShieldDashboardData { AnomalyAffectedUsers = 1 };
        data.AnomalyAffectedUsersDisplay.Should().Be("1 user affected");
        data.HasAnomalyAffectedUsers.Should().BeTrue();
    }

    // ================================================================
    //  ShieldDashboardData — Sparkline
    // ================================================================

    [Fact]
    public void SparklineData_HasActivityTrend_FalseWhenLessThanThreePoints()
    {
        var data = new ShieldDashboardData();
        data.HasActivityTrend.Should().BeFalse();
        data.ActivitySparkline.Add(new SparklinePoint(DateTime.UtcNow.AddHours(-1), 10));
        data.ActivitySparkline.Add(new SparklinePoint(DateTime.UtcNow, 20));
        data.HasActivityTrend.Should().BeFalse(); // 2 points is not enough
    }

    [Fact]
    public void SparklineData_HasActivityTrend_TrueWithThreeOrMorePoints()
    {
        var data = new ShieldDashboardData
        {
            ActivitySparkline = new List<SparklinePoint>
            {
                new(DateTime.UtcNow.AddHours(-2), 5),
                new(DateTime.UtcNow.AddHours(-1), 10),
                new(DateTime.UtcNow, 15)
            }
        };
        data.HasActivityTrend.Should().BeTrue();
    }

    [Fact]
    public void SparklineData_ActivitySparklinePoints_NormalizesToCanvasCoordinates()
    {
        var data = new ShieldDashboardData
        {
            ActivitySparkline = new List<SparklinePoint>
            {
                new(DateTime.UtcNow.AddHours(-2), 0),
                new(DateTime.UtcNow.AddHours(-1), 50),
                new(DateTime.UtcNow, 100)
            }
        };

        var points = data.ActivitySparklinePoints;
        points.Should().HaveCount(3);

        // First point at x=0, last at x=240
        points[0].X.Should().BeApproximately(0, 0.001);
        points[2].X.Should().BeApproximately(240, 0.001);

        // Max value (100) should yield lowest Y (near 0, top of canvas)
        points[2].Y.Should().BeLessThan(points[0].Y);

        // Y is bounded within [0, 40]
        foreach (var pt in points)
        {
            pt.X.Should().BeGreaterThanOrEqualTo(0);
            pt.Y.Should().BeGreaterThanOrEqualTo(0);
            pt.Y.Should().BeLessThanOrEqualTo(40);
        }
    }

    [Fact]
    public void SparklineData_ActivitySparklinePoints_EmptyWhenFewerThanTwoPoints()
    {
        var data = new ShieldDashboardData();
        data.ActivitySparklinePoints.Should().BeEmpty();
    }
}
