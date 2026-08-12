using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Schema evolution for the auth tables.
/// <para><c>EnsureCreated</c> builds a whole schema or nothing at all, so a server that has been
/// running never receives a new table from the entity mapping alone. It needs a step like this one,
/// and a fresh database needs the mapping — both, or half the installations are wrong.</para>
/// </summary>
public sealed class AuthMigrations : IMigrationModule
{
    /// <inheritdoc />
    public string ModuleId => "nexussyncserver.auth";

    /// <inheritdoc />
    public IReadOnlyList<IMigration> Migrations { get; } = [new AddKeyContractState()];

    /// <summary>Adds the table recording what each key last did with each contract.</summary>
    private sealed class AddKeyContractState : IMigration
    {
        public string Id => "20260812_add_key_contract_state";

        public async Task UpAsync(DbContext context, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(context);

            // IF NOT EXISTS so a database EnsureCreated already built from the current model
            // reaches the same end state as one being upgraded. Column types follow what EF
            // generates for the mapped entity, so the two paths do not diverge.
            //
            // key_id takes the table's default collation rather than naming one. auth_api_keys.id
            // is char(36) under the database's utf8mb4 collation, and InnoDB requires a foreign
            // key's column to match its referent in type *and* collation — an explicit
            // ascii_general_ci here fails with errno 150 and a message that says only
            // "incorrectly formed".
            //
            // datetime, not datetime(6), for the same reason registry_contracts uses it: that is
            // what EF emits for a DateTimeOffset, and the two creation paths have to agree.
            const string sql =
                """
                CREATE TABLE IF NOT EXISTS auth_key_contract_state (
                    key_id            char(36) NOT NULL,
                    contract_id       varchar(128) NOT NULL,
                    negotiated_major  int NOT NULL,
                    negotiated_minor  int NOT NULL,
                    supported_major   int NULL,
                    supported_minor   int NULL,
                    last_seen_at      datetime NOT NULL,
                    PRIMARY KEY (key_id, contract_id),
                    KEY ix_key_contract_state_contract (contract_id),
                    CONSTRAINT fk_key_contract_state_key
                        FOREIGN KEY (key_id) REFERENCES auth_api_keys (id) ON DELETE CASCADE
                )
                """;

            await context.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
        }
    }
}
