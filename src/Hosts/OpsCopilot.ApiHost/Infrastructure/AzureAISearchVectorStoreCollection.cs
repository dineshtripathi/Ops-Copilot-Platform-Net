using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.VectorData;

// Disambiguate: Azure.Search.Documents.Models.VectorSearchOptions (no generic)
// vs Microsoft.Extensions.VectorData.VectorSearchOptions<TRecord> (generic)
using AzureVectorSearchOptions = Azure.Search.Documents.Models.VectorSearchOptions;

namespace OpsCopilot.ApiHost.Infrastructure;

/// <summary>
/// Slice 150 — Production vector store collection backed by Azure AI Search.
/// Connects via managed identity (DefaultAzureCredential).
/// The Azure AI Search index must be pre-created by the operator with fields
/// matching the camelCase property names of TRecord.
/// </summary>
internal sealed class AzureAISearchVectorStoreCollection<TKey, TRecord>
    : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly SearchClient      _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly string            _indexName;
    private readonly string            _keyFieldName;
    private readonly string            _vectorFieldName;
    private readonly PropertyInfo      _keyProperty;
    private readonly IReadOnlyList<string> _dataFieldNames; // excludes vector field

    internal AzureAISearchVectorStoreCollection(
        Uri             endpoint,
        string          indexName,
        TokenCredential credential)
    {
        _indexName = indexName;

        // Configure serializer: camelCase naming matches Azure Search index conventions
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy    = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var clientOptions = new SearchClientOptions
        {
            Serializer = new JsonObjectSerializer(jsonOptions)
        };

        _searchClient = new SearchClient(endpoint, indexName, credential, clientOptions);
        _indexClient  = new SearchIndexClient(endpoint, credential);

        // Reflect on TRecord to locate key and vector properties
        var properties = typeof(TRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        _keyProperty = properties.FirstOrDefault(
            p => p.GetCustomAttribute<VectorStoreKeyAttribute>() != null)
            ?? throw new InvalidOperationException(
                $"{typeof(TRecord).Name} has no [VectorStoreKey] property.");

        var vectorProperty = properties.FirstOrDefault(
            p => p.GetCustomAttribute<VectorStoreVectorAttribute>() != null)
            ?? throw new InvalidOperationException(
                $"{typeof(TRecord).Name} has no [VectorStoreVector] property.");

        _keyFieldName    = ToCamelCase(_keyProperty.Name);
        _vectorFieldName = ToCamelCase(vectorProperty.Name);

        // Data fields used in Select (omit vector field to avoid large payloads)
        _dataFieldNames = properties
            .Where(p => p != vectorProperty)
            .Select(p => ToCamelCase(p.Name))
            .ToList();
    }

    // ── Name ──────────────────────────────────────────────────────────────────

    public override string Name => _indexName;

    // ── Index existence ───────────────────────────────────────────────────────

    public override async Task<bool> CollectionExistsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexClient.GetIndexAsync(_indexName, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Azure AI Search indexes must be pre-created by the operator.
    /// This method is a no-op; use the Azure Portal or azd infra to create the index.
    /// </summary>
    public override Task EnsureCollectionExistsAsync(
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task EnsureCollectionDeletedAsync(
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // ── Get ───────────────────────────────────────────────────────────────────

    public override async Task<TRecord?> GetAsync(
        TKey key,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _searchClient.GetDocumentAsync<TRecord>(
                key.ToString()!, null, cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public override IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "AzureAISearchVectorStoreCollection does not support arbitrary LINQ filter expressions. " +
            "Use SearchAsync for vector similarity queries.");

    // ── Upsert ────────────────────────────────────────────────────────────────

    public override async Task<TKey> UpsertAsync(
        TRecord record,
        CancellationToken cancellationToken = default)
    {
        await _searchClient.MergeOrUploadDocumentsAsync(
            new[] { record }, null, cancellationToken);

        return (TKey)(_keyProperty.GetValue(record)
            ?? throw new InvalidOperationException(
                $"VectorStoreKey property on {typeof(TRecord).Name} returned null."));
    }

    public override async Task UpsertAsync(
        IEnumerable<TRecord> records,
        CancellationToken cancellationToken = default)
    {
        await _searchClient.MergeOrUploadDocumentsAsync(records, null, cancellationToken);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public override async Task DeleteAsync(
        TKey key,
        CancellationToken cancellationToken = default)
    {
        await _searchClient.DeleteDocumentsAsync(
            _keyFieldName, new[] { key.ToString()! }, null, cancellationToken);
    }

    // ── Vector search ─────────────────────────────────────────────────────────

    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput vector,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        float[]? queryVector = vector switch
        {
            ReadOnlyMemory<float> rom => rom.ToArray(),
            float[] arr              => arr,
            _                        => null
        };

        if (queryVector is null) yield break;

        var vectorQuery = new VectorizedQuery(queryVector)
        {
            KNearestNeighborsCount = top
        };
        vectorQuery.Fields.Add(_vectorFieldName);

        var searchOptions = new SearchOptions { Size = top };
        searchOptions.VectorSearch = new AzureVectorSearchOptions();
        searchOptions.VectorSearch.Queries.Add(vectorQuery);

        // Exclude vector field: reduces payload and avoids ReadOnlyMemory<float> deserialization
        foreach (var field in _dataFieldNames)
            searchOptions.Select.Add(field);

        var response = await _searchClient.SearchAsync<TRecord>(
            string.Empty, searchOptions, cancellationToken);

        await foreach (var hit in response.Value.GetResultsAsync()
            .WithCancellation(cancellationToken))
        {
            if (hit.Document is not null)
                yield return new VectorSearchResult<TRecord>(
                    hit.Document, (float)(hit.Score ?? 0d));
        }
    }

    // ── Service locator ───────────────────────────────────────────────────────

    public override object? GetService(Type serviceType, object? serviceKey = null) => null;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
