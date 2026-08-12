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

        var documents = Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        foreach (var path in documents)
            await LoadAsync(path, cancellationToken).ConfigureAwait(false);

        mLog.LogInformation("Registry ready with {Count} contract(s).", mRegistry.ContractIds.Count);

        WarnAboutOrphans(documents, cancellationToken);
    }

    /// <summary>
    /// Names registered contracts that no document backs any more.
    /// <para>The directory delivers contracts; it does not mirror them. A contract is the
    /// promise a client is already sending data under, and records in storage refer to it —
    /// so a file that disappears must not take the registration with it. A mistyped mount
    /// path would otherwise retire every contract on the server, and Docker creates a missing
    /// bind-mount source as an empty directory without complaining.</para>
    /// <para>What was missing is not the removal but the notice. Starting with "ready with 2
    /// contract(s)" while the directory is empty invites exactly the conclusion that something
    /// is broken, and leaves the operator to reconstruct the truth from the database.</para>
    /// </summary>
    private void WarnAboutOrphans(IReadOnlyList<string> documents, CancellationToken ct)
    {
        if (mRegistry.ContractIds.Count == 0) return;

        var onDisk = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in documents)
        {
            try
            {
                onDisk.Add(ContractJson.Parse(File.ReadAllText(path)).ContractId);
            }
            catch (Exception ex) when (ex is IOException or ContractDefinitionException)
            {
                // Already reported by LoadAsync. Counting it as absent here would produce a
                // second complaint about the same file, worded as if it were a different fault.
            }
        }

        var orphans = mRegistry.ContractIds.Where(id => !onDisk.Contains(id)).ToArray();
        if (orphans.Length == 0) return;

        mLog.LogWarning(
            "{Count} registered contract(s) have no document in {Directory}: {Contracts}. "
            + "They stay registered and served — clients built against them keep working, and "
            + "their stored records keep their meaning. Delete the rows from registry_contracts "
            + "to retire one.",
            orphans.Length,
            mOptions.ContractsDirectory,
            string.Join(", ", orphans));
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
