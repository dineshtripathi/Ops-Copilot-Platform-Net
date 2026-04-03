using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class RegexPiiRedactorTests
{
    private readonly RegexPiiRedactor _sut = new();

    [Fact]
    public void Redact_EmailAddress_ReplacedWithToken()
    {
        var result = _sut.Redact("Contact us at support@example.com for help.");

        Assert.Equal("Contact us at [EMAIL] for help.", result);
    }

    [Fact]
    public void Redact_UsPhoneNumber_ReplacedWithToken()
    {
        var result = _sut.Redact("Call me on 555-123-4567 anytime.");

        Assert.Equal("Call me on [PHONE] anytime.", result);
    }

    [Fact]
    public void Redact_SocialSecurityNumber_ReplacedWithToken()
    {
        var result = _sut.Redact("SSN: 123-45-6789 on file.");

        Assert.Equal("SSN: [SSN] on file.", result);
    }

    [Fact]
    public void Redact_CreditCardNumber_ReplacedWithToken()
    {
        var result = _sut.Redact("Card: 4111-1111-1111-1111 expired.");

        Assert.Equal("Card: [CC] expired.", result);
    }

    [Fact]
    public void Redact_Ipv4Address_ReplacedWithToken()
    {
        var result = _sut.Redact("Request originated from 192.168.1.42 yesterday.");

        Assert.Equal("Request originated from [IP] yesterday.", result);
    }

    [Fact]
    public void Redact_MultiplePiiTypesInOneString_AllReplaced()
    {
        var input = "User john.doe@corp.com dialed 800-555-0199 from 10.0.0.1";

        var result = _sut.Redact(input);

        Assert.DoesNotContain("john.doe@corp.com", result);
        Assert.DoesNotContain("800-555-0199",      result);
        Assert.DoesNotContain("10.0.0.1",          result);
        Assert.Contains("[EMAIL]",  result);
        Assert.Contains("[PHONE]",  result);
        Assert.Contains("[IP]",     result);
    }

    [Fact]
    public void Redact_NoPii_ReturnsSameString()
    {
        const string clean = "Everything looks fine here, no PII at all.";

        var result = _sut.Redact(clean);

        Assert.Equal(clean, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NullOrEmpty_ReturnsInputUnchanged(string? input)
    {
        var result = _sut.Redact(input!);

        Assert.Equal(input, result);
    }
}
