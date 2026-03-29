using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using OpsCopilot.ApiHost.Infrastructure;
using OpsCopilot.Rag.Domain;
using Xunit;

namespace OpsCopilot.ApiHost.Tests.Infrastructure;

/// <summary>
/// Slice 144 — Unit tests for VectorStore infrastructure DI registration.
/// These tests verify service container shape without hitting real Azure endpoints.
/// </summary>
public sealed class VectorStoreExtensionsTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static IServiceCollection BuildServices(
        Dictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new ServiceCollection()
            .AddVectorStoreInfrastructure(config);
    }

    // ── Embedding generator ───────────────────────────────────────────────────

    [Fact]
    public void NoEndpoint_RegistersNullEmbeddingGenerator()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = ""
        });

        using var sp = services.BuildServiceProvider();
        var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.NotNull(generator);
    }

    [Fact]
    public async Task NullEmbeddingGenerator_ReturnsCorrectDimensions()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = ""
        });

        using var sp = services.BuildServiceProvider();
        var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        var result = await generator.GenerateAsync(["hello", "world"]);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(1536, e.Vector.Length));
    }

    // ── Vector store collections ──────────────────────────────────────────────

    [Fact]
    public void BothFlagsOff_NoVectorCollectionsRegistered()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = "",
            ["Rag:UseVectorRunbooks"] = "false",
            ["Rag:UseVectorMemory"] = "false"
        });

        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<VectorStoreCollection<Guid, VectorRunbookDocument>>());
        Assert.Null(sp.GetService<VectorStoreCollection<Guid, IncidentMemoryDocument>>());
    }

    [Fact]
    public void UseVectorRunbooks_True_RegistersRunbookCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = "",
            ["Rag:UseVectorRunbooks"] = "true",
            ["Rag:UseVectorMemory"] = "false"
        });

        using var sp = services.BuildServiceProvider();

        var collection = sp.GetService<VectorStoreCollection<Guid, VectorRunbookDocument>>();
        Assert.NotNull(collection);
        Assert.Null(sp.GetService<VectorStoreCollection<Guid, IncidentMemoryDocument>>());
    }

    [Fact]
    public void UseVectorMemory_True_RegistersMemoryCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = "",
            ["Rag:UseVectorRunbooks"] = "false",
            ["Rag:UseVectorMemory"] = "true"
        });

        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<VectorStoreCollection<Guid, VectorRunbookDocument>>());
        var collection = sp.GetService<VectorStoreCollection<Guid, IncidentMemoryDocument>>();
        Assert.NotNull(collection);
    }

    // ── DevInMemoryVectorStoreCollection round-trip ───────────────────────────

    [Fact]
    public async Task DevInMemoryCollection_UpsertAndGetRoundTrip()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"] = "",
            ["Rag:UseVectorRunbooks"] = "true"
        });

        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();

        var id = Guid.NewGuid();
        var doc = new VectorRunbookDocument
        {
            Id = id,
            RunbookId = "rk-1",
            Title = "Test Runbook",
            Content = "Restart the service."
        };

        await collection.UpsertAsync(doc);

        var retrieved = await collection.GetAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal("rk-1", retrieved.RunbookId);
        Assert.Equal("Test Runbook", retrieved.Title);
    }
}
