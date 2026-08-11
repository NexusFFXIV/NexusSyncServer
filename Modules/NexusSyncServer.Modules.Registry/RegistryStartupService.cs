using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusKit.Sync.Contracts;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Loads the in-memory snapshot, then registers any contract documents found on disk.
/// </summary>
internal sealed class RegistryStartupService : IHostedService
{
    private readonly IContractRegistry mRegistry;
    private readonly RegistryOptions mOptions;
    private readonly ILogger<RegistryStartupService> mLog;

    public RegistryStartupService(
        IContractRegistry registry,
        IOptions<RegistryOptions> options,
        ILogger<RegistryStartupService> log)
    {
        mRegistry = registry;
        mOptions = options.Value;
        mLog = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Database first. Contracts already registered must be servable even if the directory
        // is empty, missing, or not mounted at all.
        await mRegistry.RefreshAsync(cancellationToken).ConfigureAwait(false);

        var directory = mOptions.ContractsDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            mLog.LogInformation(
                "No contract directory at '{Directory}'; serving {Count} contract(s) from the database.",
                directory, mRegistry.ContractIds.Count);
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            await LoadAsync(path, cancellationToken).ConfigureAwait(false);

        mLog.LogInformation("Registry ready with {Count} contract(s).", mRegistry.ContractIds.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task LoadAsync(string path, CancellationToken ct)
    {
        SyncContract contract;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            contract = ContractJson.Parse(json);
        }
        catch (Exception ex) when (ex is ContractDefinitionException or IOException)
        {
            Fail(path, ex.Message);
            return;
        }

        var result = await mRegistry.RegisterAsync(contract, ct).ConfigureAwait(false);

        switch (result.Status)
        {
            case RegistrationStatus.Registered:
                mLog.LogInformation("Registered {Contract} {Version} from {File}",
                    contract.ContractId, contract.Version, Path.GetFileName(path));
                break;

            case RegistrationStatus.Unchanged:
                // The normal case on every restart after the first. Debug, not information —
                // otherwise every boot logs a line per contract saying nothing happened.
                mLog.LogDebug("{Contract} {Version} already registered, unchanged.",
                    contract.ContractId, contract.Version);
                break;

            default:
                Fail(path, result.ToString());
                break;
        }
    }

    private void Fail(string path, string reason)
    {
        if (mOptions.FailOnInvalidContract)
            throw new InvalidOperationException($"Contract file '{path}' could not be registered: {reason}");

        mLog.LogError("Contract file {File} could not be registered and was skipped: {Reason}",
            Path.GetFileName(path), reason);
    }
}
