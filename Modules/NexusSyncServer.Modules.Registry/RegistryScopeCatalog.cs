using NexusKit.Sync.Contracts;
using NexusSyncServer.Hosting.Catalog;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Derives the scope catalogue from the registered contracts.
/// <para>Scopes are computed from each contract's collections rather than stored, which is
/// the same rule the server enforces at request time. A hand-maintained list would drift, and
/// the failure mode of that drift is a picker offering a permission that guards nothing — or
/// worse, omitting one that does.</para>
/// </summary>
internal sealed class RegistryScopeCatalog(IContractRegistry registry) : IScopeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<ScopeOption> Available()
    {
        var options = new List<ScopeOption>();

        foreach (var contractId in registry.ContractIds)
        {
            var versions = registry.VersionsOf(contractId);
            if (versions.Count == 0) continue;

            // The highest version only. Within a major, changes are additive, so the newest
            // minor's collections are a superset of every older one's — listing them all
            // would repeat the same scopes with nothing to tell them apart.
            var contract = registry.Find(contractId, versions.Max());
            if (contract is null) continue;

            // The per-contract half of the built-in read scope. Emitted here rather than
            // special-cased in the picker so it flows through the same selection, the same
            // qualification and the same storage as everything else.
            options.Add(new ScopeOption(
                contractId,
                BuiltInScopes.ReadContracts,
                "this contract's document",
                "pull",
                contract.Collections.Count,
                null,
                null));

            foreach (var collection in contract.Collections)
            {
                options.Add(new ScopeOption(
                    contractId,
                    ContractScopes.For(collection),
                    collection.Name,
                    ContractScopes.VerbFor(collection.Direction),
                    collection.Fields.Count,
                    collection.Retention is { } retention ? DurationText.Format(retention) : null,
                    collection.RateLimit?.PerMinute));
            }
        }

        return options
            .OrderBy(o => o.ContractId, StringComparer.Ordinal)
            .ThenBy(o => o.Scope, StringComparer.Ordinal)
            .ToArray();
    }
}
