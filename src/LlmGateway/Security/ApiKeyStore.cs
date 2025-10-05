using System.Security.Cryptography;
using System.Text;
using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Security;

/// <summary>A client API key as the gateway sees it after authentication.</summary>
public sealed record ApiKeyDefinition(string Name, IReadOnlySet<string> AllowedModels, bool IsAdmin)
{
    public bool AllowsAllModels => AllowedModels.Count == 0 || AllowedModels.Contains("*");

    public bool CanUse(string modelAlias) => AllowsAllModels || AllowedModels.Contains(modelAlias);
}

/// <summary>
/// Looks up presented keys by their SHA-256 hash. Gateway keys are long random strings, so a fast
/// unsalted hash is enough to keep them out of configuration files without a password KDF.
/// </summary>
public sealed class ApiKeyStore
{
    private readonly Dictionary<string, ApiKeyDefinition> _byHash;

    public ApiKeyStore(IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _byHash = options.Value.ApiKeys.ToDictionary(
            pair => pair.Value.KeyHash.ToLowerInvariant(),
            pair => new ApiKeyDefinition(
                pair.Key,
                pair.Value.AllowedModels.ToHashSet(StringComparer.OrdinalIgnoreCase),
                pair.Value.IsAdmin),
            StringComparer.Ordinal);
    }

    public bool TryFind(string presentedKey, out ApiKeyDefinition key) =>
        _byHash.TryGetValue(Hash(presentedKey), out key!);

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
