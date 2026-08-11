namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// Storage configuration, bound from the <c>Storage</c> configuration section.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// MariaDB connection string. Supplied through configuration or the environment —
    /// never committed. Compose passes it as an environment variable.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// How long an applied operation id is remembered for idempotency. A client offline longer
    /// than this and then retrying re-applies its writes, which is harmless for keyed upserts.
    /// </summary>
    public TimeSpan OperationDedupeWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Largest batch accepted in one push, advertised to clients at handshake.</summary>
    public int MaxRecordsPerPush { get; set; } = 500;

    /// <summary>Largest page returned from a pull.</summary>
    public int MaxRecordsPerPull { get; set; } = 1000;

    /// <summary>
    /// Largest single record payload, in bytes. A blunt guard rather than a per-contract
    /// quota: contracts are registered by the operator, so the abuse case this protects
    /// against is a bug or a misconfigured client, not a hostile schema.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    /// <summary>Throws when the options cannot produce a working storage layer.</summary>
    /// <exception cref="InvalidOperationException">A required value is missing or unusable.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(ConnectionString)} is required. Set it in configuration or as "
                + $"the environment variable {SectionName}__{nameof(ConnectionString)}.");
        }

        if (OperationDedupeWindow <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(OperationDedupeWindow)} must be positive.");

        if (MaxRecordsPerPush <= 0) throw new InvalidOperationException($"{nameof(MaxRecordsPerPush)} must be positive.");
        if (MaxRecordsPerPull <= 0) throw new InvalidOperationException($"{nameof(MaxRecordsPerPull)} must be positive.");
        if (MaxPayloadBytes <= 0) throw new InvalidOperationException($"{nameof(MaxPayloadBytes)} must be positive.");
    }
}
