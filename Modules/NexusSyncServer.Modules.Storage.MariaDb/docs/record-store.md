# The record store (NexusSyncServer.Modules.Storage.MariaDb)

How contract-defined data is stored, read back in order, and kept honest.

## The shape

```sql
CREATE SEQUENCE storage_record_seq START WITH 1 INCREMENT BY 1;

CREATE TABLE storage_records (
    contract_id  VARCHAR(128),
    collection   VARCHAR(64),
    record_key   VARCHAR(512),
    seq          BIGINT,
    revision     INT      DEFAULT 1,
    payload      JSON,
    deleted      TINYINT(1) DEFAULT 0,
    owner_id     CHAR(36),
    updated_at   DATETIME(6),
    PRIMARY KEY (contract_id, collection, record_key)
);

CREATE INDEX ix_records_cursor ON storage_records (contract_id, collection, seq);
```

`storage_` is the module's table-name prefix, not a schema — in MariaDB a schema is a database.

`seq` carries no column default. The sequence is drawn explicitly by the upsert, which has to
draw it again on update anyway to move the row to the end of the cursor.

The primary key is 2816 bytes of utf8mb4, which is what caps `record_key` at 512: InnoDB
refuses an index key beyond 3072.

`updated_at` is stored to whole seconds. The provider truncates `DateTimeOffset` parameters,
which costs nothing here — retention and the dedupe window are the only readers, and ordering
is `seq`'s job, not a timestamp's.

`ix_records_cursor` is the one index that must exist before any data does — every pull is
exactly that predicate.

`owner_id` is unused by the single-operator model and present from the start anyway: it is the
column a shared deployment would need, and adding it later means migrating a table that by then
holds everything.

## Writing

Every record in a batch goes through, in order:

1. **Idempotency** — is this `opId` already applied? Then `Duplicate`, and nothing is written.
2. **Direction** — is this collection an uplink? A downlink write is refused.
3. **Size** — is the payload within `MaxPayloadBytes`?
4. **Contract** — `PayloadValidator` against the collection: types, required fields, ranges, lengths.
5. **Upsert** — insert or update, drawing a new sequence either way.

The whole batch runs in one transaction. A partially applied batch whose response says
"accepted" is the failure worth spending a transaction to avoid.

Validation happens **here, on the server**. A client may validate too for a faster error, but
it is never the authority — the contract exists so a forged, buggy or outdated client cannot
store what the contract forbids. Any change making this more permissive is a security change,
not a convenience change.

### Why the upsert is raw SQL

```sql
INSERT INTO storage.records (…, seq, revision, …)
VALUES (…, nextval('storage.record_seq'), 1, …)
ON CONFLICT (contract_id, collection, key) DO UPDATE SET
    seq      = nextval('storage.record_seq'),
    revision = storage.records.revision + 1,
    …
```

**An updated record has to move to the end of the cursor.** A client that already read past its
old sequence would otherwise never see the change — the record would sit there, updated, and
invisible to everyone who had already synced. That means a fresh `nextval` on update, which is
a database-side expression EF cannot produce through a tracked property; it would write back
whatever value the entity happened to hold.

Doing it in one statement also makes the upsert atomic, with no read-then-write race between
concurrent pushes of the same key.

### The sequence is global

One sequence for all collections, not one each. The protocol only requires monotonicity
*within* a collection, which a global sequence satisfies — with gaps, and gaps cost a client
nothing because it always takes `NextCursor` from the response rather than computing it.
Per-collection sequences would mean a counter table and a hot row.

## Reading

```
GET /v1/{contract}/{collection}/pull?version=1.0&since=42&limit=100
```

Returns changes with `seq > since`, ordered, plus `nextCursor` and `hasMore`.

**Tombstones are carried explicitly** — a `RecordChange` with `deleted: true` and no payload.
Omitting deleted records would leave a client mirroring the collection with no way to learn
that something it holds is gone.

**`nextCursor` is authoritative even on an empty page.** A server that skipped sequences —
pruned records, filtered rows — would otherwise leave the client re-requesting the same gap
forever.

## Indexes from the contract

A collection declaring `"indexed": ["label"]` gets, at registration:

```sql
CREATE INDEX IF NOT EXISTS "ix_rec_…" ON storage.records ((payload ->> 'label'))
WHERE contract_id = '…' AND collection = '…';
```

Partial, so the index only covers the rows it is for.

The identifiers are interpolated rather than parameterised, because MariaDB does not accept
parameters in DDL. That is safe only because every component has already passed
`ContractNames` validation — and the store re-checks before building the statement rather than
trusting that. Payload values are never interpolated; those go through parameters.

Index names are hashed down to fit MariaDB's 63-character identifier limit, which contract
ids can exceed on their own.

## Idempotency window

Applied operation ids are kept for `OperationDedupeWindow` (7 days by default), then pruned by
age — which is why a ULID is the intended shape for an `opId`: sortable ids make age-based
pruning possible.

A client offline longer than the window and then retrying re-applies its writes. Harmless,
because writes are keyed upserts: the data ends up identical and it costs one round trip.
