using System.Text.RegularExpressions;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Network;

public static class IpNeighborParser
{
    private static readonly Regex LineRegex = new(
        @"^(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b.*?(?:lladdr\s+(?<mac>[0-9a-fA-F:]{17}))?\s*(?<state>REACHABLE|STALE|DELAY|PROBE|PERMANENT|NOARP|INCOMPLETE|FAILED|NONE)?\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<NetworkClient> Parse(string output)
    {
        var clients = new List<NetworkClient>();
        if (string.IsNullOrWhiteSpace(output)) return clients;

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = LineRegex.Match(raw);
            if (!m.Success) continue;

            var mac = m.Groups["mac"].Success ? m.Groups["mac"].Value.ToLowerInvariant() : string.Empty;
            var state = m.Groups["state"].Value;

            if (mac.Length == 0) continue;
            var reachable = state is "REACHABLE" or "STALE" or "DELAY" or "PROBE" or "PERMANENT";

            clients.Add(new NetworkClient(m.Groups["ip"].Value, mac, reachable));
        }

        return clients;
    }
}
