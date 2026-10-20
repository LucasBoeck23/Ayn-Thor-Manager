namespace AynThorManager.Core.Models;

public enum ReverseStreamState
{
    Unavailable,
    Checking,
    Active,
    Inactive,
    Error
}

public sealed record Prerequisite(
    string Name,
    bool Ok,
    string? Guidance = null,
    string? Url = null)
{
    public static readonly IReadOnlySet<string> BlockingNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sunshine instalado",
        "Moonlight instalado no Thor",
        "Thor conectado",
    };

    public bool IsBlocking => BlockingNames.Contains(Name);
}

public sealed record ReverseStreamStatus(
    ReverseStreamState State,
    string? Reason = null);
