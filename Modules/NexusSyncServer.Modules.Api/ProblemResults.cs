using Microsoft.AspNetCore.Http;
using NexusSyncServer.Modules.Auth;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Api;

/// <summary>
/// Builds the RFC 9457 responses the protocol specifies.
/// <para>Every failure the client is expected to branch on gets a stable
/// <see cref="SyncProblemType"/> and, where it helps, extension fields carrying what the
/// client needs to act — the versions a server actually knows, the fields that failed
/// validation. A bare status code would leave the caller able to report that something went
/// wrong and nothing more.</para>
/// </summary>
internal static class ProblemResults
{
    public static IResult Unauthenticated(AuthFailure failure) => Build(
        SyncProblemType.Unauthenticated,
        "Unauthenticated",
        StatusCodes.Status401Unauthorized,
        failure switch
        {
            AuthFailure.Missing => "No usable API key was presented.",
            AuthFailure.Unknown => "The presented key is not recognised.",
            AuthFailure.Revoked => "The presented key, or its account, has been disabled.",
            AuthFailure.Expired => "The presented key has expired.",
            _ => "The presented key was refused.",
        });

    public static IResult RateLimited() => Build(
        SyncProblemType.LimitExceeded,
        "Too many requests",
        StatusCodes.Status429TooManyRequests,
        "The rate limit for this key has been exceeded. Retry later.");

    public static IResult ScopeMissing(string scope) => Build(
        SyncProblemType.ScopeMissing,
        "Scope missing",
        StatusCodes.Status403Forbidden,
        $"This key does not carry the '{scope}' scope.",
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["requiredScope"] = scope });

    public static IResult ContractForbidden(string contractId) => Build(
        SyncProblemType.ScopeMissing,
        "Contract not permitted",
        StatusCodes.Status403Forbidden,
        $"This key is restricted to a different contract and may not be used with '{contractId}'.");

    public static IResult UnknownContract(string contractId, IReadOnlyList<ContractVersion> known) => Build(
        SyncProblemType.UnknownContract,
        "Unknown contract",
        StatusCodes.Status404NotFound,
        $"No contract '{contractId}' is registered on this server.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["serverVersions"] = known.Select(v => v.ToString()).ToArray(),
        });

    public static IResult ContractMismatch(
        string contractId,
        ContractVersion wanted,
        IReadOnlyList<ContractVersion> known)
    {
        // The client asked for a major this server does not serve. Listing what it does serve
        // is what turns "mismatch" into something the author can act on.
        var newer = known.Count > 0 && known.Max().Major > wanted.Major;

        return Build(
            SyncProblemType.ContractMismatch,
            "Contract mismatch",
            StatusCodes.Status409Conflict,
            $"'{contractId}' {wanted} has no compatible version here.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["serverVersions"] = known.Select(v => v.ToString()).ToArray(),
                ["reason"] = newer ? "client-older" : "client-newer",
            });
    }

    public static IResult UnknownCollection(string contractId, string collection) => Build(
        SyncProblemType.UnknownCollection,
        "Unknown collection",
        StatusCodes.Status404NotFound,
        $"Contract '{contractId}' declares no collection '{collection}'.");

    public static IResult DirectionViolation(string collection, SyncDirection direction) => Build(
        SyncProblemType.DirectionViolation,
        "Direction violation",
        StatusCodes.Status403Forbidden,
        direction == SyncDirection.Downlink
            ? $"'{collection}' is a downlink collection; clients read it and may not write to it."
            : $"'{collection}' is an uplink collection; clients write to it and may not read it.");

    public static IResult ProtocolUnsupported(int presented) => Build(
        SyncProblemType.ProtocolUnsupported,
        "Protocol version unsupported",
        StatusCodes.Status400BadRequest,
        $"This server speaks protocol {SyncProtocolVersion.MinimumSupported}–{SyncProtocolVersion.Current}, "
        + $"the client presented {presented}.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["serverProtocolMin"] = SyncProtocolVersion.MinimumSupported,
            ["serverProtocolMax"] = SyncProtocolVersion.Current,
        });

    public static IResult BatchTooLarge(int presented, int limit) => Build(
        SyncProblemType.LimitExceeded,
        "Batch too large",
        StatusCodes.Status413PayloadTooLarge,
        $"The batch carried {presented} records; this server accepts at most {limit} per push.",
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxRecordsPerPush"] = limit });

    public static IResult Malformed(string detail) => Build(
        "about:blank",
        "Malformed request",
        StatusCodes.Status400BadRequest,
        detail);

    private static IResult Build(
        string type,
        string title,
        int status,
        string detail,
        IDictionary<string, object?>? extensions = null) =>
        Results.Problem(
            type: type,
            title: title,
            statusCode: status,
            detail: detail,
            extensions: extensions);
}
