# Deployment (NexusSyncServer)

Running it for real, and the things that bite.

## Put HTTPS in front of it

The container serves plain HTTP on 8080 and is meant to sit behind a reverse proxy that
terminates TLS. That is not laziness — certificate renewal, HTTP/2 and rate limiting all belong
to something that already does them well.

API keys are bearer credentials. Over plain HTTP everyone on the path has them, which is why
the plugin client refuses a `http://` address unless explicitly overridden for localhost.

**Forward the original scheme and host.** The OAuth callback URL is derived from the incoming
request, so behind a proxy without forwarded headers it becomes the proxy's internal address
and the provider rejects the redirect:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

Deriving it rather than configuring it means a deployment does not have to declare its own
address twice — at the cost of this one requirement, which fails loudly rather than silently.

## Back up before upgrading

Migrations run at startup. That makes upgrades a `docker compose pull && up -d` and rollbacks
genuinely hard: pulling the previous image tag does not un-drop a column.

- take a database backup first — the step, not the advice
- read the release notes for an **Operator impact** section
- **pin a tag** in compose rather than tracking `latest`, so a restart never becomes an
  unplanned upgrade

## The database is not published by default

Compose leaves the MariaDB port unmapped. It has no reason to be reachable from the host;
uncomment the mapping while debugging with `psql` and comment it back out.

## Credentials

Nothing sensitive belongs in an environment variable that can be read from `docker inspect`,
the compose file, shell history, or `/proc`.

| What | Where |
|---|---|
| Database password | `.env`, or a secret manager |
| OAuth client secrets | `.env`, or a secret manager |
| Bootstrap API key | a **file**, mounted — never a variable |

`SECRETS_DIR` points at the credential directory and defaults to `./secrets` inside the repo,
which is convenient and fine locally. Point it elsewhere for anything real: a credentials
folder in a working tree is one `git add -f`, one archived copy or one file-sync client away
from leaking.

## First run

1. `docker compose up -d`
2. Drop contract documents into `./contracts` and restart, or register later
3. Get a key — `--issue-key`, or a bootstrap secret; see
   [api-keys.md](../../Modules/NexusSyncServer.Modules.Auth/docs/api-keys.md)
4. Enable a sign-in provider if users should serve themselves

A server with no sign-in provider is a valid deployment. Keys are then issued by the operator,
which is a reasonable posture for a handful of known users.

## Probes

| Endpoint | Point it at | Failure means |
|---|---|---|
| `/health` | liveness probe | restart the container |
| `/ready` | readiness probe | take it out of rotation |

Do not point a liveness probe at `/ready`. It depends on the database, and an outage would
restart every healthy instance in a loop. See
[the Hosting docs](../../NexusSyncServer.Hosting/docs/health.md).

## Scaling past one instance

Two things are per-instance today and would need attention:

- **the rate limiter** is in memory, so N replicas allow N times the configured budget. Fine as
  a runaway-client guard, not as a quota. A shared limiter belongs in Redis.
- **the key validation cache** is per instance, so a revocation takes effect within
  `Auth:ValidationCacheLifetime` on each — 30 seconds by default.

Neither breaks correctness. Both are written down rather than discovered.

## Logs

Structured, to stdout, where the container runtime collects them. Two warnings on every start
are expected in a single-container deployment rather than faults:

```
Storing keys in a directory '/home/nexussyncserver/.aspnet/DataProtection-Keys' …
No XML encryptor configured. Key {…} may be persisted to storage in unencrypted form.
```

Both are about ASP.NET data protection, which signs the sign-in cookie. The keys live inside
the container, so destroying it invalidates existing sign-in cookies and everyone signs in
again — harmless here, because nothing else is protected with them and API keys are unaffected.
It matters in exactly two cases: running more than one replica, where each would sign cookies
with its own keys, and wanting sessions to survive a redeploy. Both are fixed the same way, by
mounting a volume at that path.
