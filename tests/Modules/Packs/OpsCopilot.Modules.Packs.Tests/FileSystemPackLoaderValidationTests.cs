using OpsCopilot.Packs.Domain.Models;
using OpsCopilot.Packs.Infrastructure;
using Xunit;

namespace OpsCopilot.Modules.Packs.Tests;

/// <summary>
/// Unit tests for <see cref="FileSystemPackLoader.Validate"/> — the internal
/// static validation logic covering the 15 rules defined in the Slice 34
/// specification. Uses real temp directories for file-existence checks.
/// </summary>
public class FileSystemPackLoaderValidationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _packDir;

    public FileSystemPackLoaderValidationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "packs-tests-" + Guid.NewGuid().ToString("N"));
        _packDir = Path.Combine(_tempRoot, "azure-vm");
        Directory.CreateDirectory(_packDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static PackManifest ValidManifest(
        string name = "azure-vm",
        string version = "1.0.0",
        string description = "Operational pack for Azure VM management.",
        IReadOnlyList<string>? resourceTypes = null,
        string minimumMode = "A",
        IReadOnlyList<EvidenceCollector>? evidenceCollectors = null,
        IReadOnlyList<PackRunbook>? runbooks = null,
        IReadOnlyList<PackSafeAction>? safeActions = null) =>
        new(
            name,
            version,
            description,
            resourceTypes ?? new[] { "Microsoft.Compute/virtualMachines" },
            minimumMode,
            evidenceCollectors ?? Array.Empty<EvidenceCollector>(),
            runbooks ?? Array.Empty<PackRunbook>(),
            safeActions ?? Array.Empty<PackSafeAction>());

    private void CreatePackFile(string relativePath, string content = "")
    {
        var fullPath = Path.Combine(_packDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Valid pack — all fields correct, referenced files exist
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ValidPack_ReturnsIsValid()
    {
        CreatePackFile("queries/cpu.kql", "Perf | where CounterName == 'cpu'");
        CreatePackFile("runbooks/restart.md", "# Restart\nRestart the VM.");
        CreatePackFile("actions/restart.json", "{}");

        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "A", "queries/cpu.kql") },
            runbooks: new[] { new PackRunbook("restart-vm", "runbooks/restart.md") },
            safeActions: new[] { new PackSafeAction("restart-svc", "C", "actions/restart.json") });

        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Folder name not kebab-case (Rule 2)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Azure-VM")]
    [InlineData("azure_vm")]
    [InlineData("azure vm")]
    [InlineData("-azure-vm")]
    public void Validate_NonKebabFolderName_ReturnsError(string dirName)
    {
        var manifest = ValidManifest(name: dirName);
        var result = FileSystemPackLoader.Validate(manifest, dirName, _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("kebab-case"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Name does not match directory (Rule 3)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_NameDirectoryMismatch_ReturnsError()
    {
        var manifest = ValidManifest(name: "k8s-basic");
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must match the directory name"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Missing / empty name (Rule 3)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsError(string? name)
    {
        var manifest = ValidManifest(name: name!);
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'name' is required"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Missing version (Rule 4)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingVersion_ReturnsError(string? version)
    {
        var manifest = ValidManifest(version: version!);
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'version' is required"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. Missing description (Rule 5)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingDescription_ReturnsError(string? desc)
    {
        var manifest = ValidManifest(description: desc!);
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'description' is required"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Empty resourceTypes array (Rule 6)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_EmptyResourceTypes_ReturnsError()
    {
        var manifest = ValidManifest(resourceTypes: Array.Empty<string>());
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'resourceTypes' must be a non-empty array"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. Invalid minimumMode (Rule 7)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("X")]
    [InlineData("a")]
    [InlineData("")]
    public void Validate_InvalidMinimumMode_ReturnsError(string mode)
    {
        var manifest = ValidManifest(minimumMode: mode);
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'minimumMode' must be 'A', 'B', or 'C'"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. Invalid evidenceCollectors[].requiredMode (Rule 8)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InvalidEvidenceCollectorRequiredMode_ReturnsError()
    {
        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "Z", null) });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("requiredMode must be 'A', 'B', or 'C'"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. safeActions[].requiresMode not "C" (Rule 9)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_SafeActionRequiresModeNotC_ReturnsError()
    {
        var manifest = ValidManifest(
            safeActions: new[] { new PackSafeAction("restart-svc", "A", null) });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("requiresMode must be 'C'"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Non-kebab-case IDs (Rule 10)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CPU_Usage")]
    [InlineData("Cpu-Usage")]
    [InlineData("-bad-id")]
    public void Validate_NonKebabCaseIds_ReturnsError(string id)
    {
        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector(id, "A", null) });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("kebab-case"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Duplicate IDs within same list (Rule 11)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_DuplicateEvidenceCollectorIds_ReturnsError()
    {
        var manifest = ValidManifest(
            evidenceCollectors: new[]
            {
                new EvidenceCollector("cpu-usage", "A", null),
                new EvidenceCollector("cpu-usage", "B", null)
            });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate") && e.Contains("cpu-usage"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. queryFile referenced but missing on disk (Rule 12)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingQueryFile_ReturnsError()
    {
        // Do NOT create the file — it should be missing
        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "A", "queries/cpu.kql") });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("queryFile") && e.Contains("not found"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. runbooks[].file referenced but missing on disk (Rule 13)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingRunbookFile_ReturnsError()
    {
        var manifest = ValidManifest(
            runbooks: new[] { new PackRunbook("restart-vm", "runbooks/restart.md") });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("file") && e.Contains("not found"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. safeActions[].definitionFile missing on disk (Rule 14)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingDefinitionFile_ReturnsError()
    {
        var manifest = ValidManifest(
            safeActions: new[] { new PackSafeAction("restart-svc", "C", "actions/restart.json") });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("definitionFile") && e.Contains("not found"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. Secrets heuristic — file contains secret marker (Rule 15)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_FileContainsSecretMarker_ReturnsError()
    {
        CreatePackFile("config.kql", "let connectionString = 'Server=...'");

        var manifest = ValidManifest();
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("secret marker") || e.Contains("connectionstring"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 17. Clean files — no secrets detected (Rule 15 negative)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_CleanFiles_NoSecretErrors()
    {
        CreatePackFile("queries/cpu.kql", "Perf | where CounterName == 'cpu'");

        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "A", "queries/cpu.kql") });

        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Contains("secret"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 18. Multiple errors accumulated
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MultipleErrors_AllAccumulated()
    {
        var manifest = new PackManifest(
            "", "", "", Array.Empty<string>(), "X",
            Array.Empty<EvidenceCollector>(),
            Array.Empty<PackRunbook>(),
            Array.Empty<PackSafeAction>());
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        // Expect at least: name required, name/dir mismatch, version required,
        // description required, resourceTypes empty, minimumMode invalid
        Assert.True(result.Errors.Count >= 4,
            $"Expected ≥4 errors but got {result.Errors.Count}: {string.Join("; ", result.Errors)}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 19. Files that exist on disk pass file-existence checks
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ReferencedFilesExist_NoFileErrors()
    {
        CreatePackFile("queries/cpu.kql", "Perf | summarize avg(CounterValue)");
        CreatePackFile("runbooks/restart.md", "# Restart VM");
        CreatePackFile("actions/restart.json", "{ \"type\": \"safe\" }");

        var manifest = ValidManifest(
            evidenceCollectors: new[] { new EvidenceCollector("cpu-usage", "A", "queries/cpu.kql") },
            runbooks: new[] { new PackRunbook("restart-vm", "runbooks/restart.md") },
            safeActions: new[] { new PackSafeAction("restart-svc", "C", "actions/restart.json") });

        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ═══════════════════════════════════════════════════════════════
    // 20. Duplicate runbook IDs detected (Rule 11)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_DuplicateRunbookIds_ReturnsError()
    {
        CreatePackFile("runbooks/a.md", "# A");
        CreatePackFile("runbooks/b.md", "# B");

        var manifest = ValidManifest(
            runbooks: new[]
            {
                new PackRunbook("restart-vm", "runbooks/a.md"),
                new PackRunbook("restart-vm", "runbooks/b.md")
            });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate") && e.Contains("restart-vm"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 21. Duplicate safeAction IDs detected (Rule 11)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_DuplicateSafeActionIds_ReturnsError()
    {
        var manifest = ValidManifest(
            safeActions: new[]
            {
                new PackSafeAction("restart-svc", "C", null),
                new PackSafeAction("restart-svc", "C", null)
            });
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate") && e.Contains("restart-svc"));
    }

    // ═══════════════════════════════════════════════════════════════
    // 22. Valid modes for all three layers
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void Validate_ValidMinimumModes_ReturnsIsValid(string mode)
    {
        var manifest = ValidManifest(minimumMode: mode);
        var result = FileSystemPackLoader.Validate(manifest, "azure-vm", _packDir);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
