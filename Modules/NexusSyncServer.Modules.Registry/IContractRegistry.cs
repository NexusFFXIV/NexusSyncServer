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

    /// <summary>
    /// Every version of one contract that is still served, ascending. Empty when unknown.
    /// <para>Retired versions are absent. This is what peers are told exists, so it must not name a
    /// version they would then be refused.</para>
    /// </summary>
    IReadOnlyList<ContractVersion> VersionsOf(string contractId);

    /// <summary>
    /// Every registered version, retired ones included, ascending.
    /// <para>For the operator's view rather than a peer's: the portal has to show a retired version
    /// in order to offer bringing it back, and to show who is still hanging off it.</para>
    /// </summary>
    IReadOnlyList<ContractVersion> AllVersionsOf(string contractId);

    /// <summary>Whether a registered version has been taken out of service.</summary>
    bool IsRetired(string contractId, ContractVersion version);

    /// <summary>
    /// Picks the version a client should be served: the highest still-served minor in
    /// <c>[lowest served, wanted]</c> within the wanted major. Null when nothing fits.
    /// <para>Bounded <b>above</b> by what the client asked for, which is the half that used to be
    /// missing. A client naming a minor is stating the newest document it has been built and checked
    /// against; serving it something newer assumes minors are purely additive, and they are not —
    /// a field's type may widen or narrow between them. A client on 1.4 against a server holding 1.2
    /// still gets 1.2, which is the case that rule was written for and still works.</para>
    /// <para>Bounded <b>below</b> by what the operator still serves. Null here does not always mean
    /// the same thing: if the contract has served versions of that major and all of them are newer,
    /// the peer is too old and needs rebuilding — a distinct answer from "no such major", and worth
    /// telling it apart at the endpoint.</para>
    /// <para>Note what is <i>not</i> considered — the client's hash. Matching on it would lock
    /// out every deployed client on any trivial edit.</para>
    /// </summary>
    SyncContract? Negotiate(string contractId, ContractVersion wanted);

    /// <summary>
    /// The lowest version of a major still served, or null when none is.
    /// <para>What a peer refused by <see cref="Negotiate"/> must be told: the version it has to
    /// reach. A non-null answer here after a failed negotiation is what separates "too old, update
    /// the build" from "no such major".</para>
    /// </summary>
    ContractVersion? MinimumServed(string contractId, int major);

    /// <summary>
    /// Looks up one exact version, retired or not. Null when it is not registered.
    /// <para>Retired versions are included deliberately: callers that name a specific version are
    /// asking about that document, not asking to be served.</para>
    /// </summary>
    SyncContract? Find(string contractId, ContractVersion version);

    /// <summary>
    /// Takes a version out of service, or puts it back. Idempotent.
    /// <para>The row survives either way — see <see cref="RegisteredContractEntity.RetiredAt"/> for
    /// why a withdrawal must not be a deletion.</para>
    /// </summary>
    /// <returns>False when that version is not registered.</returns>
    Task<bool> SetRetiredAsync(string contractId, ContractVersion version, bool retired, CancellationToken ct);

    /// <summary>
    /// Builds the public descriptor for a contract, at a given version or the highest still served.
    /// Null when the contract is unknown, or when the named version is not served.
    /// <para><c>AvailableVersions</c> lists only what is served. That is what makes retirement work
    /// end to end without the client knowing the concept exists: it chooses the newest version it
    /// can honour from what it is offered, and a retired one is simply never offered.</para>
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
