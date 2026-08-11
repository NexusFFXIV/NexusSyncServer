using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Seeds operator accounts and the bootstrap key, if configured.
/// </summary>
internal sealed class AuthStartupService : IHostedService
{
    private readonly IServiceScopeFactory mScopes;
    private readonly AuthOptions mOptions;
    private readonly ILogger<AuthStartupService> mLog;

    public AuthStartupService(
        IServiceScopeFactory scopes,
        IOptions<AuthOptions> options,
        ILogger<AuthStartupService> log)
    {
        mScopes = scopes;
        mOptions = options.Value;
        mLog = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        await SeedOperatorsAsync(db, cancellationToken).ConfigureAwait(false);
        await SeedBootstrapKeyAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedOperatorsAsync(ServerDbContext db, CancellationToken ct)
    {
        foreach (var entry in mOptions.OperatorIdentities.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var separator = entry.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == entry.Length - 1)
            {
                mLog.LogError(
                    "Operator identity '{Entry}' is not in provider:subject form and was ignored.", entry);
                continue;
            }

            var provider = entry[..separator];
            var subject = entry[(separator + 1)..];

            var identity = await db.Set<AccountIdentityEntity>()
                .FirstOrDefaultAsync(i => i.Provider == provider && i.Subject == subject, ct).ConfigureAwait(false);

            if (identity is null)
            {
                // The person has not signed in yet. Not an error — this is the normal state on
                // a fresh deployment, and their first sign-in will find the entry.
                mLog.LogInformation(
                    "Operator identity {Entry} is configured but has not signed in yet.", entry);
                continue;
            }

            var account = await db.Set<AccountEntity>()
                .FirstOrDefaultAsync(a => a.Id == identity.AccountId, ct).ConfigureAwait(false);

            if (account is null || account.IsOperator) continue;

            account.IsOperator = true;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            mLog.LogInformation("Promoted {Entry} to operator.", entry);
        }
    }

    private async Task SeedBootstrapKeyAsync(ServerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mOptions.BootstrapKeyFile)) return;

        if (!File.Exists(mOptions.BootstrapKeyFile))
        {
            // Configured but absent means the secret was not mounted. Loud, because the
            // operator is expecting a working key and would otherwise find out at the client.
            mLog.LogError(
                "Auth:BootstrapKeyFile points at '{Path}', which does not exist. No key was seeded.",
                mOptions.BootstrapKeyFile);
            return;
        }

        var key = (await File.ReadAllTextAsync(mOptions.BootstrapKeyFile, ct).ConfigureAwait(false)).Trim();

        if (!ApiKeyFormat.IsWellFormed(key))
        {
            mLog.LogError(
                "The bootstrap key file does not contain a well-formed key ({Prefix} followed by {Length} characters). "
                + "No key was seeded.",
                ApiKeyFormat.Prefix, ApiKeyFormat.BodyLength);
            return;
        }

        var hash = ApiKeySecret.Hash(key);
        var keyId = ApiKeySecret.KeyIdOf(key);

        // Idempotent by hash, so restarts and redeploys are no-ops rather than a growing pile
        // of identical keys.
        var exists = await db.Set<ApiKeyEntity>()
            .AnyAsync(k => k.KeyHash == hash, ct).ConfigureAwait(false);

        if (exists) return;

        const string bootstrapName = "bootstrap";

        var account = await db.Set<AccountEntity>()
            .FirstOrDefaultAsync(a => a.DisplayName == bootstrapName, ct).ConfigureAwait(false);

        if (account is null)
        {
            account = new AccountEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = bootstrapName,
                IsOperator = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Add(account);
        }

        db.Add(new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            KeyHash = hash,
            AccountId = account.Id,
            ContractId = mOptions.BootstrapKeyContract,
            Scopes = mOptions.BootstrapKeyScopes.ToList(),
            Label = "seeded from bootstrap secret",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Redacted — the plaintext was read from the secret and is deliberately not written
        // anywhere, including here.
        mLog.LogWarning(
            "Seeded bootstrap key {KeyId} with scopes [{Scopes}]. Issue a proper key and remove "
            + "the bootstrap secret once the deployment is up.",
            ApiKeyFormat.Redact(key), string.Join(", ", mOptions.BootstrapKeyScopes));
    }
}
