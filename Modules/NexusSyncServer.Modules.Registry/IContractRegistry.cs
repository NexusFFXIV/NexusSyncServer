using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// The contracts this server knows.
/// <para>Reads are synchronous and hit an in-memory snapshot rather than the database. Every
/// client handshake and every push resolves a contract, so this is the hottest lookup on the
/// server — and contracts change roughly never, which makes a cache the obvious trade.</para>
/// </summary>
public interface IContractRegistry
{
    /// <summary>Every registered contract id, sorted.</summary>
    IReadOnlyList<string> ContractIds { get; }

    /// <summary>Every registered version of one contract, ascending. Empty when unknown.</summary>
    IReadOnlyList<ContractVersion> VersionsOf(string contractId);

    /// <summary>
    /// Picks the version a client should be served: the highest registered minor that shares
    /// the wanted major. Null when nothing compatible is registered.
    /// <para>Note what is <i>not</i> considered — the client's hash. Matching on it would lock
    /// out every deployed client on any trivial edit.</para>
    /// </summary>
    SyncContract? Negotiate(string contractId, ContractVersion wanted);

    /// <summary>Looks up one exact version. Null when it is not registered.</summary>
    SyncContract? Find(string contractId, ContractVersion version);

    /// <summary>
    /// Builds the public descriptor for a contract, at a given version or the highest one.
    /// Null when the contract is unknown.
    /// </summary>
    ContractDescriptor? Describe(string contractId, ContractVersion? version = null);

    /// <summary>
    /// Registers a contract version. Operator-side only — this is not exposed to arbitrary
    /// callers, which is what removes the whole quota and abuse surface a shared registry
    /// would need.
    /// </summary>
    Task<RegistrationResult> RegisterAsync(SyncContract contract, CancellationToken ct);

    /// <summary>Reloads the in-memory snapshot from the database.</summary>
    Task RefreshAsync(CancellationToken ct);
}
