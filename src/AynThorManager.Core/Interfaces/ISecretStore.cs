namespace AynThorManager.Core.Interfaces;

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string secret, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
