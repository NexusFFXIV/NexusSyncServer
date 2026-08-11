namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// Thrown when an external provider refuses a sign-in or answers in a way that cannot be used.
/// <para>Carries the provider id so a deployment running several can tell which one failed —
/// "sign-in failed" with two providers configured is not an actionable log line.</para>
/// </summary>
public sealed class IdentityProviderException : Exception
{
    /// <summary>Creates the exception.</summary>
    public IdentityProviderException(string provider, string message, Exception? inner = null)
        : base($"[{provider}] {message}", inner) =>
        Provider = provider;

    /// <summary>The provider that failed.</summary>
    public string Provider { get; }
}
