using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Mount;

/// <summary>
/// Orchestrates mounting the Thor's filesystem in the PC's native file manager via a
/// native FUSE mount written in this repo (no third-party <c>adbfs</c> binary). The app is
/// an orchestrator, not an installer — this service only verifies, mounts/unmounts and
/// opens the file manager; it never installs software and never asks for sudo (Req 3). It
/// mirrors the structure of <see cref="Streaming.ReverseStreamingService"/>: collaborators
/// via constructor DI, a <c>StateChanged</c> event exposing a <see cref="MountStatus"/>,
/// and a subscription to <see cref="DeviceConnection.StateChanged"/>. Unlike the
/// reverse-streaming orchestrator it implements <see cref="IDisposable"/> so the UI can
/// detach the device subscription cleanly when the window closes (and so no handler
/// outlives the service).
///
/// Collaborators:
/// <list type="bullet">
///   <item><description><c>device</c>: the already connected Thor — supplies the ADB serial to mount with (no IP asked, Req 1.2) and raises <c>StateChanged(null)</c> when the Thor drops so the service can unmount (Req 4.1).</description></item>
///   <item><description><c>prerequisites</c>: host-side FUSE checks, via a Core abstraction so Services never references Infrastructure (Req 3).</description></item>
///   <item><description><c>mount</c>: the native FUSE mount itself, mounted/unmounted by the happy path (Req 1, Req 4).</description></item>
///   <item><description><c>bookmark</c>: the native file manager's sidebar shortcut — adds "Ayn Thor" pointing at the mount point after mounting and removes it on unmount (incl. the device-dropped path and close). Best-effort: a sidebar failure never fails the mount (Req 1.5, 4.1).</description></item>
///   <item><description><c>fileManager</c>: opens the native file manager at the mount point (Req 1.1/1.3).</description></item>
/// </list>
/// </summary>
public sealed class ThorMountService : IDisposable
{
    /// <summary>The display name the sidebar shortcut carries (Req 1.5).</summary>
    private const string BookmarkLabel = "Ayn Thor";

    private readonly DeviceConnection device;
    private readonly IMountPrerequisites prerequisites;
    private readonly IThorMount mount;
    private readonly ISidebarBookmark bookmark;
    private readonly IFileManagerLauncher fileManager;
    private readonly ILogger<ThorMountService> logger;

    private readonly object gate = new();
    private MountStatus status = MountStatus.NotMounted;
    private bool disposed;

    public ThorMountService(
        DeviceConnection device,
        IMountPrerequisites prerequisites,
        IThorMount mount,
        ISidebarBookmark bookmark,
        IFileManagerLauncher fileManager,
        ILogger<ThorMountService> logger)
    {
        this.device = device;
        this.prerequisites = prerequisites;
        this.mount = mount;
        this.bookmark = bookmark;
        this.fileManager = fileManager;
        this.logger = logger;

        // Req 4.1: the Thor can drop off its own hotspot. DeviceConnection raises
        // StateChanged(null) when the device is lost; reflect that by unmounting whatever
        // the app mounted (and removing the sidebar shortcut) so the file manager is never
        // left stuck on a dead folder.
        this.device.StateChanged += OnDeviceStateChanged;
    }

    /// <summary>
    /// The current mount status the UI reflects (Req 3). Starts at
    /// <see cref="MountStatus.NotMounted"/> and transitions through
    /// <see cref="MountState.Mounting"/> / <see cref="MountState.Mounted"/> /
    /// <see cref="MountState.Error"/> as the flow runs. Each change is also pushed through
    /// <see cref="StateChanged"/>.
    /// </summary>
    public MountStatus Status
    {
        get { lock (gate) return status; }
    }

    /// <summary>
    /// Raised whenever <see cref="Status"/> changes, so the UI can drive its state
    /// (NotMounted / Mounting / Mounted / Error) without polling (Req 3).
    /// </summary>
    public event Action<MountStatus>? StateChanged;

    /// <summary>
    /// Req 2: re-evaluates every prerequisite and returns the full list — the host-side
    /// FUSE checks plus a "Thor conectado?" check this service owns. Nothing is
    /// cached: each call probes fresh, so a user who resolves a missing piece and retries
    /// sees the updated state. Callers decide what to show by filtering on
    /// <see cref="MountPrerequisite.Ok"/>: anything not ok is "what's missing" and carries
    /// guidance. The app only orients — it never installs (Req 2.1/2.2).
    /// </summary>
    public async Task<IReadOnlyList<MountPrerequisite>> CheckPrerequisitesAsync(CancellationToken ct = default)
    {
        // Req 2.1/2.2: host-side FUSE checks (Core abstraction; never installs).
        var hostChecks = await prerequisites.CheckAsync(ct);

        // Req 2.3: the Thor-connected check belongs here — this service owns the
        // DeviceConnection. When no Thor is connected the mount action is unavailable, so
        // surface it as a not-ok prerequisite with clear guidance.
        var result = new List<MountPrerequisite>(hostChecks) { BuildThorPrerequisite() };

        var missing = result.Count(p => !p.Ok);
        logger.LogInformation(
            "Mount prerequisites: {Total} checked, {Missing} missing",
            result.Count, missing);

        return result;
    }

