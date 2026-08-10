namespace AynThorManager.Api.WebSocket;

/// <summary>
/// Maps the WebSocket endpoint at /ws for real-time communication.
/// </summary>
public static class WebSocketEndpoints
{
    /// <summary>
    /// Maps the /ws WebSocket endpoint that accepts connections and delegates
    /// handling to the WebSocketHandler singleton.
    /// </summary>
    public static WebApplication MapWebSocketEndpoint(this WebApplication app)
    {
        app.UseWebSockets();

        app.Map("/ws", async (HttpContext context, WebSocketHandler handler) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await handler.HandleConnectionAsync(webSocket, context.RequestAborted);
        });

        return app;
    }
}
