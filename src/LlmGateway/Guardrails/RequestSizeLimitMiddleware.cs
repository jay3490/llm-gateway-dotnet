using LlmGateway.Configuration;
using LlmGateway.Errors;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace LlmGateway.Guardrails;

/// <summary>
/// Caps request bodies at <see cref="GuardrailOptions.MaxRequestBytes"/>. Requests that declare a
/// larger Content-Length are rejected before the body is read; for chunked bodies the server's own
/// body-size limit is lowered so reading fails once the cap is crossed.
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<GatewayOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var limit = options.Value.Guardrails.MaxRequestBytes;

        if (context.Request.ContentLength > limit)
        {
            await GatewayErrors.Create(
                    StatusCodes.Status413PayloadTooLarge,
                    $"The request body exceeds the gateway limit of {limit} bytes.",
                    "invalid_request_error",
                    code: "request_too_large")
                .ExecuteAsync(context);
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        await next(context);
    }
}
