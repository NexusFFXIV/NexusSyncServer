# NexusSyncServer.Modules.Api

The sync endpoints — the module that makes this a server rather than a database with a login
page.

**Compose it last.** It resolves the registry, the record store and the authenticator.

## Public API

| Type | File | Purpose |
|---|---|---|
| `ApiModule` | `ApiModule.cs` | `IServerModule`. Maps the endpoints and aligns JSON with the protocol's own settings. |
| `SyncEndpoints` | `SyncEndpoints.cs` | *(internal)* Handshake, push, pull, contract discovery. |
| `CallerResolver` | `CallerResolver.cs` | *(internal)* Pulls the API key off a request and validates it. |
| `ProblemResults` | `ProblemResults.cs` | *(internal)* Builds the RFC 9457 responses the protocol specifies. |

## The endpoints

| Route | Auth | Scope |
|---|---|---|
| `POST /v1/handshake` | key | — |
| `POST /v1/{contract}/{collection}/push` | key | `{collection}:push` |
| `GET /v1/{contract}/{collection}/pull` | key | `{collection}:pull` |
| `GET /v1/contracts` | **none** | — |
| `GET /v1/contracts/{contract}` | **none** | — |

Discovery is unauthenticated by design: a contract document describes shapes, not data, and
being able to read one before holding a key is what lets an author check compatibility against
a server they have not signed up to yet.

Paths come from `SyncRoutes` in `NexusKit.Sync`, so client and server derive them from the same
code rather than from two string literals that agree until one is edited.

## One place for the shared checks

Push and pull both need: authenticate, confirm the key may touch this contract, negotiate the
version, find the collection, confirm the direction, confirm the scope. That sequence lives in
a single method both call.

A scope check that ran before the write on one path and after it on the other is exactly the
asymmetry nobody notices in review — and exactly the kind that turns into a disclosure.

## Errors carry what a client needs to act

Every failure a client is expected to branch on gets a stable `SyncProblemType` and, where it
helps, extension fields:

```jsonc
// 409
{
  "type": "https://nexusffxiv.dev/problems/contract-mismatch",
  "title": "Contract mismatch",
  "status": 409,
  "detail": "'acme.tracker' 9.0 has no compatible version here.",
  "serverVersions": ["1.0", "1.1"],
  "reason": "client-newer"
}
```

A bare status code would leave the caller able to report that something went wrong and nothing
more. `serverVersions` is what turns that into a message an author can act on.

Branch on `type`. `detail` is free text for humans and is not stable.

## Per-record outcomes, not per-batch verdicts

A push returns one outcome per submitted record — `accepted`, `duplicate` or `rejected` with
the validation problems attached.

One malformed record in a batch of fifty should not force the other forty-nine to be resent,
and a client that cannot tell *which* record failed has no option but to retry the whole batch
forever.

Match outcomes to records by `opId`, never by position: a server is free to reorder or coalesce
internally.

## Route and body must agree

A push names its contract and collection in both the route and the body. Disagreement is
refused rather than resolved in favour of either — it means a client bug, and accepting one
silently would turn it into a data bug instead.

## JSON

The module aligns ASP.NET's serializer with `SyncJson` from the protocol norm. Both sides using
the same settings is what keeps a field from silently arriving as `null` because one end
changed a naming policy.

## Further reading

| Document | What it covers |
|---|---|
| [docs/endpoints.md](docs/endpoints.md) | Each operation in detail, with the failure modes and what they mean |

## License

**AGPL-3.0-only.**
