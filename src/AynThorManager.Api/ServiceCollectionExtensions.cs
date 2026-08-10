using AynThorManager.Core.Interfaces;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Services.Adb;
using AynThorManager.Services.FileStorage;
using AynThorManager.Services.Transfer;

namespace AynThorManager.Api;

/// <summary>
/// Extension methods for registering application services into the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ADB-related services: command executor, command queue, connection manager,
    /// heartbeat background service, and AdbOptions configuration.
    /// </summary>
    public static IServiceCollection AddAdbServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AdbOptions>(config.GetSection("Adb"));

        services.AddSingleton<IAdbCommandExecutor, AdbCommandExecutor>();
        services.AddSingleton<ICommandQueue, CommandQueue>();
        services.AddSingleton<AdbConnectionService>();
        services.AddSingleton<IAdbConnectionManager>(sp => sp.GetRequiredService<AdbConnectionService>());
        services.AddHostedService<HeartbeatService>();

        return services;
    }

    /// <summary>
    /// Registers file storage services for device file/directory CRUD operations.
    /// </summary>
    public static IServiceCollection AddFileStorageServices(this IServiceCollection services)
    {
        services.AddSingleton<IFileStorageService, FileStorageService>();
        return services;
    }

    /// <summary>
    /// Registers transfer services for file upload operations with progress tracking.
    /// </summary>
    public static IServiceCollection AddTransferServices(this IServiceCollection services)
    {
        services.AddSingleton<ITransferService, TransferService>();
        return services;
    }
}
