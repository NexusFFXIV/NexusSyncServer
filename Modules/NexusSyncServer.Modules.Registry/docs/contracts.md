# Writing and registering a contract (NexusSyncServer.Modules.Registry)

A contract declares what may be stored and read. Registering one provisions storage,
endpoints, scopes and indexes from it — no server code, no rebuild.

## The showcase

[`example.showcase.json`](example.showcase.json) exists to be read, not run. It is a
**deliberately generic example**: `example.showcase`, `observations`, `reference_items`. Nothing
in it describes a real plugin, and it is kept out of the mounted `contracts/` directory so it
never gets registered on somebody's server by accident.

```jsonc
{
  "contractId": "example.showcase",
  "version": "1.0",
  "collections": [
    {
      "name": "observations",
      "direction": "uplink",              // client → server
      "key": "observation_id",
      "fields": {
        "observation_id": { "type": "guid",      "required": true },
        "label":          { "type": "string",    "required": true, "maxLength": 64 },
        "count":          { "type": "integer",   "min": 0, "max": 10000 },
        "score":          { "type": "number",    "min": 0, "max": 1 },
        "confirmed":      { "type": "boolean" },
        "observed_at":    { "type": "timestamp" }
      },
      "indexed":   ["label"],             // gets a generated column with an index on it
      "rateLimit": { "perMinute": 60 },
      "retention": "90d"
    },
    {
      "name": "reference_items",
      "direction": "downlink",            // server → client
      "key": "id",
      "fields": { /* … */ }
    }
  ]
}
```

Between the two collections it covers every field type, both directions, keys, constraints,
declared indexes, a rate limit and a retention. Copy it, rename everything, delete what you do
not need.

> The real file carries no comments — the parser rejects unknown properties, and JSON has no
> comment syntax. The annotations above are for reading only.

## Registering it

Contracts arrive through a **mounted directory**, never baked into the image:

```yaml
volumes:
  - ./contracts:/contracts:ro
```

Drop `*.json` files in and restart. Each is registered if new, ignored if unchanged, and
refused with a logged reason if it would break peers already on that major version.

That directory is the deployment's contract set — a thing you can version, review in a pull
request and roll back. Baking contracts into the image would make changing one a rebuild, which
is the opposite of the point.

An empty directory is fine. The database is authoritative: a contract registered yesterday
keeps being served whether or not its file is still there today. **Deleting a file does not
unregister the contract** — clients have negotiated against it, and pulling it out from under
them silently is worse than leaving it.

## Rules worth knowing before you write one

**Contract ids need at least two dot-separated segments** — `acme.tracker`, not `tracker`. The
leading segment is your namespace; without it two unrelated authors both calling their contract
`tracker` collide the moment both land on one server.

**Names are lowercase letters, digits and underscores.** Narrower than JSON allows, because
these become storage paths, index names, scope strings and URL segments.

**Directions are separate collections.** An uplink and a downlink are different datasets, not
two sides of one. Nothing has to correspond between them.

**A registered version is immutable.** Re-registering the identical document is a no-op;
registering a *different* document under the same version is refused. Clients have cached that
shape. Publish a new minor instead.

**Within a major, changes must be additive.** Adding optional fields and widening bounds is
fine. Removing a field, changing a type, adding a required field or tightening a bound is not —
those need a new major, and both majors then coexist.

The full rules live in
[`NexusKit.Sync/docs/contracts.md`](https://github.com/NexusFFXIV/NexusKit/blob/main/NexusKit.Sync/docs/contracts.md)
and [`versioning.md`](https://github.com/NexusFFXIV/NexusKit/blob/main/NexusKit.Sync/docs/versioning.md).

## Checking what a server speaks

Both endpoints are public — a contract describes shapes, not data:

```bash
curl http://localhost:8080/v1/contracts
curl http://localhost:8080/v1/contracts/example.showcase
```

The second returns the canonical document and its hash, which is what a client compares against
at handshake.
