using System.Text.Json;
using LlmGateway.Providers;
using LlmGateway.Routing;
using Microsoft.AspNetCore.Diagnostics;

namespace LlmGateway.Errors;

/// <summary>
/// Turns exceptions that escape an endpoint into OpenAI-style error responses. Client errors from an
/// upstream (a 400 for an unsupported parameter, say) are passed through with the upstream's own body.
/// </summary>
public sealed partial class GatewayExceptionHandler(ILogger<GatewayExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = exception switch
        {
            BadHttpRequestException bad => GatewayErrors.Create(bad.StatusCode, RequestBodyMessage(bad), "invalid_request_error", code: "invalid_request"),
            UpstreamException upstream => UpstreamError(upstream),
            AllUpstreamsFailedException allFailed => AllUpstreamsFailed(allFailed),
            _ => null,
        };

        if (result is null)
        {
            LogUnhandled(logger, exception);
            result = GatewayErrors.Create(StatusCodes.Status500InternalServerError, "The gateway failed to process the request.", "server_error");
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }

    private static string RequestBodyMessage(BadHttpRequestException exception) =>
        exception.InnerException is JsonException json
            ? $"The request body is not valid JSON: {json.Message}"
            : exception.Message;

    private static IResult UpstreamError(UpstreamException exception)
    {
        if (!string.IsNullOrWhiteSpace(exception.ResponseBody) && IsJson(exception.ResponseBody))
        {
            return Results.Content(exception.ResponseBody, "application/json", statusCode: exception.StatusCode);
        }

        return GatewayErrors.Create(exception.StatusCode, exception.Message, "upstream_error");
    }

    private static IResult AllUpstreamsFailed(AllUpstreamsFailedException exception)
    {
        if (exception.AllRateLimited)
        {
            return GatewayErrors.Create(StatusCodes.Status429TooManyRequests, exception.Message, "rate_limit_error", code: "upstream_rate_limited");
        }

        var status = exception.LastTimedOut ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway;
        return GatewayErrors.Create(status, exception.Message, "upstream_error", code: "upstream_unavailable");
    }

    private static bool IsJson(string body)
    {
        try
        {
            using var _ = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing a gateway request")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
