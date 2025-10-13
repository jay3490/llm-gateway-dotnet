using LlmGateway.Guardrails;

namespace LlmGateway.Tests.Unit;

public sealed class PiiRedactorTests
{
    [Theory]
    [InlineData("mail jane.doe+tag@example.co.uk now", "mail [REDACTED_EMAIL] now")]
    [InlineData("a@b.io and c.d@e-f.org", "[REDACTED_EMAIL] and [REDACTED_EMAIL]")]
    public void Emails_are_redacted(string input, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(input).Text);

    [Theory]
    [InlineData("call (415) 555-2671 today")]
    [InlineData("call 415-555-2671 today")]
    [InlineData("call 415.555.2671 today")]
    [InlineData("call 1 415 555 2671 today")]
    [InlineData("call +1 415 555 2671 today")]
    [InlineData("call +44 20 7946 0958 today")]
    public void Phone_numbers_are_redacted(string input) =>
        Assert.Equal("call [REDACTED_PHONE] today", PiiRedactor.Redact(input).Text);

    [Theory]
    [InlineData("card 4111 1111 1111 1111 ok")]
    [InlineData("card 4111-1111-1111-1111 ok")]
    [InlineData("card 4111111111111111 ok")]
    [InlineData("card 3782 822463 10005 ok")]
    [InlineData("card 5555555555554444 ok")]
    public void Luhn_valid_card_numbers_are_redacted(string input) =>
        Assert.Equal("card [REDACTED_CARD] ok", PiiRedactor.Redact(input).Text);

    [Theory]
    [InlineData("order 4111111111111112 shipped")]
    [InlineData("released on 2026-10-27 as version 1.2.3")]
    [InlineData("the answer is 42 and pi is 3.14159")]
    [InlineData("ticket 12345678")]
    public void Other_numbers_are_left_alone(string input)
    {
        var (text, redactions) = PiiRedactor.Redact(input);

        Assert.Equal(input, text);
        Assert.Equal(0, redactions);
    }

    [Fact]
    public void Redactions_are_counted()
    {
        var (_, redactions) = PiiRedactor.Redact("me@x.com, 415-555-2671, 4111 1111 1111 1111");

        Assert.Equal(3, redactions);
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("79927398713", false)]
    [InlineData("378282246310005", true)]
    public void Luhn_check(string number, bool valid) =>
        Assert.Equal(valid, PiiRedactor.IsLuhnValid(number));
}
