namespace AynThorManager.Core.Models;

public enum MountState
{
    NotMounted,
    Mounting,
    Mounted,
    Error
}

public sealed record MountStatus(
    MountState State,
    string? MountPoint = null,
    string? Reason = null)
{
    public static MountStatus NotMounted { get; } = new(MountState.NotMounted);

    public static MountStatus Mounting { get; } = new(MountState.Mounting);

    public static MountStatus Mounted(string mountPoint) =>
        new(MountState.Mounted, MountPoint: mountPoint);

    public static MountStatus Error(string reason) =>
        new(MountState.Error, Reason: reason);
}

public sealed record MountPrerequisite(
    string Name,
    bool Ok,
    string? Guidance = null,
    string? Url = null);
