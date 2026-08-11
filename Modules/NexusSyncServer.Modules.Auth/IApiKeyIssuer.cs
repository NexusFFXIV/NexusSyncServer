namespace NexusSyncServer.Modules.Auth;

/// <summary>The plaintext key, returned exactly once.</summary>
/// <param name="Key">The full <c>nxs_…</c> key. Show it to the user now; it cannot be recovered.</param>
/// <param name="KeyId">The lookup prefix, safe to display and log afterwards.</param>
/// <param name="ExpiresAt">When it expires, or null.</param>
public sealed record IssuedApiKey(string Key, string KeyId, DateTimeOffset? ExpiresAt);

/// <summary>
/// Creating, rotating and revoking API keys.
/// </summary>
public interface IApiKeyIssuer
{
    /// <summary>
    /// Issues a key for an account.
    /// <para>The returned plaintext is the only copy that will ever exist — the server stores
    /// its hash and nothing else. A caller that loses it must issue a new key.</para>
    /// </summary>
    /// <param name="accountId">Owning account.</param>
    /// <param name="scopes">Scopes to grant. Should be a subset of what the contract implies.</param>
    /// <param name="contractId">Restrict the key to one contract, or null for any.</param>
    /// <param name="label">Free-text label so the user can tell their keys apart.</param>
    /// <param name="lifetime">Overrides the configured default lifetime.</param>
    /// <param name="ct">Cancels the issuance.</param>
    Task<IssuedApiKey> IssueAsync(
        Guid accountId,
        IReadOnlyCollection<string> scopes,
        string? contractId,
        string? label,
        TimeSpan? lifetime,
        CancellationToken ct);

    /// <summary>Revokes a key. Idempotent — revoking an already-revoked key changes nothing.</summary>
    Task<bool> RevokeAsync(Guid keyId, CancellationToken ct);

    /// <summary>
    /// Sets or brings forward a key's expiry, leaving the secret untouched.
    /// <para>Only ever shortens. Setting an expiry on a key that had none counts as shortening;
    /// pushing an existing one further out does not, and is refused. The reason is that a key
    /// already handed to somebody was handed over with a lifetime attached — quietly extending
    /// it grants access nobody reviewed, and does it to a credential that may already be
    /// somewhere it should not be. Ending one early is always safe; ending it later is a new
    /// decision, and a new key is where that decision belongs.</para>
    /// <para>Takes effect within the authenticator's validation cache window, not instantly.</para>
    /// </summary>
    /// <param name="keyId">The key to change.</param>
    /// <param name="expiresAt">The new expiry. Must be sooner than the current one, if any.</param>
    /// <param name="ct">Cancels the change.</param>
    /// <returns>False when the key is unknown or the change would extend it.</returns>
    Task<bool> ShortenExpiryAsync(Guid keyId, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Replaces a key's secret in place, keeping its label, scopes, contract and expiry.
    /// <para>The old secret stops working the moment this returns — there is no overlap, and
    /// no second row. Usage timestamps are cleared, because they described the secret that no
    /// longer exists.</para>
    /// </summary>
    /// <returns>The new plaintext, or null when the key is unknown or already revoked.</returns>
    Task<string?> RotateAsync(Guid keyId, CancellationToken ct);

    /// <summary>
    /// Replaces a key's scopes and its contract restriction, leaving the secret untouched.
    /// <para>Both directions: permissions may be added as well as removed. Adding to a key
    /// already in circulation widens what an existing secret can reach — worth knowing, and
    /// accepted here because the owner could issue an equally wide key anyway. Where that
    /// matters, revoke and issue instead, so the widening comes with a new secret.</para>
    /// <para>Takes effect within the authenticator's validation cache window.</para>
    /// </summary>
    /// <param name="keyId">The key to change.</param>
    /// <param name="scopes">The new set, replacing the old one entirely.</param>
    /// <param name="contractId">The new restriction, or null for none.</param>
    /// <param name="ct">Cancels the change.</param>
    /// <returns>False when the key is unknown or revoked.</returns>
    /// <exception cref="ArgumentException">A scope is malformed.</exception>
    Task<bool> SetScopesAsync(
        Guid keyId, IReadOnlyCollection<string> scopes, string? contractId, CancellationToken ct);

    /// <summary>Lists an account's keys. Never returns key material.</summary>
    Task<IReadOnlyList<ApiKeyEntity>> ListAsync(Guid accountId, CancellationToken ct);
}
