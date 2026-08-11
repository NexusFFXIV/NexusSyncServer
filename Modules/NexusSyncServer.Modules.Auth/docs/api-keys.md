# Getting an API key (NexusSyncServer.Modules.Auth)

A client needs a key to do anything but read contract documents. There are three ways to get
one; **two exist today**, and the third is the one most people will eventually use.

| Way | Status | For |
|---|---|---|
| [CLI](#1-cli) | **works** | Operators, recovery, small deployments |
| [Bootstrap secret](#2-bootstrap-secret) | **works** | Unattended and scripted provisioning |
| [Self-service portal](#3-self-service-portal) | **works** | End users signing in themselves |

## 1. CLI

The direct route. Runs against the same database and configuration as the server, then exits
without ever listening.

```bash
docker compose exec server /app/NexusSyncServer --issue-key \
    --scopes observations:push,reference_items:pull \
    --contract example.showcase \
    --label "my plugin, my machine"
```

```
  account : 531ba3e6-69b8-44e5-970c-4113fd3b0873
  scopes  : observations:push, reference_items:pull
  contract: example.showcase

  This key is shown once and is not recoverable:

  nxs_aqsb0068vfrx1q97rwbp5y1zfdp1zk38
```

| Flag | Meaning |
|---|---|
| `--scopes` | Required. Comma-separated. Bare scopes are qualified against `--contract`; without it they are refused, because a bare scope on an unrestricted key grants nothing. Write them qualified — `example.showcase/observations:push` — for a key spanning contracts. The built-in `contract:pull` needs no contract. |
| `--contract` | Restricts the key to one contract, and qualifies any bare scopes against it. Ignored when the scopes name more than one contract — such a key cannot carry a single restriction. |
| `--label` | Free text, so several keys are tellable apart later. |
| `--operator` | Marks the owning account as an operator — may register contracts. |

It runs migrations first, so it works against an empty database. That matters: bootstrapping a
fresh deployment is exactly when you need it.

> On Windows with Git Bash, prefix the command with `MSYS_NO_PATHCONV=1` — otherwise
> `/app/NexusSyncServer` is rewritten into a Windows path before Docker sees it.

## 2. Bootstrap secret

For a deployment that should come up without anyone typing a command.

Generate a key, mount it as a secret, name it in configuration:

```bash
# any 32 characters from 0123456789abcdefghjkmnpqrstvwxyz — note there is no i, l, o or u
openssl rand -hex 16 | tr -d '\n' | tr 'ilou' 'jkmn' | sed 's/^/nxs_/' > secrets/bootstrap_key
```

```yaml
# .env
BOOTSTRAP_KEY_FILE=/run/secrets/bootstrap_key
BOOTSTRAP_KEY_SCOPE_0=reference_items:pull
BOOTSTRAP_KEY_CONTRACT=example.showcase
```

Seeding is idempotent by hash, so restarts and redeploys change nothing.

**Why a mounted file and not an environment variable.** Environment variables show up in
`docker inspect`, in the compose file, in shell history, and in `/proc` for anything running as
the same user. A mounted secret is none of those. The server reads the file once, stores only
the SHA-256, and never writes the plaintext anywhere — including its own logs, where it appears
as `nxs_bcde…789a`.

Remove the secret once a real key has been issued. The server logs a warning at every start
until you do.

## 3. Self-service portal

Sign in at `/account/signin`, then manage keys at `/account/keys`. One button per configured
provider; a server with none says so rather than showing an empty box.

`/account/keys` requires a session and sends anonymous visitors to the provider picker,
returning them afterwards to the page they wanted.

**Permissions are picked, not typed.** The form lists every scope the registered contracts
imply, grouped per contract, each with what granting it actually does — how many fields, how
long records are kept, how many writes a minute. All of it derived from the contract, so a
description cannot promise something the server does not enforce. A server composed without a
registry has no such list and falls back to a text field.

**One key may span several contracts.** Useful when a plugin offers a single field to paste a
token into. Scopes are then stored qualified — `example.showcase/observations:push` — because
two contracts may each declare a collection called `reports`, and a key covering both has to
say which one it means. Tick within one contract and the key stays pinned to it.

**`contract:pull` is built in.** It belongs to no contract and is offered separately. With it a
client can fetch contract documents from this server, and the server's version of a contract
becomes the authoritative one. Without it the endpoints answer `403`, and the client must ship
the contract itself.

### Expiry

Optional on creation — empty means the key never expires. A date means the end of that day.

An expiry can be **brought forward** afterwards through the pencil in the key's row, or set on
a key that had none. It cannot be pushed further out: a key already in somebody's hands was
handed over with a lifetime attached, and extending it grants access nobody reviewed, possibly
to a secret that is already somewhere it should not be. Create a new key for that.

Expired keys are refused at the door — the authenticator checks the timestamp, so no sweep or
auto-revocation is involved and the row survives as a record of what the key did.

### Renew

Replaces a key's secret **in place**. Label, scopes, contract and expiry stay; the old secret
stops working immediately, and the list does not grow. The remaining lifetime carries over
rather than resetting — renewing replaces a secret, it does not extend a grant.

Usage timestamps are cleared, because they described the secret that no longer exists. The
cost is worth naming: after a rotation the row's history covers both secrets, so a leaked old
one can no longer be told apart from the new one.

## Sessions are checked against the database

A sign-in cookie is self-contained, so nothing about it would normally consult the database
again. That is checked on **every request** instead: an account that has been deleted or has
`disabled_at` set is signed out on its next request and its cookie cleared.

Setting `disabled_at` in `auth_accounts` is therefore all it takes to lock somebody out — no
restart, no waiting for a cookie to expire. Without the check the flag would have been
decorative until then.

## What the client does with the key

The user pastes it into the plugin's settings. On that side it is:

- kept in its own store, excluded from settings export
- shown as a password field with a reveal toggle
- encrypted with DPAPI (`ProtectedData`, CurrentUser scope)
- validated on entry by a probe handshake, so a typo is caught immediately

See `NexusKit.Modules.Sync/docs/connections.md`.

## Why the server cannot show you a key again

Only `SHA-256(key)` is stored. The plaintext exists exactly once — in the response that created
it.

That is deliberate, and it is what makes several other things true: a database dump cannot be
replayed as credentials, an operator with full database access still cannot impersonate a user,
and a leaked backup is not a leaked credential set. The cost is that "show me my key again" has
no answer. Rotate instead: issue a new key, update the client, revoke the old one.

## Revocation

Revoking sets a timestamp rather than deleting the row — the audit trail of what that key did
stays joinable only while the key exists.

Revocation takes effect within `Auth:ValidationCacheLifetime` (30 seconds by default). That
window is the trade for not querying the database on every single request; shorten it if you
would rather pay the queries.

Disabling an *account* rejects every key it holds at once, without revoking them one at a time.
