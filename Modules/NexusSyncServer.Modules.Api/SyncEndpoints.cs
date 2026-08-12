// Microsoft.AspNetCore.Builder carries the minimal-API Map* overloads. Without it the
// compiler binds to the legacy RequestDelegate ones in Microsoft.AspNetCore.Routing and
// rejects every handler here.
using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Modules.Auth;
using NexusSyncServer.Modules.Registry;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusSyncServer.Modules.Storage.MariaDb.Records;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Api;

/// <summary>
/// The four protocol operations, mapped onto the routes <c>SyncRoutes</c> defines.
/// </summary>
internal sealed class SyncEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/" + SyncRoutes.Handshake(), HandshakeAsync).WithName("Handshake");
        endpoints.MapPost($"/{SyncRoutes.Root}/{{contract}}/{{collection}}/push", PushAsync).WithName("Push");
        endpoints.MapGet($"/{SyncRoutes.Root}/{{contract}}/{{collection}}/pull", PullAsync).WithName("Pull");

        // Behind contract:pull, both of them. A document is the shape of everything this
        // server holds, and the list gives away which contracts exist — protecting the
        // documents while publishing the index would be half a gate.
        //
        // This was open once, so that an author could check compatibility before signing up.
        // It is closed now because the operating model is one server per author: client and
        // server come from the same hands, so nobody needed that and everybody could see the
        // schema.
        endpoints.MapGet("/" + SyncRoutes.Contracts(), ListContracts).WithName("ListContracts");
        endpoints.MapGet($"/{SyncRoutes.Root}/contracts/{{contract}}", DescribeContract)
            .WithName("DescribeContract");
    }

    private static async Task<IResult> HandshakeAsync(
        HttpContext http,
        HandshakeRequest request,
        IApiKeyAuthenticator auth,
        IContractRegistry registry,
        IOptions<StorageOptions> storage,
        IKeyContractStateWriter state,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        if (!SyncProtocolVersion.IsSupported(request.ProtocolVersion))
            return ProblemResults.ProtocolUnsupported(request.ProtocolVersion);

        var caller = await CallerResolver.ResolveAsync(http, auth, ct).ConfigureAwait(false);
        if (!caller.Ok) return caller.Problem!;

        if (!caller.Caller!.CanUseContract(request.ContractId))
            return ProblemResults.ContractForbidden(request.ContractId);

        var known = registry.VersionsOf(request.ContractId);
        if (known.Count == 0) return ProblemResults.UnknownContract(request.ContractId, known);

        var negotiated = registry.Negotiate(request.ContractId, request.Version);
        if (negotiated is null) return NoVersionFor(registry, request.ContractId, request.Version, known);

        // The key's scopes intersected with what the contract actually declares. A key may
        // legitimately carry fewer; it must never carry more than the contract can express.
        // Bare scopes, deliberately: this list is what the client compares against its own
        // contract's scopes, and it already knows which contract it asked about.
        var granted = negotiated.Scopes
            .Where(s => caller.Caller.HasScope(request.ContractId, s))
            .ToArray();

        var limits = new SyncLimits(
            storage.Value.MaxRecordsPerPush,
            storage.Value.MaxPayloadBytes,
            storage.Value.MaxRecordsPerPull);

        // A write on the hottest path, which the handshake did not have before. It is one upsert
        // per handshake, and it buys the only view an operator has of who is still on what.
        //
        // Never fatal: this is bookkeeping, and a peer that can be served must be served even if
        // recording the fact fails. A handshake refused because a report table was unavailable
        // would be an outage caused entirely by observability.
        try
        {
            await state.RecordAsync(
                caller.Caller.KeyRowId,
                request.ContractId,
                negotiated.Version,
                request.SupportedVersion,
                ct).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            logs.CreateLogger("NexusSyncServer.Modules.Api.Handshake").LogWarning(
                ex, "Could not record handshake state for key {KeyId} on {Contract}",
                caller.Caller.KeyId, request.ContractId);
        }

        return Results.Ok(new HandshakeResult(
            negotiated.Version,
            negotiated.Hash,
            granted,
            limits));
    }

    private static async Task<IResult> PushAsync(
        HttpContext http,
        string contract,
        string collection,
        PushRequest request,
        IApiKeyAuthenticator auth,
        IContractRegistry registry,
        IRecordStore store,
        IOptions<StorageOptions> storage,
        CancellationToken ct)
    {
        // The route and the body both name the contract and collection. Disagreement means a
        // client bug; accepting either silently would make it a data bug instead.
        if (!string.Equals(contract, request.ContractId, StringComparison.Ordinal)
            || !string.Equals(collection, request.Collection, StringComparison.Ordinal))
        {
            return ProblemResults.Malformed("The route and the body name different contracts or collections.");
        }

        var resolved = await ResolveCollectionAsync(
            http, auth, registry, contract, request.Version, collection, SyncDirection.Uplink, ct).ConfigureAwait(false);

        if (resolved.Problem is not null) return resolved.Problem;

        if (request.Records.Count > storage.Value.MaxRecordsPerPush)
            return ProblemResults.BatchTooLarge(request.Records.Count, storage.Value.MaxRecordsPerPush);

        var outcomes = await store
            .WriteAsync(contract, resolved.Collection!, request.Records, resolved.Caller!.AccountId, ct)
            .ConfigureAwait(false);

        return Results.Ok(new PushResult(outcomes));
    }

    private static async Task<IResult> PullAsync(
        HttpContext http,
        string contract,
        string collection,
        string version,
        long since,
        int? limit,
        IApiKeyAuthenticator auth,
        IContractRegistry registry,
        IRecordStore store,
        IOptions<StorageOptions> storage,
        CancellationToken ct)
    {
        if (!ContractVersion.TryParse(version, out var parsed))
            return ProblemResults.Malformed($"'{version}' is not a contract version. Expected 'major.minor'.");

        var resolved = await ResolveCollectionAsync(
            http, auth, registry, contract, parsed, collection, SyncDirection.Downlink, ct).ConfigureAwait(false);

        if (resolved.Problem is not null) return resolved.Problem;

        var page = await store
            .ReadAsync(contract, collection, since, limit ?? storage.Value.MaxRecordsPerPull, ct)
            .ConfigureAwait(false);

        return Results.Ok(page);
    }

    /// <summary>
    /// Refuses anyone without <see cref="QualifiedScope.ReadContracts"/>, or null to continue.
    /// <para>Hand-rolled rather than an authorization policy, for the same reason
    /// <see cref="CallerResolver"/> is: these endpoints answer with Problem Details, and the
    /// built-in challenge pipeline produces empty 401s.</para>
    /// </summary>
    private static async Task<(AuthenticatedCaller? Caller, IResult? Denied)> ResolveReaderAsync(
        HttpContext http, IApiKeyAuthenticator auth, CancellationToken ct)
    {
        var caller = await CallerResolver.ResolveAsync(http, auth, ct).ConfigureAwait(false);
        return caller.Ok ? (caller.Caller, null) : (null, caller.Problem);
    }

    /// <summary>
    /// Refuses anyone who may not read <paramref name="contractId"/>'s document.
    /// <para>Hand-rolled rather than an authorization policy, for the same reason
    /// <see cref="CallerResolver"/> is: these endpoints answer with Problem Details, and the
    /// built-in challenge pipeline produces empty 401s.</para>
    /// </summary>
    private static async Task<IResult?> RequireContractReadAsync(
        HttpContext http, IApiKeyAuthenticator auth, string contractId, CancellationToken ct)
    {
        var (caller, denied) = await ResolveReaderAsync(http, auth, ct).ConfigureAwait(false);
        if (denied is not null) return denied;

        return caller!.HasScope(contractId, QualifiedScope.ReadContracts)
            ? null
            : ProblemResults.ScopeMissing(QualifiedScope.ReadContracts);
    }

    private static async Task<IResult> ListContracts(
        HttpContext http, IContractRegistry registry, IApiKeyAuthenticator auth, CancellationToken ct)
    {
        var (caller, denied) = await ResolveReaderAsync(http, auth, ct).ConfigureAwait(false);
        if (denied is not null) return denied;

        // Filtered, not refused. A key holding one contract's grant should see that contract
        // and learn nothing about the others — an outright refusal would tell it there is
        // something to be refused, and the full list would undo the point of granting one.
        // An empty list is a valid answer, and indistinguishable from a server with none.
        var readable = QualifiedScope.ReadableContracts(caller!.Scopes, registry.ContractIds);

        return Results.Ok(readable
            .Select(id => new { contractId = id, versions = registry.VersionsOf(id).Select(v => v.ToString()) })
            .ToArray());
    }

    private static async Task<IResult> DescribeContract(
        HttpContext http,
        string contract,
        string? version,
        IContractRegistry registry,
        IApiKeyAuthenticator auth,
        CancellationToken ct)
    {
        if (await RequireContractReadAsync(http, auth, contract, ct).ConfigureAwait(false) is { } denied)
            return denied;

        ContractVersion? wanted = null;

        if (!string.IsNullOrEmpty(version))
        {
            if (!ContractVersion.TryParse(version, out var parsed))
                return ProblemResults.Malformed($"'{version}' is not a contract version. Expected 'major.minor'.");

            wanted = parsed;
        }

        var descriptor = registry.Describe(contract, wanted);

        return descriptor is null
            ? ProblemResults.UnknownContract(contract, registry.VersionsOf(contract))
            : Results.Ok(descriptor);
    }

    private readonly record struct Resolved(
        AuthenticatedCaller? Caller,
        CollectionDefinition? Collection,
        IResult? Problem);

    /// <summary>
    /// Tells the two ways negotiation can come up empty apart, because they need different answers.
    /// <para>If the major is served but every remaining version of it is newer than the peer's, the
    /// peer is below the minimum: no renegotiation reaches it and the build has to be updated. If
    /// the major is not served at all, that is the ordinary mismatch. Collapsing both into
    /// "mismatch" would tell an author to check their version number when the actual instruction is
    /// to ship a new release.</para>
    /// </summary>
    private static IResult NoVersionFor(
        IContractRegistry registry,
        string contractId,
        ContractVersion wanted,
        IReadOnlyList<ContractVersion> known)
    {
        if (registry.MinimumServed(contractId, wanted.Major) is { } minimum)
            return ProblemResults.ContractTooOld(contractId, wanted, minimum, known);

        return ProblemResults.ContractMismatch(contractId, wanted, known);
    }

    /// <summary>
    /// The checks every data operation shares: authenticate, confirm the key may touch this
    /// contract, negotiate the version, find the collection, and confirm the direction.
    /// <para>Shared so the order cannot drift between push and pull — a scope check that ran
    /// after the write on one path and before it on the other is exactly the kind of asymmetry
    /// nobody notices in review.</para>
    /// </summary>
    private static async Task<Resolved> ResolveCollectionAsync(
        HttpContext http,
        IApiKeyAuthenticator auth,
        IContractRegistry registry,
        string contractId,
        ContractVersion version,
        string collectionName,
        SyncDirection expected,
        CancellationToken ct)
    {
        var caller = await CallerResolver.ResolveAsync(http, auth, ct).ConfigureAwait(false);
        if (!caller.Ok) return new Resolved(null, null, caller.Problem);

        if (!caller.Caller!.CanUseContract(contractId))
            return new Resolved(null, null, ProblemResults.ContractForbidden(contractId));

        var known = registry.VersionsOf(contractId);
        if (known.Count == 0) return new Resolved(null, null, ProblemResults.UnknownContract(contractId, known));

        var contract = registry.Negotiate(contractId, version);
        if (contract is null)
            return new Resolved(null, null, NoVersionFor(registry, contractId, version, known));

        var collection = contract.FindCollection(collectionName);
        if (collection is null)
            return new Resolved(null, null, ProblemResults.UnknownCollection(contractId, collectionName));

        if (collection.Direction != expected)
            return new Resolved(null, null, ProblemResults.DirectionViolation(collectionName, collection.Direction));

        if (!caller.Caller.HasScope(contractId, collection.Scope))
            return new Resolved(null, null, ProblemResults.ScopeMissing(collection.Scope));

        return new Resolved(caller.Caller, collection, null);
    }
}
