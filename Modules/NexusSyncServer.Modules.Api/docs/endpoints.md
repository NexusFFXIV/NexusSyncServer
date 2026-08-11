# Endpoints (NexusSyncServer.Modules.Api)

The four operations, their failure modes, and what a client should do about each.

## Handshake

```http
POST /v1/handshake
Authorization: Bearer nxs_…

{ "contractId": "example.showcase", "version": "1.0",
  "contractHash": "e13c…", "clientAgent": "MyPlugin/1.0", "protocolVersion": 1 }
```

```jsonc
{
  "negotiatedVersion": "1.0",
  "serverContractHash": "e13c…",
  "grantedScopes": ["observations:push", "reference_items:pull"],
  "limits": { "maxRecordsPerPush": 500, "maxPayloadBytes": 262144, "maxRecordsPerPull": 1000 }
}
```

The server picks the **highest registered minor sharing the client's major**. A client asking
for 1.4 against a server on 1.2 gets 1.2 and must not use anything newer.

`contractHash` is carried for diagnosis, not as a gate. Matching on it would lock out every
deployed client on any trivial edit; seeing both hashes in one log line is what turns a
mismatch from a mystery into a diff.

`grantedScopes` is the key's scopes intersected with what the contract declares. A key may
carry fewer than the contract implies — treat a missing scope as a disabled feature, not an
error to retry. The user simply did not grant it.

| Failure | Status | Meaning |
|---|---|---|
| `protocol-unsupported` | 400 | The client speaks a wire version this server does not |
| `unauthenticated` | 401 | No key, unknown key, revoked, or expired |
| `scope-missing` | 403 | The key is restricted to a different contract |
| `unknown-contract` | 404 | Nothing registered under that id |
| `contract-mismatch` | 409 | Registered, but no version shares the client's major |
| `limit-exceeded` | 429 | Over the key's rate limit |

## Push

```http
POST /v1/example.showcase/observations/push
Authorization: Bearer nxs_…

{ "contractId": "example.showcase", "version": "1.0", "collection": "observations",
  "records": [ { "opId": "01JZ…", "key": "abc", "payload": { … } } ] }
```

```jsonc
{
  "outcomes": [
    { "opId": "01JZ0001", "status": "accepted" },
    { "opId": "01JZ0002", "status": "rejected",
      "problems": [ { "field": "count", "message": "Value 99999 is above the maximum 10000." } ] }
  ]
}
```

**The call succeeds even when records are rejected.** Inspect the outcomes; a 200 does not mean
everything was stored.

| Status | What the client does |
|---|---|
| `accepted` | Drop the outbox entry |
| `duplicate` | Drop it — already applied under this `opId`, the data is there |
| `rejected` | Do not retry unchanged; quarantine or discard |

Deletes are records with `deleted: true` and no payload.

A batch over `maxRecordsPerPush` is refused **whole** (413) rather than truncated. A partially
applied batch the client believes was applied entirely is the worse failure.

## Pull

```http
GET /v1/example.showcase/reference_items/pull?version=1.0&since=42&limit=100
Authorization: Bearer nxs_…
```

```jsonc
{
  "changes": [
    { "key": "item-1", "payload": { … }, "deleted": false,
      "sequence": 43, "revision": 2, "updatedAt": "2026-08-04T12:00:00+00:00" }
  ],
  "nextCursor": 43,
  "hasMore": false
}
```

Take `nextCursor` from the response rather than computing it from the last change — an empty
page still needs a cursor, and a server that skipped sequences would otherwise leave the client
re-requesting the same gap forever.

Loop while `hasMore` is set instead of waiting for the next poll interval.

## Contract discovery

```http
GET /v1/contracts
GET /v1/contracts/example.showcase?version=1.0
```

No authentication. The second returns the canonical document and its hash — everything a client
needs to verify what a server speaks before committing to it.

## Direction violations

Pushing to a downlink or pulling an uplink returns 403 `direction-violation`, not 404. The
collection exists; the operation does not apply to it, and saying so precisely saves an author
from hunting a typo that is not there.

## Rate limiting

429 `limit-exceeded`, counted per key. `SyncProtocolException.IsTransient` marks it as worth
retrying later — unlike most protocol problems, which will produce the identical answer next
time.

The limiter is in-memory and therefore **per instance**: with two replicas the effective budget
doubles. Fine as a guard against a runaway client, not usable as a quota.
