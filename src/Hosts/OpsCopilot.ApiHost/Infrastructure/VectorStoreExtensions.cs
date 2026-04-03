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
using Qdrant.Client;
using Qdrant.Client.Grpc;

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
        var backend        = configuration["Rag:VectorBackend"] ?? "InMemory";
        var useAzureSearch = backend.Equals("AzureAISearch", StringComparison.OrdinalIgnoreCase);
        var useQdrant      = backend.Equals("Qdrant", StringComparison.OrdinalIgnoreCase);

        if (bool.TryParse(configuration["Rag:UseVectorRunbooks"], out var useRunbooks) && useRunbooks)
        {
            VectorStoreCollection<Guid, VectorRunbookDocument> runbookCollection;

            if (useAzureSearch)
            {
                var endpoint  = configuration["Rag:AzureAISearch:Endpoint"];
                var indexName = configuration["Rag:AzureAISearch:RunbooksIndexName"]
                                ?? "opscopilot-runbooks";

                runbookCollection = !string.IsNullOrWhiteSpace(endpoint)
                    ? new AzureAISearchVectorStoreCollection<Guid, VectorRunbookDocument>(
                        new Uri(endpoint), indexName, new DefaultAzureCredential())
                    : new DevInMemoryVectorStoreCollection<Guid, VectorRunbookDocument>();
            }
            else if (useQdrant)
            {
                var host       = configuration["Rag:Qdrant:Host"] ?? "";
                var portStr    = configuration["Rag:Qdrant:Port"];
                var apiKey     = configuration["Rag:Qdrant:ApiKey"];
                var collName   = configuration["Rag:Qdrant:RunbooksCollectionName"] ?? "opscopilot-runbooks";
                int.TryParse(portStr, out var port);
                port = port > 0 ? port : 6334;

                runbookCollection = !string.IsNullOrWhiteSpace(host)
                    ? new QdrantVectorStoreCollection<Guid, VectorRunbookDocument>(
                        new QdrantClient(host: host, port: port, https: false,
                            apiKey: string.IsNullOrEmpty(apiKey) ? null : apiKey),
                        collName)
                    : new DevInMemoryVectorStoreCollection<Guid, VectorRunbookDocument>();
            }
            else
            {
                runbookCollection = new DevInMemoryVectorStoreCollection<Guid, VectorRunbookDocument>();
            }

            services.AddSingleton<VectorStoreCollection<Guid, VectorRunbookDocument>>(runbookCollection);
        }

        if (bool.TryParse(configuration["Rag:UseVectorMemory"], out var useMemory) && useMemory)
        {
            VectorStoreCollection<Guid, IncidentMemoryDocument> memoryCollection;

            if (useAzureSearch)
            {
                var endpoint  = configuration["Rag:AzureAISearch:Endpoint"];
                var indexName = configuration["Rag:AzureAISearch:MemoryIndexName"]
                                ?? "opscopilot-incident-memory";

                memoryCollection = !string.IsNullOrWhiteSpace(endpoint)
                    ? new AzureAISearchVectorStoreCollection<Guid, IncidentMemoryDocument>(
                        new Uri(endpoint), indexName, new DefaultAzureCredential())
                    : new DevInMemoryVectorStoreCollection<Guid, IncidentMemoryDocument>();
            }
            else if (useQdrant)
            {
                var host       = configuration["Rag:Qdrant:Host"] ?? "";
                var portStr    = configuration["Rag:Qdrant:Port"];
                var apiKey     = configuration["Rag:Qdrant:ApiKey"];
                var collName   = configuration["Rag:Qdrant:MemoryCollectionName"] ?? "opscopilot-incident-memory";
                int.TryParse(portStr, out var port);
                port = port > 0 ? port : 6334;

                memoryCollection = !string.IsNullOrWhiteSpace(host)
                    ? new QdrantVectorStoreCollection<Guid, IncidentMemoryDocument>(
                        new QdrantClient(host: host, port: port, https: false,
                            apiKey: string.IsNullOrEmpty(apiKey) ? null : apiKey),
                        collName)
                    : new DevInMemoryVectorStoreCollection<Guid, IncidentMemoryDocument>();
            }
            else
            {
                memoryCollection = new DevInMemoryVectorStoreCollection<Guid, IncidentMemoryDocument>();
            }

            services.AddSingleton<VectorStoreCollection<Guid, IncidentMemoryDocument>>(memoryCollection);
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

    // ── QdrantVectorStoreCollection ────────────────────────────────────────────

    private sealed class QdrantVectorStoreCollection<TKey, TRecord>
        : VectorStoreCollection<TKey, TRecord>
        where TKey : notnull
        where TRecord : class
    {
        private readonly QdrantClient _client;
        private readonly string _collectionName;
        private readonly PropertyInfo _keyProp;
        private readonly PropertyInfo _vectorProp;
        private readonly IReadOnlyList<PropertyInfo> _dataProps;
        private readonly ulong _vectorDimensions;

        public override string Name => _collectionName;

        public QdrantVectorStoreCollection(QdrantClient client, string collectionName)
        {
            _client         = client;
            _collectionName = collectionName;

            var type = typeof(TRecord);

            _keyProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                           .First(p => p.GetCustomAttribute<VectorStoreKeyAttribute>() != null);

            _vectorProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                              .First(p => p.GetCustomAttribute<VectorStoreVectorAttribute>() != null);

            _dataProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.GetCustomAttribute<VectorStoreDataAttribute>() != null)
                             .ToArray();

            var vectorAttr = _vectorProp.GetCustomAttribute<VectorStoreVectorAttribute>()!;
            _vectorDimensions = (ulong)vectorAttr.Dimensions;
        }

        public override async Task<bool> CollectionExistsAsync(CancellationToken ct = default)
            => await _client.CollectionExistsAsync(_collectionName, ct);

        public override async Task EnsureCollectionExistsAsync(CancellationToken ct = default)
        {
            if (!await _client.CollectionExistsAsync(_collectionName, ct))
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = _vectorDimensions, Distance = Distance.Cosine },
                    cancellationToken: ct);
        }

        public override async Task EnsureCollectionDeletedAsync(CancellationToken ct = default)
        {
            if (await _client.CollectionExistsAsync(_collectionName, ct))
                await _client.DeleteCollectionAsync(_collectionName, cancellationToken: ct);
        }

        public override async Task<TRecord?> GetAsync(
            TKey key,
            RecordRetrievalOptions? options = null,
            CancellationToken ct = default)
        {
            var pointId = new PointId { Uuid = ((Guid)(object)key).ToString("D") };
            var results = await _client.RetrieveAsync(
                _collectionName, new[] { pointId },
                withPayload: true, withVectors: false,
                cancellationToken: ct);
            return results.Count == 0 ? null : ToRecord(results[0].Id, results[0].Payload);
        }

        public override IAsyncEnumerable<TRecord> GetAsync(
            Expression<Func<TRecord, bool>> filter,
            int top,
            FilteredRecordRetrievalOptions<TRecord>? options = null,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TRecord>();

        public override async Task<TKey> UpsertAsync(
            TRecord record,
            CancellationToken ct = default)
        {
            await _client.UpsertAsync(_collectionName, [ToPointStruct(record)], cancellationToken: ct);
            return (TKey)_keyProp.GetValue(record)!;
        }

        public override async Task UpsertAsync(
            IEnumerable<TRecord> records,
            CancellationToken ct = default)
        {
            var points = records.Select(ToPointStruct).ToList();
            if (points.Count > 0)
                await _client.UpsertAsync(_collectionName, points, cancellationToken: ct);
        }

        public override async Task DeleteAsync(TKey key, CancellationToken ct = default)
            => await _client.DeleteAsync(
                _collectionName, new[] { Guid.Parse(ToUuid(key)) }, cancellationToken: ct);

        public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
            TInput vector,
            int top,
            VectorSearchOptions<TRecord>? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (vector is not ReadOnlyMemory<float> floatVec)
                yield break;

            var results = await _client.SearchAsync(
                _collectionName, floatVec.ToArray(), limit: (ulong)top, cancellationToken: ct);

            foreach (var r in results)
                yield return new VectorSearchResult<TRecord>(ToRecord(r.Id, r.Payload), r.Score);
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) => null;

        // ── Serialization helpers ──────────────────────────────────────────────

        private string ToUuid(TKey key) => ((Guid)(object)key).ToString("D");

        private PointStruct ToPointStruct(TRecord record)
        {
            var point = new PointStruct
            {
                Id      = new PointId { Uuid = ToUuid((TKey)_keyProp.GetValue(record)!) },
                Vectors = new Vectors { Vector = new Vector { Dense = new DenseVector() } }
            };

            var vecVal = _vectorProp.GetValue(record);
            var floats = vecVal is ReadOnlyMemory<float> m ? m.ToArray() : new float[_vectorDimensions];
            point.Vectors.Vector.Dense.Data.AddRange(floats);

            foreach (var prop in _dataProps)
                point.Payload[prop.Name] = ToQdrantValue(prop.GetValue(record));

            return point;
        }

        private TRecord ToRecord(PointId id, IDictionary<string, Value> payload)
        {
            var record = (TRecord)RuntimeHelpers.GetUninitializedObject(typeof(TRecord));

            _keyProp.SetValue(record, (TKey)(object)Guid.Parse(id.Uuid));

            foreach (var prop in _dataProps)
                if (payload.TryGetValue(prop.Name, out var val))
                    prop.SetValue(record, FromQdrantValue(val, prop.PropertyType));

            return record;
        }

        private static Value ToQdrantValue(object? value) => value switch
        {
            null               => new Value { StringValue = string.Empty },
            string s           => new Value { StringValue = s },
            bool b             => new Value { BoolValue = b },
            int n              => new Value { IntegerValue = n },
            long n             => new Value { IntegerValue = n },
            double d           => new Value { DoubleValue = d },
            DateTimeOffset dto => new Value { StringValue = dto.ToString("O") },
            _                  => new Value { StringValue = value.ToString() ?? string.Empty }
        };

        private static object? FromQdrantValue(Value value, System.Type targetType)
        {
            if (targetType == typeof(string))         return value.StringValue;
            if (targetType == typeof(bool))           return value.BoolValue;
            if (targetType == typeof(int))            return (int)value.IntegerValue;
            if (targetType == typeof(long))           return value.IntegerValue;
            if (targetType == typeof(double))         return value.DoubleValue;
            if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value.StringValue);
            return value.StringValue;
        }
    }
}
