using System.Security.Cryptography;
using System.Text;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Generating and hashing key material.
/// </summary>
public static class ApiKeySecret
{
    /// <summary>Characters of the lookup prefix stored alongside the hash.</summary>
    public const int KeyIdLength = 8;

    /// <summary>
    /// Generates a fresh key: <c>nxs_</c> plus 32 characters drawn uniformly from the
    /// Crockford-style alphabet.
    /// </summary>
    public static string Generate()
    {
        var alphabet = ApiKeyFormat.Alphabet;
        var body = new char[ApiKeyFormat.BodyLength];

        for (var i = 0; i < body.Length; i++)
        {
            // RandomNumberGenerator, not Random: this is a credential, and GetInt32 draws
            // without the modulo bias a naive "random byte % 32" would introduce.
            body[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return ApiKeyFormat.Prefix + new string(body);
    }

    /// <summary>Lowercase hex SHA-256 of the whole key, including its prefix.</summary>
    /// <remarks>
    /// Plain SHA-256 rather than a password hash. A key is 160 bits of uniform randomness, so
    /// there is no dictionary to run against it — the work factor of bcrypt or Argon2 would buy
    /// nothing and would cost real latency on every single request.
    /// </remarks>
    public static string Hash(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>
    /// The indexed lookup prefix: the first characters of the key body, after <c>nxs_</c>.
    /// </summary>
    public static string KeyIdOf(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (key.Length < ApiKeyFormat.Prefix.Length + KeyIdLength)
            throw new ArgumentException("Key is too short to derive a lookup id from.", nameof(key));

        return key.Substring(ApiKeyFormat.Prefix.Length, KeyIdLength);
    }

    /// <summary>
    /// Compares a candidate hash against a stored one in constant time.
    /// <para>Both values are public-safe hashes rather than secrets, so the timing channel is
    /// weak — but comparing hashes is exactly the place where a length-dependent early exit
    /// costs nothing to avoid.</para>
    /// </summary>
    public static bool HashesMatch(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a ?? string.Empty),
            Encoding.UTF8.GetBytes(b ?? string.Empty));
}
