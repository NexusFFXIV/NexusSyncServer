using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// Runs migrations once, at startup, before the server begins serving.
/// </summary>
internal sealed class StorageStartupService : IHostedService
{
    private readonly IServiceProvider mServices;
    private readonly ILogger<StorageStartupService> mLog;

    public StorageStartupService(IServiceProvider services, ILogger<StorageStartupService> log)
    {
        mServices = services;
        mLog = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = mServices.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();

        // Deliberately not wrapped in a try/catch. A database that cannot be migrated is a
        // server that must not accept writes, and starting anyway would mean answering
        // requests against a schema nobody has verified. Failing here stops the container,
        // which is the visible outcome an operator can act on.
        await runner.RunAsync(cancellationToken).ConfigureAwait(false);

        mLog.LogInformation("Storage ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
