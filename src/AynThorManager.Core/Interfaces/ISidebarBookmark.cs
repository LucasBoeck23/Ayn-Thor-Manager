namespace AynThorManager.Core.Interfaces;

public interface ISidebarBookmark
{
    Task AddAsync(string path, string label, CancellationToken ct = default);

    Task RemoveAsync(string path, CancellationToken ct = default);
}
