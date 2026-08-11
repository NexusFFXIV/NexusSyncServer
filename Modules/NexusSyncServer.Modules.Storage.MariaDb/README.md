# NexusSyncServer.Modules.Storage.MariaDb

MariaDB storage: the shared `DbContext`, the generic record store, the migration runner and
retention housekeeping.

**Compose this first.** It owns the context every other module contributes its tables to.

## Public API

| Type | File | Purpose |
|---|---|---|
| `StorageMariaDbModule` | `StorageMariaDbModule.cs` | `IServerModule`. Registers everything below and validates the connection string at startup. |
| `ServerDbContext` | `ServerDbContext.cs` | The one context. Collects every `IEntityModule` and prefixes each module's tables with the name it declares. |
| `IRecordStore`, `RecordStore` | `Records/` | Reading and writing contract-defined records — the only place that touches the generic store. |
| `RecordEntity` | `Records/RecordEntity.cs` | One stored record: contract, collection, key, sequence, JSON payload, tombstone flag. |
| `AppliedOpEntity` | `Records/AppliedOpEntity.cs` | Applied push operations. What makes a retry idempotent. |
| `AppliedMigrationEntity` | `Records/AppliedMigrationEntity.cs` | Migration ledger, keyed per module. |
| `MigrationRunner` | `MigrationRunner.cs` | Applies pending steps at startup, one transaction each. |
| `DatabaseReadinessCheck` | `DatabaseReadinessCheck.cs` | Reports the database through `/ready`, never `/health`. |
| `StorageOptions` | `StorageOptions.cs` | Connection string, batch and payload limits, dedupe window. |

## Configuration

```jsonc
{
  "Storage": {
    "ConnectionString": "Server=db;Port=3306;Database=nexussyncserver;User Id=nexussyncserver;Password=…;",
    "MaxRecordsPerPush": 500,
    "MaxRecordsPerPull": 1000,
    "MaxPayloadBytes": 262144,
    "OperationDedupeWindow": "7.00:00:00"
  }
}
```

The connection string is validated at **startup**, not on first use. A server that boots,
passes its liveness probe and only then reveals it has no database is harder to diagnose than
one that refuses to start with a message naming the missing setting.

## One generic table, not one per collection

Contracts are registered at runtime, so there is no migration in which per-collection tables
could be created. Records therefore share one table with a JSON payload:

```sql
storage_records(contract_id, collection, record_key, seq, revision,
                payload json, deleted, owner_id, updated_at)
```

`record_key` rather than `key`, which is reserved in MariaDB and would need quoting in every
hand-written statement — the kind of detail that gets forgotten exactly once.

The alternative — a table per collection — would mean an author cannot add a collection without
a server deployment, which is the one thing this design exists to avoid.

**The cost is real:** JSON is slower than a purpose-built table and cannot use ordinary column
indexes. Two things offset it. Every contract-declared `indexed` field gets a virtual generated
column extracting that JSON path, plus an index on
`(contract_id, collection, <column>)` — created at registration. And a collection that outgrows
this can be served by a module with its own tables, the seam for which is `IEntityModule`.

Three limits of that index worth knowing:

- The generated column is **`VARCHAR(191)`, left-truncated**. A longer value would abort the
  whole insert under strict mode, so it is cut instead — losing the write would be the worse
  trade. Lookups on values beyond 191 characters degrade to a prefix match plus a filter.
- Columns are keyed by **field name alone**, so two contracts declaring the same field share
  one column. The extraction is identical, and a row without that path simply yields NULL.
- The discriminators sit **in the index key** rather than in a `WHERE` clause, because MariaDB
  has no partial index. Same selectivity, one index shared across contracts.

There is deliberately **no general-purpose index on the payload**. MariaDB has no equivalent of
a GIN index over a whole JSON document, so a query on an undeclared field is a full scan.
Declaring the field in the contract's `indexed` list is the supported answer.

## Two details that are easy to get wrong

**An updated record moves to the end of the cursor.** The upsert draws a fresh
`NEXT VALUE FOR storage_record_seq` on update, not just on insert. Without that, a client that
already read past a record's old sequence would never see the change. It is a raw
`ON DUPLICATE KEY UPDATE` statement for exactly this reason: a database-side expression EF
cannot express through a tracked property.

**The sequence is MariaDB-specific.** MySQL has no `CREATE SEQUENCE`, so neither does the EF
provider — it is created by `MigrationRunner` with raw DDL, and strictly *after*
`EnsureCreated`. In MariaDB a sequence is reported as a table, and EF decides whether to
create the schema by asking whether the database has any tables; creating the sequence first
would convince it the schema already existed and leave the database empty.

**Pull returns one row more than asked for**, then drops it. That is how `HasMore` is answered
without a second count query.

## Retention and pruning

A collection may declare a retention (`180d`). Records past it are deleted by the maintenance
loop, along with operation ids past the dedupe window.

A client offline longer than the dedupe window and then retrying will re-apply its writes.
Harmless: writes are keyed upserts, so it costs a round trip and changes nothing.

## Further reading

| Document | What it covers |
|---|---|
| [docs/record-store.md](docs/record-store.md) | The storage shape, cursors, tombstones, idempotency, and the indexing story |

## License

**AGPL-3.0-only.**
