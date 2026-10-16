using CliWrap;
using CliWrap.Buffered;

namespace AynThorManager.Infrastructure.Adb;

/// <summary>
/// Picks which <c>adb</c> binary the app should use.
///
/// Some distro-packaged adb builds (notably Debian's) ship without mDNS support,
/// so <c>adb mdns services</c> returns nothing and the wireless-debugging port
/// cannot be discovered automatically. The official Google platform-tools adb has
/// mDNS built in. This locator prefers an adb that reports a working mDNS backend,
/// falling back to whatever is on PATH.
/// </summary>
public static class AdbLocator
{
    /// <summary>
    /// Returns the path to the preferred adb. Checks candidate locations in order
    /// and picks the first whose server reports a working mDNS daemon; if none do,
    /// returns the first adb that exists, or "adb" (PATH) as a last resort.
    /// </summary>
    public static async Task<string> ResolveAsync(IEnumerable<string> candidates, CancellationToken ct = default)
    {
        string? firstExisting = null;

        foreach (var path in candidates)
        {
            if (!await ExistsAsync(path, ct))
                continue;

            firstExisting ??= path;

            if (await HasMdnsAsync(path, ct))
                return path; // best: adb with working mDNS
        }

        return firstExisting ?? "adb";
    }

    /// <summary>The default search order: an adb bundled next to the app, then PATH.</summary>
    public static IEnumerable<string> DefaultCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "platform-tools", "adb");
        yield return Path.Combine(baseDir, "adb");
        // Common manual install location for the official platform-tools.
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "platform-tools", "adb");
        yield return "adb"; // whatever is on PATH (may be the distro build)
    }

    private static async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        if (path == "adb") return true; // assume PATH lookup
        if (File.Exists(path)) return true;
        await Task.CompletedTask;
        return false;
    }

    private static async Task<bool> HasMdnsAsync(string path, CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap(path)
                .WithArguments(new[] { "mdns", "check" })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            // A working backend reports e.g. "mdns daemon version [adb discovery ...]".
            // A build without mDNS prints nothing (or an error).
            var text = (result.StandardOutput + result.StandardError);
            return text.Contains("mdns daemon version", StringComparison.OrdinalIgnoreCase)
                || text.Contains("discovery", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
