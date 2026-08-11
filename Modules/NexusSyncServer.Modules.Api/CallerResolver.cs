using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using NexusSyncServer.Modules.Auth;

namespace NexusSyncServer.Modules.Api;

/// <summary>The caller, or the response to send instead.</summary>
internal readonly record struct CallerOrProblem(AuthenticatedCaller? Caller, IResult? Problem)
{
    public bool Ok => Caller is not null;
}

/// <summary>
/// Pulls the API key off a request and validates it.
/// <para>A plain helper rather than an ASP.NET authentication scheme. The protocol specifies
/// Problem Details bodies for every failure, and the built-in challenge pipeline produces
/// empty 401s — bending it into shape would be more code than this, and less obvious.</para>
/// </summary>
internal static class CallerResolver
{
    public static async Task<CallerOrProblem> ResolveAsync(
        HttpContext http,
        IApiKeyAuthenticator authenticator,
        CancellationToken ct)
    {
        var presented = ExtractBearer(http.Request.Headers[HeaderNames.Authorization]);
        var agent = http.Request.Headers.UserAgent.ToString();

        var result = await authenticator.AuthenticateAsync(presented, agent, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            // Stashed so anything downstream — an audit filter, a page — can see who this is
            // without validating the key a second time.
            http.Items[AuthenticatedCaller.HttpContextItemKey] = result.Caller;
            return new CallerOrProblem(result.Caller, null);
        }

        var problem = result.Failure == AuthFailure.RateLimited
            ? ProblemResults.RateLimited()
            : ProblemResults.Unauthenticated(result.Failure ?? AuthFailure.Missing);

        return new CallerOrProblem(null, problem);
    }

    private static string? ExtractBearer(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;

        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }
}
