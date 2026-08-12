namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Registry configuration, bound from the <c>Registry</c> configuration section.
/// </summary>
public sealed class RegistryOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Registry";

    /// <summary>
    /// Directory scanned for <c>*.json</c> contract documents at startup. Each is registered
    /// if it is new or unchanged; a document that would break existing peers is refused and
    /// logged.
    /// <para>A mounted directory is the whole registration story until the admin UI exists,
    /// and it stays useful afterwards: it makes an operator's set of contracts a file they can
    /// version, review and redeploy rather than state living only in a database.</para>
    /// </summary>
    public string? ContractsDirectory { get; set; } = "contracts";

    /// <summary>
    /// Whether a contract file that fails to register should stop the server.
    /// <para>Default false: a server already serving three contracts should not refuse to
    /// start because a fourth file is malformed. The failure is logged as an error, and the
    /// contract simply is not available — which surfaces as a clear 404 on handshake rather
    /// than an outage for everybody.</para>
    /// </summary>
    public bool FailOnInvalidContract { get; set; }

    /// <summary>
    /// Whether a narrowing type change should be refused when stored records would not survive it.
    /// <para>Default false, matching <see cref="FailOnInvalidContract"/>: the scan always runs and
    /// always reports, but by default it warns rather than blocks. Narrowing is a deliberate choice
    /// an operator makes about their own data, and the point of the check is that the cost is
    /// visible <i>before</i> the change lands, not that the server second-guesses the decision.
    /// Turn this on where a bad row must stop a deployment instead of appearing in a log.</para>
    /// </summary>
    public bool BlockNarrowingWithBadData { get; set; }

    /// <summary>
    /// How many records to read per narrowing check. Zero means every one.
    /// <para>The scan reads payloads, so on a large collection it is not free. The cap keeps a
    /// registration from stalling startup; what it never does is turn a partial scan into a clean
    /// bill of health — a truncated scan is reported as truncated, with the numbers, so nobody reads
    /// "no problems found" as "no problems exist".</para>
    /// </summary>
    public int NarrowingScanLimit { get; set; } = 50_000;
}
