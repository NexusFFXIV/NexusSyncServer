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

    /// <summary>
    /// When this version was taken out of service, or null while it is still served.
    /// <para>Retiring is how a minimum version gets set, and it is a withdrawal rather than a
    /// deletion on purpose. The row stays, for two reasons: the history of what was once served is
    /// worth keeping, and <c>ContractCompatibility.CheckAll</c> must still weigh a candidate against
    /// it. A conversion chain that was invalid across a retired version does not become valid
    /// because nobody speaks it any more — stored records written under it are still there.</para>
    /// <para>What retiring does change is what is offered: a retired version disappears from
    /// <c>AvailableVersions</c>, from negotiation and from <c>Describe</c>. A peer that can go no
    /// higher than a retired version is refused and needs rebuilding, which is the honest outcome —
    /// the alternative is handing it a newer document it has never been checked against.</para>
    /// </summary>
    public DateTimeOffset? RetiredAt { get; set; }
}
