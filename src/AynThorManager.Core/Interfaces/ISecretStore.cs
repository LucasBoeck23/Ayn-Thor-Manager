namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Stores and retrieves a secret (e.g. the hotspot Wi-Fi password) in the OS credential
/// vault. Implemented in Infrastructure over the system keyring (Secret Service /
/// libsecret). Best-effort: when no keyring backend is available the implementation
/// degrades gracefully (get returns null, set/delete are no-ops) rather than throwing,
/// so the app still works — it just won't remember the secret.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the stored secret for <paramref name="key"/>, or null if none.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores (or replaces) the secret for <paramref name="key"/>.</summary>
    Task SetAsync(string key, string secret, CancellationToken ct = default);

    /// <summary>Removes the stored secret for <paramref name="key"/>, if any.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
