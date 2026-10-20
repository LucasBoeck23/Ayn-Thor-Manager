using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<PrereqItem> _mountPrereqItems = [];

    private async void BtnOpenThorFiles_Click(object? sender, RoutedEventArgs e)
    {
        if (_device is null || !_device.IsConnected)
        {
            await ShowMountPrerequisitesAsync();
            return;
        }

        BtnOpenThorFiles.IsEnabled = false;
        MountStatusText.Text = "Montando...";
        MountStatusText.Foreground = SecondaryBrush;

        var result = await _thorMount.OpenAsync();

        BtnOpenThorFiles.IsEnabled = _device.IsConnected;

        if (result.IsSuccess)
        {
            SetMountPrereqPanelVisible(false);
            return;
        }

        if (result.Error!.Code == "MOUNT_PREREQUISITES_MISSING")
        {
            await ShowMountPrerequisitesAsync();
            MountStatusText.Text = "Pré-requisitos pendentes";
            MountStatusText.Foreground = DangerBrush;
            return;
        }

        MountStatusText.Text = result.Error!.Message;
        MountStatusText.Foreground = DangerBrush;
    }

    private async Task ShowMountPrerequisitesAsync()
    {
        if (!ReferenceEquals(MountPrereqList.ItemsSource, _mountPrereqItems))
            MountPrereqList.ItemsSource = _mountPrereqItems;

        var prereqs = await _thorMount.CheckPrerequisitesAsync();
        var missing = prereqs.Where(p => !p.Ok).ToList();

        _mountPrereqItems.Clear();
        foreach (var p in missing)
            _mountPrereqItems.Add(new PrereqItem { Name = p.Name, Guidance = p.Guidance, Url = p.Url });

        SetMountPrereqPanelVisible(missing.Count > 0);
    }

    private void SetMountPrereqPanelVisible(bool visible)
    {
        MountPrereqPanel.IsVisible = visible;
        if (visible)
            ScrcpyHint.IsVisible = false;
        else if (!PrereqPanel.IsVisible)
            ScrcpyHint.IsVisible = true;
    }

    private void OnMountStateChanged(MountStatus status)
    {
        switch (status.State)
        {
            case MountState.Mounting:
                BtnOpenThorFiles.IsEnabled = false;
                MountStatusText.Text = "Montando...";
                MountStatusText.Foreground = SecondaryBrush;
                break;

            case MountState.Mounted:
                BtnOpenThorFiles.IsEnabled = _device is not null && _device.IsConnected;
                MountStatusText.Text = "Montado (aberto no gerenciador)";
                MountStatusText.Foreground = SuccessBrush;
                SetMountPrereqPanelVisible(false);
                break;

            case MountState.Error:
                MountStatusText.Text = status.Reason ?? "Erro ao montar.";
                MountStatusText.Foreground = DangerBrush;
                UpdateMountAvailability();
                break;

            case MountState.NotMounted:
            default:
                MountStatusText.Text = "";
                MountStatusText.Foreground = SecondaryBrush;
                UpdateMountAvailability();
                break;
        }
    }

    private void UpdateMountAvailability()
    {
        var connected = _device is not null && _device.IsConnected;
        BtnOpenThorFiles.IsEnabled = connected;
        BtnOpenThorFiles.SetValue(ToolTip.TipProperty,
            connected ? null : "Conecte o Thor para abrir seus arquivos.");

        if (!connected)
        {
            SetMountPrereqPanelVisible(false);
            if (_thorMount is null || _thorMount.Status.State != MountState.Error)
            {
                MountStatusText.Text = "";
                MountStatusText.Foreground = SecondaryBrush;
            }
        }
    }
}
