# Liveness and readiness (NexusSyncServer.Hosting)

Two probes that look similar and mean opposite things to an orchestrator.

| Endpoint | Question | Failure means |
|---|---|---|
| `/health` | Is the process alive and answering? | **Restart the container** |
| `/ready` | Can it serve traffic right now? | **Take it out of rotation** |

## Why the distinction is load-bearing

The obvious-looking implementation checks the database in `/health`. Do that, and a database
outage restarts every server instance in a loop — a healthy process killed repeatedly for a
fault that has nothing to do with it, which also throws away warm connections and caches at
exactly the moment recovery needs them.

Reported through `/ready` instead, the same outage takes instances out of the load balancer and
puts them back by themselves when the database returns. Nothing restarts, nothing loses state.

So `/health` answers from nothing but the process being able to respond, and everything with a
dependency goes in an `IReadinessCheck`.

## Contributing a check

```csharp
public sealed class QueueReadinessCheck : IReadinessCheck
{
    public string Name => "queue";

    public async Task<string?> CheckAsync(CancellationToken ct) =>
        await _queue.IsReachableAsync(ct) ? null : "unreachable";
}
```

Registered like anything else:

```csharp
services.AddSingleton<IReadinessCheck, QueueReadinessCheck>();
```

Null means ready. A string is the reason, and it is returned to the caller — so keep
connection strings and credentials out of it.

A check that throws counts as failed rather than escaping as a 500. An orchestrator reads both
the same way, but a person reads a 500 as "the readiness endpoint is broken", which sends them
looking in the wrong place.

## The response

```jsonc
// 200
{ "status": "ready" }

// 503
{ "status": "not-ready", "failures": { "database": "cannot connect" } }
```

## In the container

The Dockerfile's `HEALTHCHECK` probes `/health` by running the same binary with
`--healthcheck`. The aspnet base image ships neither curl nor wget, and adding one to probe
your own process is a package and a CVE surface for something the process can already do.

`/ready` is deliberately **not** wired to `HEALTHCHECK` — Docker's healthcheck restarts
containers, which is the liveness behaviour. Point your orchestrator's readiness probe at it
instead.
