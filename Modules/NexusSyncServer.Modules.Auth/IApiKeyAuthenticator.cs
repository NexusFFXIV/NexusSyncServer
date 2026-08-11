namespace NexusSyncServer.Modules.Auth;

/// <summary>Why a presented key was refused.</summary>
public enum AuthFailure
{
    /// <summary>No <c>Authorization: Bearer</c> header, or not shaped like a key.</summary>
    Missing,

    /// <summary>Well-formed but matches no issued key.</summary>
    Unknown,

    /// <summary>The key or its account has been revoked or disabled.</summary>
    Revoked,

    /// <summary>The key has passed its expiry.</summary>
    Expired,

    /// <summary>The caller is over their rate limit.</summary>
    RateLimited,
}

/// <summary>The outcome of validating a presented key.</summary>
/// <param name="Caller">The caller, when validation succeeded.</param>
/// <param name="Failure">Why it failed, otherwise.</param>
public sealed record AuthResult(AuthenticatedCaller? Caller, AuthFailure? Failure)
{
    /// <summary>True when the key is good.</summary>
    public bool Succeeded => Caller is not null;

    /// <summary>Creates a success.</summary>
    public static AuthResult Success(AuthenticatedCaller caller) => new(caller, null);

    /// <summary>Creates a failure.</summary>
    public static AuthResult Fail(AuthFailure failure) => new(null, failure);
}

/// <summary>
/// Validates a presented API key.
/// <para>Called on every authenticated request, which is why the implementation caches — see
/// <see cref="AuthOptions.ValidationCacheLifetime"/> for the trade between a database query
/// per request and the delay before a revocation bites.</para>
/// </summary>
public interface IApiKeyAuthenticator
{
    /// <summary>
    /// Validates a key and counts the request against its rate limit.
    /// </summary>
    /// <param name="presentedKey">The raw value from the <c>Authorization</c> header, or null.</param>
    /// <param name="clientAgent">The caller's user agent, recorded for the audit trail.</param>
    /// <param name="ct">Cancels the validation.</param>
    Task<AuthResult> AuthenticateAsync(string? presentedKey, string? clientAgent, CancellationToken ct);
}
