using System.Text.RegularExpressions;

namespace LlmGateway.Providers;

/// <summary>
/// Deterministic bag-of-words embeddings using the hashing trick: each lower-cased word is hashed
/// (FNV-1a) into one of <see cref="Dimensions"/> buckets with a hash-derived sign, and the vector is
/// L2-normalised. Texts that share words get a high cosine similarity, which is enough to exercise
/// the semantic cache offline. It carries no real meaning beyond word overlap.
/// </summary>
public static partial class HashingEmbedder
{
    public const int Dimensions = 256;

    public static float[] Embed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var vector = new float[Dimensions];

        foreach (Match word in WordPattern().Matches(text.ToLowerInvariant()))
        {
            var hash = Fnv1a(word.ValueSpan);
            var bucket = (int)(hash % Dimensions);
            vector[bucket] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private static uint Fnv1a(ReadOnlySpan<char> value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
