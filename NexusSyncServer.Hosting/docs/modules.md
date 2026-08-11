# Modules (NexusSyncServer.Hosting)

The seams a module can implement, and what belongs in each.

## The seams

| Interface | Implement it when the module… | Called |
|---|---|---|
| `IServerModule` | …exists at all. Registers services. | Once, at registration, in composition order |
| `IEndpointModule` | …serves HTTP routes | Once, after the container is built |
| `IPortalPageModule` | …contributes pages to the web interface | Once, at routing |
| `IEntityModule` | …has tables of its own | At model creation |
| `IMigrationModule` | …evolves those tables | At startup, before serving |
| `IMaintenanceContributor` | …needs periodic housekeeping | On the shared maintenance loop |
| `IReadinessCheck` | …has a dependency that can be down | On every `/ready` |

A module class may implement several. `AuthModule` is both `IServerModule` and
`IPortalPageModule`; it registers `SignInEndpoints` separately as an `IEndpointModule`, because
routes and registration belong to different phases.

## Registration is not resolution

`Register` runs while the container is being built. Resolving a service there works until
someone changes composition order, and then fails somewhere unrelated — which is why
`IServerContext` exposes configuration and environment and nothing else.

Work that needs a running service goes in an `IHostedService`:

```csharp
public void Register(IServiceCollection services, IServerContext context)
{
    services.AddSingleton<IWidgetCache, WidgetCache>();
    services.AddHostedService<WidgetWarmupService>();   // runs after the container exists
}
```

## Tables: yours, not the users'

`IEntityModule` declares a MariaDB **schema** and maps the module's own entities into it.
The host applies the schema to everything the module configures, so implementations do not
repeat it — and cannot forget it.

```csharp
public sealed class WidgetEntityModule : IEntityModule
{
    public string SchemaName => "widgets";

    public void ConfigureEntities(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WidgetEntity>(e => { e.ToTable("widgets"); /* … */ });
}
```

The name is applied as a **table-name prefix**, so the table above is created as
`widgets_widgets` — a module mapping `state` gets `widgets_state`, and two modules can both
own a table called `state` without colliding.

A prefix rather than a real namespace because in MariaDB a schema *is* a database. One per
module would mean a connection string, a grant and a backup per module, to separate tables
that are meant to be joinable and to live or die together. `SHOW TABLES LIKE 'widgets\_%'`
answers the same question a schema would have.

**Contract-defined user data does not belong here.** That lives in the generic record store —
one JSON table shared by every contract — which is exactly what lets a contract be registered
at runtime without a migration. If storage needed a migration per collection, an author could
not add one without a server deployment, and the whole design falls over.

## Migrations run at startup

Which makes upgrades easy and rollbacks hard: an operator rolls back by pulling the previous
image tag, and that does not un-drop a column. Prefer additive steps, and say so in the release
notes when a step is destructive.

Each step runs in its own transaction, not one per module — a failure then leaves everything
before it applied and recorded, so the next start resumes instead of replaying work that
already succeeded.

Keep applied migrations in the list. Removing one does not undo it; it only makes the history
unreadable to whoever debugs the database later.

## Pages: components, not pages

A module contributing UI exposes **components**, and mounts them on a default page that is a
few lines long. That is what lets an operator run the interface as delivered or drop the same
components into one of their own — a module offering only fixed pages forces the first option.

```csharp
public Assembly ComponentAssembly => typeof(WidgetModule).Assembly;

public IEnumerable<PortalPage> Pages =>
[
    new PortalPage("/widgets", "Widgets", Order: 20),
];
```

`PortalPage` describes the **link**, not the route — Blazor discovers routes from `@page`
attributes, which is why the assembly is handed over separately. A module whose pages compile
but never appear has usually forgotten `ComponentAssembly`.

`RequiredScope` on a `PortalPage` hides a navigation entry. It does not protect anything: the
URL is reachable whether or not something linked to it, so the page itself must check.

## Housekeeping shares one loop

The host drives every `IMaintenanceContributor` from a single timer rather than each module
starting its own. One loop is one place to see what runs, one place to stop it on shutdown, and
no risk of a module leaking a timer that outlives it.

A contributor that throws is logged and skipped — housekeeping is the least important thing the
process does, and a server that stops serving because a prune job failed would be a poor trade.
Honour the cancellation token: it fires on shutdown, and ignoring it delays every restart.
