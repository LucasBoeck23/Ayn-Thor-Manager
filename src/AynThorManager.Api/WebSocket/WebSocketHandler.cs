using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Api.WebSocket;

/// <summary>
/// Manages WebSocket connections and broadcasts real-time events to connected clients.
/// Does NOT take ITransferService in constructor to avoid circular DI dependency.
/// </summary>
public sealed class WebSocketHandler : IWebSocketNotifier, IDisposable
{
    private readonly ConcurrentDictionary<string, System.Net.WebSockets.WebSocket> _clients = new();
    private readonly List<IDisposable> _subscriptions = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Subscribes to observable streams. Called after DI is fully resolved.
    /// </summary>
    public void SubscribeToEvents(ITransferService transferService, IAdbConnectionManager connectionManager)
    {
        _subscriptions.Add(transferService.ProgressUpdates.Subscribe(
            p => _ = SendTransferProgressAsync(p, CancellationToken.None)));

        _subscriptions.Add(connectionManager.StatusChanges.Subscribe(
            s => _ = SendDeviceStatusAsync(s, CancellationToken.None)));
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString("N");
        _clients.TryAdd(clientId, webSocket);

        try
        {
            var buffer = new byte[512];
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            _clients.TryRemove(clientId, out _);
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* best effort */ }
            }
        }
    }

    public Task SendTransferProgressAsync(TransferProgress progress, CancellationToken ct) =>
        BroadcastAsync(new WsMessage("transfer_progress", progress), ct);

    public Task SendDeviceStatusAsync(DeviceStatus status, CancellationToken ct) =>
        BroadcastAsync(new WsMessage("device_status", new
        {
            status = status.Status.ToString().ToLowerInvariant(),
            ipAddress = status.IpAddress,
            message = status.Message
        }), ct);

    public Task SendTransferCompletedAsync(TransferResult result, CancellationToken ct) =>
        BroadcastAsync(new WsMessage("transfer_completed", result), ct);

    public Task SendTransferFailedAsync(TransferResult failure, CancellationToken ct) =>
        BroadcastAsync(new WsMessage("transfer_failed", failure), ct);

    private async Task BroadcastAsync(WsMessage message, CancellationToken ct)
    {
        if (_clients.IsEmpty) return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (id, ws) in _clients)
        {
            if (ws.State != WebSocketState.Open) { _clients.TryRemove(id, out _); continue; }
            try { await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct); }
            catch { _clients.TryRemove(id, out _); }
        }
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();
    }
}
