namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// One external identity linked to an account — a Discord user, an XIVAuth user, or whatever
/// provider plugin an operator has composed in.
/// <para>A separate table rather than a column on the account, for two reasons. An operator
/// can enable several providers at once and let each user pick, which means an account may
/// carry more than one identity. And adding a provider later must not mean migrating the
/// accounts table — it is a new row shape, not a new column.</para>
/// </summary>
public sealed class AccountIdentityEntity
{
    /// <summary>Internal identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The account this identity signs in to.</summary>
    public Guid AccountId { get; set; }

    /// <summary>The provider's id, e.g. <c>discord</c> or <c>xivauth</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The provider's stable identifier for this user — the <c>sub</c> of the identity.
    /// Stored as text: Discord snowflakes exceed <see cref="long"/> range in ways JSON and
    /// tooling handle badly, and other providers may not use numbers at all.
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>Display name as the provider reported it at the last sign-in.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Avatar URL as the provider reported it, for the admin and portal views.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>When this identity was first linked.</summary>
    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>When it was last used to sign in.</summary>
    public DateTimeOffset LastSignInAt { get; set; }
}
