using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Auth;

/// <inheritdoc />
public sealed class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    private sealed record CacheEntry(AuthenticatedCaller Caller, Guid KeyRowId, DateTimeOffset Until);

    private readonly IServiceScopeFactory mScopes;
    private readonly AuthOptions mOptions;
    private readonly ILogger<ApiKeyAuthenticator> mLog;

    private readonly ConcurrentDictionary<string, CacheEntry> mCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RateWindow> mRates = new(StringComparer.Ordinal);

    /// <summary>Creates the authenticator.</summary>
    public ApiKeyAuthenticator(
        IServiceScopeFactory scopes,
        IOptions<AuthOptions> options,
        ILogger<ApiKeyAuthenticator> log)
    {
        mScopes = scopes;
        mOptions = options.Value;
        mLog = log;
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateAsync(string? presentedKey, string? clientAgent, CancellationToken ct)
    {
        // Shape-check before touching the database. A malformed header is the common case
        // under a scanner, and it should cost nothing.
        if (!ApiKeyFormat.IsWellFormed(presentedKey)) return AuthResult.Fail(AuthFailure.Missing);

        var hash = ApiKeySecret.Hash(presentedKey!);
        var now = DateTimeOffset.UtcNow;

        if (mCache.TryGetValue(hash, out var cached) && cached.Until > now)
        {
            return CheckRate(cached.Caller)
                ? AuthResult.Success(cached.Caller)
                : AuthResult.Fail(AuthFailure.RateLimited);
        }

        var resolved = await ResolveAsync(presentedKey!, hash, clientAgent, now, ct).ConfigureAwait(false);
        if (!resolved.Succeeded) return resolved;

        mCache[hash] = new CacheEntry(resolved.Caller!, Guid.Empty, now + mOptions.ValidationCacheLifetime);

        return CheckRate(resolved.Caller!)
            ? resolved
            : AuthResult.Fail(AuthFailure.RateLimited);
    }

    private async Task<AuthResult> ResolveAsync(
        string presentedKey,
        string hash,
        string? clientAgent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var keyId = ApiKeySecret.KeyIdOf(presentedKey);

        // Narrowed by the non-secret prefix first — the hash cannot be searched without
        // hashing every row. Several rows can share a prefix by chance, so the hash still
        // decides, in constant time.
        var candidates = await db.Set<ApiKeyEntity>()
            .Where(k => k.KeyId == keyId)
            .ToListAsync(ct).ConfigureAwait(false);

        var key = candidates.FirstOrDefault(k => ApiKeySecret.HashesMatch(k.KeyHash, hash));
        if (key is null) return AuthResult.Fail(AuthFailure.Unknown);

        if (key.RevokedAt is not null) return AuthResult.Fail(AuthFailure.Revoked);
        if (key.ExpiresAt is { } expiry && expiry <= now) return AuthResult.Fail(AuthFailure.Expired);

        var account = await db.Set<AccountEntity>()
            .FirstOrDefaultAsync(a => a.Id == key.AccountId, ct).ConfigureAwait(false);

        // A disabled account disables every key it holds, without anyone having to revoke them
        // one at a time.
        if (account is null || account.DisabledAt is not null) return AuthResult.Fail(AuthFailure.Revoked);

        key.LastUsedAt = now;
        key.LastUsedAgent = Truncate(clientAgent, 128);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return AuthResult.Success(new AuthenticatedCaller(
            account.Id,
            key.KeyId,
            key.ContractId,
            key.Scopes.ToHashSet(StringComparer.Ordinal),
            account.IsOperator));
    }

    private bool CheckRate(AuthenticatedCaller caller)
    {
        var window = mRates.GetOrAdd(caller.KeyId, _ => new RateWindow());
        var allowed = window.TryTake(mOptions.RequestsPerMinute);

        if (!allowed)
            mLog.LogWarning("Key {KeyId} is over its rate limit of {Limit}/min", caller.KeyId, mOptions.RequestsPerMinute);

        return allowed;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// A fixed one-minute window per key.
    /// <para><b>In-memory, therefore per instance.</b> Two replicas each allow the configured
    /// budget, so the effective limit is the budget times the replica count. That is
    /// acceptable for what this is — a guard against a runaway client, not a billing meter —
    /// and it stays honest only because it is written down here. A shared limiter belongs in
    /// Redis, and should arrive with the first deployment that runs more than one instance.
    /// </para>
    /// </summary>
    private sealed class RateWindow
    {
        private readonly Lock mGate = new();
        private DateTimeOffset mWindowStart = DateTimeOffset.UtcNow;
        private int mCount;

        public bool TryTake(int limit)
        {
            lock (mGate)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - mWindowStart >= TimeSpan.FromMinutes(1))
                {
                    mWindowStart = now;
                    mCount = 0;
                }

                if (mCount >= limit) return false;
                mCount++;
                return true;
            }
        }
    }
}
