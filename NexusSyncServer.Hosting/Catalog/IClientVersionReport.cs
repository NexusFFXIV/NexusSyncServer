namespace NexusSyncServer.Hosting.Catalog;

/// <summary>
/// How many peers sit on one version of one contract.
/// </summary>
/// <param name="ContractId">The contract.</param>
/// <param name="Major">Major of the version they negotiated.</param>
/// <param name="Minor">Minor of the version they negotiated.</param>
/// <param name="Clients">How many keys last handshook at this version.</param>
/// <param name="CouldGoHigher">
/// Of those, how many reported a ceiling above it — they have simply not moved up yet. The rest
/// either cannot, or are old enough not to say. Separating the two is the entire reason this report
/// exists: without it, a version with peers on it looks the same whether they are stuck or merely
/// slow, and only one of those is safe to retire.
/// </param>
/// <param name="Scopes">
/// The union of the bare scopes carried by the keys in this row, e.g. <c>observations:push</c>.
/// <para>A union rather than a per-key breakdown, which makes it an approximation — but one that
/// errs consistently: more roles mean more ways to be blocked, so a version looks harder to retire
/// than it is rather than easier. That is the safe direction for the decision this informs.</para>
/// </param>
public sealed record ContractVersionUsage(
    string ContractId,
    int Major,
    int Minor,
    int Clients,
    int CouldGoHigher,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Who is still speaking which version, so an operator can see when an old one is safe to retire
/// instead of guessing.
/// <para>Deliberately here rather than on the auth module, and deliberately narrow. The module that
/// authenticates keys is the one that knows what they did; the module that displays contracts is the
/// one that knows what a version means. Neither may depend on the other, so only strings and counts
/// cross this boundary — the provider aggregates, the consumer renders.</para>
/// <para>Nothing is required to implement it. A server composed without the auth module simply has
/// no report, and the page should omit the section rather than show an empty one — the same posture
/// as <see cref="IScopeCatalog"/>.</para>
/// </summary>
public interface IClientVersionReport
{
    /// <summary>
    /// Every contract and version peers were last seen on, in no particular order.
    /// <para>A snapshot taken per call: peers handshake continuously, so a cached answer would age
    /// into a wrong one exactly when somebody is deciding whether to retire something.</para>
    /// </summary>
    Task<IReadOnlyList<ContractVersionUsage>> UsageAsync(CancellationToken ct);
}
