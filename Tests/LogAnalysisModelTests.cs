using SalesforceDebugAnalyzer.Models;
using FluentAssertions;
using Xunit;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Tests for LogAnalysis computed properties: OperationIcon and OperationType.
/// These cover the null-guard fix applied in GH #12 (CS8602 compiler warnings).
///
/// The switch expressions use EntryPoint?.ToLower() which can yield null when
/// EntryPoint is null/empty; each arm now guards with "ep is not null &amp;&amp;" before
/// calling .Contains() to prevent a NullReferenceException.
/// </summary>
public class LogAnalysisModelTests
{
    // =====================================================================
    //  OperationIcon — null / empty EntryPoint falls to default arm
    // =====================================================================

    [Fact]
    public void OperationIcon_NullEntryPoint_ReturnsDefaultPackageIcon()
    {
        // EntryPoint is non-nullable but the switch uses ?. null-propagation;
        // setting it to a reflection-assigned null validates the null guard path.
        var analysis = new LogAnalysis();
        // Simulate null by reflection (property initialised to string.Empty by default)
        typeof(LogAnalysis)
            .GetProperty(nameof(LogAnalysis.EntryPoint))!
            .SetValue(analysis, null);

        analysis.OperationIcon.Should().Be("📦");
    }

    [Fact]
    public void OperationIcon_EmptyEntryPoint_ReturnsDefaultPackageIcon()
    {
        var analysis = new LogAnalysis { EntryPoint = string.Empty };
        analysis.OperationIcon.Should().Be("📦");
    }

    // =====================================================================
    //  OperationIcon — known keyword routing
    // =====================================================================

    [Theory]
    [InlineData("CaseTrigger", "⚡")]
    [InlineData("MyFlow__Interview", "🔄")]
    [InlineData("MyValidation", "✓")]
    [InlineData("SendEmail@future", "🔮")]
    [InlineData("Queueable_Job", "🔮")]
    [InlineData("MyController@AuraEnabled", "🎯")]
    [InlineData("Lightning_Component", "🎯")]
    [InlineData("BatchApexJob", "📊")]
    [InlineData("ScheduledJob", "⏰")]
    [InlineData("MyWebService", "🌐")]
    [InlineData("RESTResource_API", "🌐")]
    [InlineData("VisualForce_Page", "📄")]
    [InlineData("MyTestClass", "🧪")]
    [InlineData("GenericApexClass", "📦")]
    public void OperationIcon_KnownKeywords_ReturnCorrectIcon(string entryPoint, string expectedIcon)
    {
        var analysis = new LogAnalysis { EntryPoint = entryPoint };
        analysis.OperationIcon.Should().Be(expectedIcon);
    }

    [Fact]
    public void OperationIcon_IsCaseInsensitive()
    {
        var upper = new LogAnalysis { EntryPoint = "CASETRIGGER" };
        var lower = new LogAnalysis { EntryPoint = "casetrigger" };
        var mixed = new LogAnalysis { EntryPoint = "CaseTrigger" };

        upper.OperationIcon.Should().Be("⚡");
        lower.OperationIcon.Should().Be("⚡");
        mixed.OperationIcon.Should().Be("⚡");
    }

    // =====================================================================
    //  OperationType — null / empty EntryPoint falls to default arm
    // =====================================================================

    [Fact]
    public void OperationType_NullEntryPoint_ReturnsApexClass()
    {
        var analysis = new LogAnalysis();
        typeof(LogAnalysis)
            .GetProperty(nameof(LogAnalysis.EntryPoint))!
            .SetValue(analysis, null);

        analysis.OperationType.Should().Be("Apex Class");
    }

    [Fact]
    public void OperationType_EmptyEntryPoint_ReturnsApexClass()
    {
        var analysis = new LogAnalysis { EntryPoint = string.Empty };
        analysis.OperationType.Should().Be("Apex Class");
    }

    // =====================================================================
    //  OperationType — known keyword routing
    // =====================================================================

    [Theory]
    [InlineData("CaseTrigger", "Apex Trigger")]
    [InlineData("MyFlow__Interview", "Flow")]
    [InlineData("MyValidation", "Validation Rule")]
    [InlineData("SendEmail@future", "Async Apex")]
    [InlineData("Queueable_Job", "Async Apex")]
    [InlineData("MyController@AuraEnabled", "Lightning")]
    [InlineData("Lightning_Component", "Lightning")]
    [InlineData("BatchApexJob", "Batch Apex")]
    [InlineData("ScheduledJob", "Scheduled Apex")]
    [InlineData("MyWebService", "Web Service")]
    [InlineData("RESTResource_API", "Web Service")]
    [InlineData("VisualForce_Page", "Visualforce")]
    [InlineData("MyTestClass", "Test Class")]
    [InlineData("GenericApexClass", "Apex Class")]
    public void OperationType_KnownKeywords_ReturnCorrectLabel(string entryPoint, string expectedType)
    {
        var analysis = new LogAnalysis { EntryPoint = entryPoint };
        analysis.OperationType.Should().Be(expectedType);
    }

    [Fact]
    public void OperationType_IsCaseInsensitive()
    {
        var upper = new LogAnalysis { EntryPoint = "CASETRIGGER" };
        var lower = new LogAnalysis { EntryPoint = "casetrigger" };

        upper.OperationType.Should().Be("Apex Trigger");
        lower.OperationType.Should().Be("Apex Trigger");
    }

    // =====================================================================
    //  Defaults — ensure LogAnalysis initialises safely
    // =====================================================================

    [Fact]
    public void LogAnalysis_DefaultsAreNonNull()
    {
        var analysis = new LogAnalysis();

        analysis.LogId.Should().BeEmpty();
        analysis.EntryPoint.Should().BeEmpty();
        analysis.DatabaseOperations.Should().NotBeNull();
        analysis.LimitSnapshots.Should().NotBeNull();
        analysis.RootNode.Should().NotBeNull();
        analysis.OperationIcon.Should().NotBeNullOrEmpty();
        analysis.OperationType.Should().NotBeNullOrEmpty();
    }
}
