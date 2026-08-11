using AynThorManager.Api;
using AynThorManager.Api.Endpoints;
using AynThorManager.Api.Middleware;
using AynThorManager.Api.WebSocket;
using AynThorManager.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Register application services
builder.Services.AddAdbServices(builder.Configuration);
builder.Services.AddFileStorageServices();
builder.Services.AddStreamServices();

// WebSocketHandler and TransferService have a circular dependency.
// Register IWebSocketNotifier first, then TransferService.
builder.Services.AddSingleton<WebSocketHandler>();
builder.Services.AddSingleton<IWebSocketNotifier>(sp => sp.GetRequiredService<WebSocketHandler>());
builder.Services.AddTransferServices();

var app = builder.Build();

// Wire up WebSocket subscriptions after DI is resolved
var wsHandler = app.Services.GetRequiredService<WebSocketHandler>();
wsHandler.SubscribeToEvents(
    app.Services.GetRequiredService<ITransferService>(),
    app.Services.GetRequiredService<IAdbConnectionManager>());

// Middleware and endpoints
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDeviceEndpoints();
app.MapFileEndpoints();
app.MapStreamEndpoints();
app.MapWebSocketEndpoint();

Console.WriteLine($"Ayn Thor Manager API starting on http://localhost:5000");
app.Run("http://localhost:5000");

// Make the implicit Program class accessible for integration tests
public partial class Program;
