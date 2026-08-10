using System.Text.Json;
using AynThorManager.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger =
        Substitute.For<ILogger<ExceptionHandlingMiddleware>>();

    private readonly IHostEnvironment _productionEnvironment = CreateEnvironment("Production");
    private readonly IHostEnvironment _developmentEnvironment = CreateEnvironment("Development");

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var responseSent = false;

        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                responseSent = true;
                return Task.CompletedTask;
            },
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        responseSent.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_InProduction_Returns500WithGenericMessage()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/files";

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Something went wrong"),
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");

        context.Response.Body.Position = 0;
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(500);
        problemDetails.Title.Should().Be("Internal Server Error");
        problemDetails.Detail.Should().Be("An unexpected error occurred while processing your request.");
        problemDetails.Type.Should().Be("https://tools.ietf.org/html/rfc7231#section-6.6.1");
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_InDevelopment_IncludesExceptionDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/files";

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Detailed error info"),
            _logger,
            _developmentEnvironment);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        body.Should().Contain("Detailed error info");
        body.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_LogsError()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "POST";
        context.Request.Path = "/api/device/connect";

        var exception = new InvalidOperationException("Test error");

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_WhenClientDisconnects_DoesNotWriteResponse()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = cts.Token;

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(cts.Token),
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_NotFromClient_Returns500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/files";

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException("Timeout"),
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionDoesNotLeakDetails_InProduction()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/files";

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Sensitive internal details: connection string = foo"),
            _logger,
            _productionEnvironment);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        body.Should().NotContain("Sensitive internal details");
        body.Should().NotContain("connection string");
    }
}
