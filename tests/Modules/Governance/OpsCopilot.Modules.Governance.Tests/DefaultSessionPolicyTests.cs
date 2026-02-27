using Moq;
using OpsCopilot.Governance.Application.Configuration;
using OpsCopilot.Governance.Application.Policies;
using OpsCopilot.Governance.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.Governance.Tests;

public sealed class DefaultSessionPolicyTests
{
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public void GetSessionTtl_ReturnsTimeSpanFromResolver()
    {
        var resolver = new Mock<ITenantAwareGovernanceOptionsResolver>();
        resolver.Setup(r => r.Resolve(TenantId))
            .Returns(new ResolvedGovernanceOptions([], TokenBudget: null, SessionTtlMinutes: 45));

        var sut = new DefaultSessionPolicy(resolver.Object);

        var result = sut.GetSessionTtl(TenantId);

        Assert.Equal(TimeSpan.FromMinutes(45), result);
    }
}
