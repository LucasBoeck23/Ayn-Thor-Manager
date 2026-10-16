using System.Text.RegularExpressions;
using AynThorManager.Core.Models;

namespace AynThorManager.Infrastructure.Network;

/// <summary>
/// Pure parser for the output of <c>ip neigh show dev &lt;iface&gt;</c>. Kept
/// separate from process execution so it can be unit-tested against recorded output.
///
/// Example lines:
///   192.168.1.17 lladdr 02:16:19:92:86:0e REACHABLE
///   192.168.1.250 FAILED
///   10.42.0.123 dev wlp2s0 lladdr aa:bb:cc:dd:ee:ff STALE
/// </summary>
public static class IpNeighborParser
{
    // Captures the IPv4 address, optional lladdr MAC, and the trailing state word.
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

            // A usable client has a MAC and is not in a dead state.
            if (mac.Length == 0) continue;
            var reachable = state is "REACHABLE" or "STALE" or "DELAY" or "PROBE" or "PERMANENT";

            clients.Add(new NetworkClient(m.Groups["ip"].Value, mac, reachable));
        }

        return clients;
    }
}
