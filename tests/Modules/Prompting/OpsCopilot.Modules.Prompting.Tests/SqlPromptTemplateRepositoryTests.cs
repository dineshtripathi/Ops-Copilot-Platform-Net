using Microsoft.EntityFrameworkCore;
using OpsCopilot.Prompting.Domain.Entities;
using OpsCopilot.Prompting.Infrastructure.Persistence;
using OpsCopilot.Prompting.Infrastructure.Repositories;
using Xunit;

namespace OpsCopilot.Modules.Prompting.Tests;

public sealed class SqlPromptTemplateRepositoryTests
{
    private static PromptingDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<PromptingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PromptingDbContext(options);
    }

    [Fact]
    public async Task FindActiveAsync_ActiveTemplate_ReturnsTemplate()
    {
        await using var db = CreateInMemoryDb();
        db.PromptTemplates.Add(PromptTemplate.Create("triage", "System prompt content", version: 1));
        await db.SaveChangesAsync();
        var repo = new SqlPromptTemplateRepository(db);

        var result = await repo.FindActiveAsync("triage");

        Assert.NotNull(result);
        Assert.Equal("triage", result!.PromptKey);
        Assert.Equal("System prompt content", result.Content);
    }

    [Fact]
    public async Task FindActiveAsync_NoTemplate_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var repo = new SqlPromptTemplateRepository(db);

        var result = await repo.FindActiveAsync("triage");

        Assert.Null(result);
    }

    [Fact]
    public async Task FindActiveAsync_InactiveTemplate_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var template = PromptTemplate.Create("triage", "Old prompt", version: 1);
        template.Deactivate();
        db.PromptTemplates.Add(template);
        await db.SaveChangesAsync();
        var repo = new SqlPromptTemplateRepository(db);

        var result = await repo.FindActiveAsync("triage");

        Assert.Null(result);
    }

    [Fact]
    public async Task FindActiveAsync_MultipleVersionsOneActive_ReturnsHighestActive()
    {
        await using var db = CreateInMemoryDb();
        var v1 = PromptTemplate.Create("triage", "v1 content", version: 1);
        v1.Deactivate();
        var v2 = PromptTemplate.Create("triage", "v2 content", version: 2);
        db.PromptTemplates.AddRange(v1, v2);
        await db.SaveChangesAsync();
        var repo = new SqlPromptTemplateRepository(db);

        var result = await repo.FindActiveAsync("triage");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Version);
        Assert.Equal("v2 content", result.Content);
    }

    [Fact]
    public async Task FindActiveAsync_WrongKey_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        db.PromptTemplates.Add(PromptTemplate.Create("triage", "content"));
        await db.SaveChangesAsync();
        var repo = new SqlPromptTemplateRepository(db);

        var result = await repo.FindActiveAsync("chat");

        Assert.Null(result);
    }
}
