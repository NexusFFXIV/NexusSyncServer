namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// One issued API key.
/// <para><b>The key itself is not here.</b> Only its SHA-256 is stored; the plaintext exists
/// exactly once, in the response that created it. A database dump therefore cannot be replayed
/// as credentials, and "show me my key again" is a question the server is unable to answer —
/// which is the intended answer.</para>
/// </summary>
public sealed class ApiKeyEntity
{
    /// <summary>Internal identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The first 8 characters of the key body, indexed for lookup.
    /// <para>Validation cannot search by hash without hashing every candidate, so a
    /// non-secret prefix narrows it to one row first. Eight characters of a 32-character body
    /// leave 24 unknown — around 2^120 — so this does not meaningfully help an attacker who
    /// has already read the database, and the same prefix is what the UI displays anyway.</para>
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the full key.</summary>
    public required string KeyHash { get; set; }

    /// <summary>The owning account.</summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// The contract this key may be used with, or null for any registered contract.
    /// <para>Scoping a key to one contract limits the blast radius of a leak to that
    /// contract's data — worth doing whenever a plugin only ever talks to one.</para>
    /// </summary>
    public string? ContractId { get; set; }

    /// <summary>
    /// The scopes granted, e.g. <c>reports:push</c>. A subset of what the contract implies:
    /// a user granting less than everything is normal, and a client should treat a missing
    /// scope as a disabled feature rather than an error.
    /// </summary>
    public required List<string> Scopes { get; set; }

    /// <summary>Free-text label the user chose, so several keys are tellable apart.</summary>
    public string? Label { get; set; }

    /// <summary>When the key was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it expires, or null for no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// When it was revoked, or null while valid. Revoked keys are kept rather than deleted —
    /// the audit trail of what that key did stays meaningful only if the key still exists.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Last time the key authenticated successfully.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Client agent seen on the last use. Together with <see cref="LastUsedAt"/> this is what
    /// makes "one build is hammering the API" an answerable question.
    /// </summary>
    public string? LastUsedAgent { get; set; }

    /// <summary>
    /// When this key's secret was last replaced, or null if it never was.
    /// <para>Rotation happens in place: the row keeps its label, scopes, contract and expiry,
    /// and only the secret changes. Issuing a replacement row instead would grow the list by
    /// one every time somebody rotates, which turns a page people visit to recover access
    /// into a graveyard.</para>
    /// <para>The cost, and it is real: once rotated, the row's usage history covers both
    /// secrets. If the old one leaked, its activity can no longer be told apart from the
    /// new one's. Separate rows would preserve that distinction — this trades it for a list
    /// that stays readable, which is the right trade for a grant that did not change.</para>
    /// </summary>
    public DateTimeOffset? RotatedAt { get; set; }
}
