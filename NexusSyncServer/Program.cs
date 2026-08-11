using NexusSyncServer.Hosting;
using NexusSyncServer.Modules.Api;
using NexusSyncServer;
using NexusSyncServer.Modules.Auth;
using NexusSyncServer.Modules.Auth.Discord;
using NexusSyncServer.Modules.Auth.XivAuth;
using NexusSyncServer.Modules.Registry;
using NexusSyncServer.Modules.Storage.MariaDb;

// The container's HEALTHCHECK runs this same binary with --healthcheck rather than shelling
// out to curl, which the aspnet base image does not ship. Handled before anything else is
// built: the probe must not need a database, a config file or a port of its own.
if (HealthCheckCommand.IsRequested(args))
    return await HealthCheckCommand.RunAsync().ConfigureAwait(false);

var builder = WebApplication.CreateBuilder(args);

// The composition root. Modules are added at build time rather than discovered at runtime —
// see NexusSyncServer.Hosting/README.md for why. Order matters where one module's registration reads
// another's tables or options; the modules themselves resolve through DI, not through each
// other.
builder.AddNexusSyncServer(hub => hub
    // Storage first: it owns the DbContext every other module contributes its tables to.
    .AddModule<StorageMariaDbModule>()
    .AddModule<RegistryModule>()
    .AddModule<AuthModule>()

    // Sign-in providers. Each is inert unless enabled in configuration, so composing both in
    // costs nothing and lets an operator switch by editing config rather than rebuilding.
    .AddModule<XivAuthModule>()
    .AddModule<DiscordAuthModule>()

    // API last: it resolves the registry, the record store and the authenticator.
    .AddModule<ApiModule>());

var app = builder.Build();

// Key issuance runs against the same container and configuration as the server, then exits
// without ever listening. Placed after Build so it gets the real DI graph, and before
// UseNexusSyncServer so it never maps a route.
if (IssueKeyCommand.IsRequested(args))
    return await IssueKeyCommand.RunAsync(app, args).ConfigureAwait(false);

// The generic overload also maps the web interface, rooted at this project's App component.
// Use the non-generic UseNexusSyncServer() for a headless, API-only deployment.
app.UseNexusSyncServer<NexusSyncServer.Components.App>();

await app.RunAsync().ConfigureAwait(false);

// Explicit exit code: the command paths above return one, so every path here has to.
return 0;
