using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Turns an external identity into a local account.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Finds the account behind an external identity, creating it on first sign-in.
    /// <para>Also refreshes the stored display name and avatar, so the admin view does not
    /// show what someone was called two years ago.</para>
    /// </summary>
    Task<AccountEntity> ResolveAsync(ExternalIdentity identity, CancellationToken ct);

    /// <summary>Loads an account by id, or null.</summary>
    Task<AccountEntity?> FindAsync(Guid accountId, CancellationToken ct);

    /// <summary>The external identities linked to an account.</summary>
    Task<IReadOnlyList<AccountIdentityEntity>> IdentitiesOfAsync(Guid accountId, CancellationToken ct);
}
