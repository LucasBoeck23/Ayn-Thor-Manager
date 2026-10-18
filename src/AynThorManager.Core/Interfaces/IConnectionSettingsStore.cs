using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Loads and saves the non-secret connection preferences (e.g. the last hotspot SSID)
/// so the UI can pre-fill the fields on the next launch. Implemented in Infrastructure;
/// it persists to a small config file. Never stores the Wi-Fi password — that goes to
/// <see cref="ISecretStore"/>.
/// </summary>
public interface IConnectionSettingsStore
{
    /// <summary>Loads the saved settings, or defaults when nothing was saved yet.</summary>
    Task<ConnectionSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the settings, overwriting the previous values.</summary>
    Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default);
}
