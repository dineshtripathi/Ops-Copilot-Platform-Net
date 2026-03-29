using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using OpenAI;
using OpsCopilot.Rag.Domain;

namespace OpsCopilot.ApiHost.Infrastructure;

internal static class VectorStoreExtensions
{
    private const int DefaultEmbeddingDimensions = 1536;

    internal static IServiceCollection AddVectorStoreInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        RegisterEmbeddingGenerator(services, configuration);
        RegisterCollections(services, configuration);
        return services;
    }

    // ── Embedding generator ────────────────────────────────────────────────────

    private static void RegisterEmbeddingGenerator(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var endpoint = configuration["AI:AzureOpenAI:Endpoint"];
        var deploymentName = configuration["AI:AzureOpenAI:EmbeddingDeploymentName"]
                             ?? "text-embedding-3-small";

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            {
                var client = new Azure.AI.OpenAI.AzureOpenAIClient(
                    new Uri(endpoint),
                    new DefaultAzureCredential());
                return client.GetEmbeddingClient(deploymentName)
                             .AsIEmbeddingGenerator(DefaultEmbeddingDimensions);
            });
        }
        else
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                new NullEmbeddingGenerator());
        }
    }

    // ── Vector store collections ───────────────────────────────────────────────

    private static void RegisterCollections(
        IServiceCollection services,
        IConfiguration configuration)
    {
        if (bool.TryParse(configuration["Rag:UseVectorRunbooks"], out var useRunbooks) && useRunbooks)
        {
            services.AddSingleton<VectorStoreCollection<Guid, VectorRunbookDocument>>(
                new DevInMemoryVectorStoreCollection<Guid, VectorRunbookDocument>());
        }

        if (bool.TryParse(configuration["Rag:UseVectorMemory"], out var useMemory) && useMemory)
        {
            services.AddSingleton<VectorStoreCollection<Guid, IncidentMemoryDocument>>(
                new DevInMemoryVectorStoreCollection<Guid, IncidentMemoryDocument>());
        }
    }

    // ── NullEmbeddingGenerator ─────────────────────────────────────────────────

    private sealed class NullEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public EmbeddingGeneratorMetadata Metadata { get; } =
            new EmbeddingGeneratorMetadata(
                providerName: "null",
                providerUri: null,
                defaultModelId: "null",
                defaultModelDimensions: DefaultEmbeddingDimensions);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values
                .Select(_ => new Embedding<float>(new float[DefaultEmbeddingDimensions]))
                .ToList();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public TService? GetService<TService>(object? key = null) where TService : class => null;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    // ── DevInMemoryVectorStoreCollection ──────────────────────────────────────

    private sealed class DevInMemoryVectorStoreCollection<TKey, TRecord>
        : VectorStoreCollection<TKey, TRecord>
        where TKey : notnull
        where TRecord : class
    {
        private readonly ConcurrentDictionary<TKey, TRecord> _store = new();

        private static readonly PropertyInfo? _keyProperty =
            typeof(TRecord)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p =>
                    p.GetCustomAttribute<VectorStoreKeyAttribute>() != null);

        public override string Name => "dev-in-memory";

        public override Task<bool> CollectionExistsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public override Task EnsureCollectionExistsAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task EnsureCollectionDeletedAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task<TRecord?> GetAsync(
            TKey key,
            RecordRetrievalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(key, out var record);
            return Task.FromResult<TRecord?>(record);
        }

        public override IAsyncEnumerable<TRecord> GetAsync(
            Expression<Func<TRecord, bool>> filter,
            int top,
            FilteredRecordRetrievalOptions<TRecord>? options = null,
            CancellationToken cancellationToken = default)
            => EmptyAsyncEnumerable<TRecord>();

        public override Task<TKey> UpsertAsync(
            TRecord record,
            CancellationToken cancellationToken = default)
        {
            var key = (TKey)(_keyProperty!.GetValue(record)
                ?? throw new InvalidOperationException(
                    $"VectorStoreKey property on {typeof(TRecord).Name} returned null."));
            _store[key] = record;
            return Task.FromResult(key);
        }

        public override Task UpsertAsync(
            IEnumerable<TRecord> records,
            CancellationToken cancellationToken = default)
        {
            foreach (var record in records)
            {
                var key = (TKey)(_keyProperty!.GetValue(record)
                    ?? throw new InvalidOperationException(
                        $"VectorStoreKey property on {typeof(TRecord).Name} returned null."));
                _store[key] = record;
            }

            return Task.CompletedTask;
        }

        public override Task DeleteAsync(
            TKey key,
            CancellationToken cancellationToken = default)
        {
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
            TInput vector,
            int top,
            VectorSearchOptions<TRecord>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Dev stub: no vector search — yield nothing
            await Task.Yield();
            yield break;
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) => null;

        private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
        {
            await Task.Yield();
            yield break;
        }
    }
}
