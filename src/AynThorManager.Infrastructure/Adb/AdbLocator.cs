using CliWrap;
using CliWrap.Buffered;

namespace AynThorManager.Infrastructure.Adb;

public static class AdbLocator
{
    public static async Task<string> ResolveAsync(IEnumerable<string> candidates, CancellationToken ct = default)
    {
        string? firstExisting = null;

        foreach (var path in candidates)
        {
            if (!await ExistsAsync(path, ct))
                continue;

            firstExisting ??= path;

            if (await HasMdnsAsync(path, ct))
                return path; 
        }

        return firstExisting ?? "adb";
    }

    public static IEnumerable<string> DefaultCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "platform-tools", "adb");
        yield return Path.Combine(baseDir, "adb");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "platform-tools", "adb");
        yield return "adb"; 
    }

    private static async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        if (path == "adb") return true; 
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
