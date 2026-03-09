using SalesforceDebugAnalyzer.Models;
using SalesforceDebugAnalyzer.Services;
using FluentAssertions;
using Xunit;

namespace SalesforceDebugAnalyzer.Tests;

/// <summary>
/// Unit tests for PiiScannerService regex patterns and result model.
/// </summary>
public class PiiScannerTests
{
    private readonly PiiScannerService _sut = new();

    // ================================================================
    //  Email detection
    // ================================================================

    [Theory]
    [InlineData("User email: alice@example.com was stored")]
    [InlineData("Contact record contains bob.smith+filter@subdomain.co.uk")]
    [InlineData("FIELD_VALUE|Email|charlie_99@test.io")]
    public void Detects_Email(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Should().ContainSingle(m => m.PiiType == "Email");
    }

    [Theory]
    [InlineData("System event from no-reply@salesforce.com")]
    [InlineData("Callout to api@force.com completed")]
    public void DoesNot_Flag_SalesforceEmail(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Where(m => m.PiiType == "Email").Should().BeEmpty();
    }

    [Fact]
    public void Email_Masking_PreservesLocalPrefix()
    {
        var masked = PiiScannerService.MaskEmail("alice@example.com");

        masked.Should().StartWith("al");
        masked.Should().Contain("@example.com");
        masked.Should().NotContain("alice");
    }

    // ================================================================
    //  SSN detection
    // ================================================================

    [Theory]
    [InlineData("SSN entered: 123-45-6789")]
    [InlineData("customer ssn=234-56-7890 submitted")]
    public void Detects_SSN(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Should().ContainSingle(m => m.PiiType == "SSN" && m.Severity == "High");
    }

    [Theory]
    [InlineData("000-12-3456")]  // SSN starting with 000 is invalid
    [InlineData("666-12-3456")]  // 666 prefix was previously invalid
    [InlineData("12345-6789")]   // wrong format
    public void DoesNot_Flag_InvalidSsn(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Where(m => m.PiiType == "SSN").Should().BeEmpty();
    }

    [Fact]
    public void SSN_Masking_KeepsLastFour()
    {
        var masked = PiiScannerService.MaskSsn("123-45-6789");

        masked.Should().Be("***-**-6789");
    }

    // ================================================================
    //  Credit card detection
    // ================================================================

    [Theory]
    [InlineData("token=4111111111111111 card")]    // Visa test number
    [InlineData("cc: 5412 3456 7890 1234")]         // MC with spaces
    [InlineData("amex: 3714-496353-98431")]          // Amex with dashes
    public void Detects_CreditCard(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Should().Contain(m => m.PiiType == "Credit Card" && m.Severity == "High");
    }

    [Fact]
    public void CreditCard_Masking_KeepsLastFour()
    {
        var masked = PiiScannerService.MaskCard("4111111111111111");

        masked.Should().EndWith("1111");
        masked.Should().StartWith("****");
    }

    // ================================================================
    //  Phone detection
    // ================================================================

    [Theory]
    [InlineData("phone: (555) 867-5309")]
    [InlineData("tel: 555-867-5309")]
    [InlineData("+1 555 867 5309")]
    public void Detects_Phone(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Should().Contain(m => m.PiiType == "Phone" && m.Severity == "Medium");
    }

    [Fact]
    public void Phone_Masking_KeepsLastFour()
    {
        var masked = PiiScannerService.MaskPhone("555-867-5309");

        masked.Should().EndWith("5309");
    }

    // ================================================================
    //  IP address detection
    // ================================================================

    [Theory]
    [InlineData("Client IP: 203.0.113.42")]
    [InlineData("Remote addr=8.8.8.8 seen")]
    public void Detects_PublicIp(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Should().Contain(m => m.PiiType == "IP Address" && m.Severity == "Low");
    }

    [Theory]
    [InlineData("source: 192.168.1.100")]   // private
    [InlineData("addr: 10.0.0.5")]           // private
    [InlineData("loopback: 127.0.0.1")]      // loopback
    public void DoesNot_Flag_PrivateIp(string logLine)
    {
        var result = _sut.Scan(logLine);

        result.Matches.Where(m => m.PiiType == "IP Address").Should().BeEmpty();
    }

    // ================================================================
    //  PiiScanResult model
    // ================================================================

    [Fact]
    public void RiskLevel_IsClean_WhenNoMatches()
    {
        var result = _sut.Scan("SELECT Id FROM Account WHERE Name = 'Test'");

        result.RiskLevel.Should().Be("Clean");
        result.HasPii.Should().BeFalse();
        result.TotalMatches.Should().Be(0);
    }

    [Fact]
    public void RiskLevel_IsHigh_WhenSsnPresent()
    {
        var result = _sut.Scan("ssn value: 234-56-7890");

        result.RiskLevel.Should().Be("High");
        result.HasPii.Should().BeTrue();
    }

    [Fact]
    public void RiskLevel_IsMedium_WhenOnlyEmailPresent()
    {
        var result = _sut.Scan("user@domain.com logged in");

        result.RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void ByType_GroupsMatchesByCategory()
    {
        var lines = new[]
        {
            "email1: alice@domain.com",
            "email2: bob@domain.com",
            "ssn: 234-56-7890"
        };

        var result = _sut.Scan(string.Join("\n", lines));

        var groups = result.ByType.ToList();
        groups.Should().Contain(g => g.Key == "Email");
        groups.Should().Contain(g => g.Key == "SSN");
        groups.First(g => g.Key == "Email").Count().Should().Be(2);
    }

    [Fact]
    public void Context_ContainsRedactedPlaceholder()
    {
        var result = _sut.Scan("Contact email was alice@example.com please call");

        var match = result.Matches.First(m => m.PiiType == "Email");
        match.Context.Should().Contain("[REDACTED]");
        match.Context.Should().NotContain("alice@example.com");
    }

    [Fact]
    public void LineNumber_IsOneIndexed()
    {
        var lines = new[] { "first line", "second with alice@domain.com here" };
        var result = _sut.Scan(string.Join("\n", lines));

        result.Matches.Should().ContainSingle(m => m.PiiType == "Email" && m.LineNumber == 2);
    }

    [Fact]
    public void Scan_EmptyString_ReturnsClean()
    {
        var result = _sut.Scan(string.Empty);

        result.HasPii.Should().BeFalse();
        result.RiskLevel.Should().Be("Clean");
    }

    [Fact]
    public void Scan_LargeLogWithNoMatches_ReturnsClean()
    {
        // Simulate a typical Salesforce log header with no PII
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
            sb.AppendLine($"09:00:0{i % 10}:000 (100000)|CODE_UNIT_STARTED|[1]|SomeClass");
        sb.AppendLine("LIMIT_USAGE_FOR_NS|(default)|SOQL queries: 5 out of 100");

        var result = _sut.Scan(sb.ToString());

        result.HasPii.Should().BeFalse();
    }
}
