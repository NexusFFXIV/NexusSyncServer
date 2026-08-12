using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Catalog;
using NexusSyncServer.Modules.Storage.MariaDb;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Turns the per-key handshake record into the per-version tally the portal shows.
/// <para>Lives here because the table does. What crosses out of this module is counts and scope
/// strings — see <see cref="IClientVersionReport"/> for why nothing richer may.</para>
/// </summary>
public sealed class KeyContractStateReport : IClientVersionReport
{
    private readonly IServiceScopeFactory mScopes;

    /// <summary>Creates the report.</summary>
    public KeyContractStateReport(IServiceScopeFactory scopes) => mScopes = scopes;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContractVersionUsage>> UsageAsync(CancellationToken ct)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        // Joined rather than two queries so a key revoked between them cannot produce a row whose
        // scopes are unknown. Read-only and small — one row per key per contract in use.
        var rows = await db.Set<KeyContractStateEntity>()
            .AsNoTracking()
            .Join(
                db.Set<ApiKeyEntity>().AsNoTracking(),
                state => state.KeyId,
                key => key.Id,
                (state, key) => new
                {
                    state.ContractId,
                    state.NegotiatedMajor,
                    state.NegotiatedMinor,
                    state.SupportedMajor,
                    state.SupportedMinor,
                    key.Scopes,
                    key.RevokedAt,
                })
            .Where(r => r.RevokedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .GroupBy(r => (r.ContractId, r.NegotiatedMajor, r.NegotiatedMinor))
            .Select(group => new ContractVersionUsage(
                group.Key.ContractId,
                group.Key.NegotiatedMajor,
                group.Key.NegotiatedMinor,
                group.Count(),

                // Reported a ceiling strictly above what it settled on. A null ceiling is an older
                // build that never said, and counts as neither — it is unknown, not "cannot".
                group.Count(r =>
                    r.SupportedMajor is { } major
                    && r.SupportedMinor is { } minor
                    && (major, minor).CompareTo((r.NegotiatedMajor, r.NegotiatedMinor)) > 0),

                group
                    .SelectMany(r => r.Scopes)
                    .Select(Bare)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Strips the contract qualifier a stored scope may carry. Scopes are stored as
    /// <c>contract/scope</c> but travel and compare bare, and the consumer of this report matches
    /// them against a contract's own scopes, which are bare by construction.
    /// </summary>
    private static string Bare(string scope) =>
        QualifiedScope.TryParse(scope, out _, out var bare) ? bare : scope;
}
