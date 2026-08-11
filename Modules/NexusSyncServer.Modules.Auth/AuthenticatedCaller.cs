namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Who is making the current request, once their key has been validated.
/// </summary>
/// <param name="AccountId">The owning account, recorded against anything they write.</param>
/// <param name="KeyId">The key prefix, for the audit log. Never the key itself.</param>
/// <param name="ContractId">The contract the key is limited to, or null for any.</param>
/// <param name="Scopes">The scopes this key carries.</param>
/// <param name="IsOperator">Whether the account may register contracts and administer others.</param>
public sealed record AuthenticatedCaller(
    Guid AccountId,
    string KeyId,
    string? ContractId,
    IReadOnlySet<string> Scopes,
    bool IsOperator)
{
    /// <summary>Key used to stash the caller on <c>HttpContext.Items</c>.</summary>
    public const string HttpContextItemKey = "nexussyncserver.caller";

    /// <summary>True when the key carries the given scope.</summary>
    /// <summary>
    /// Whether this caller may perform <paramref name="scope"/> on <paramref name="contractId"/>.
    /// <para>The contract is required rather than optional: a scope without one cannot be
    /// answered correctly on a key that spans several contracts, and an overload that omitted
    /// it would be the easy thing to call by mistake.</para>
    /// </summary>
    public bool HasScope(string contractId, string scope) =>
        QualifiedScope.Grants(Scopes, ContractId, contractId, scope);

    /// <summary>
    /// True when the key may be used with the given contract — either it is unrestricted, or
    /// it names exactly this one.
    /// </summary>
    public bool CanUseContract(string contractId) =>
        ContractId is null || string.Equals(ContractId, contractId, StringComparison.Ordinal);
}
