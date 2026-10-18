namespace AynThorManager.Core.Models;

/// <summary>
/// Persisted, non-secret connection preferences for the Thor's hotspot. Only the SSID
/// is stored here (plain config); the Wi-Fi password is a credential and lives in the
/// system keyring via <see cref="AynThorManager.Core.Interfaces.ISecretStore"/>, never
/// in this file.
/// </summary>
public sealed record ConnectionSettings(string? LastSsid = null);
