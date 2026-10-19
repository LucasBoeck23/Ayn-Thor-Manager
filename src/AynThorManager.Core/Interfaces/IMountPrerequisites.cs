using AynThorManager.Core.Models;

namespace AynThorManager.Core.Interfaces;

/// <summary>
/// Verifies the host-side prerequisite for mounting the Thor's filesystem — FUSE
/// (<c>fusermount</c>/<c>fusermount3</c>) — and reports it as a
/// <see cref="MountPrerequisite"/>. The native FUSE→ADB translator is our own code, so
/// there is no longer any third-party <c>adbfs</c> binary to look for; the only system
/// dependency is <c>libfuse</c>/<c>fusermount</c> (Req 3). The implementation lives in the
/// Infrastructure layer (it inspects the PATH for the fusermount binary); the Services
/// orchestrator depends on this abstraction so it never references Infrastructure directly.
/// Implementations only inspect and guide — they never install anything and never ask for
/// sudo (Req 2). The "Thor connected?" check is not part of this contract: it belongs to
/// the Services orchestrator, which owns the <c>DeviceConnection</c>.
/// </summary>
public interface IMountPrerequisites
{
    /// <summary>
    /// Runs the host-side check and returns one <see cref="MountPrerequisite"/> for FUSE
    /// availability (Req 3.2/3.3). Callers filter on <see cref="MountPrerequisite.Ok"/> to
    /// find what is missing.
    /// </summary>
    Task<IReadOnlyList<MountPrerequisite>> CheckAsync(CancellationToken ct = default);
}
