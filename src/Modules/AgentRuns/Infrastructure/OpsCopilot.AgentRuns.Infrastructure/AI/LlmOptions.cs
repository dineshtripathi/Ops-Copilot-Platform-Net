namespace OpsCopilot.AgentRuns.Infrastructure.AI;

/// <summary>
/// Typed configuration for the <c>AI:</c> section in appsettings.
/// Used to register <see cref="Microsoft.Extensions.AI.IChatClient"/> in DI.
/// </summary>
/// <remarks>
/// Accepted values for <see cref="Provider"/>:
/// <list type="bullet">
///   <item><c>"AzureOpenAI"</c> — Azure OpenAI / Azure AI Foundry endpoint, Managed Identity or API key</item>
///   <item><c>"GitHubModels"</c> — GitHub Models inference endpoint, GitHub PAT</item>
///   <item><c>""</c> or absent — LLM disabled; <see cref="Microsoft.Extensions.AI.IChatClient"/> is not
///     registered and orchestrators degrade gracefully.</item>
/// </list>
/// </remarks>
internal sealed class LlmOptions
{
    public string Provider { get; init; } = "";
    public AzureOpenAiSection AzureOpenAI { get; init; } = new();
    public GitHubModelsSection GitHubModels { get; init; } = new();
}

internal sealed class AzureOpenAiSection
{
    /// <summary>Resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c></summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Deployment / model name (default: <c>gpt-4o-mini</c>).</summary>
    public string DeploymentName { get; init; } = "gpt-4o-mini";

    /// <summary>
    /// Optional API key. When empty, <c>DefaultAzureCredential</c> (Managed Identity) is used.
    /// Sourced from Key Vault or User Secrets — never log this value.
    /// </summary>
    public string ApiKey { get; init; } = "";
}

internal sealed class GitHubModelsSection
{
    public string Endpoint { get; init; } = "https://models.inference.ai.azure.com";
    public string ModelId  { get; init; } = "gpt-4o-mini";

    /// <summary>
    /// GitHub PAT with model inference scope.
    /// Falls back to <c>GITHUB_TOKEN</c> environment variable when blank.
    /// Never log this value.
    /// </summary>
    public string Token { get; init; } = "";
}
