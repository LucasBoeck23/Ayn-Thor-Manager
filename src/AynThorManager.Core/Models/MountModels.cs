namespace AynThorManager.Core.Models;

/// <summary>
/// The lifecycle of the Thor FUSE mount. <see cref="NotMounted"/> is idle/nothing
/// mounted; <see cref="Mounting"/> covers an in-progress mount; <see cref="Mounted"/> is a
/// live mount (the mount point path is carried by <see cref="MountStatus.MountPoint"/>);
/// <see cref="Error"/> is a failed attempt (see <see cref="MountStatus.Reason"/>).
/// </summary>
public enum MountState
{
    NotMounted,
    Mounting,
    Mounted,
    Error
}

/// <summary>
/// The overall mount status the UI reflects: the current <see cref="State"/> plus an
/// optional payload. <see cref="MountPoint"/> is populated for
/// <see cref="MountState.Mounted"/> (the local path the file manager opens) and
/// <see cref="Reason"/> is populated for <see cref="MountState.Error"/> (why it failed).
/// The static factories model the "discriminated-union" shape idiomatically: callers
/// build a status for a state and only the relevant payload is set.
/// </summary>
public sealed record MountStatus(
    MountState State,
    string? MountPoint = null,
    string? Reason = null)
{
    /// <summary>Nothing is mounted.</summary>
    public static MountStatus NotMounted { get; } = new(MountState.NotMounted);

    /// <summary>A mount is in progress.</summary>
    public static MountStatus Mounting { get; } = new(MountState.Mounting);

    /// <summary>The Thor is mounted at <paramref name="mountPoint"/>.</summary>
    public static MountStatus Mounted(string mountPoint) =>
        new(MountState.Mounted, MountPoint: mountPoint);

    /// <summary>The mount failed; <paramref name="reason"/> explains why.</summary>
    public static MountStatus Error(string reason) =>
        new(MountState.Error, Reason: reason);
}

/// <summary>
/// A single prerequisite for mounting the Thor's filesystem (e.g. "FUSE available?",
/// "Thor connected?"). Mirrors the <see cref="Prerequisite"/> pattern:
/// <see cref="Ok"/> is whether it is satisfied; when it is not, <see cref="Guidance"/>
/// explains how to resolve it (the app orients, it never installs) and <see cref="Url"/>
/// is an optional link. Kept distinct from <see cref="Prerequisite"/> because its
/// blocking semantics and check names belong to the mount flow, not reverse streaming.
/// </summary>
public sealed record MountPrerequisite(
    string Name,
    bool Ok,
    string? Guidance = null,
    string? Url = null);
