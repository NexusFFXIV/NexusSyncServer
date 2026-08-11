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
}
