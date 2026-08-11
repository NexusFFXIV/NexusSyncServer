namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Someone who may hold API keys.
/// <para>Deliberately almost empty, and deliberately <b>not</b> tied to any one sign-in
/// provider. External identities live in <see cref="AccountIdentityEntity"/>, one row per
/// linked provider, so the same person can sign in with Discord today and XIVAuth tomorrow
/// and still be the same account holding the same keys.</para>
/// <para>What is not stored: no password, no email address. A credential this server never
/// holds cannot leak from it.</para>
/// </summary>
public sealed class AccountEntity
{
    /// <summary>Internal identifier, recorded as the owner of anything this account writes.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name from the most recent sign-in, for the admin view. Not authoritative.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether this account may register contracts and administer other accounts.
    /// <para>Contract registration is operator-only, and that is what removes the entire
    /// quota, tenancy and abuse surface a public registration endpoint would need.</para>
    /// </summary>
    public bool IsOperator { get; set; }

    /// <summary>When the account was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the account was disabled, or null while active. Disabling rejects every key it
    /// holds without deleting the audit trail those keys are part of.
    /// </summary>
    public DateTimeOffset? DisabledAt { get; set; }
}
