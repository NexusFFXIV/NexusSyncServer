using System.Collections.Concurrent;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Holds a freshly issued key for the one page load that shows it.
/// <para>Exists because issuing has to answer with a redirect. Without one, the browser holds
/// a POST it can repeat: F5 issues another key, and the back button issues a third. With one,
/// the plaintext has to survive from the POST to the following GET — and this is the least
/// bad place to keep it.</para>
/// <para><b>Not the query string</b>, which would put a live credential in the address bar,
/// the browser history and every log along the way. <b>Not a cookie</b> either, which would
/// put it on disk. Here it stays in this process, is readable exactly once, and expires in
/// two minutes whether or not anyone reads it.</para>
/// <para>Per-instance and deliberately not shared: behind two replicas a reveal would
/// sometimes land on the instance that does not have it. The cost of that is one lost
/// display and a Renew away from being fixed, which is a better trade than putting live
/// secrets in shared storage to save a click.</para>
/// </summary>
public sealed class RevealStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, (string Key, DateTimeOffset Expires)> mPending = new();

    /// <summary>Stores a key and returns the ticket that redeems it.</summary>
    public Guid Stash(string plaintextKey)
    {
        Sweep();

        var ticket = Guid.NewGuid();
        mPending[ticket] = (plaintextKey, DateTimeOffset.UtcNow.Add(Lifetime));
        return ticket;
    }

    /// <summary>
    /// Returns the key for this ticket and forgets it. Null when unknown, spent or expired.
    /// </summary>
    public string? Redeem(Guid ticket)
    {
        Sweep();

        if (!mPending.TryRemove(ticket, out var entry)) return null;
        return entry.Expires > DateTimeOffset.UtcNow ? entry.Key : null;
    }

    /// <summary>
    /// Drops anything past its moment. Called on every access rather than on a timer: the
    /// dictionary holds live credentials, and one that nobody collected should not sit here
    /// until the next write happens to come along.
    /// </summary>
    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (ticket, entry) in mPending)
        {
            if (entry.Expires <= now) mPending.TryRemove(ticket, out _);
        }
    }
}
