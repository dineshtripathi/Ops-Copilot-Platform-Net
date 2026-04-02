using Microsoft.Extensions.AI;

namespace OpsCopilot.AgentRuns.Application.Abstractions;

/// <summary>
/// Trims a conversation's message list so it stays within a token budget
/// before each LLM call. Prevents context-window overflow in multi-turn loops.
/// Slice 200 — §3.15 LLM Context Window Management.
/// </summary>
public interface IContextWindowManager
{
    /// <summary>
    /// Trims <paramref name="messages"/> in-place so total estimated token count
    /// is at or below <paramref name="budgetTokens"/>.
    /// System messages are never removed. Oldest non-System messages are dropped first.
    /// </summary>
    void TrimToTokenBudget(List<ChatMessage> messages, int budgetTokens);
}
