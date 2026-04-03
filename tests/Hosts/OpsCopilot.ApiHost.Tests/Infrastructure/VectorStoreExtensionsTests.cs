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

    // ── Slice 150: AzureAISearch backend registration ─────────────────────────

    [Fact]
    public void VectorBackend_AzureAISearch_WithEndpoint_RegistersAzureSearchRunbookCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]              = "",
            ["Rag:UseVectorRunbooks"]                 = "true",
            ["Rag:VectorBackend"]                    = "AzureAISearch",
            ["Rag:AzureAISearch:Endpoint"]           = "https://fake-test.search.windows.net",
            ["Rag:AzureAISearch:RunbooksIndexName"]  = "test-runbooks"
        });

        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();

        Assert.NotNull(collection);
        Assert.Equal("test-runbooks", collection.Name);
    }

    [Fact]
    public void VectorBackend_AzureAISearch_EmptyEndpoint_FallsBackToInMemoryRunbooks()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]    = "",
            ["Rag:UseVectorRunbooks"]       = "true",
            ["Rag:VectorBackend"]          = "AzureAISearch",
            ["Rag:AzureAISearch:Endpoint"] = ""
        });

        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();

        Assert.NotNull(collection);
        Assert.Equal("dev-in-memory", collection.Name);
    }

    [Fact]
    public void VectorBackend_AzureAISearch_WithEndpoint_RegistersAzureSearchMemoryCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]             = "",
            ["Rag:UseVectorMemory"]                  = "true",
            ["Rag:VectorBackend"]                   = "AzureAISearch",
            ["Rag:AzureAISearch:Endpoint"]          = "https://fake-test.search.windows.net",
            ["Rag:AzureAISearch:MemoryIndexName"]   = "test-incident-memory"
        });

        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, IncidentMemoryDocument>>();

        Assert.NotNull(collection);
        Assert.Equal("test-incident-memory", collection.Name);
    }

    [Fact]
    public void VectorBackend_AzureAISearch_DefaultIndexNames_AreUsedWhenNotConfigured()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]    = "",
            ["Rag:UseVectorRunbooks"]       = "true",
            ["Rag:UseVectorMemory"]         = "true",
            ["Rag:VectorBackend"]          = "AzureAISearch",
            ["Rag:AzureAISearch:Endpoint"] = "https://fake-test.search.windows.net"
            // RunbooksIndexName and MemoryIndexName intentionally omitted
        });

        using var sp = services.BuildServiceProvider();
        var runbooks = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();
        var memory   = sp.GetRequiredService<VectorStoreCollection<Guid, IncidentMemoryDocument>>();

        Assert.Equal("opscopilot-runbooks",         runbooks.Name);
        Assert.Equal("opscopilot-incident-memory",  memory.Name);
    }

    // ── Slice 160: Qdrant backend registration ────────────────────────────────

    [Fact]
    public void VectorBackend_Qdrant_WithHost_RegistersQdrantRunbookCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]              = "",
            ["Rag:UseVectorRunbooks"]                 = "true",
            ["Rag:VectorBackend"]                    = "Qdrant",
            ["Rag:Qdrant:Host"]                      = "qdrant.example.com",
            ["Rag:Qdrant:RunbooksCollectionName"]     = "test-runbooks"
        });
        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();
        Assert.NotNull(collection);
        Assert.Equal("test-runbooks", collection.Name);
    }

    [Fact]
    public void VectorBackend_Qdrant_EmptyHost_FallsBackToInMemoryRunbooks()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]  = "",
            ["Rag:UseVectorRunbooks"]     = "true",
            ["Rag:VectorBackend"]        = "Qdrant",
            ["Rag:Qdrant:Host"]          = ""
        });
        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();
        Assert.NotNull(collection);
        Assert.Equal("dev-in-memory", collection.Name);
    }

    [Fact]
    public void VectorBackend_Qdrant_WithHost_RegistersQdrantMemoryCollection()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]          = "",
            ["Rag:UseVectorMemory"]               = "true",
            ["Rag:VectorBackend"]                = "Qdrant",
            ["Rag:Qdrant:Host"]                  = "qdrant.example.com",
            ["Rag:Qdrant:MemoryCollectionName"]   = "test-incident-memory"
        });
        using var sp = services.BuildServiceProvider();
        var collection = sp.GetRequiredService<VectorStoreCollection<Guid, IncidentMemoryDocument>>();
        Assert.NotNull(collection);
        Assert.Equal("test-incident-memory", collection.Name);
    }

    [Fact]
    public void VectorBackend_Qdrant_DefaultCollectionNames_AreUsedWhenNotConfigured()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["AI:AzureOpenAI:Endpoint"]  = "",
            ["Rag:UseVectorRunbooks"]     = "true",
            ["Rag:UseVectorMemory"]       = "true",
            ["Rag:VectorBackend"]        = "Qdrant",
            ["Rag:Qdrant:Host"]          = "qdrant.example.com"
            // collection names intentionally omitted — defaults apply
        });
        using var sp = services.BuildServiceProvider();
        var runbooks = sp.GetRequiredService<VectorStoreCollection<Guid, VectorRunbookDocument>>();
        var memory   = sp.GetRequiredService<VectorStoreCollection<Guid, IncidentMemoryDocument>>();
        Assert.Equal("opscopilot-runbooks",        runbooks.Name);
        Assert.Equal("opscopilot-incident-memory", memory.Name);
    }
}
