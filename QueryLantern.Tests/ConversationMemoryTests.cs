namespace QueryLantern.Tests;

using QueryLantern.Services;
using Xunit;

/// <summary>
/// Verifies conversation memory records turns and resolves elliptical follow-up questions against the
/// most recent prior turn.
/// </summary>
public class ConversationMemoryTests
{
    [Fact]
    public void First_Question_Is_Not_A_FollowUp()
    {
        var svc = new ConversationMemoryService();
        var resolved = svc.Resolve("How many orders last month?");
        Assert.False(resolved.WasFollowUp);
        Assert.Equal("How many orders last month?", resolved.Resolved);
    }

    [Fact]
    public void FollowUp_Resolves_Against_Previous_Turn()
    {
        var svc = new ConversationMemoryService();
        svc.Record("How many orders last month?", "SELECT ...", "42 rows");
        var resolved = svc.Resolve("What about for the EU region?");
        Assert.True(resolved.WasFollowUp);
        Assert.Contains("previous question", resolved.Resolved);
        Assert.Contains("How many orders last month?", resolved.Resolved);
    }

    [Fact]
    public void Pronoun_It_Is_Replaced_With_Anchor_Question()
    {
        var svc = new ConversationMemoryService();
        svc.Record("total revenue by region", "SELECT ...", "rows");
        var resolved = svc.Resolve("show it by month");
        Assert.Contains("total revenue by region", resolved.Resolved);
        Assert.DoesNotContain(" it ", resolved.Resolved);
    }

    [Fact]
    public void History_Rolls_Over_Max_Turns()
    {
        var svc = new ConversationMemoryService();
        for (var i = 0; i < 15; i++)
        {
            svc.Record($"question {i}", "sql", "r");
        }

        Assert.True(svc.History.Count <= 10);
    }
}
