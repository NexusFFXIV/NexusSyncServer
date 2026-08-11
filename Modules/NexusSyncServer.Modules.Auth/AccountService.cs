using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Modules.Auth.Providers;
using NexusSyncServer.Modules.Storage.MariaDb;

namespace NexusSyncServer.Modules.Auth;

/// <inheritdoc />
public sealed class AccountService : IAccountService
{
    private readonly ServerDbContext mDb;
    private readonly AuthOptions mOptions;
    private readonly ILogger<AccountService> mLog;

    /// <summary>Creates the service.</summary>
    public AccountService(ServerDbContext db, IOptions<AuthOptions> options, ILogger<AccountService> log)
    {
        mDb = db;
        mOptions = options.Value;
        mLog = log;
    }

    /// <inheritdoc />
    public async Task<AccountEntity> ResolveAsync(ExternalIdentity identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var now = DateTimeOffset.UtcNow;

        var link = await mDb.Set<AccountIdentityEntity>()
            .FirstOrDefaultAsync(i => i.Provider == identity.Provider && i.Subject == identity.Subject, ct)
            .ConfigureAwait(false);

        if (link is not null)
        {
            // Refresh what the provider says about them. Display names change, and a stale one
            // in the admin view is a small thing that makes the view untrustworthy.
            link.DisplayName = identity.DisplayName;
            link.AvatarUrl = identity.AvatarUrl;
            link.LastSignInAt = now;

            var existing = await mDb.Set<AccountEntity>()
                .FirstAsync(a => a.Id == link.AccountId, ct).ConfigureAwait(false);

            existing.DisplayName = identity.DisplayName ?? existing.DisplayName;
            await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = identity.DisplayName,
            // Seeded operators are promoted here rather than only at startup, so an operator
            // configured before their first sign-in gets the flag the moment they arrive.
            IsOperator = mOptions.OperatorIdentities.Any(
                e => string.Equals(e, $"{identity.Provider}:{identity.Subject}", StringComparison.Ordinal)),
            CreatedAt = now,
        };

        mDb.Add(account);
        mDb.Add(new AccountIdentityEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Provider = identity.Provider,
            Subject = identity.Subject,
            DisplayName = identity.DisplayName,
            AvatarUrl = identity.AvatarUrl,
            LinkedAt = now,
            LastSignInAt = now,
        });

        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);

        mLog.LogInformation(
            "Created account {Account} from {Identity}{Operator}",
            account.Id, identity, account.IsOperator ? " (operator)" : "");

        return account;
    }

    /// <inheritdoc />
    public async Task<AccountEntity?> FindAsync(Guid accountId, CancellationToken ct) =>
        await mDb.Set<AccountEntity>().FirstOrDefaultAsync(a => a.Id == accountId, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountIdentityEntity>> IdentitiesOfAsync(Guid accountId, CancellationToken ct) =>
        await mDb.Set<AccountIdentityEntity>()
            .AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .OrderBy(i => i.Provider)
            .ToListAsync(ct).ConfigureAwait(false);
}
