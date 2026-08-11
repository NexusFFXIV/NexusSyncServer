namespace NexusSyncServer.Modules.Storage.MariaDb.Records;

/// <summary>
/// One migration step that has run, recorded per module.
/// <para>Keyed by module id rather than by a single global ordering, so modules evolve
/// independently: adding a module to an existing deployment applies only its own history,
/// without renumbering or replaying anyone else's.</para>
/// </summary>
public sealed class AppliedMigrationEntity
{
    /// <summary>The contributing module's id, matching its <c>IServerModule.Id</c>.</summary>
    public required string ModuleId { get; set; }

    /// <summary>The migration's id within that module.</summary>
    public required string MigrationId { get; set; }

    /// <summary>When it ran.</summary>
    public DateTimeOffset AppliedAt { get; set; }
}
