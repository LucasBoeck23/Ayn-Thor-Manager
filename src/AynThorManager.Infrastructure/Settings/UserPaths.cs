namespace AynThorManager.Infrastructure.Settings;

internal static class UserPaths
{
    public static string ConfigHome()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome))
            return configHome;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }
}
