using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;
using Microsoft.Extensions.Logging;

namespace AynThorManager.Services.Streaming;

/// <summary>
/// Thin ADB client for the Moonlight app on the Thor. The app is only ever verified
/// and opened — never installed (the app is an orchestrator, not an installer). Talks
/// to the device through the shared <see cref="DeviceConnection"/>, so it rides the
/// same command queue and connection state as everything else.
/// </summary>
public sealed class MoonlightClient(
    DeviceConnection device,
    ILogger<MoonlightClient> logger)
{
    /// <summary>Confirmed package name of the official Moonlight client on Android.</summary>
    public const string Package = "com.limelight";

    // Activity-agnostic launch: monkey opens the app by package via its LAUNCHER
    // intent, so we don't need to hard-code an exact activity name (to be confirmed
    // during manual validation). Kept as a stable, simple arg string.
    private const string LaunchArgs =
        "shell monkey -p com.limelight -c android.intent.category.LAUNCHER 1";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Checks whether Moonlight is installed on the Thor via <c>pm list packages</c>.
    /// Returns <c>true</c> only when the command succeeds and the package shows up in
    /// the output. A disconnected Thor or a failed command means "not confirmed",
    /// i.e. <c>false</c>. Never attempts to install anything.
    /// </summary>
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

    /// <summary>
    /// Opens Moonlight on the Thor via ADB using an activity-agnostic launcher intent
    /// (<c>monkey ... LAUNCHER</c>). Returns success when the launch command exits
    /// cleanly, or a failure (never an exception) when the Thor is disconnected or the
    /// command fails.
    /// </summary>
    /// <remarks>
    /// Known limitation (Req 4, moonlight-android issue #878): the official Moonlight
    /// (<c>com.limelight</c>) does NOT expose App Intents, so there is no way to pass a
    /// host/IP through <c>am start</c> to auto-connect. The app can only be opened to
    /// its PC list; the final "connect" tap is manual in v1 (Req 4.4). The
    /// <paramref name="ip"/> is therefore NOT passed to the app — it's the PC's IP from
    /// the hotspot gateway flow, kept only so the UI/orchestrator can display/guide the
    /// user on which host to tap (Req 4.3/4.4), and it's logged here.
    /// </remarks>
    /// <param name="ip">PC IP from the hotspot gateway flow, used for guidance only.</param>
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
