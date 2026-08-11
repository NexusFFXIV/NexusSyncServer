using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Auth;

/// <inheritdoc />
public sealed class ApiKeyIssuer : IApiKeyIssuer
{
    private readonly ServerDbContext mDb;
    private readonly AuthOptions mOptions;
    private readonly ILogger<ApiKeyIssuer> mLog;

    /// <summary>Creates the issuer.</summary>
    public ApiKeyIssuer(ServerDbContext db, IOptions<AuthOptions> options, ILogger<ApiKeyIssuer> log)
    {
        mDb = db;
        mOptions = options.Value;
        mLog = log;
    }

    /// <inheritdoc />
    public async Task<IssuedApiKey> IssueAsync(
        Guid accountId,
        IReadOnlyCollection<string> scopes,
        string? contractId,
        string? label,
        TimeSpan? lifetime,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        Validate(scopes);

        var effective = lifetime ?? mOptions.DefaultKeyLifetime;
        var key = ApiKeySecret.Generate();

        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            KeyId = ApiKeySecret.KeyIdOf(key),
            KeyHash = ApiKeySecret.Hash(key),
            AccountId = accountId,
            ContractId = contractId,
            Scopes = scopes.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            Label = label,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = effective is { } l ? DateTimeOffset.UtcNow + l : null,
        };

        mDb.Add(entity);
        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);

        // Redacted, always. This line exists so an operator can correlate an issuance with
        // later use; printing the key here would put a live credential in the log file.
        mLog.LogInformation(
            "Issued key {KeyId} for account {Account} with scopes [{Scopes}]{Contract}",
            ApiKeyFormat.Redact(key), accountId, string.Join(", ", entity.Scopes),
            contractId is null ? "" : $" restricted to {contractId}");

        return new IssuedApiKey(key, entity.KeyId, entity.ExpiresAt);
    }

    /// <inheritdoc />
    /// <summary>Rejects anything that is not a scope this server could honour.</summary>
    private static void Validate(IEnumerable<string> scopes)
    {
        foreach (var scope in scopes)
        {
            // Built-in, contract-free, and therefore not expressible in the collection:verb
            // grammar the check below applies.
            // Either form: the global grant, or one contract's.
            if (string.Equals(scope, QualifiedScope.ReadContracts, StringComparison.Ordinal)) continue;
            if (QualifiedScope.TryParse(scope, out var readContract, out var readTail)
                && string.Equals(readTail, QualifiedScope.ReadContracts, StringComparison.Ordinal))
            {
                if (!ContractNames.IsValidContractId(readContract))
                    throw new ArgumentException($"'{scope}' names no valid contract.", nameof(scopes));

                continue;
            }

            // A scope may name its contract — example.showcase/observations:push — which is what
            // lets one key span several. Validate the collection:verb half either way; the
            // contract half is checked by ContractNames, not by the scope grammar.
            var bare = QualifiedScope.TryParse(scope, out var scopeContract, out var tail) ? tail : scope;

            if (!ContractScopes.TryParse(bare, out _, out _))
                throw new ArgumentException($"'{scope}' is not a valid scope.", nameof(scopes));

            if (QualifiedScope.IsQualified(scope) && !ContractNames.IsValidContractId(scopeContract))
                throw new ArgumentException($"'{scope}' names no valid contract.", nameof(scopes));
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetScopesAsync(
        Guid keyId, IReadOnlyCollection<string> scopes, string? contractId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0) throw new ArgumentException("A key needs at least one scope.", nameof(scopes));

        var entity = await mDb.Set<ApiKeyEntity>().FirstOrDefaultAsync(k => k.Id == keyId, ct).ConfigureAwait(false);
        if (entity is null || entity.RevokedAt is not null) return false;

        // Validated the same way issuing does, so the two cannot drift into accepting
        // different things — which is how a scope nobody can spell ends up stored.
        Validate(scopes);

        entity.Scopes = scopes.Distinct(StringComparer.Ordinal).ToList();
        entity.ContractId = contractId;

        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
        mLog.LogInformation("Key {KeyId} now carries [{Scopes}]", entity.KeyId, string.Join(", ", entity.Scopes));

        return true;
    }

    /// <inheritdoc />
    public async Task<string?> RotateAsync(Guid keyId, CancellationToken ct)
    {
        var entity = await mDb.Set<ApiKeyEntity>().FirstOrDefaultAsync(k => k.Id == keyId, ct).ConfigureAwait(false);
        if (entity is null || entity.RevokedAt is not null) return null;

        var key = ApiKeySecret.Generate();

        entity.KeyId = ApiKeySecret.KeyIdOf(key);
        entity.KeyHash = ApiKeySecret.Hash(key);
        entity.RotatedAt = DateTimeOffset.UtcNow;

        // These described the previous secret. Carrying them over would claim the new key had
        // already been used, which is exactly the signal somebody checks after a suspected leak.
        entity.LastUsedAt = null;
        entity.LastUsedAgent = null;

        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
        mLog.LogInformation("Rotated key {KeyId}", entity.KeyId);

        return key;
    }

    /// <inheritdoc />
    public async Task<bool> ShortenExpiryAsync(Guid keyId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var key = await mDb.Set<ApiKeyEntity>().FirstOrDefaultAsync(k => k.Id == keyId, ct).ConfigureAwait(false);
        if (key is null) return false;

        // Refuse anything that is not a shortening, including a date already behind us —
        // that would be a revocation wearing a different name, and revocation says so plainly.
        if (expiresAt <= DateTimeOffset.UtcNow) return false;
        if (key.ExpiresAt is { } current && expiresAt >= current) return false;

        key.ExpiresAt = expiresAt;
        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);

        mLog.LogInformation("Key {KeyId} now expires {Expiry:u}", key.KeyId, expiresAt);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(Guid keyId, CancellationToken ct)
    {
        var key = await mDb.Set<ApiKeyEntity>().FirstOrDefaultAsync(k => k.Id == keyId, ct).ConfigureAwait(false);
        if (key is null) return false;

        // Revoked, not deleted: the audit trail of what this key did stays readable only while
        // the key row exists to join against.
        if (key.RevokedAt is null)
        {
            key.RevokedAt = DateTimeOffset.UtcNow;
            await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
            mLog.LogInformation("Revoked key {KeyId}", key.KeyId);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeyEntity>> ListAsync(Guid accountId, CancellationToken ct) =>
        await mDb.Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(k => k.AccountId == accountId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
}
