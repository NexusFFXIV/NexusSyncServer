namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// What one key last did with one contract.
/// <para>Keyed on the pair, not on the key alone. A key may span several contracts, and a single
/// column on the key row would be overwritten by whichever contract spoke last — after three
/// handshakes it would hold a number that is a lie about two of them.</para>
/// <para>Incidentally the only place that records which contracts a key <i>actually</i> uses. Its
/// scopes say what it would be allowed to do, which is a different question and usually a larger
/// answer.</para>
/// </summary>
public sealed class KeyContractStateEntity
{
    /// <summary>
    /// The key's row identity — <see cref="ApiKeyEntity.Id"/>, not the eight-character prefix,
    /// which is not unique.
    /// </summary>
    public Guid KeyId { get; set; }

    /// <summary>The contract this row is about.</summary>
    public required string ContractId { get; set; }

    /// <summary>Major of the version actually served on the last handshake.</summary>
    public int NegotiatedMajor { get; set; }

    /// <summary>Minor of the version actually served on the last handshake.</summary>
    public int NegotiatedMinor { get; set; }

    /// <summary>
    /// Major of the highest version the peer reported it could speak, or null when it did not say.
    /// <para>Null is its own answer and must not be read as zero: it means an older build that
    /// predates the field, so nothing is known about its ceiling. Only a peer that reports one can
    /// tell "has not moved up yet" from "cannot move up", and that distinction is the whole reason
    /// an operator can retire a version instead of guessing.</para>
    /// </summary>
    public int? SupportedMajor { get; set; }

    /// <summary>Minor of the highest version the peer reported it could speak, or null.</summary>
    public int? SupportedMinor { get; set; }

    /// <summary>When this key last handshook for this contract.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
