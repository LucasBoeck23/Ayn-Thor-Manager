using AynThorManager.Api.Middleware;
using AynThorManager.Core.Interfaces;

namespace AynThorManager.Api.Endpoints;

public static class StreamEndpoints
{
    public static RouteGroupBuilder MapStreamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/stream").WithTags("Stream");

        group.MapPost("/start", StartAsync);
        group.MapPost("/stop", StopAsync);
        group.MapGet("/status", GetStatus);

        return group;
    }

    private static async Task<IResult> StartAsync(IStreamService streamService, CancellationToken ct)
    {
        var result = await streamService.StartAsync(ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Ok(new { streaming = true });
    }

    private static async Task<IResult> StopAsync(IStreamService streamService, CancellationToken ct)
    {
        var result = await streamService.StopAsync(ct);
        if (!result.IsSuccess) return ErrorMapping.ToProblemResult(result.Error!);
        return TypedResults.Ok(new { streaming = false });
    }

    private static IResult GetStatus(IStreamService streamService) =>
        TypedResults.Ok(new { streaming = streamService.IsStreaming });
}
