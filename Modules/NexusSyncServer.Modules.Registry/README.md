# NexusSyncServer.Modules.Registry

Which contracts this server speaks, at which versions. Registering one provisions storage,
endpoints, scopes and indexes from it.

Depends on the storage module for its table and for index creation — compose it after
`StorageMariaDbModule`.

**→ [Writing and registering a contract](docs/contracts.md)** is probably what you want.

## Public API

| Type | File | Purpose |
|---|---|---|
| `RegistryModule` | `RegistryModule.cs` | `IServerModule`. Registers the table, the registry and the startup loader. |
| `IContractRegistry`, `ContractRegistry` | `*.cs` | Negotiation, lookup, description, registration. Reads are synchronous from an in-memory snapshot. |
| `RegistrationResult`, `RegistrationStatus` | `RegistrationResult.cs` | What happened: registered, unchanged, conflict, incompatible. |
| `RegisteredContractEntity` | `RegisteredContractEntity.cs` | One registered version. Several rows per contract is normal — versions coexist. |
| `RegistryEntityModule` | `RegistryEntityModule.cs` | The table. |
| `RegistryOptions` | `RegistryOptions.cs` | Contract directory, and whether a bad file stops the server. |

## Configuration

```jsonc
{
  "Registry": {
    "ContractsDirectory": "/contracts",
    "FailOnInvalidContract": false
  }
}
```

## Where contracts come from

A **mounted directory**, never baked into the image:

```yaml
volumes:
  - ./contracts:/contracts:ro
```

That makes the deployment's contract set a thing you can version, review in a pull request and
roll back. Baking them in would make changing one a rebuild — the opposite of the point, since
the whole design exists so an author does not need a server deployment to change their schema.

Registration is **operator-side only**. There is no public endpoint for it, and that absence is
what removes the entire quota, tenancy and abuse surface a shared registry would need.

## Three rules the registry enforces

**A registered version is immutable.** Re-registering the identical document is a no-op — which
matters, or every restart of a server that loads from disk would fail. Registering a
*different* document under the same version is refused: clients have negotiated against it and
cached its shape, and swapping it underneath them produces validation failures nobody can trace
back to a re-registration.

**Within a major, evolution must be additive.** Checked against the highest registered minor of
that major, because that is the one peers are actually using. Adding optional fields and
widening bounds passes; removing a field, changing a type, adding a required field or
tightening a bound does not.

**Deleting a file does not unregister a contract.** The database is authoritative. A contract
registered yesterday keeps being served whether or not its file is still there — clients have
negotiated against it, and pulling it out from under them silently is worse than leaving it.

## Reads come from a snapshot

Every handshake and every push resolves a contract, so this is the hottest lookup on the
server — and contracts change roughly never. The snapshot is replaced wholesale on
registration rather than mutated, so readers never see a half-updated map and need no lock.

A stored document that no longer parses is logged and skipped rather than failing startup: one
bad row should not take down a server that is serving five other contracts fine.

## A bad file does not stop the server

By default. A server already serving three contracts should not refuse to start because a
fourth file is malformed — the failure is logged, that contract is simply unavailable, and
clients get a clean 404 on handshake instead of everyone getting an outage. Set
`FailOnInvalidContract` if you would rather fail loudly.

## Further reading

| Document | What it covers |
|---|---|
| [docs/contracts.md](docs/contracts.md) | Writing a contract, the showcase example, and the rules before you write one |
| [docs/example.showcase.json](docs/example.showcase.json) | A generic sample covering every field type and both directions |

## License

**AGPL-3.0-only.**
