using System.Globalization;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Errors;
using Microsoft.Extensions.Options;

namespace LlmGateway.Guardrails;

/// <summary>
/// Checks chat and embedding requests against the blocked-term policy, then, when enabled, redacts
/// PII from every message before the handler runs, so upstreams and anything stored later only
/// ever see the redacted text.
/// </summary>
public sealed partial class GuardrailsFilter(
    BlockedTermPolicy blockedTerms,
    IOptions<GatewayOptions> options,
    ILogger<GuardrailsFilter> logger) : IEndpointFilter
{
    public const string RedactedHeader = "X-Gateway-Pii-Redacted";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var chat = context.Arguments.OfType<ChatCompletionRequest>().FirstOrDefault();
        var embedding = context.Arguments.OfType<EmbeddingRequest>().FirstOrDefault();

        var texts = chat?.Messages.Select(m => m.GetText()) ?? embedding?.GetTextInputs() ?? [];
        if (texts.Any(blockedTerms.IsBlocked))
        {
            LogBlocked(logger, context.HttpContext.Request.Path);
            return GatewayErrors.Create(
                StatusCodes.Status400BadRequest,
                "The request was rejected by the gateway's content policy.",
                "invalid_request_error",
                code: "content_policy_violation");
        }

        if (options.Value.Guardrails.RedactPii)
        {
            var redactions = 0;
            string Redact(string text)
            {
                var (redacted, count) = PiiRedactor.Redact(text);
                redactions += count;
                return redacted;
            }

            if (chat is not null)
            {
                foreach (var message in chat.Messages)
                {
                    message.RewriteText(Redact);
                }
            }

            embedding?.RewriteTextInputs(Redact);

            if (redactions > 0)
            {
                context.HttpContext.Response.Headers[RedactedHeader] = redactions.ToString(CultureInfo.InvariantCulture);
            }
        }

        return await next(context);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a request to {Path} that matched the blocked-term policy")]
    private static partial void LogBlocked(ILogger logger, PathString path);
}
