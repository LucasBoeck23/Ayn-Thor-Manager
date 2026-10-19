using Avalonia.Media;
using AynThorManager.Core.Models;
using AynThorManager.Services.Connection;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private static readonly IBrush SuccessBrush = SolidColorBrush.Parse("#00e676");
    private static readonly IBrush DangerBrush = SolidColorBrush.Parse("#ff5252");
    private static readonly IBrush AccentBrush = SolidColorBrush.Parse("#00d4ff");
    private static readonly IBrush SecondaryBrush = SolidColorBrush.Parse("#78909c");

    private void ShowConnectResult(Result<ThorDevice> result)
    {
        if (!result.IsSuccess)
        {
            ConnMessage.Text = result.Error!.Message;
            return;
        }

        ConnMessage.Text = "Conectado!";
    }

    private void OnDeviceChanged(ThorDevice? device)
    {
        if (device is null)
        {
            StatusDot.Fill = DangerBrush;
            StatusText.Text = "Desconectado";
            StopNetStats();
            // Req 1.5: the PC→Thor action becomes unavailable without a Thor connected.
            UpdateActionAvailability();
            // Req 2.3: the "Abrir arquivos do Thor" action is also unavailable now.
            UpdateMountAvailability();
            return;
        }

        StatusDot.Fill = SuccessBrush;
        StatusText.Text = $"Conectado ({device.Model})";
        StartNetStats();
        // Req 1.5: re-enable the PC→Thor action now that a Thor is connected.
        UpdateActionAvailability();
        // Req 2.3: re-enable the "Abrir arquivos do Thor" action now that a Thor is connected.
        UpdateMountAvailability();
    }
}
