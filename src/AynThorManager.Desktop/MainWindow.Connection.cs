using Avalonia.Interactivity;
using Avalonia.Media;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;

namespace AynThorManager.Desktop;

/// <summary>
/// Connection UI. All device logic lives in <see cref="DeviceConnection"/>;
/// this file only forwards clicks and renders the result.
/// </summary>
public partial class MainWindow
{
    private const string CompanionPackage = "com.aynthor.link";

    private static readonly IBrush SuccessBrush = SolidColorBrush.Parse("#00e676");
    private static readonly IBrush DangerBrush = SolidColorBrush.Parse("#ff5252");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#00d4ff");
    private static readonly IBrush SecondaryBrush = SolidColorBrush.Parse("#78909c");

    private async void BtnScan_Click(object? sender, RoutedEventArgs e)
    {
        SetConnectButtonsEnabled(false);
        ConnMessage.Text = "Buscando...";
        try { await ShowConnectResult(await _device.AutoConnectAsync(default)); }
        catch (Exception ex) { ConnMessage.Text = $"Erro: {ex.Message}"; }
        finally { SetConnectButtonsEnabled(true); }
    }

    private async void BtnConnect_Click(object? sender, RoutedEventArgs e)
    {
        SetConnectButtonsEnabled(false);
        ConnMessage.Text = "Conectando...";
        try { await ShowConnectResult(await _device.ConnectAsync(IpInput.Text ?? "", default)); }
        catch (Exception ex) { ConnMessage.Text = $"Erro: {ex.Message}"; }
        finally { SetConnectButtonsEnabled(true); }
    }

    private void SetConnectButtonsEnabled(bool enabled)
    {
        BtnScan.IsEnabled = enabled;
        BtnConnect.IsEnabled = enabled;
    }

    private async Task ShowConnectResult(Result<ThorDevice> result)
    {
        if (!result.IsSuccess)
        {
            ConnMessage.Text = result.Error!.Message;
            return;
        }

        var device = result.Value!;
        if (device.Transport == DeviceTransport.Wifi)
            IpInput.Text = device.Serial;

        ConnMessage.Text = "Conectado!";
        await CheckCompanion();

        _currentPath = "/storage/";
        await LoadDirectory();
    }

    /// <summary>Keeps the header in sync with the real connection, including drops.</summary>
    private void OnDeviceChanged(ThorDevice? device)
    {
        if (device is null)
        {
            StatusDot.Fill = DangerBrush;
            StatusText.Text = "Desconectado";
            BtnInstallCompanion.IsVisible = false;
            StopNetStats();
            return;
        }

        var via = device.Transport == DeviceTransport.Usb ? "USB" : "Wi-Fi";
        StatusDot.Fill = SuccessBrush;
        StatusText.Text = $"Conectado via {via} ({device.Model})";
        StartNetStats();
    }

    private async Task CheckCompanion()
    {
        var check = await _device.RunAsync($"shell pm list packages {CompanionPackage}", TimeSpan.FromSeconds(5), default);
        var installed = check.IsSuccess && check.Value!.StandardOutput.Contains(CompanionPackage);

        BtnInstallCompanion.IsVisible = !installed;
        if (!installed)
            ConnMessage.Text = "Conectado. O Ayn Thor Link não está instalado no Thor.";
    }

    private bool _installing;
    private async void BtnInstallCompanion_Click(object? sender, RoutedEventArgs e)
    {
        if (_installing) return;

        var apkPath = FindApkPath();
        if (apkPath is null)
        {
            ConnMessage.Text = "APK do Ayn Thor Link não encontrado no PC.";
            return;
        }

        _installing = true;
        BtnInstallCompanion.Content = "⏳ Instalando...";

        try
        {
            var install = await _device.RunAsync($"install -r \"{System.IO.Path.GetFullPath(apkPath)}\"", TimeSpan.FromSeconds(60), default);
            if (install.IsSuccess && install.Value!.StandardOutput.Contains("Success"))
            {
                await _device.RunAsync($"shell pm grant {CompanionPackage} android.permission.WRITE_SECURE_SETTINGS", TimeSpan.FromSeconds(5), default);
                await _device.RunAsync($"shell am start -n {CompanionPackage}/.MainActivity", TimeSpan.FromSeconds(5), default);

                BtnInstallCompanion.IsVisible = false;
                ConnMessage.Text = "Ayn Thor Link instalado!";
            }
            else
            {
                ConnMessage.Text = install.IsSuccess
                    ? $"Falha ao instalar: {(install.Value!.StandardError + " " + install.Value.StandardOutput).Trim()}"
                    : install.Error!.Message;
            }
        }
        finally
        {
            BtnInstallCompanion.Content = "📲 Instalar Ayn Thor Link";
            _installing = false;
        }
    }

    private static string? FindApkPath()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "ayn-thor-link.apk"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "ayn-thor-link.apk"),
            System.IO.Path.Combine("mobile", "ayn-thor-link", "release", "ayn-thor-link.apk"),
            System.IO.Path.Combine("mobile", "ayn-thor-link", "app", "build", "outputs", "apk", "debug", "app-debug.apk"),
            System.IO.Path.Combine("mobile", "ayn-thor-link", "app", "build", "outputs", "apk", "release", "app-release.apk"),
        };

        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
