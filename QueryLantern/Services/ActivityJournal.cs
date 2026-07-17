namespace QueryLantern.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QueryLantern.Security;

/// <summary>
/// An append-only, tamper-evident journal of agent activity (queries run, writes approved or
/// rejected). Each entry is signed with the local Ed25519 identity and chained to the previous
/// entry's signature, so any modification of history invalidates the chain on verification.
/// </summary>
public sealed class ActivityJournal
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IdentityService _identity;
    private byte[] _lastSignature = Array.Empty<byte>();

    public ActivityJournal(string path, IdentityService identity)
    {
        _path = path;
        _identity = identity;
    }

    public sealed record JournalEntry(long Seq, DateTime Timestamp, string Type, string Payload, string PrevSig, string Sig);

    /// <summary>
    /// Appends an entry and returns the signed record. Thread safe; persists immediately.
    /// </summary>
    public JournalEntry Append(string type, string payload)
    {
        lock (_gate)
        {
            var seq = NextSeq();
            var ts = DateTime.UtcNow;
            var prev = Convert.ToBase64String(_lastSignature);
            var signed = SignInput(prev, seq, ts, type, payload);
            var sig = _identity.Sign(signed);
            var entry = new JournalEntry(seq, ts, type, payload, prev, Convert.ToBase64String(sig));
            File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n");
            _lastSignature = sig;
            return entry;
        }
    }

    /// <summary>
    /// Returns all entries, verifying the signature chain. Returns false in the out param if the
    /// chain is broken (tampering detected).
    /// </summary>
    public IReadOnlyList<JournalEntry> ReadAll(out bool chainValid)
    {
        chainValid = true;
        var entries = new List<JournalEntry>();
        if (!File.Exists(_path)) return entries;

        byte[] prevSig = Array.Empty<byte>();
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<JournalEntry>(line)
                ?? throw new InvalidOperationException("Corrupt journal line.");
            var signed = SignInput(entry.PrevSig, entry.Seq, entry.Timestamp, entry.Type, entry.Payload);
            if (!_identity.Verify(signed, Convert.FromBase64String(entry.Sig)) ||
                Convert.ToBase64String(prevSig) != entry.PrevSig)
            {
                chainValid = false;
            }
            entries.Add(entry);
            prevSig = Convert.FromBase64String(entry.Sig);
        }

        return entries;
    }

    private long NextSeq()
    {
        if (!File.Exists(_path)) return 1;
        var last = File.ReadAllLines(_path).LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (last is null) return 1;
        var entry = JsonSerializer.Deserialize<JournalEntry>(last)!;
        return entry.Seq + 1;
    }

    private static byte[] SignInput(string prevSig, long seq, DateTime ts, string type, string payload)
    {
        var sb = new StringBuilder();
        sb.Append(prevSig).Append('|').Append(seq).Append('|')
          .Append(ts.ToString("O")).Append('|').Append(type).Append('|').Append(payload);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
