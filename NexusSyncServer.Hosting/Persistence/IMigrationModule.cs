namespace NexusSyncServer.Hosting.Persistence;

/// <summary>
/// A module's schema-evolution history.
/// </summary>
public interface IMigrationModule
{
    /// <summary>
    /// Identifier scoped to the contributing module, matching its <c>IServerModule.Id</c>.
    /// Used as the foreign key in the applied-migrations table, which is why it must not
    /// change when the CLR type is renamed.
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// The full history, in any order — the host applies pending steps sorted by
    /// <see cref="IMigration.Id"/>.
    /// <para>Keep applied entries in the list. Removing one does not undo it; it only makes
    /// the history unreadable to whoever debugs the database later.</para>
    /// </summary>
    IReadOnlyList<IMigration> Migrations { get; }
}
