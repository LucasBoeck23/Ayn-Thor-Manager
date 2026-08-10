using AynThorManager.Api.Middleware;
using AynThorManager.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AynThorManager.UnitTests.Middleware;

public sealed class ErrorMappingTests
{
    [Theory]
    [InlineData("DEVICE_NOT_CONNECTED", StatusCodes.Status409Conflict)]
    [InlineData("DEVICE_UNAUTHORIZED", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("INVALID_IP_FORMAT", StatusCodes.Status400BadRequest)]
    [InlineData("CONNECTION_ALREADY_ACTIVE", StatusCodes.Status409Conflict)]
    [InlineData("CONNECTION_TIMEOUT", StatusCodes.Status504GatewayTimeout)]
    [InlineData("PATH_NOT_FOUND", StatusCodes.Status404NotFound)]
    [InlineData("PATH_NOT_ALLOWED", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("PERMISSION_DENIED", StatusCodes.Status403Forbidden)]
    [InlineData("TIMEOUT", StatusCodes.Status504GatewayTimeout)]
    [InlineData("INVALID_NAME", StatusCodes.Status400BadRequest)]
    [InlineData("NAME_TOO_LONG", StatusCodes.Status400BadRequest)]
    [InlineData("PATH_TOO_LONG", StatusCodes.Status400BadRequest)]
    [InlineData("NAME_ALREADY_EXISTS", StatusCodes.Status409Conflict)]
    [InlineData("INSUFFICIENT_SPACE", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("TRANSFER_IN_PROGRESS", StatusCodes.Status409Conflict)]
    [InlineData("FILE_LIMIT_EXCEEDED", StatusCodes.Status400BadRequest)]
    [InlineData("TRANSFER_FAILED", StatusCodes.Status500InternalServerError)]
    [InlineData("TRANSFER_NOT_IN_PROGRESS", StatusCodes.Status409Conflict)]
    [InlineData("INVALID_INPUT", StatusCodes.Status400BadRequest)]
    public void ToProblemResult_KnownErrorCode_ReturnsCorrectStatusCode(string errorCode, int expectedStatus)
    {
        var error = new Error(errorCode, "Test message");

        var result = ErrorMapping.ToProblemResult(error);

        var problemResult = result as ProblemHttpResult;
        problemResult.Should().NotBeNull();
        problemResult!.StatusCode.Should().Be(expectedStatus);

        var problemDetails = problemResult.ProblemDetails;
        problemDetails.Detail.Should().Be("Test message");
        problemDetails.Status.Should().Be(expectedStatus);
        problemDetails.Extensions.Should().ContainKey("code");
        problemDetails.Extensions["code"].Should().Be(errorCode);
    }

    [Fact]
    public void ToProblemResult_UnknownErrorCode_Returns500()
    {
        var error = new Error("UNKNOWN_ERROR", "Something unexpected");

        var result = ErrorMapping.ToProblemResult(error);

        var problemResult = result as ProblemHttpResult;
        problemResult.Should().NotBeNull();
        problemResult!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var problemDetails = problemResult.ProblemDetails;
        problemDetails.Title.Should().Be("Internal Server Error");
        problemDetails.Extensions["code"].Should().Be("UNKNOWN_ERROR");
    }

    [Fact]
    public void ToProblemResult_WithDetails_IncludesDetailsInExtensions()
    {
        var details = new Dictionary<string, object>
        {
            ["requiredSpace"] = 1073741824L,
            ["availableSpace"] = 536870912L
        };
        var error = new Error("INSUFFICIENT_SPACE", "Not enough space", details);

        var result = ErrorMapping.ToProblemResult(error);

        var problemResult = result as ProblemHttpResult;
        problemResult.Should().NotBeNull();

        var problemDetails = problemResult!.ProblemDetails;
        problemDetails.Extensions.Should().ContainKey("code");
        problemDetails.Extensions["code"].Should().Be("INSUFFICIENT_SPACE");
        problemDetails.Extensions.Should().ContainKey("requiredSpace");
        problemDetails.Extensions.Should().ContainKey("availableSpace");
        problemDetails.Extensions["requiredSpace"].Should().Be(1073741824L);
        problemDetails.Extensions["availableSpace"].Should().Be(536870912L);
    }

    [Fact]
    public void ToProblemResult_WithNullDetails_StillIncludesCodeExtension()
    {
        var error = new Error("PATH_NOT_FOUND", "Path not found");

        var result = ErrorMapping.ToProblemResult(error);

        var problemResult = result as ProblemHttpResult;
        problemResult.Should().NotBeNull();

        var problemDetails = problemResult!.ProblemDetails;
        problemDetails.Extensions.Should().ContainKey("code");
        problemDetails.Extensions["code"].Should().Be("PATH_NOT_FOUND");
    }

    [Theory]
    [InlineData("DEVICE_NOT_CONNECTED", "https://tools.ietf.org/html/rfc7231#section-6.5.8", "Conflict")]
    [InlineData("INVALID_IP_FORMAT", "https://tools.ietf.org/html/rfc7231#section-6.5.1", "Bad Request")]
    [InlineData("PATH_NOT_FOUND", "https://tools.ietf.org/html/rfc7231#section-6.5.4", "Not Found")]
    [InlineData("PERMISSION_DENIED", "https://tools.ietf.org/html/rfc7231#section-6.5.3", "Forbidden")]
    [InlineData("DEVICE_UNAUTHORIZED", "https://tools.ietf.org/html/rfc4918#section-11.2", "Unprocessable Entity")]
    [InlineData("TIMEOUT", "https://tools.ietf.org/html/rfc7231#section-6.6.5", "Gateway Timeout")]
    [InlineData("TRANSFER_FAILED", "https://tools.ietf.org/html/rfc7231#section-6.6.1", "Internal Server Error")]
    public void ToProblemResult_SetsCorrectRfcTypeAndTitle(string errorCode, string expectedType, string expectedTitle)
    {
        var error = new Error(errorCode, "Test message");

        var result = ErrorMapping.ToProblemResult(error);

        var problemResult = result as ProblemHttpResult;
        problemResult.Should().NotBeNull();

        var problemDetails = problemResult!.ProblemDetails;
        problemDetails.Type.Should().Be(expectedType);
        problemDetails.Title.Should().Be(expectedTitle);
    }
}
