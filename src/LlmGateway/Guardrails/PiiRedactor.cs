using System.Text.RegularExpressions;

namespace LlmGateway.Guardrails;

/// <summary>
/// Replaces e-mail addresses, phone numbers and payment card numbers with placeholders. Card
/// candidates are only redacted when they pass the Luhn check, which keeps order numbers and other
/// long digit runs intact. Phone detection is pattern-based: North American numbers with or without
/// separators, and international numbers written with a leading <c>+</c>.
/// </summary>
public static partial class PiiRedactor
{
    public const string EmailPlaceholder = "[REDACTED_EMAIL]";
    public const string PhonePlaceholder = "[REDACTED_PHONE]";
    public const string CardPlaceholder = "[REDACTED_CARD]";

    /// <summary>Returns the redacted text and how many values were replaced.</summary>
    public static (string Text, int Redactions) Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var redactions = 0;

        // Cards first, so their digit groups are not mistaken for phone numbers.
        text = CardCandidate().Replace(text, match =>
        {
            if (!IsLuhnValid(match.Value))
            {
                return match.Value;
            }

            redactions++;
            return CardPlaceholder;
        });

        text = Email().Replace(text, _ =>
        {
            redactions++;
            return EmailPlaceholder;
        });

        text = InternationalPhone().Replace(text, match =>
        {
            var digits = match.Value.Count(char.IsAsciiDigit);
            if (digits is < 8 or > 15)
            {
                return match.Value;
            }

            redactions++;
            return PhonePlaceholder;
        });

        text = NorthAmericanPhone().Replace(text, _ =>
        {
            redactions++;
            return PhonePlaceholder;
        });

        return (text, redactions);
    }

    /// <summary>The Luhn checksum used by payment card numbers, over a 13–19 digit candidate.</summary>
    public static bool IsLuhnValid(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var digits = candidate.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    [GeneratedRegex(@"(?<!\d)\d(?:[ -]?\d){12,18}(?!\d)")]
    private static partial Regex CardCandidate();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w+])\+\d{1,3}(?:[ .-]?\(?\d{1,4}\)?){2,5}(?!\w)")]
    private static partial Regex InternationalPhone();

    [GeneratedRegex(@"(?<![\w+])(?:1[ .-]?)?(?:\(\d{3}\)[ ]?|\d{3}[ .-]?)\d{3}[ .-]?\d{4}(?!\w)")]
    private static partial Regex NorthAmericanPhone();
}
