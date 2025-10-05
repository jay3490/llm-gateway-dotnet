using LlmGateway.Contracts;

namespace LlmGateway.Errors;

/// <summary>Error results in the OpenAI error envelope, so OpenAI client libraries surface them normally.</summary>
public static class GatewayErrors
{
    public static IResult InvalidRequest(string message, string? param = null) =>
        Create(StatusCodes.Status400BadRequest, message, "invalid_request_error", param, "invalid_request");

    public static IResult ModelNotFound(string model) =>
        Create(StatusCodes.Status404NotFound, $"The model '{model}' does not exist.", "invalid_request_error", "model", "model_not_found");

    public static IResult ModelNotAllowed(string model) =>
        Create(StatusCodes.Status403Forbidden, $"This API key is not allowed to use the model '{model}'.", "permission_error", "model", "model_not_allowed");

    public static IResult Create(int statusCode, string message, string type, string? param = null, string? code = null) =>
        Results.Json(new ErrorResponse(new ErrorBody(message, type, param, code)), GatewayJson.Options, statusCode: statusCode);
}
