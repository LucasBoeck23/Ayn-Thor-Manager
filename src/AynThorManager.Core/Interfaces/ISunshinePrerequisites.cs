using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Verifies the PC-side prerequisites for reverse streaming (PC → Thor) and reports
/// each as a <see cref="Prerequisite"/>. The implementation lives in the Infrastructure
/// layer; the Services orchestrator depends on this abstraction so it never references
/// Infrastructure directly. Implementations only inspect and guide — they never install
/// anything and never ask for sudo (Req 2).
/// </summary>
public interface ISunshinePrerequisites
{
    /// <summary>
    /// Runs the PC-side checks and returns one <see cref="Prerequisite"/> per check
    /// (installed, running, encoder, Wayland capture). Callers filter on
    /// <see cref="Prerequisite.Ok"/> to find what is missing.
    /// </summary>
    Task<IReadOnlyList<Prerequisite>> CheckAsync(CancellationToken ct = default);
}
