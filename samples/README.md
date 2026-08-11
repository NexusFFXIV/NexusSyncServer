# contracts/

**Mount point, not content.** Put your `*.json` contract documents here and the server registers
them at startup.

This directory ships empty on purpose. The image contains no contracts at all — `.dockerignore`
excludes both this directory and `samples/`, so nothing is baked in and the same published image
serves every author. Working examples live in [`../samples/`](../samples/); copy one here to try
the server out.

## What a contract is

A contract declares named datasets — **collections** — and which way each one flows. It is data,
not code: registering a document provisions storage, endpoints and permissions from it, so
adding a collection costs no server code and no rebuilt image.

```jsonc
{
  "contractId": "acme.myplugin",     // author namespace + name, at least two parts
  "version": "1.0",                  // major.minor — no patch; a contract has no bugs to fix
  "collections": [
    {
      "name": "reports",
      "direction": "uplink",         // uplink: the plugin writes. downlink: the plugin reads.
      "key": "item_id",              // required, and one of string / integer / guid
      "fields": {
        "item_id": { "type": "string", "required": true, "maxLength": 64 },
        "rating":  { "type": "integer", "min": 1, "max": 5 },
        "note":    { "type": "string", "maxLength": 500 }
      },
      "indexed":   ["item_id"],      // each becomes a generated column with an index
      "rateLimit": { "perMinute": 60 },
      "retention": "180d"            // 180d / 12h / 30m / 45s — omit to keep forever
    }
  ]
}
```

### The rules that catch people out

**Directions are separate collections, not two sides of one.** A plugin may push three uplinks
and read one downlink with nothing corresponding between them. There is no bidirectional
direction, because it would force conflict resolution into every implementation.

**Six field types**, each mapping onto exactly one JSON representation: `string`, `integer`,
`number`, `boolean`, `timestamp`, `guid`.

- `timestamp` **requires an explicit UTC offset** — a trailing `Z` or `±hh:mm`. A value without
  one is rejected rather than assumed, because a client in Berlin and one in Tokyo sending
  `2026-08-04T12:00:00` mean instants nine hours apart.
- `ulong` has no `integer` mapping. Values above `long.MaxValue` have no JSON number form that
  every implementation reads back identically, so use `string`. FFXIV ContentIds live exactly
  in that range.

**Names are narrower than JSON allows.** Contract ids are dot-separated lowercase segments with
at least two parts; collection and field names are lowercase letters, digits and underscores,
starting with a letter. These names become storage paths, index names, scope strings and URL
segments, each with its own idea of what is legal.

**Constraints are enforced server-side on every write.** A client may check too, for a faster
error, but it is never the authority — the contract exists so a forged or outdated client cannot
store what it forbids.

**Scopes are computed, never declared.** Each collection implies exactly one:
`reports:push` for an uplink, `items:pull` for a downlink. A hand-written list would drift from
the collections it guards.

## Loading them

Mount the directory read-only and restart:

```yaml
services:
  server:
    image: ghcr.io/nexusffxiv/nexussyncserver:latest
    volumes:
      - ./contracts:/contracts:ro
```

Registration happens at startup and is **idempotent** — the same document re-registers to no
effect, so restarts and redeploys are free. Configurable through
`Registry__ContractsDirectory`, which defaults to `/contracts`.

Read-only is deliberate: the server never writes here, and a writable mount would let a bug in
the container edit the definitions it is being held to.

## Changing one

Within a major version, changes must be **additive**. Adding a field is fine; removing one,
renaming one, tightening a type or making an optional field required is not — the registry
refuses it, because clients built against the old shape are still out there.

Raise the **minor** for additions. Raise the **major** for anything else, and expect to run both
versions until the old clients are gone; the registry keeps them side by side, and the handshake
picks the highest minor a client and the server both know within the same major.

Validate before you deploy:

```bash
SyncProbe contract ./contracts/acme.myplugin.json
```

That parses the document, prints the canonical form and its hash, and lists the scopes it
implies — all offline, with no server involved.
