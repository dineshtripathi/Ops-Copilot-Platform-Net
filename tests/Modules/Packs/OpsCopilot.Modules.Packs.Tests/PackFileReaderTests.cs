using Microsoft.Extensions.Logging.Abstractions;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="PackFileReader"/> path-traversal guard and file reading.
/// Uses real temporary directories for deterministic filesystem assertions.
/// </summary>
public sealed class PackFileReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackFileReader _reader;

    public PackFileReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PackFileReaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _reader = new PackFileReader(NullLogger<PackFileReader>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private string CreatePackFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return relativePath;
    }

    // ═══════════════════════════════════════════════════════════════
    // Empty / null parameters
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null, "file.md")]
    [InlineData("", "file.md")]
    [InlineData("   ", "file.md")]
    public async Task ReadFileAsync_EmptyPackPath_ReturnsNull(string? packPath, string relativePath)
    {
        var result = await _reader.ReadFileAsync(packPath!, relativePath);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("somepath", null)]
    [InlineData("somepath", "")]
    [InlineData("somepath", "   ")]
    public async Task ReadFileAsync_EmptyRelativePath_ReturnsNull(string packPath, string? relativePath)
    {
        var result = await _reader.ReadFileAsync(packPath, relativePath!);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Path traversal rejection
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\windows\\system32\\config")]
    [InlineData("subdir/../../escape.txt")]
    public async Task ReadFileAsync_TraversalPath_ReturnsNull(string relativePath)
    {
        var result = await _reader.ReadFileAsync(_tempDir, relativePath);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Absolute path rejection
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadFileAsync_AbsolutePath_ReturnsNull()
    {
        var absolutePath = Path.Combine(_tempDir, "runbooks", "run.md");

        var result = await _reader.ReadFileAsync(_tempDir, absolutePath);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Missing file
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadFileAsync_FileDoesNotExist_ReturnsNull()
    {
        var result = await _reader.ReadFileAsync(_tempDir, "nonexistent.md");

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Valid reads
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadFileAsync_ValidFile_ReturnsContent()
    {
        const string expected = "# Runbook\nRestart the VM.";
        CreatePackFile("runbooks/restart.md", expected);

        var result = await _reader.ReadFileAsync(_tempDir, "runbooks/restart.md");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ReadFileAsync_NestedSubdirectory_ReturnsContent()
    {
        const string expected = "SELECT * FROM table";
        CreatePackFile("queries/kql/cpu.kql", expected);

        var result = await _reader.ReadFileAsync(_tempDir, "queries/kql/cpu.kql");

        Assert.Equal(expected, result);
    }
}
