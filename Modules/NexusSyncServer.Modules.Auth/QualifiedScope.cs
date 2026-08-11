namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Scope strings that name the contract they belong to: <c>example.showcase/reports:push</c>.
/// <para>A bare <c>reports:push</c> is only unambiguous inside one contract, and two contracts
/// may each declare a collection called <c>reports</c>. That was harmless while every key was
/// pinned to a single contract. It stops being harmless the moment one key spans several —
/// which it must, because a plugin typically offers one field to paste a token into.</para>
/// <para><b>Qualification is a storage and enforcement concern only.</b> The wire keeps bare
/// scopes: the handshake answers with the scopes of the contract being negotiated, which is
/// what a client compares against its own contract. Publishing qualified scopes there would
/// break every existing client for no gain, since the client already knows which contract it
/// is talking about.</para>
/// </summary>
public static class QualifiedScope
{
    /// <summary>
    /// Lets a key read contract documents from this server.
    /// <para>Built in and generic: it belongs to no contract and is never produced by
    /// <c>ContractScopes.All</c>, so it has to be offered and accepted by hand wherever
    /// scopes are handled. That is the cost of it not being derived from anything.</para>
    /// <para>A contract document is the shape of everything a server holds — collections,
    /// fields, ranges. Publishing it to anyone who knows the address is free reconnaissance.
    /// Behind this scope it stays with keys that were issued and can be revoked.</para>
    /// <para>Holding it also settles version disagreements: a client that can fetch the
    /// document uses the server's, rather than negotiating its own against it.</para>
    /// </summary>
    public const string ReadContracts = NexusSyncServer.Hosting.Catalog.BuiltInScopes.ReadContracts;

    /// <summary>
    /// Whether these scopes may read <paramref name="contractId"/>'s document.
    /// <para>The global grant wins outright — holding it makes any per-contract grant
    /// redundant rather than additive, which is why the two are offered as alternatives
    /// instead of stacking.</para>
    /// </summary>
    public static bool CanReadContract(IReadOnlySet<string> scopes, string contractId)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return scopes.Contains(ReadContracts) || scopes.Contains(Of(contractId, ReadContracts));
    }

    /// <summary>
    /// Which of <paramref name="all"/> these scopes may read.
    /// <para>Used to filter the contract index. Refusing the whole call instead would tell a
    /// caller with one contract's grant that others exist; returning everything would defeat
    /// the point of granting one.</para>
    /// </summary>
    public static IReadOnlyList<string> ReadableContracts(
        IReadOnlySet<string> scopes, IEnumerable<string> all)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(all);

        return scopes.Contains(ReadContracts)
            ? all.ToArray()
            : all.Where(c => scopes.Contains(Of(c, ReadContracts))).ToArray();
    }

    /// <summary>Separates the contract from the scope. Not legal in either part.</summary>
    public const char Separator = '/';

    /// <summary>Builds <c>contract/scope</c>.</summary>
    public static string Of(string contractId, string scope) => $"{contractId}{Separator}{scope}";

    /// <summary>True when the string carries a contract.</summary>
    public static bool IsQualified(string value) => value.Contains(Separator, StringComparison.Ordinal);

    /// <summary>Splits a qualified scope, or returns false for a bare one.</summary>
    public static bool TryParse(string value, out string contractId, out string scope)
    {
        var index = value.IndexOf(Separator, StringComparison.Ordinal);
        if (index <= 0 || index == value.Length - 1)
        {
            contractId = string.Empty;
            scope = value;
            return false;
        }

        contractId = value[..index];
        scope = value[(index + 1)..];
        return true;
    }

    /// <summary>
    /// Whether a stored scope set permits <paramref name="scope"/> on <paramref name="contractId"/>.
    /// </summary>
    /// <param name="scopes">What the key carries. May mix qualified and bare entries.</param>
    /// <param name="keyContractId">The key's contract restriction, or null for none.</param>
    /// <param name="contractId">The contract being accessed.</param>
    /// <param name="scope">The bare scope the collection requires.</param>
    public static bool Grants(
        IReadOnlySet<string> scopes, string? keyContractId, string contractId, string scope)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        // The built-in one answers to either form.
        if (string.Equals(scope, ReadContracts, StringComparison.Ordinal))
            return CanReadContract(scopes, contractId);

        // The current form.
        if (scopes.Contains(Of(contractId, scope))) return true;

        // Keys issued before qualification existed carry bare scopes. They stay valid, but
        // only where they were already unambiguous — on a key pinned to this one contract.
        // A bare scope on an unrestricted key is exactly the ambiguity qualification exists
        // to remove, so it grants nothing.
        return keyContractId is not null
               && string.Equals(keyContractId, contractId, StringComparison.Ordinal)
               && scopes.Contains(scope);
    }
}
