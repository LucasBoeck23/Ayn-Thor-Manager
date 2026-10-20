using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

public interface ISunshinePrerequisites
{
    Task<IReadOnlyList<Prerequisite>> CheckAsync(CancellationToken ct = default);
}
