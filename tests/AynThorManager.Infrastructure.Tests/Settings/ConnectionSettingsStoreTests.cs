using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Settings;

namespace AynThorManager.Infrastructure.Tests.Settings;

/// <summary>
/// Round-trips the non-secret connection preferences through a temp file via the
/// internal path seam, and checks the best-effort fallbacks (missing/corrupt file).
/// </summary>
public class ConnectionSettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public ConnectionSettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "atm-tests-" + Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Load_returns_defaults_when_no_file_exists()
    {
        var store = new ConnectionSettingsStore(_file);

        var settings = await store.LoadAsync();

        Assert.Null(settings.LastSsid);
    }

    [Fact]
    public async Task Save_then_load_round_trips_the_ssid()
    {
        var store = new ConnectionSettingsStore(_file);

        await store.SaveAsync(new ConnectionSettings("AynThor"));
        var settings = await new ConnectionSettingsStore(_file).LoadAsync();

        Assert.Equal("AynThor", settings.LastSsid);
    }

    [Fact]
    public async Task Save_creates_the_parent_directory()
    {
        // The config dir (~/.config/ayn-thor-manager) may not exist yet.
        Assert.False(Directory.Exists(_dir));
        var store = new ConnectionSettingsStore(_file);

        await store.SaveAsync(new ConnectionSettings("AynThor"));

        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task Save_overwrites_the_previous_value()
    {
        var store = new ConnectionSettingsStore(_file);

        await store.SaveAsync(new ConnectionSettings("Primeiro"));
        await store.SaveAsync(new ConnectionSettings("Segundo"));

        Assert.Equal("Segundo", (await store.LoadAsync()).LastSsid);
    }

    [Fact]
    public async Task Load_falls_back_to_defaults_on_a_corrupt_file()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(_file, "{ this is not valid json");
        var store = new ConnectionSettingsStore(_file);

        var settings = await store.LoadAsync();

        Assert.Null(settings.LastSsid); // no throw, just defaults
    }
}
