namespace AynThorManager.Core.DTOs;

/// <summary>
/// WebSocket message envelope for real-time communication with clients.
/// </summary>
/// <param name="Type">Message type identifier (e.g., "transfer_progress", "device_status").</param>
/// <param name="Payload">The message payload object.</param>
public sealed record WsMessage(string Type, object Payload);
