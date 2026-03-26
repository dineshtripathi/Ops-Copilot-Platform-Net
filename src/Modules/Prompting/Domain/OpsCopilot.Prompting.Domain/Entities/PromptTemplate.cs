namespace OpsCopilot.Prompting.Domain.Entities;

/// <summary>
/// A versioned system-prompt template identified by a prompt key (e.g. "triage", "chat").
/// Only one template per key should have <see cref="IsActive"/> = true at any time.
/// </summary>
public sealed class PromptTemplate
{
    public Guid   PromptTemplateId { get; private set; }
    public string PromptKey        { get; private set; } = string.Empty;
    public int    Version          { get; private set; }
    public string Content          { get; private set; } = string.Empty;
    public bool   IsActive         { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core required private parameterless ctor
    private PromptTemplate() { }

    public static PromptTemplate Create(string promptKey, string content, int version = 1) => new()
    {
        PromptTemplateId = Guid.NewGuid(),
        PromptKey        = promptKey,
        Version          = version,
        Content          = content,
        IsActive         = true,
        CreatedAt        = DateTimeOffset.UtcNow,
    };

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public PromptTemplate NewVersion(string newContent)
    {
        Deactivate();
        return Create(PromptKey, newContent, Version + 1);
    }
}
