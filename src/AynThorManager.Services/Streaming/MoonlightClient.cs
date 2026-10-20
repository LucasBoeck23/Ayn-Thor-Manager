using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

public sealed class MoonlightClient(
    DeviceConnection device,
    ILogger<MoonlightClient> logger)
{
    public const string Package = "com.limelight";

    private const string LaunchArgs =
        "shell monkey -p com.limelight -c android.intent.category.LAUNCHER 1";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    public async Task<bool> IsInstalledAsync(CancellationToken ct)
    {
        var result = await device.RunAsync($"shell pm list packages {Package}", CheckTimeout, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Could not verify Moonlight: {Error}", result.Error!.Code);
            return false;
        }

        var commandResult = result.Value!;
        var installed = commandResult.Success
            && commandResult.StandardOutput.Contains($"package:{Package}", StringComparison.Ordinal);

        logger.LogInformation("Moonlight ({Package}) installed on Thor: {Installed}", Package, installed);
        return installed;
    }

    public async Task<Result> LaunchAsync(string ip, CancellationToken ct)
    {
        logger.LogInformation(
            "Opening Moonlight ({Package}) on Thor; guide user to host {Ip} (official app can't auto-connect via intent)",
            Package, ip);

        var result = await device.RunAsync(LaunchArgs, LaunchTimeout, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Could not open Moonlight: {Error}", result.Error!.Code);
            return Result.Failure(new Error("MOONLIGHT_LAUNCH_FAILED",
                $"Não foi possível abrir o Moonlight no Thor: {result.Error.Message}"));
        }

        var commandResult = result.Value!;
        if (!commandResult.Success)
        {
            logger.LogWarning("Moonlight launch command failed (exit {Exit}): {StdErr}",
                commandResult.ExitCode, commandResult.StandardError);
            return Result.Failure(new Error("MOONLIGHT_LAUNCH_FAILED",
                "Não foi possível abrir o Moonlight no Thor. Verifique a conexão e tente novamente."));
        }

        logger.LogInformation("Moonlight ({Package}) opened on Thor", Package);
        return Result.Success();
    }
}
