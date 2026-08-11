using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusSyncServer.Modules.Storage.MariaDb.Records;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Database-backed registry with an in-memory snapshot for reads.
/// </summary>
public sealed class ContractRegistry : IContractRegistry
{
    private readonly IServiceScopeFactory mScopes;
    private readonly ILogger<ContractRegistry> mLog;

    // Replaced wholesale on refresh rather than mutated. Readers therefore never see a
    // half-updated map and need no lock — the cost is copying a dictionary that holds a
    // handful of entries and changes about once a release.
    private volatile ImmutableDictionary<string, ImmutableSortedDictionary<ContractVersion, SyncContract>> mSnapshot =
        ImmutableDictionary<string, ImmutableSortedDictionary<ContractVersion, SyncContract>>.Empty;

    /// <summary>Creates the registry.</summary>
    public ContractRegistry(IServiceScopeFactory scopes, ILogger<ContractRegistry> log)
    {
        mScopes = scopes;
        mLog = log;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ContractIds =>
        mSnapshot.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<ContractVersion> VersionsOf(string contractId) =>
        mSnapshot.TryGetValue(contractId, out var versions) ? versions.Keys.ToArray() : [];

    /// <inheritdoc />
    public SyncContract? Negotiate(string contractId, ContractVersion wanted)
    {
        if (!mSnapshot.TryGetValue(contractId, out var versions)) return null;

        SyncContract? best = null;

        foreach (var (version, contract) in versions)
        {
            if (version.Major != wanted.Major) continue;

            // Highest registered minor within the major — not the client's minor. A client on
            // 1.4 against a server on 1.2 gets 1.2 and must not use anything newer; a client
            // on 1.0 against a server on 1.2 gets 1.2, which is safe because minors are
            // additive by the compatibility rules.
            if (best is null || version > best.Version) best = contract;
        }

        return best;
    }

    /// <inheritdoc />
    public SyncContract? Find(string contractId, ContractVersion version) =>
        mSnapshot.TryGetValue(contractId, out var versions) && versions.TryGetValue(version, out var contract)
            ? contract
            : null;

    /// <inheritdoc />
    public ContractDescriptor? Describe(string contractId, ContractVersion? version = null)
    {
        if (!mSnapshot.TryGetValue(contractId, out var versions) || versions.IsEmpty) return null;

        var selected = version is { } v
            ? versions.TryGetValue(v, out var exact) ? exact : null
            : versions[versions.Keys.Last()];

        if (selected is null) return null;

        return new ContractDescriptor(
            contractId,
            versions.Keys.ToArray(),
            selected.Version,
            selected.CanonicalJson,
            selected.Hash);
    }

    /// <inheritdoc />
    public async Task<RegistrationResult> RegisterAsync(SyncContract contract, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contract);

        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var existingRows = await db.Set<RegisteredContractEntity>()
            .Where(c => c.ContractId == contract.ContractId)
            .ToListAsync(ct).ConfigureAwait(false);

        var sameVersion = existingRows.FirstOrDefault(
            c => c.Major == contract.Version.Major && c.Minor == contract.Version.Minor);

        if (sameVersion is not null)
        {
            if (string.Equals(sameVersion.Hash, contract.Hash, StringComparison.Ordinal))
                return new RegistrationResult(RegistrationStatus.Unchanged, contract);

            // A registered version is immutable. Clients have negotiated against it and cached
            // its shape; quietly swapping the document underneath them would produce
            // validation failures nobody could trace back to a re-registration.
            return new RegistrationResult(
                RegistrationStatus.Conflict,
                Problems:
                [
                    $"{contract.ContractId} {contract.Version} is already registered with a different document "
                    + $"(registered {sameVersion.Hash}, offered {contract.Hash}). Publish a new minor version instead.",
                ]);
        }

        // Compatibility is judged against the highest registered minor of the same major —
        // that is the one peers are actually using.
        var predecessor = existingRows
            .Where(c => c.Major == contract.Version.Major)
            .OrderByDescending(c => c.Minor)
            .Select(c => ContractJson.Parse(c.CanonicalJson))
            .FirstOrDefault();

        if (predecessor is not null)
        {
            var compatibility = ContractCompatibility.Check(predecessor, contract);
            if (!compatibility.IsCompatible)
                return new RegistrationResult(RegistrationStatus.Incompatible, Problems: compatibility.BreakingChanges);
        }

        db.Add(new RegisteredContractEntity
        {
            ContractId = contract.ContractId,
            Major = contract.Version.Major,
            Minor = contract.Version.Minor,
            CanonicalJson = contract.CanonicalJson,
            Hash = contract.Hash,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Indexes after the row, not before: an index without a registered contract is
        // orphaned state nobody would think to clean up.
        var store = scope.ServiceProvider.GetRequiredService<IRecordStore>();
        foreach (var collection in contract.Collections)
            await store.EnsureIndexesAsync(contract.ContractId, collection, ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);

        mLog.LogInformation(
            "Registered contract {Contract} {Version} ({Hash})",
            contract.ContractId, contract.Version, contract.Hash);

        return new RegistrationResult(RegistrationStatus.Registered, contract);
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var rows = await db.Set<RegisteredContractEntity>()
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableSortedDictionary<ContractVersion, SyncContract>>(
            StringComparer.Ordinal);

        foreach (var group in rows.GroupBy(r => r.ContractId, StringComparer.Ordinal))
        {
            var versions = ImmutableSortedDictionary.CreateBuilder<ContractVersion, SyncContract>();

            foreach (var row in group)
            {
                try
                {
                    versions[new ContractVersion(row.Major, row.Minor)] = ContractJson.Parse(row.CanonicalJson);
                }
                catch (ContractDefinitionException ex)
                {
                    // A stored document that no longer parses means this build is older than
                    // the one that wrote it, or the row was edited by hand. Skipping one
                    // version keeps the rest of the server serving; failing startup over it
                    // would take everything down for one bad row.
                    mLog.LogError(
                        ex, "Registered contract {Contract} {Major}.{Minor} could not be parsed and was skipped.",
                        row.ContractId, row.Major, row.Minor);
                }
            }

            if (versions.Count > 0) builder[group.Key] = versions.ToImmutable();
        }

        mSnapshot = builder.ToImmutable();
    }
}
