using Microsoft.EntityFrameworkCore;

namespace NexusSyncServer.Hosting.Persistence;

/// <summary>
/// A module contributing its own tables to the shared server database.
/// <para><b>This is for the module's own bookkeeping</b> — API keys, registered contracts,
/// migration history — <b>not for contract-defined user data</b>. That lives in the generic
/// record store, which is precisely what allows a contract to be registered at runtime without
/// anyone writing a migration.</para>
/// </summary>
public interface IEntityModule
{
    /// <summary>
    /// Namespace the module's tables live in, e.g. <c>auth</c>. Lowercase letters, digits
    /// and underscores.
    /// <para>Applied as a table-name prefix, so a table mapped as <c>accounts</c> becomes
    /// <c>auth_accounts</c>. The same convention the client side uses on SQLite, and for the
    /// same reason: in MariaDB a schema <i>is</i> a database, so a schema per module would
    /// mean a connection string, a grant and a backup per module — to separate tables meant
    /// to be joinable and to live or die together.</para>
    /// </summary>
    string SchemaName { get; }

    /// <summary>
    /// Maps the module's entities. The host prefixes everything configured here with
    /// <see cref="SchemaName"/>, so implementations do not repeat it per entity.
    /// </summary>
    void ConfigureEntities(ModelBuilder modelBuilder);
}
