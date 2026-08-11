using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Modules;

namespace NexusSyncServer.Hosting;

/// <summary>
/// The composition root: <c>AddNexusSyncServer</c> at registration, <c>UseNexusSyncServer</c> at routing.
/// <para>Built on <see cref="WebApplicationBuilder"/> rather than a bespoke host builder like
/// the client side's <c>PluginHostBuilder</c>. A Dalamud plugin has no host of its own and
/// needs one; ASP.NET Core already provides configuration, DI, logging and lifetime, and
/// wrapping them would only hide facilities an operator expects to find.</para>
/// </summary>
public static class NexusSyncServerHostingExtensions
{
    /// <summary>Registers the modules this instance is composed of.</summary>
    /// <example>
    /// <code>
    /// builder.AddNexusSyncServer(hub => hub
    ///     .AddModule&lt;StorageMariaDbModule&gt;()
    ///     .AddModule&lt;RegistryModule&gt;()
    ///     .AddModule&lt;AuthModule&gt;()
    ///     .AddModule&lt;ApiModule&gt;());
    /// </code>
    /// </example>
    public static WebApplicationBuilder AddNexusSyncServer(
        this WebApplicationBuilder builder,
        Action<NexusSyncServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var context = new ServerContext(
            builder.Configuration,
            builder.Environment.EnvironmentName,
            builder.Environment.IsDevelopment());

        builder.Services.AddSingleton<IServerContext>(context);

        var hub = new NexusSyncServerBuilder(builder.Services, context);
        configure(hub);

        builder.Services.AddSingleton(new ModuleCatalog(hub.Modules.Select(m => m.Id).ToArray()));
        builder.Services.AddHostedService<MaintenanceService>();

        // Static server-side rendering only — no interactive circuit. The built-in pages list
        // and submit; neither needs one, and not having one means they work with JavaScript
        // disabled and survive a dropped connection. A module that genuinely needs
        // interactivity can add it without this getting in the way.
        builder.Services.AddRazorComponents();

        return builder;
    }

    /// <summary>
    /// Maps every module's endpoints, the container probes, and the web interface rooted at
    /// <typeparamref name="TRootComponent"/>.
    /// </summary>
    /// <typeparam name="TRootComponent">
    /// The application's root component — the one rendering the HTML document. Supplied by the
    /// host rather than by this assembly, because the document is the thing an operator most
    /// often wants to own.
    /// </typeparam>
    public static WebApplication UseNexusSyncServer<TRootComponent>(this WebApplication app)
        where TRootComponent : Microsoft.AspNetCore.Components.IComponent
    {
        ArgumentNullException.ThrowIfNull(app);

        UseNexusSyncServer(app);

        app.UseAuthentication();
        app.UseAuthorization();

        // Required for the SSR form posts the built-in pages use. Without it every form
        // submission fails with a 400 that says nothing useful.
        app.UseAntiforgery();

        app.MapStaticAssets();

        // Routes come from @page attributes in the module assemblies, so the router has to be
        // told where to look — a module's pages otherwise compile and are simply unreachable.
        var moduleAssemblies = app.Services.GetServices<IPortalPageModule>()
            .Select(m => m.ComponentAssembly)
            .Distinct()
            .ToArray();

        app.MapRazorComponents<TRootComponent>()
            .AddAdditionalAssemblies(moduleAssemblies);

        return app;
    }

    /// <summary>
    /// Maps every module's endpoints plus the container probes, without a web interface.
    /// <para>For a headless deployment — API only, no pages. Most hosts want the generic
    /// overload instead.</para>
    /// </summary>
    public static WebApplication UseNexusSyncServer(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var catalog = app.Services.GetRequiredService<ModuleCatalog>();
        app.Logger.LogInformation("NexusSyncServer composed with modules: {Modules}", catalog);

        foreach (var module in app.Services.GetServices<IEndpointModule>())
            module.MapEndpoints(app);

        MapProbes(app);
        return app;
    }

    private static void MapProbes(IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is running and can answer. Nothing else — see IReadinessCheck
        // for why touching the database here would be actively harmful.
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .AllowAnonymous();

        endpoints.MapGet("/ready", async (
            IEnumerable<IReadinessCheck> checks,
            CancellationToken ct) =>
        {
            var failures = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var check in checks)
            {
                string? reason;
                try
                {
                    reason = await check.CheckAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A check that throws is a check that failed. Letting the exception escape
                    // would turn /ready into a 500, which an orchestrator reads as the same
                    // thing but an operator reads as "the readiness endpoint is broken".
                    reason = ex.GetType().Name;
                }

                if (reason is not null) failures[check.Name] = reason;
            }

            return failures.Count == 0
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not-ready", failures }, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithName("Ready")
            .AllowAnonymous();
    }

    private sealed record ServerContext(
        IConfiguration Configuration,
        string EnvironmentName,
        bool IsDevelopment) : IServerContext;
}
