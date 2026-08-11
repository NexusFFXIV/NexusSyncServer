namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Authentication configuration, bound from the <c>Auth</c> configuration section.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// External identities seeded as operators, in <c>provider:subject</c> form — for example
    /// <c>xivauth:12345</c> or <c>discord:987654321</c>.
    /// <para>Breaks the bootstrap circle: registering a contract requires an operator, and
    /// promoting an account requires an operator. One entry on first boot is enough, and it
    /// can be removed afterwards.</para>
    /// <para>Provider-qualified rather than a bare id, because a deployment may run several
    /// providers and the same number can mean different people on different ones.</para>
    /// </summary>
    public IList<string> OperatorIdentities { get; } = [];

    /// <summary>
    /// Path to a file containing one API key to seed at startup — a Docker secret, not an
    /// environment variable.
    /// <para>Exists for unattended provisioning: without it the only way to obtain a key is
    /// <c>docker compose exec … --issue-key</c>, which is a manual step in an otherwise
    /// scripted deployment.</para>
    /// <para><b>Why a file and not a config value.</b> Environment variables are visible in
    /// <c>docker inspect</c>, in the compose file, in shell history and in the process
    /// environment of anything that can read <c>/proc</c>. A mounted secret is none of those.
    /// The server reads the file once, stores only the hash, and never writes the plaintext
    /// anywhere — so the credential exists in exactly two places: the operator's secret store
    /// and the client that uses it.</para>
    /// <para>Seeding is idempotent by hash: restarts and redeploys change nothing. Remove the
    /// secret once a real key has been issued.</para>
    /// </summary>
    /// <example>
    /// <code>
    /// # generate one, then mount it as a secret
    /// openssl rand -hex 20 | tr -d '\n' | sed 's/^/nxs_/' &gt; bootstrap_key
    /// </code>
    /// </example>
    public string? BootstrapKeyFile { get; set; }

    /// <summary>Scopes granted to the seeded key. Required when <see cref="BootstrapKeyFile"/> is set.</summary>
    public IList<string> BootstrapKeyScopes { get; } = [];

    /// <summary>Contract the seeded key is limited to, or null for any.</summary>
    public string? BootstrapKeyContract { get; set; }

    /// <summary>
    /// Default lifetime for a newly issued key, or null for no expiry.
    /// <para>Null by default. A key that silently expires mid-session looks to a user like the
    /// plugin breaking, and a rotation they chose is better than one imposed on them.</para>
    /// </summary>
    public TimeSpan? DefaultKeyLifetime { get; set; }

    /// <summary>Requests allowed per key per minute, across all endpoints.</summary>
    public int RequestsPerMinute { get; set; } = 300;

    /// <summary>
    /// How long a validated key stays cached before it is re-read from the database.
    /// <para>Short on purpose: this is the delay between an operator revoking a key and the
    /// revocation taking effect. Long enough to spare the database a query per request,
    /// short enough that "revoke" means what it says.</para>
    /// </summary>
    public TimeSpan ValidationCacheLifetime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws when the options cannot produce a working auth layer.</summary>
    /// <exception cref="InvalidOperationException">A value is unusable.</exception>
    public void Validate()
    {
        if (RequestsPerMinute <= 0)
            throw new InvalidOperationException($"{nameof(RequestsPerMinute)} must be positive.");

        if (ValidationCacheLifetime < TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(ValidationCacheLifetime)} cannot be negative.");

        if (DefaultKeyLifetime is { } lifetime && lifetime <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(DefaultKeyLifetime)} must be positive when set.");

        if (string.IsNullOrWhiteSpace(BootstrapKeyFile)) return;

        if (BootstrapKeyScopes.Count == 0)
            throw new InvalidOperationException($"{nameof(BootstrapKeyFile)} is set but {nameof(BootstrapKeyScopes)} is empty.");
    }
}
