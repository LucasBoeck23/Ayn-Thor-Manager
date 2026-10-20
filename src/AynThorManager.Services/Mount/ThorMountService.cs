using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Mount;

public sealed class ThorMountService : IDisposable
{
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

        this.device.StateChanged += OnDeviceStateChanged;
    }

    public MountStatus Status
    {
        get { lock (gate) return status; }
    }

    public event Action<MountStatus>? StateChanged;

    public async Task<IReadOnlyList<MountPrerequisite>> CheckPrerequisitesAsync(CancellationToken ct = default)
    {
        var hostChecks = await prerequisites.CheckAsync(ct);

        var result = new List<MountPrerequisite>(hostChecks) { BuildThorPrerequisite() };

        var missing = result.Count(p => !p.Ok);
        logger.LogInformation(
            "Mount prerequisites: {Total} checked, {Missing} missing",
            result.Count, missing);

        return result;
    }

    public async Task<Result> OpenAsync(CancellationToken ct = default)
    {
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

        if (mount.IsMounted && mount.MountPoint is { } existing)
        {
            logger.LogInformation("Thor already mounted at {MountPoint}; reopening file manager", existing);
            return await OpenFileManagerAsync(existing, ct);
        }

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
            const string reason = "A montagem terminou sem um ponto de montagem válido.";
            logger.LogError("Mount reported success but no mount point is set");
            SetStatus(MountStatus.Error(reason));
            return Result.Failure(new Error("MOUNT_FAILED", reason));
        }

        SetStatus(MountStatus.Mounted(mountPoint));

        await AddBookmarkAsync(mountPoint, ct);

        return await OpenFileManagerAsync(mountPoint, ct);
    }

    public async Task UnmountAsync(CancellationToken ct = default)
    {
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

        if (!string.IsNullOrWhiteSpace(mountPoint))
            await RemoveBookmarkAsync(mountPoint, ct);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        device.StateChanged -= OnDeviceStateChanged;
    }

    private void OnDeviceStateChanged(ThorDevice? current)
    {
        if (current is not null || !mount.IsMounted)
            return;

        logger.LogWarning("Thor dropped off the hotspot; unmounting to avoid a dead folder");

        _ = UnmountAsync();
    }

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

    private MountPrerequisite BuildThorPrerequisite() =>
        device.Current is not null
            ? new MountPrerequisite("Thor conectado", true)
            : new MountPrerequisite(
                "Thor conectado",
                false,
                "Conecte o Thor para abrir seus arquivos. A ação fica indisponível sem o Thor conectado.");

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
