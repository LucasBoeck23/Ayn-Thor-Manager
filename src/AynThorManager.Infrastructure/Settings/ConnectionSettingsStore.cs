using System.Text.Json;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Settings;

public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public ConnectionSettingsStore() : this(DefaultFilePath())
    {
    }

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
        }
    }
}
