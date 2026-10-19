using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

/// <summary>
/// Desktop wiring for the "Abrir arquivos do Thor" flow (Req 1/2): the button mounts the
/// Thor via the native FUSE→ADB mount and opens the native file manager at the mount point,
/// reflecting the four mount states (indisponível sem Thor, montando, montado, erro). When
/// FUSE is missing it surfaces the guided prerequisite list instead of mounting — the app
/// only orients, it never installs. Mirrors the reverse-streaming UI conventions: a code-behind
/// handler, the shared <see cref="PrereqItem"/> row, <see cref="OpenUrlAsync"/> for links,
/// and a Dispatcher-marshaled state handler.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Collection backing the mount prerequisite list (<c>MountPrereqList</c>). Holds only
    /// the not-ok items (FUSE/Thor conectado) when the mount is blocked.
    /// </summary>
    private readonly ObservableCollection<PrereqItem> _mountPrereqItems = [];

    /// <summary>
    /// Req 1.1: mounts the Thor (if not already mounted) and opens the native file manager
    /// at the mount point; if a mount is already live, just reopens it (Req 1.3). The
    /// service re-checks prerequisites and gates on them, so a missing FUSE/Thor
    /// returns a prerequisites-missing failure — in that case we render the guided list
    /// (Req 2.2) rather than a flat error. Shows the "montando" state while the attempt is
    /// in flight; the final mounted/error state comes from the service via
    /// <see cref="OnMountStateChanged"/>.
    /// </summary>
    private async void BtnOpenThorFiles_Click(object? sender, RoutedEventArgs e)
    {
        // Req 2.3: unavailable without a connected Thor. The button is already disabled in
        // that case, but guard here too since state can change between render and click.
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
            // Success: the service drove the status to Mounted via StateChanged, which
            // OnMountStateChanged already reflected. Hide any stale guided list.
            SetMountPrereqPanelVisible(false);
            return;
        }

        // Req 2.2: a prerequisites-missing failure means FUSE (or the Thor) is not
        // ready — surface the guided checklist with each missing item's guidance and link
        // instead of a flat error. Any other failure is shown inline next to the button.
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

    /// <summary>
    /// Req 2.2: re-checks the mount prerequisites (fresh every call) and renders the not-ok
    /// items — FUSE missing (with how-to-obtain guidance and an official link) and
    /// "Thor conectado?" — in the guided panel. Shows the panel only when something is
    /// missing. The app only orients; it never installs.
    /// </summary>
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

    /// <summary>
    /// Shows/hides the mount guided panel. Keeps it mutually exclusive with the scrcpy hint
    /// (the default center content) so exactly one owns the center at a time; the PC→Thor
    /// prereq panel manages its own exclusivity with the hint separately.
    /// </summary>
    private void SetMountPrereqPanelVisible(bool visible)
    {
        MountPrereqPanel.IsVisible = visible;
        if (visible)
            ScrcpyHint.IsVisible = false;
        else if (!PrereqPanel.IsVisible)
            ScrcpyHint.IsVisible = true;
    }

    /// <summary>
    /// Reflects the backend mount status on the button and its label (Req 1): the four
    /// states are unavailable (no Thor), mounting, mounted, and error. Already marshaled
    /// onto the Dispatcher by the caller.
    /// </summary>
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

    /// <summary>
    /// Req 2.3: the "Abrir arquivos do Thor" action is available only with a connected
    /// Thor. Enables/disables the button and sets a tooltip explaining why it is
    /// unavailable. Called on device state changes and after a mount settles.
    /// </summary>
    private void UpdateMountAvailability()
    {
        var connected = _device is not null && _device.IsConnected;
        BtnOpenThorFiles.IsEnabled = connected;
        BtnOpenThorFiles.SetValue(ToolTip.TipProperty,
            connected ? null : "Conecte o Thor para abrir seus arquivos.");

        // When the Thor drops, the guided list / "mounting" label no longer apply; clear
        // the status unless a mount error is being shown so the user still sees it.
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
