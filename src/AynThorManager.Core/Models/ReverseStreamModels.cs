namespace AynThorManager.Core.Models;

/// <summary>
/// The lifecycle of a reverse stream (PC → Thor). <see cref="Unavailable"/> means no
/// Thor is connected; <see cref="Checking"/> covers prerequisite verification and
/// link measurement; <see cref="Active"/> is a running stream; <see cref="Inactive"/>
/// is idle/stopped; <see cref="Error"/> is a failed attempt (see the reason).
/// </summary>
public enum ReverseStreamState
{
    Unavailable,
    Checking,
    Active,
    Inactive,
    Error
}

/// <summary>
/// A single PC prerequisite for reverse streaming (e.g. Sunshine installed, encoder
/// available). <see cref="Ok"/> is whether it is satisfied; when it is not,
/// <see cref="Guidance"/> explains how to resolve it and <see cref="Url"/> is an
/// optional link (e.g. the official download page).
///
/// Prerequisites split into two kinds (Req 5/6, design "informacional e nunca bloqueia"):
/// <list type="bullet">
///   <item><description><b>Blocking</b> — must be satisfied to start the reverse stream
///   (Sunshine installed, Moonlight installed on the Thor, Thor connected). Their names
///   are listed in <see cref="BlockingNames"/>.</description></item>
///   <item><description><b>Best-effort / advisory</b> — everything else (hardware encoder,
///   Wayland Portal capture, Sunshine already running). A not-ok best-effort check only
///   warns the user (e.g. a software-encode performance caveat); it never blocks the
///   start.</description></item>
/// </list>
/// <see cref="IsBlocking"/> classifies a prerequisite by name so the UI and the
/// orchestrator share one definition.
/// </summary>
public sealed record Prerequisite(
    string Name,
    bool Ok,
    string? Guidance = null,
    string? Url = null)
{
    /// <summary>
    /// The names of the prerequisites that must be satisfied before a reverse stream can
    /// start. Everything not listed here is best-effort/advisory and only warns. Kept
    /// here so the UI gate and the orchestrator gate agree on what truly blocks.
    /// </summary>
    public static readonly IReadOnlySet<string> BlockingNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sunshine instalado",
        "Moonlight instalado no Thor",
        "Thor conectado",
    };

    /// <summary>
    /// True when this prerequisite must be resolved before starting (see
    /// <see cref="BlockingNames"/>); false for best-effort/advisory checks that only warn.
    /// </summary>
    public bool IsBlocking => BlockingNames.Contains(Name);
}

/// <summary>
/// The overall reverse-stream status: the current <see cref="State"/> plus an optional
/// <see cref="Reason"/> describing why (populated for <see cref="ReverseStreamState.Error"/>
/// and when a process exits).
/// </summary>
public sealed record ReverseStreamStatus(
    ReverseStreamState State,
    string? Reason = null);
