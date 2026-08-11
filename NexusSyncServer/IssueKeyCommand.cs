using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Modules.Auth;
using NexusSyncServer.Modules.Storage.MariaDb;

namespace NexusSyncServer;

/// <summary>
/// The <c>--issue-key</c> mode: mints an API key from the command line.
/// <para>Exists because there is a bootstrap circle otherwise — the portal issues keys, but
/// reaching the portal needs a sign-in provider configured, and a server may legitimately run
/// with none. This is also how an operator recovers when their own access is gone.</para>
/// <para>Run it against the same database the server uses:</para>
/// <code>
/// docker compose exec server /app/NexusSyncServer --issue-key \
///     --scopes observations:push,reference_items:pull --contract example.showcase
///
/// Or, spanning contracts — then the scopes carry their own:
///     --scopes example.showcase/observations:push,example.workshop/recipes:pull
/// </code>
/// </summary>
internal static class IssueKeyCommand
{
    /// <summary>True when the process was started to mint a key rather than to serve.</summary>
    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, "--issue-key", StringComparison.Ordinal));

    /// <summary>Mints the key and prints it. Returns 0 on success.</summary>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var scopes = Value(args, "--scopes")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (scopes is not { Length: > 0 })
        {
            await Console.Error.WriteLineAsync(
                "--issue-key requires --scopes, e.g. --scopes reports:push,venues:pull").ConfigureAwait(false);
            return 1;
        }

        var contract = Value(args, "--contract");

        // Qualify anything bare against --contract. A bare scope only means something on a key
        // pinned to one contract; without --contract there is nothing to pin it to, and the
        // key would be issued looking correct and granting nothing.
        // The built-in belongs to no contract, so it is neither qualified nor missing one.
        var unqualified = scopes
            .Where(s => !QualifiedScope.IsQualified(s)
                        && !string.Equals(s, QualifiedScope.ReadContracts, StringComparison.Ordinal))
            .ToArray();
        if (unqualified.Length > 0 && string.IsNullOrWhiteSpace(contract))
        {
            await Console.Error.WriteLineAsync(
                $"--issue-key: {string.Join(", ", unqualified)} name no contract. Either pass "
                + "--contract, or write them as contract/scope, e.g. "
                + "example.showcase/observations:push — which is what a key spanning several "
                + "contracts needs.").ConfigureAwait(false);
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(contract))
        {
            scopes = scopes
                .Select(s => QualifiedScope.IsQualified(s)
                             || string.Equals(s, QualifiedScope.ReadContracts, StringComparison.Ordinal)
                    ? s
                    : QualifiedScope.Of(contract, s))
                .ToArray();
        }

        // A key whose scopes span contracts cannot carry a single restriction.
        var contracts = scopes
            .Select(s => QualifiedScope.TryParse(s, out var c, out _) ? c : null)
            .Where(c => c is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (contracts.Length > 1) contract = null;
        var label = Value(args, "--label") ?? "issued from CLI";
        var operatorFlag = args.Any(a => string.Equals(a, "--operator", StringComparison.Ordinal));

        using var scope = app.Services.CreateScope();

        // The server's hosted services have not run, so the schema may not exist yet. Running
        // the migration here means `--issue-key` works against an empty database — which is
        // exactly the situation someone bootstrapping a fresh deployment is in.
        await scope.ServiceProvider.GetRequiredService<MigrationRunner>()
            .RunAsync(CancellationToken.None).ConfigureAwait(false);

        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var account = await FindOrCreateAccountAsync(db, operatorFlag).ConfigureAwait(false);

        var issuer = scope.ServiceProvider.GetRequiredService<IApiKeyIssuer>();

        IssuedApiKey issued;
        try
        {
            issued = await issuer
                .IssueAsync(account.Id, scopes, contract, label, null, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        // The only time this value will ever exist. Printed to stdout on its own line so it
        // can be captured, and flagged loudly because it cannot be recovered.
        Console.WriteLine();
        Console.WriteLine($"  account : {account.Id}{(account.IsOperator ? " (operator)" : "")}");
        Console.WriteLine($"  scopes  : {string.Join(", ", scopes)}");
        Console.WriteLine($"  contract: {contract ?? "(any)"}");
        Console.WriteLine();
        Console.WriteLine("  This key is shown once and is not recoverable:");
        Console.WriteLine();
        Console.WriteLine($"  {issued.Key}");
        Console.WriteLine();

        return 0;
    }

    private static async Task<AccountEntity> FindOrCreateAccountAsync(ServerDbContext db, bool asOperator)
    {
        // A CLI-issued key belongs to a local account with no external identity — nobody can
        // sign in as it, which is the point: it is a service credential, not a person.
        const string cliDisplayName = "CLI";

        var existing = await db.Set<AccountEntity>()
            .FirstOrDefaultAsync(a => a.DisplayName == cliDisplayName).ConfigureAwait(false);

        if (existing is not null)
        {
            if (asOperator && !existing.IsOperator)
            {
                existing.IsOperator = true;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return existing;
        }

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = cliDisplayName,
            IsOperator = asOperator,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Add(account);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return account;
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
