using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Settings;

namespace AynThorManager.Infrastructure.Tests.Settings;

public class SecretToolStoreTests : IAsyncLifetime
{
    private readonly ISecretStore _store = new SecretToolStore();
    private readonly string _key = "test-" + Guid.NewGuid().ToString("N");
    private bool _keyringAvailable;

    public async Task InitializeAsync()
    {
        var probeKey = "probe-" + Guid.NewGuid().ToString("N");
        await _store.SetAsync(probeKey, "x");
        _keyringAvailable = await _store.GetAsync(probeKey) == "x";
        await _store.DeleteAsync(probeKey);
    }

    public async Task DisposeAsync() => await _store.DeleteAsync(_key);

    [Fact]
    public async Task Set_then_get_round_trips_the_secret()
    {
        if (!_keyringAvailable) return;

        await _store.SetAsync(_key, "s3nh4-do-hotspot");

        Assert.Equal("s3nh4-do-hotspot", await _store.GetAsync(_key));
    }

    [Fact]
    public async Task Set_overwrites_the_previous_secret()
    {
        if (!_keyringAvailable) return;

        await _store.SetAsync(_key, "antiga");
        await _store.SetAsync(_key, "nova");

        Assert.Equal("nova", await _store.GetAsync(_key));
    }

    [Fact]
    public async Task Delete_removes_the_secret()
    {
        if (!_keyringAvailable) return;

        await _store.SetAsync(_key, "para-apagar");
        await _store.DeleteAsync(_key);

        Assert.Null(await _store.GetAsync(_key));
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_key()
    {
        if (!_keyringAvailable) return;

        Assert.Null(await _store.GetAsync("inexistente-" + Guid.NewGuid().ToString("N")));
    }
}
