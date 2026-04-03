using Microsoft.Extensions.AI;
using OpsCopilot.AgentRuns.Application.Services;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

/// <summary>
/// Slice 200 — DefaultContextWindowManager unit tests.
///
/// Covers token estimation via chars/4 heuristic, budget enforcement order,
/// and System-message protection. No IO, no mocks — pure unit tests.
/// </summary>
public sealed class ContextWindowManagerTests
{
    private static readonly DefaultContextWindowManager Sut = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Each [Fact] constructs messages whose TextContent length is known so that
    // token estimates are deterministic: budgetTokens = chars / 4.
    //
    // TextContent.ToString() returns the text, so 40 chars → 10 tokens.

    private static ChatMessage SystemMsg(string text)
        => new(ChatRole.System, text);

    private static ChatMessage UserMsg(string text)
        => new(ChatRole.User, text);

    private static ChatMessage AssistantMsg(string text)
        => new(ChatRole.Assistant, text);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UnderBudget_MessagesNotTrimmed()
    {
        // Each message text is 40 chars → 10 tokens each; budget = 50 tokens.
        var messages = new List<ChatMessage>
        {
            UserMsg(new string('A', 40)),      // 10 tokens
            AssistantMsg(new string('B', 40)), // 10 tokens
        };
        // Total: 20 tokens; budget: 50 → nothing should be removed.
        Sut.TrimToTokenBudget(messages, budgetTokens: 50);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void ExactlyAtBudget_MessagesNotTrimmed()
    {
        // 2 messages × 40 chars = 80 chars → 20 tokens; budget = 20.
        var messages = new List<ChatMessage>
        {
            UserMsg(new string('A', 40)),
            AssistantMsg(new string('B', 40)),
        };
        Sut.TrimToTokenBudget(messages, budgetTokens: 20);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void OverBudget_OldestNonSystemMessageDropped()
    {
        // Budget = 10 tokens (40 chars). Two user messages of 40 chars each =
        // 20 tokens total → exceeds budget, oldest non-system dropped.
        var toBeDropped = UserMsg(new string('A', 40));
        var toKeep      = UserMsg(new string('B', 40));

        var messages = new List<ChatMessage> { toBeDropped, toKeep };
        Sut.TrimToTokenBudget(messages, budgetTokens: 10);

        // The oldest user message should have been dropped; the newer one kept.
        Assert.DoesNotContain(toBeDropped, messages);
        Assert.Contains(toKeep, messages);
    }

    [Fact]
    public void SystemMessages_NeverDropped()
    {
        // Only system messages remain; none may be dropped even if over budget.
        var sys1 = SystemMsg(new string('S', 80)); // 20 tokens
        var sys2 = SystemMsg(new string('T', 80)); // 20 tokens

        var messages = new List<ChatMessage> { sys1, sys2 };
        // Budget is 10 tokens but both are System — cannot trim.
        Sut.TrimToTokenBudget(messages, budgetTokens: 10);

        Assert.Equal(2, messages.Count);
        Assert.Contains(sys1, messages);
        Assert.Contains(sys2, messages);
    }

    [Fact]
    public void EmptyList_NoException()
    {
        var messages = new List<ChatMessage>();
        var ex = Record.Exception(() => Sut.TrimToTokenBudget(messages, budgetTokens: 100));
        Assert.Null(ex);
        Assert.Empty(messages);
    }

    [Fact]
    public void MultipleOverBudget_DropsUntilUnderBudget()
    {
        // 5 user messages × 40 chars = 200 chars → 50 tokens.
        // Budget = 10 tokens → need to drop 4 messages to get to 10 tokens.
        var messages = Enumerable.Range(1, 5)
            .Select(i => UserMsg(new string((char)('A' + i), 40)))
            .ToList();

        Sut.TrimToTokenBudget(messages, budgetTokens: 10);

        // Only 1 message (10 tokens) should remain.
        Assert.Single(messages);
    }

    [Fact]
    public void SystemMessages_RetainedWhenUserMessagesDropped()
    {
        // System + 3 user messages; budget forces user messages out first.
        var sys = SystemMsg(new string('S', 40));  // 10 tokens
        var u1  = UserMsg(new string('A', 40));    // 10 tokens
        var u2  = UserMsg(new string('B', 40));    // 10 tokens
        var u3  = UserMsg(new string('C', 40));    // 10 tokens

        var messages = new List<ChatMessage> { sys, u1, u2, u3 };
        // Total: 40 tokens; budget = 20 → drop 2 user messages.
        Sut.TrimToTokenBudget(messages, budgetTokens: 20);

        Assert.Contains(sys, messages);
        Assert.DoesNotContain(u1, messages);
        Assert.DoesNotContain(u2, messages);
        Assert.Contains(u3, messages);
    }
}
