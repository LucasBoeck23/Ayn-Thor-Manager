using System.Windows;
using System.Windows.Media;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        BtnScan.IsEnabled = false;
        ConnMessage.Text = "Buscando...";

        try
        {
            var result = await _executor.ExecuteAsync("devices", TimeSpan.FromSeconds(3), default);
            if (result.Success)
            {
                foreach (var line in result.StandardOutput.Split('\n'))
                {
                    if (line.StartsWith("List") || !line.Contains('\t')) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0].Contains(':'))
                    {
                        var addr = parts[0].Trim();
                        IpInput.Text = addr;
                        await ConnectToDevice(addr);
                        return;
                    }
                }
            }

            var mdns = await _executor.ExecuteAsync("mdns services", TimeSpan.FromSeconds(3), default);
            if (mdns.Success && !string.IsNullOrWhiteSpace(mdns.StandardOutput))
            {
                foreach (var line in mdns.StandardOutput.Split('\n'))
                {
                    if (line.StartsWith("List")) continue;
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var endpoint = parts[^1].Trim();
                        if (endpoint.Contains(':'))
                        {
                            IpInput.Text = endpoint;
                            await ConnectToDevice(endpoint);
                            return;
                        }
                    }
                }
            }

            ConnMessage.Text = "Nenhum device encontrado.\nAtive 'Depuracao sem fio' no Thor e tente novamente.";
        }
        catch (Exception ex) { ConnMessage.Text = $"Erro: {ex.Message}"; }
        finally { BtnScan.IsEnabled = true; }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpInput.Text.Trim();
        if (string.IsNullOrEmpty(ip)) { ConnMessage.Text = "Digite o IP"; return; }

        if (ip.EndsWith(":7100"))
        {
            ConnMessage.Text = "Porta 7100 e do companion, nao do ADB.\nUse 'Buscar' para encontrar a porta correta.";
            return;
        }

        await ConnectToDevice(ip);
    }

    private async Task ConnectToDevice(string address)
    {
        ConnMessage.Text = "Conectando...";
        var result = await _connectionService.ConnectAsync(address, default);

        if (result.IsSuccess && result.Value!.Status == DeviceStatusType.Connected)
        {
            var target = address.Contains(':') ? address : $"{address}:5555";
            var verify = await _executor.ExecuteAsync($"-s {target} shell echo ok", TimeSpan.FromSeconds(3), default);

            if (!verify.Success || !verify.StandardOutput.Contains("ok"))
            {
                await _executor.ExecuteAsync($"disconnect {target}", TimeSpan.FromSeconds(3), default);
                StatusDot.Fill = (Brush)FindResource("Danger");
                StatusText.Text = "Desconectado";
                ConnMessage.Text = "Device offline. Ative 'Depuracao sem fio' no Thor e tente novamente.";
                BtnInstallCompanion.Visibility = Visibility.Collapsed;
                return;
            }

            StatusDot.Fill = (Brush)FindResource("Success");
            StatusText.Text = $"Conectado ({address})";
            ConnMessage.Text = "Conectado!";

            StartNetStats();
            await InstallCompanionIfNeeded();
            await LoadDirectory();
        }
        else
        {
            ConnMessage.Text = result.Error?.Message ?? result.Value?.Message ?? "Falha ao conectar";
        }
    }

    private async Task InstallCompanionIfNeeded()
    {
        var target = GetAdbTargetPrefix();

        var check = await _executor.ExecuteAsync($"{target}shell pm list packages com.aynthor.link", TimeSpan.FromSeconds(5), default);
        if (check.Success && check.StandardOutput.Contains("com.aynthor.link"))
        {
            BtnInstallCompanion.Visibility = Visibility.Collapsed;
            return;
        }

        BtnInstallCompanion.Visibility = Visibility.Visible;
        ConnMessage.Text = "Companion nao encontrado no Thor.";
    }

    private bool _installing;
    private async void BtnInstallCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        _installing = true;

        var apkPath = FindApkPath();
        if (apkPath is null)
        {
            ConnMessage.Text = "APK nao encontrado no PC.";
            _installing = false;
            return;
        }

        BtnInstallCompanion.Content = "⏳ Instalando...";
        var target = GetAdbTargetPrefix();

        var install = await _executor.ExecuteAsync($"{target}install -r \"{System.IO.Path.GetFullPath(apkPath)}\"", TimeSpan.FromSeconds(60), default);
        if (install.Success && install.StandardOutput.Contains("Success"))
        {
            await _executor.ExecuteAsync($"{target}shell pm grant com.aynthor.link android.permission.WRITE_SECURE_SETTINGS", TimeSpan.FromSeconds(5), default);
            await _executor.ExecuteAsync($"{target}shell am start -n com.aynthor.link/.MainActivity", TimeSpan.FromSeconds(5), default);

            BtnInstallCompanion.Visibility = Visibility.Collapsed;
            ConnMessage.Text = "Ayn Thor Link instalado!\nPode desativar 'Depuracao sem fio' no Thor.";
        }
        else
        {
            ConnMessage.Text = $"Falha: {(install.StandardError + " " + install.StandardOutput).Trim()}";
            BtnInstallCompanion.Content = "📲 Instalar Ayn Thor Link";
        }

        _installing = false;
    }

    private static string? FindApkPath()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "ayn-thor-link.apk"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "ayn-thor-link.apk"),
            @"mobile\ayn-thor-link\release\ayn-thor-link.apk",
            @"mobile\ayn-thor-link\app\build\outputs\apk\debug\app-debug.apk",
            @"mobile\ayn-thor-link\app\build\outputs\apk\release\app-release.apk",
        };

        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
