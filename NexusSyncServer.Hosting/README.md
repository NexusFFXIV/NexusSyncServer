# NexusSyncServer.Hosting

The module model and the composition root. **Reference this to write your own server module.**

Mirrors `IPluginModule` on the client side deliberately: an author who has written a NexusKit
module already knows the shape, and the two halves of a feature end up looking like each other.

## Public API

| Type | File | Purpose |
|---|---|---|
| `IServerModule` | `Modules/IServerModule.cs` | One composable piece of the server: an id and a `Register`. |
| `IServerContext` | `Modules/IServerContext.cs` | What a module may read while registering — configuration and environment, nothing more. |
| `IEndpointModule` | `Modules/IEndpointModule.cs` | Contributes HTTP routes (Carter's shape: one method, given the route builder). |
| `IPortalPageModule`, `PortalPage` | `Modules/` | Contributes pages: the assembly holding routable components, plus navigation entries. |
| `IReadinessCheck` | `Modules/IReadinessCheck.cs` | Answers "can this instance serve traffic yet?" — distinct from liveness. |
| `ModuleCatalog` | `Modules/ModuleCatalog.cs` | Which modules this instance was built with. |
| `IEntityModule` | `Persistence/IEntityModule.cs` | The module's **own** tables, under its own table-name prefix. |
| `IMigrationModule`, `IMigration` | `Persistence/` | Per-module schema evolution. |
| `IMaintenanceContributor` | `Persistence/IMaintenanceContributor.cs` | Periodic housekeeping, driven from one shared loop. |
| `NexusSyncServerBuilder` | `NexusSyncServerBuilder.cs` | Collects modules. Obtained from `AddNexusSyncServer`. |
| `NexusSyncServerHostingExtensions` | `NexusSyncServerHostingExtensions.cs` | `AddNexusSyncServer` and `UseNexusSyncServer`. |

## Composing a server

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddNexusSyncServer(hub => hub
    .AddModule<StorageMariaDbModule>()   // owns the DbContext others contribute to
    .AddModule<RegistryModule>()
    .AddModule<AuthModule>()
    .AddModule<XivAuthModule>()
    .AddModule<ApiModule>());             // resolves the others, so it goes last

var app = builder.Build();

app.UseNexusSyncServer<App>();   // with the web interface, rooted at your App component
// app.UseNexusSyncServer();     // headless: API only, no pages

await app.RunAsync();
```

## Writing a module

```csharp
public sealed class WidgetModule : IServerModule, IEndpointModule
{
    public string Id => "acme.widgets";

    public void Register(IServiceCollection services, IServerContext context)
    {
        services.AddSingleton<IEntityModule, WidgetEntityModule>();
        services.AddScoped<IWidgetService, WidgetService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/widgets", (IWidgetService svc) => svc.All());
}
```

Do not resolve anything inside `Register` — the container is still being built. Work that needs
a running service belongs in an `IHostedService` the module registers.

## Two decisions worth knowing

**Composition is static.** Modules are referenced as packages and named in the composition
root; there is no runtime assembly loading, and adding it is not a wanted feature. Foreign code
in a server process is an attack surface and an operational problem — version conflicts,
partial failures, unclear migration order. The flexibility users need comes from *registrable
contracts*, which are data, not code. Building an image with your own modules is two lines of
Dockerfile.

**`IEntityModule` is for the module's own tables.** Contract-defined user data does not go
through it — that lives in the generic record store, which is precisely what allows a contract
to be registered at runtime without anyone writing a migration.

## Further reading

| Document | What it covers |
|---|---|
| [docs/modules.md](docs/modules.md) | The seams in detail, lifecycle, and what belongs where |
| [docs/health.md](docs/health.md) | Liveness vs readiness, and why the distinction changes what an orchestrator does |

## License

**AGPL-3.0-only.**
