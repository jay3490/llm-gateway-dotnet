using System.Text.RegularExpressions;
using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Guardrails;

/// <summary>Rejects requests that contain any configured term as a whole word, ignoring case.</summary>
public sealed class BlockedTermPolicy
{
    private readonly Regex? _pattern;

    public BlockedTermPolicy(IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var terms = options.Value.Guardrails.BlockedTerms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Regex.Escape(t.Trim()))
            .ToList();

        if (terms.Count > 0)
        {
            _pattern = new Regex(
                $@"(?<!\w)(?:{string.Join('|', terms)})(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(250));
        }
    }

    public bool IsBlocked(string text) => _pattern is not null && _pattern.IsMatch(text);
}
