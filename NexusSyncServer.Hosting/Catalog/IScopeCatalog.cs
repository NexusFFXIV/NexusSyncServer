namespace NexusSyncServer.Hosting.Catalog;

/// <summary>
/// One permission a key can be granted, and what it belongs to.
/// </summary>
/// <param name="ContractId">The contract that declares it.</param>
/// <param name="Scope">The scope string itself, e.g. <c>reports:push</c>.</param>
/// <param name="Collection">The collection it guards.</param>
/// <param name="Verb"><c>push</c> or <c>pull</c>, following the collection's direction.</param>
/// <param name="FieldCount">How many fields the collection declares.</param>
/// <param name="Retention">How long records are kept, e.g. <c>90d</c>. Null means forever.</param>
/// <param name="PerMinute">Write budget per minute, counted in records. Null means unlimited.</param>
/// <remarks>
/// Everything past <paramref name="Verb"/> exists so a picker can say what granting this
/// actually does. <c>observations:push</c> tells a reader nothing on its own — "the plugin
/// sends observations, 6 fields, kept 90 days, up to 60 records a minute" tells them whether
/// they want it. All of it comes from the contract, so it cannot drift from what is enforced.
/// </remarks>
public sealed record ScopeOption(
    string ContractId,
    string Scope,
    string Collection,
    string Verb,
    int FieldCount,
    string? Retention,
    int? PerMinute);

/// <summary>Scopes that belong to no contract.</summary>
public static class BuiltInScopes
{
    /// <summary>
    /// Reads contract documents. Granted globally as <c>contract:pull</c>, or for one contract
    /// as <c>example.showcase/contract:pull</c>.
    /// <para>Lives here rather than beside the other auth code because both the module that
    /// enforces it and the module that offers it need to name it, and neither may depend on
    /// the other.</para>
    /// </summary>
    public const string ReadContracts = "contract:pull";
}

/// <summary>
/// What scopes exist on this server, so a permission picker can offer them instead of asking
/// somebody to type them.
/// <para>Deliberately narrow, and deliberately here rather than on the registry. A module that
/// issues keys needs to know <i>which permissions exist</i>; it does not need contract
/// documents, version negotiation or registration. Putting the whole registry interface in
/// front of it would tie key issuance to a module it has no other reason to depend on, and
/// the two would then have to ship together.</para>
/// <para>Only strings cross this boundary, so this assembly stays free of the contract model
/// as well. The provider translates; the consumer renders.</para>
/// <para>Nothing is required to implement it. A server composed without a registry simply has
/// no catalogue, and a picker should fall back to free text rather than offering an empty
/// list — see <c>NexusApiKeyManager</c>.</para>
/// </summary>
public interface IScopeCatalog
{
    /// <summary>
    /// Every grantable scope, in a stable order.
    /// <para>A snapshot: contracts can be registered while the server runs, so this is asked
    /// per render rather than cached by the caller.</para>
    /// </summary>
    IReadOnlyList<ScopeOption> Available();
}
