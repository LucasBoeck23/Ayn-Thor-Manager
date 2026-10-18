using System.Text.Json;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Settings;

/// <summary>
/// Persists the non-secret connection preferences to a small JSON file under the user's
/// config dir (<c>$XDG_CONFIG_HOME/ayn-thor-manager/settings.json</c>, falling back to
/// <c>~/.config</c>). Best-effort: a missing/corrupt file loads as defaults, and save
/// failures are swallowed so a read-only home can't crash the app. Never writes the
/// Wi-Fi password — that is the keyring's job (<see cref="ISecretStore"/>).
/// </summary>
public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    /// <summary>Uses the default per-user config path.</summary>
    public ConnectionSettingsStore() : this(DefaultFilePath())
    {
    }

    /// <summary>Test seam: persist to an explicit path.</summary>
    internal ConnectionSettingsStore(string filePath) => _filePath = filePath;

    private static string DefaultFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, "ayn-thor-manager", "settings.json");
    }

    public async Task<ConnectionSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_filePath))
                return new ConnectionSettings();

            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<ConnectionSettings>(stream, JsonOptions, ct);
            return settings ?? new ConnectionSettings();
        }
        catch (Exception)
        {
            // Corrupt/unreadable file: fall back to defaults rather than failing the UI.
            return new ConnectionSettings();
        }
    }

    public async Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, ct);
        }
        catch (Exception)
        {
            // Best-effort: a read-only home shouldn't crash the app or block connecting.
        }
    }
}
