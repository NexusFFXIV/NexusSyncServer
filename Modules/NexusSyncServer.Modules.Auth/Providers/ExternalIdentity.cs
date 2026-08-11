namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// What a provider knows about the person who just signed in.
/// </summary>
/// <param name="Provider">The provider's id, e.g. <c>discord</c>.</param>
/// <param name="Subject">The provider's stable identifier for this user.</param>
/// <param name="DisplayName">Name to show. Advisory — providers let users change it.</param>
/// <param name="AvatarUrl">Avatar to show, if the provider offers one.</param>
/// <param name="Assurances">
/// Provider-specific facts a deployment may want to gate on, as simple flags — for instance
/// XIVAuth's <c>verified_characters</c> or <c>mfa_enabled</c>.
/// <para>Kept as an open set rather than typed properties on purpose: what one provider can
/// assert has no counterpart in another, and forcing them into a shared shape would either
/// invent fields providers cannot fill or bake one provider's model into the seam.</para>
/// </param>
public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string? DisplayName,
    string? AvatarUrl,
    IReadOnlyDictionary<string, bool> Assurances)
{
    /// <summary>True when the provider asserted the named fact.</summary>
    public bool Asserts(string assurance) =>
        Assurances.TryGetValue(assurance, out var value) && value;

    /// <summary>The provider has verified at least one FFXIV character for this user.</summary>
    public const string VerifiedCharacters = "verified_characters";

    /// <summary>The provider reports multi-factor authentication as enabled.</summary>
    public const string MfaEnabled = "mfa_enabled";

    /// <inheritdoc />
    public override string ToString() => $"{Provider}:{Subject}";
}
