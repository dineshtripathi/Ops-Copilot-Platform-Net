using Microsoft.Extensions.AI;
using OpsCopilot.AgentRuns.Application.Abstractions;

namespace OpsCopilot.AgentRuns.Application.Services;

/// <summary>
/// Default context window manager: uses a character/4 heuristic to estimate tokens
/// and drops the oldest non-System messages when the budget is exceeded.
///
/// Token estimate: 1 token ≈ 4 characters (GPT-4 family heuristic).
/// System messages (<see cref="ChatRole.System"/>) are never removed.
/// Oldest User/Assistant/Tool messages are dropped first (index-order removal).
///
/// Slice 200 — §3.15 LLM Context Window Management.
/// </summary>
internal sealed class DefaultContextWindowManager : IContextWindowManager
{
    // Rough heuristic: 1 token ≈ 4 characters (GPT-4 family).
    private const int CharsPerToken = 4;

    public void TrimToTokenBudget(List<ChatMessage> messages, int budgetTokens)
    {
        while (EstimateTotal(messages) > budgetTokens)
        {
            // Find the oldest non-System message and remove it.
            int idx = messages.FindIndex(m => m.Role != ChatRole.System);
            if (idx < 0) break;  // Only System messages remain — cannot trim further.
            messages.RemoveAt(idx);
        }
    }

    private static int EstimateTotal(List<ChatMessage> messages)
    {
        int total = 0;
        foreach (var msg in messages)
            total += EstimateMessage(msg);
        return total;
    }

    private static int EstimateMessage(ChatMessage msg)
    {
        int chars = 0;
        foreach (var item in msg.Contents)
            chars += item.ToString()?.Length ?? 0;
        return chars / CharsPerToken;
    }
}
