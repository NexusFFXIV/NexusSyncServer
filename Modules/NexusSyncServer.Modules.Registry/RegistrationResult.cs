using NexusKit.Sync.Contracts;

namespace NexusSyncServer.Modules.Registry;

/// <summary>What happened when a contract version was offered to the registry.</summary>
public enum RegistrationStatus
{
    /// <summary>Stored, and any declared indexes created.</summary>
    Registered,

    /// <summary>
    /// This exact version is already registered with the same hash. Re-registering an
    /// unchanged document is a no-op rather than an error — restarting a server that loads
    /// contracts from disk must not fail on the second boot.
    /// </summary>
    Unchanged,

    /// <summary>
    /// The version exists with a <b>different</b> document. Refused: a registered version is
    /// immutable, because clients have already negotiated against it and cached its shape.
    /// Publish a new minor instead.
    /// </summary>
    Conflict,

    /// <summary>The candidate would break peers on an existing version within the same major.</summary>
    Incompatible,
}

/// <summary>The outcome of a registration attempt.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Contract">The contract, when it was accepted or already present.</param>
/// <param name="Problems">Why it was refused, for the failing statuses.</param>
public sealed record RegistrationResult(
    RegistrationStatus Status,
    SyncContract? Contract = null,
    IReadOnlyList<string>? Problems = null)
{
    /// <summary>True when the contract is now registered — whether it was just stored or already there.</summary>
    public bool IsAccepted => Status is RegistrationStatus.Registered or RegistrationStatus.Unchanged;

    /// <inheritdoc />
    public override string ToString() =>
        Problems is { Count: > 0 } ? $"{Status}: {string.Join("; ", Problems)}" : Status.ToString();
}
