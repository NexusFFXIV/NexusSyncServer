using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Registry;

/// <summary>The registry's own table.</summary>
public sealed class RegistryEntityModule : IEntityModule
{
    /// <inheritdoc />
    public string SchemaName => "registry";

    /// <inheritdoc />
    public void ConfigureEntities(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RegisteredContractEntity>(e =>
        {
            e.ToTable("contracts");

            // Keyed on the full version, not just the contract: registering 1.1 must not
            // overwrite 1.0, because clients built against 1.0 are still out there.
            e.HasKey(x => new { x.ContractId, x.Major, x.Minor });

            e.Property(x => x.ContractId).HasColumnName("contract_id").HasMaxLength(128);
            e.Property(x => x.Major).HasColumnName("major");
            e.Property(x => x.Minor).HasColumnName("minor");
            e.Property(x => x.CanonicalJson).HasColumnName("canonical_json");
            e.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(64);
            e.Property(x => x.RegisteredAt).HasColumnName("registered_at");
            e.Property(x => x.RetiredAt).HasColumnName("retired_at");

            // Negotiation always asks "highest minor for this id and major".
            e.HasIndex(x => new { x.ContractId, x.Major, x.Minor }).HasDatabaseName("ix_contracts_negotiate");
        });
    }
}