    /// <summary>
    /// Req 1 happy path. Re-checks prerequisites (fresh, never cached) and gates on them:
    /// if anything is missing it returns a failure whose message lists what's missing so
    /// the UI can guide the user (Req 2.2) — nothing is installed.
    ///
    /// When the gate passes:
    /// <list type="bullet">
    ///   <item><description>Req 1.3: if a mount is already live, it does not mount again — it just reopens the file manager at the existing mount point.</description></item>
    ///   <item><description>Req 1.1/1.2: otherwise it mounts via <see cref="IThorMount"/> using the current <see cref="DeviceConnection"/> serial (no IP asked), then opens the file manager at the new mount point.</description></item>
    /// </list>
    /// </summary>
    public async Task<Result> OpenAsync(CancellationToken ct = default)
    {
        // Req 2.2/2.3: re-evaluate prerequisites and gate. Any missing one blocks the
        // mount and the message lists them so the UI can orient the user.
        var prereqs = await CheckPrerequisitesAsync(ct);
        var missing = prereqs.Where(p => !p.Ok).ToList();
        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Select(p => p.Name));
            logger.LogInformation("Mount blocked: missing {Missing}", names);
            return Result.Failure(new Error(
                "MOUNT_PREREQUISITES_MISSING",
                $"Não é possível abrir os arquivos do Thor. Faltando: {names}."));
        }

        // Req 1.3: already mounted → never mount again, just reopen the file manager at
        // the existing mount point and keep the current status.
        if (mount.IsMounted && mount.MountPoint is { } existing)
        {
            logger.LogInformation("Thor already mounted at {MountPoint}; reopening file manager", existing);
            return await OpenFileManagerAsync(existing, ct);
        }

        // Req 1.2: mount with the serial of the Thor already connected — no IP is asked.
        // CheckPrerequisitesAsync guaranteed the Thor is connected, but re-read defensively
        // in case it dropped between the check and here.
        var serial = device.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            logger.LogInformation("Mount aborted: Thor disconnected before mounting");
            return Result.Failure(new Error(
                "DEVICE_NOT_CONNECTED",
                "O Thor não está conectado. Conecte o Thor e tente novamente."));
        }

        SetStatus(MountStatus.Mounting);
        try
        {
            // Req 1.1/1.4: the native FUSE mount mounts at a per-user mount point (owned by the Infra impl).
            await mount.MountAsync(serial, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mount the Thor");
            SetStatus(MountStatus.Error(ex.Message));
            return Result.Failure(new Error("MOUNT_FAILED", $"Não foi possível montar o Thor: {ex.Message}"));
        }

        var mountPoint = mount.MountPoint;
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            // Should not happen after a successful mount, but guard rather than open a
            // null path and reflect the inconsistency as an error.
            const string reason = "A montagem terminou sem um ponto de montagem válido.";
            logger.LogError("Mount reported success but no mount point is set");
            SetStatus(MountStatus.Error(reason));
            return Result.Failure(new Error("MOUNT_FAILED", reason));
        }

        // Req 1.1: reflect the live mount.
        SetStatus(MountStatus.Mounted(mountPoint));

        // Req 1.5: register the "Ayn Thor" sidebar shortcut pointing at the mount point.
        // Best-effort — a sidebar failure must never fail the mount, so swallow it (the
        // files are mounted regardless of whether the shortcut showed up).
        await AddBookmarkAsync(mountPoint, ct);

        // Then open the native file manager at the mount point.
        return await OpenFileManagerAsync(mountPoint, ct);
    }

    /// <summary>
    /// Req 4: unmounts whatever the app mounted. Idempotent (the mount itself is safe to
    /// unmount when nothing is mounted) and best-effort — a failing unmount still moves
    /// the status to <see cref="MountState.Error"/> so the UI reflects the real state.
    /// On success the status returns to <see cref="MountState.NotMounted"/>.
    ///
    /// Req 1.5/4.1: also removes the "Ayn Thor" sidebar shortcut. The mount point is
    /// captured BEFORE unmounting because <see cref="IThorMount.MountPoint"/> becomes
    /// <c>null</c> once unmounted. This single path covers both the device-dropped flow
    /// (<see cref="OnDeviceStateChanged"/>) and the close flow, so the shortcut is removed
    /// whenever the Thor drops or the window closes.
    /// </summary>
    public async Task UnmountAsync(CancellationToken ct = default)
    {
        // Capture before unmounting: MountPoint is cleared once the mount is torn down.
        var mountPoint = mount.MountPoint;

        try
        {
            await mount.UnmountAsync(ct);
            SetStatus(MountStatus.NotMounted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unmount the Thor");
            SetStatus(MountStatus.Error(ex.Message));
        }

        // Req 1.5/4.1: remove the sidebar shortcut regardless of how the unmount went —
        // the shortcut points at a path that is no longer (or no longer reliably) mounted.
        // Best-effort: a sidebar failure must not surface over the unmount outcome.
        if (!string.IsNullOrWhiteSpace(mountPoint))
            await RemoveBookmarkAsync(mountPoint, ct);
    }

    /// <summary>
    /// Detaches the <see cref="DeviceConnection"/> subscription so no handler outlives the
    /// service. The window owner disposes this when it closes; the actual unmount-on-close
    /// is driven explicitly by the UI via <see cref="UnmountAsync"/> (Req 3.2).
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        device.StateChanged -= OnDeviceStateChanged;
    }

    /// <summary>
    /// Req 4.1: the shared <see cref="DeviceConnection"/> changed state. When it reports
    /// the Thor was lost (<c>null</c>) and the app has a mount live, the mount can't
    /// survive — unmount it (which also removes the sidebar shortcut) so the file manager
    /// isn't left stuck on a dead folder. A (re)connection, or a drop while nothing is
    /// mounted, needs no reaction.
    /// </summary>
    private void OnDeviceStateChanged(ThorDevice? current)
    {
        if (current is not null || !mount.IsMounted)
            return;

        logger.LogWarning("Thor dropped off the hotspot; unmounting to avoid a dead folder");

        // Fire-and-forget: the event is raised synchronously from DeviceConnection and we
        // must not block it. Failures are reflected into the status by UnmountAsync.
        _ = UnmountAsync();
    }

    /// <summary>
    /// Req 1.5: adds the "Ayn Thor" sidebar shortcut at <paramref name="mountPoint"/>.
    /// Best-effort — <see cref="ISidebarBookmark"/> is itself best-effort, but this also
    /// guards so a sidebar failure never fails the mount: the files stay mounted whether
    /// or not the shortcut appears.
    /// </summary>
    private async Task AddBookmarkAsync(string mountPoint, CancellationToken ct)
    {
        try
        {
            await bookmark.AddAsync(mountPoint, BookmarkLabel, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add the sidebar shortcut at {MountPoint}", mountPoint);
        }
    }

    /// <summary>
    /// Req 1.5/4.1: removes the "Ayn Thor" sidebar shortcut at <paramref name="mountPoint"/>.
    /// Best-effort — a sidebar failure must not surface over the unmount outcome.
    /// </summary>
    private async Task RemoveBookmarkAsync(string mountPoint, CancellationToken ct)
    {
        try
        {
            await bookmark.RemoveAsync(mountPoint, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove the sidebar shortcut at {MountPoint}", mountPoint);
        }
    }

    /// <summary>
    /// Opens the native file manager at <paramref name="path"/> (Req 1.1/1.3). Best-effort:
    /// a launch failure does not tear down the mount (the files are still mounted), it is
    /// surfaced as a failure so the UI can tell the user where to look.
    /// </summary>
    private async Task<Result> OpenFileManagerAsync(string path, CancellationToken ct)
    {
        try
        {
            await fileManager.OpenAsync(path, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open the file manager at {Path}", path);
            return Result.Failure(new Error(
                "FILE_MANAGER_OPEN_FAILED",
                $"O Thor está montado em \"{path}\", mas não foi possível abrir o gerenciador de arquivos: {ex.Message}"));
        }
    }

    /// <summary>
    /// Req 2.3: builds the "Thor conectado?" prerequisite from the owned
    /// <see cref="DeviceConnection"/>. Ok only while a Thor is connected; otherwise it
    /// carries guidance telling the user the action is unavailable until they connect.
    /// </summary>
    private MountPrerequisite BuildThorPrerequisite() =>
        device.Current is not null
            ? new MountPrerequisite("Thor conectado", true)
            : new MountPrerequisite(
                "Thor conectado",
                false,
                "Conecte o Thor para abrir seus arquivos. A ação fica indisponível sem o Thor conectado.");

    /// <summary>
    /// Updates <see cref="Status"/> and notifies subscribers. Coalesces no-op transitions
    /// so the UI isn't nudged with identical statuses, and raises the event outside the
    /// lock so a handler can read <see cref="Status"/> without deadlocking.
    /// </summary>
    private void SetStatus(MountStatus next)
    {
        lock (gate)
        {
            if (status == next)
                return;
            status = next;
        }

        StateChanged?.Invoke(next);
    }
}
