using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Hosting.Persistence;
using NexusSyncServer.Modules.Storage.MariaDb.Records;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// MariaDB storage: the shared <see cref="ServerDbContext"/>, the generic record store, the
/// migration runner, and the database readiness check.
/// <para>Register this first. Every other module's <see cref="IEntityModule"/> is collected by
/// the context this one owns, and a module registered before it would still work — the context
/// resolves them all from DI — but the ordering keeps the composition root readable.</para>
/// </summary>
public sealed class StorageMariaDbModule : IServerModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.storage.mariadb";

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var options = new StorageOptions();
        context.Configuration.GetSection(StorageOptions.SectionName).Bind(options);

        // Fail at startup rather than on the first request. A server that boots, passes its
        // liveness probe and only then reveals it has no database is strictly harder to
        // diagnose than one that refuses to start with a message naming the missing setting.
        options.Validate();

        // Validate() has just established this is non-empty, which is what lets the
        // non-nullable provider argument be satisfied without another check.
        var connectionString = options.ConnectionString!;

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        services.AddDbContext<ServerDbContext>(db =>
        {
            db.UseMySQL(connectionString);
            if (context.IsDevelopment) db.EnableSensitiveDataLogging();
        });

        services.AddSingleton<IEntityModule, StorageEntityModule>();
        services.AddScoped<IRecordStore, RecordStore>();
        services.AddScoped<MigrationRunner>();
        services.AddSingleton<IReadinessCheck, DatabaseReadinessCheck>();
        services.AddHostedService<StorageStartupService>();
    }
}
