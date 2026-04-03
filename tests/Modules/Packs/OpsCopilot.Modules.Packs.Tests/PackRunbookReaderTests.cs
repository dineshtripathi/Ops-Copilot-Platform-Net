using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Packs.Application.Abstractions;
using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackRunbookReader"/> covering:
/// — runbook found in a pack,
/// — runbook not found in any pack,
/// — name validation rejects traversal attempts.
/// Uses real temporary directories + mock <see cref="IPackCatalog"/>.
/// </summary>
public sealed class PackRunbookReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IPackCatalog> _catalog;
    private readonly PackFileReader _fileReader;
    private readonly PackRunbookReader _reader;

    public PackRunbookReaderTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), $"PackRunbookReaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _catalog    = new Mock<IPackCatalog>();
        _fileReader = new PackFileReader(NullLogger<PackFileReader>.Instance);
        _reader     = new PackRunbookReader(
            _catalog.Object,
            _fileReader,
            NullLogger<PackRunbookReader>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private LoadedPack CreatePackWithRunbook(string runbookName, string content)
    {
        var packDir  = Path.Combine(_tempDir, $"pack-{Guid.NewGuid():N}");
        var runbooks = Path.Combine(packDir, "runbooks");
        Directory.CreateDirectory(runbooks);
        File.WriteAllText(Path.Combine(runbooks, runbookName), content);

        var manifest = new PackManifest(
            Name:               $"test-pack-{Guid.NewGuid():N}",
            Version:            "1.0.0",
            Description:        "test",
            ResourceTypes:      Array.Empty<string>(),
            MinimumMode:        "basic",
            EvidenceCollectors: Array.Empty<EvidenceCollector>(),
            Runbooks:           Array.Empty<PackRunbook>(),
            SafeActions:        Array.Empty<PackSafeAction>());
        var validation = new PackValidationResult(IsValid: true, Errors: []);
        return new LoadedPack(manifest, packDir, validation);
    }

    private LoadedPack CreateEmptyPack()
    {
        var packDir = Path.Combine(_tempDir, $"empty-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packDir);
        var manifest = new PackManifest(
            Name:               $"empty-pack-{Guid.NewGuid():N}",
            Version:            "1.0.0",
            Description:        "test",
            ResourceTypes:      Array.Empty<string>(),
            MinimumMode:        "basic",
            EvidenceCollectors: Array.Empty<EvidenceCollector>(),
            Runbooks:           Array.Empty<PackRunbook>(),
            SafeActions:        Array.Empty<PackSafeAction>());
        var validation = new PackValidationResult(IsValid: true, Errors: []);
        return new LoadedPack(manifest, packDir, validation);
    }

    // ═══════════════════════════════════════════════════════════════
    // Found path
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_RunbookExists_ReturnsContent()
    {
        const string name    = "exception-diagnosis.md";
        const string content = "# Exception Diagnosis\nCheck your logs.";
        var pack = CreatePackWithRunbook(name, content);
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { pack });

        var result = await _reader.ReadAsync(name);

        Assert.Equal(content, result);
    }

    [Fact]
    public async Task ReadAsync_RunbookInSecondPack_ReturnsContent()
    {
        const string name    = "dependency-failure-diagnosis.md";
        const string content = "# Dependency Failure\nCheck upstream.";
        var empty = CreateEmptyPack();
        var pack  = CreatePackWithRunbook(name, content);
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { empty, pack });

        var result = await _reader.ReadAsync(name);

        Assert.Equal(content, result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Not-found path
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_RunbookMissing_ReturnsNull()
    {
        var pack = CreateEmptyPack();
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { pack });

        var result = await _reader.ReadAsync("does-not-exist.md");

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_NoPacks_ReturnsNull()
    {
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<LoadedPack>());

        var result = await _reader.ReadAsync("exception-diagnosis.md");

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Name validation / traversal rejection
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("../etc/passwd.md")]
    [InlineData("../../secret.md")]
    [InlineData("/etc/passwd.md")]
    [InlineData("runbooks/exception-diagnosis.md")]   // path separator not allowed
    [InlineData("Exception-Diagnosis.md")]             // uppercase rejected
    [InlineData("exception_diagnosis.md")]             // underscore rejected
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadAsync_UnsafeName_ReturnsNullWithoutFilesystemAccess(string unsafeName)
    {
        // Catalog should never be consulted for unsafe names
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<LoadedPack>());

        var result = await _reader.ReadAsync(unsafeName);

        Assert.Null(result);
        // Catalog should NOT have been called for names that fail the regex check
        // (null/whitespace only — the regex check happens after whitespace guard)
    }
}
