namespace QueryLantern.Tests;

using System.IO;
using QueryLantern.Security;
using QueryLantern.Services;
using Xunit;

public class ActivityJournalTests : IDisposable
{
    private readonly string _keyFile = Path.Combine(Path.GetTempPath(), $"id_{Guid.NewGuid():N}.key");
    private readonly string _journalFile = Path.Combine(Path.GetTempPath(), $"jrn_{Guid.NewGuid():N}.journal");

    public void Dispose()
    {
        if (File.Exists(_keyFile)) File.Delete(_keyFile);
        if (File.Exists(_journalFile)) File.Delete(_journalFile);
    }

    [Fact]
    public void Identity_Sign_Verifies()
    {
        var id = new IdentityService(_keyFile);
        var data = System.Text.Encoding.UTF8.GetBytes("hello");
        var sig = id.Sign(data);
        Assert.True(id.Verify(data, sig));
        Assert.False(id.Verify(System.Text.Encoding.UTF8.GetBytes("tampered"), sig));
    }

    [Fact]
    public void Journal_Appends_And_Chain_Validates()
    {
        var id = new IdentityService(_keyFile);
        var journal = new ActivityJournal(_journalFile, id);
        journal.Append("query", "SELECT 1");
        journal.Append("write_approved", "DELETE FROM t");

        var entries = journal.ReadAll(out var valid);
        Assert.True(valid);
        Assert.Equal(2, entries.Count);
        Assert.Equal("query", entries[0].Type);
        Assert.Equal("write_approved", entries[1].Type);
    }

    [Fact]
    public void Journal_Detects_Tampering()
    {
        var id = new IdentityService(_keyFile);
        var journal = new ActivityJournal(_journalFile, id);
        journal.Append("query", "SELECT 1");

        // Tamper with the stored entry's payload.
        var lines = File.ReadAllLines(_journalFile);
        var tampered = lines[0].Replace("SELECT 1", "SELECT 2");
        File.WriteAllLines(_journalFile, new[] { tampered });

        var entries = journal.ReadAll(out var valid);
        Assert.False(valid);
    }

    [Fact]
    public void Journal_Chains_To_Previous_Signature()
    {
        var id = new IdentityService(_keyFile);
        var journal = new ActivityJournal(_journalFile, id);
        var first = journal.Append("query", "SELECT 1");
        var second = journal.Append("query", "SELECT 2");
        Assert.NotEqual(first.Sig, second.Sig);
        Assert.Equal(first.Sig, second.PrevSig);
    }
}
