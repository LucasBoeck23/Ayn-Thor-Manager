using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IConnectionSettingsStore
{
    Task<ConnectionSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default);
}
