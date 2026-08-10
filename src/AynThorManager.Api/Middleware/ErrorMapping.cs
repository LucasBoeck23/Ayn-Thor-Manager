using AynThorManager.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AynThorManager.Api.Middleware;

/// <summary>
/// Maps domain Result errors to HTTP ProblemDetails responses.
/// Consolidates all error code → HTTP status mapping in one place.
/// </summary>
public static class ErrorMapping
{
    public static IResult ToProblemResult(Error error)
    {
        var statusCode = GetHttpStatus(error.Code);
        var problemDetails = ToProblemDetails(error, statusCode);

        return TypedResults.Problem(problemDetails);
    }

    private static int GetHttpStatus(string errorCode) => errorCode switch
    {
        "DEVICE_NOT_CONNECTED" => StatusCodes.Status409Conflict,
        "DEVICE_UNAUTHORIZED" => StatusCodes.Status422UnprocessableEntity,
        "INVALID_IP_FORMAT" => StatusCodes.Status400BadRequest,
        "CONNECTION_ALREADY_ACTIVE" => StatusCodes.Status409Conflict,
        "CONNECTION_TIMEOUT" => StatusCodes.Status504GatewayTimeout,
        "PATH_NOT_FOUND" => StatusCodes.Status404NotFound,
        "PATH_NOT_ALLOWED" => StatusCodes.Status422UnprocessableEntity,
        "PERMISSION_DENIED" => StatusCodes.Status403Forbidden,
        "TIMEOUT" => StatusCodes.Status504GatewayTimeout,
        "INVALID_NAME" => StatusCodes.Status400BadRequest,
        "NAME_TOO_LONG" => StatusCodes.Status400BadRequest,
        "PATH_TOO_LONG" => StatusCodes.Status400BadRequest,
        "NAME_ALREADY_EXISTS" => StatusCodes.Status409Conflict,
        "INSUFFICIENT_SPACE" => StatusCodes.Status422UnprocessableEntity,
        "TRANSFER_IN_PROGRESS" => StatusCodes.Status409Conflict,
        "FILE_LIMIT_EXCEEDED" => StatusCodes.Status400BadRequest,
        "TRANSFER_FAILED" => StatusCodes.Status500InternalServerError,
        "TRANSFER_NOT_IN_PROGRESS" => StatusCodes.Status409Conflict,
        "INVALID_INPUT" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string GetRfcType(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        403 => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        409 => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
        504 => "https://tools.ietf.org/html/rfc7231#section-6.6.5",
        _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
    };

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        504 => "Gateway Timeout",
        _ => "Internal Server Error"
    };

    private static ProblemDetails ToProblemDetails(Error error, int statusCode)
    {
        var problemDetails = new ProblemDetails
        {
            Type = GetRfcType(statusCode),
            Title = GetTitle(statusCode),
            Status = statusCode,
            Detail = error.Message
        };

        problemDetails.Extensions["code"] = error.Code;

        if (error.Details is { Count: > 0 })
        {
            foreach (var (key, value) in error.Details)
            {
                problemDetails.Extensions[key] = value;
            }
        }

        return problemDetails;
    }
}
