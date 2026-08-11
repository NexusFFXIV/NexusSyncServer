using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Auth;

/// <summary>Accounts and API keys.</summary>
public sealed class AuthEntityModule : IEntityModule
{
    /// <inheritdoc />
    public string SchemaName => "auth";

    /// <inheritdoc />
    public void ConfigureEntities(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AccountEntity>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(64);
            e.Property(x => x.IsOperator).HasColumnName("is_operator").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
        });

        modelBuilder.Entity<AccountIdentityEntity>(e =>
        {
            e.ToTable("account_identities");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(32);
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(64);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(64);
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
            e.Property(x => x.LinkedAt).HasColumnName("linked_at");
            e.Property(x => x.LastSignInAt).HasColumnName("last_sign_in_at");

            // One identity maps to exactly one account, and the sign-in path looks it up by
            // exactly this pair on every login. Unique so two accounts cannot both claim the
            // same external user — which would make "who am I" answer differently per login.
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique().HasDatabaseName("ux_identities_provider_subject");
            e.HasIndex(x => x.AccountId).HasDatabaseName("ix_identities_account");

            e.HasOne<AccountEntity>()
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.KeyId).HasColumnName("key_id").HasMaxLength(16);
            e.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(64);
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.ContractId).HasColumnName("contract_id").HasMaxLength(128);
            // MariaDB has no array type, so the granted scopes become one comma-separated
            // column. Comma-separated rather than JSON because the scope grammar is
            // collection:verb — lowercase, digits, underscore and one colon, never a comma —
            // so the separator cannot collide, and the column stays readable to anyone
            // looking at the table directly.
            //
            // The comparer is not optional: without one EF compares List<string> by
            // reference, so adding a scope to an existing key's list in place would be
            // saved as no change at all.
            e.Property(x => x.Scopes)
                .HasColumnName("scopes")
                .HasMaxLength(512)
                .HasConversion(ScopeConverter, ScopeComparer);
            e.Property(x => x.Label).HasColumnName("label").HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            e.Property(x => x.LastUsedAgent).HasColumnName("last_used_agent").HasMaxLength(128);
            e.Property(x => x.RotatedAt).HasColumnName("rotated_at");

            // Not unique: prefixes can collide by chance, and the hash decides. Making it
            // unique would turn a 1-in-2^40 coincidence into a failed key issuance.
            e.HasIndex(x => x.KeyId).HasDatabaseName("ix_api_keys_lookup");
            e.HasIndex(x => x.AccountId).HasDatabaseName("ix_api_keys_account");

            e.HasOne<AccountEntity>()
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static readonly ValueConverter<List<string>, string> ScopeConverter = new(
        scopes => string.Join(',', scopes),
        text => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());

    private static readonly ValueComparer<List<string>> ScopeComparer = new(
        (left, right) => left != null && right != null
            ? left.SequenceEqual(right, StringComparer.Ordinal)
            : left == right,
        scopes => scopes.Aggregate(0, (hash, scope) => HashCode.Combine(hash, scope.GetHashCode(StringComparison.Ordinal))),
        scopes => scopes.ToList());
}
