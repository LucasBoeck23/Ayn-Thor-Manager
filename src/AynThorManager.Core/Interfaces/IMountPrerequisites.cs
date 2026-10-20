using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface IMountPrerequisites
{
    Task<IReadOnlyList<MountPrerequisite>> CheckAsync(CancellationToken ct = default);
}
