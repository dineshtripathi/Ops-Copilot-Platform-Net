using Microsoft.EntityFrameworkCore;
using OpsCopilot.Prompting.Application.Models;
using OpsCopilot.Prompting.Infrastructure.Persistence;
using OpsCopilot.Prompting.Infrastructure.Repositories;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

public sealed class SqlCanaryStoreTests
{
    private static DbContextOptions<PromptingDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<PromptingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static CanaryState MakeState(
        string key = "test-key",
        int version = 1,
        string content = "v1 prompt",
        int traffic = 20) =>
        new(key, version, content, traffic, DateTimeOffset.UtcNow);

    [Fact]
    public void GetCanary_NotFound_ReturnsNull()
    {
        var store = new SqlCanaryStore(CreateOptions());

        var result = store.GetCanary("missing-key");

        Assert.Null(result);
    }

    [Fact]
    public void SetCanary_NewKey_PersistsAllFields()
    {
        var opts  = CreateOptions();
        var state = MakeState("my-prompt", version: 2, content: "candidate v2", traffic: 30);
        var store = new SqlCanaryStore(opts);

        store.SetCanary("my-prompt", state);
        var found = store.GetCanary("my-prompt");

        Assert.NotNull(found);
        Assert.Equal("my-prompt",    found!.PromptKey);
        Assert.Equal(2,              found.CandidateVersion);
        Assert.Equal("candidate v2", found.CandidateContent);
        Assert.Equal(30,             found.TrafficPercent);
    }

    [Fact]
    public void SetCanary_ExistingKey_UpdatesInPlace()
    {
        var opts  = CreateOptions();
        var store = new SqlCanaryStore(opts);
        store.SetCanary("my-prompt", MakeState("my-prompt", version: 1, content: "v1", traffic: 10));

        store.SetCanary("my-prompt", MakeState("my-prompt", version: 2, content: "v2", traffic: 50));
        var found = store.GetCanary("my-prompt");

        Assert.NotNull(found);
        Assert.Equal(2,    found!.CandidateVersion);
        Assert.Equal("v2", found.CandidateContent);
        Assert.Equal(50,   found.TrafficPercent);
    }

    [Fact]
    public void RemoveCanary_ExistingKey_DeletesRow()
    {
        var opts  = CreateOptions();
        var store = new SqlCanaryStore(opts);
        store.SetCanary("my-prompt", MakeState());

        store.RemoveCanary("my-prompt");

        Assert.Null(store.GetCanary("my-prompt"));
    }

    [Fact]
    public void RemoveCanary_NotFound_DoesNotThrow()
    {
        var store = new SqlCanaryStore(CreateOptions());

        var ex = Record.Exception(() => store.RemoveCanary("ghost-key"));

        Assert.Null(ex);
    }
}
