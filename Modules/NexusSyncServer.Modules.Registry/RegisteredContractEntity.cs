namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// One registered contract version.
/// <para>Several rows per contract are normal and intended: versions live side by side, so a
/// client built against 1.0 keeps working after 1.1 is registered, and a new major coexists
/// with the old one for as long as anybody still speaks it.</para>
/// </summary>
public sealed class RegisteredContractEntity
{
    /// <summary>The contract's identity, e.g. <c>acme.venuetracker</c>.</summary>
    public required string ContractId { get; set; }

    /// <summary>Major version. Part of the key, because majors coexist.</summary>
    public int Major { get; set; }

    /// <summary>Minor version.</summary>
    public int Minor { get; set; }

    /// <summary>
    /// The canonical document, stored verbatim.
    /// <para>Kept as text rather than re-derived from a parsed model on read. What was
    /// registered is what gets served and hashed — a round trip through the model would
    /// silently "fix" a document written by a version of the parser that no longer exists.</para>
    /// </summary>
    public required string CanonicalJson { get; set; }

    /// <summary>Hash of <see cref="CanonicalJson"/>, stored so a lookup does not have to rehash.</summary>
    public required string Hash { get; set; }

    /// <summary>When this version was registered.</summary>
    public DateTimeOffset RegisteredAt { get; set; }
}
