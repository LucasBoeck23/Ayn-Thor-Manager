using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

/// <summary>
/// Presentation row for a single pending prerequisite in the PC→Thor guided flow. Maps
/// from <see cref="Prerequisite"/> and only the not-ok items are shown: <see cref="Name"/>
/// identifies the missing piece, <see cref="Guidance"/> explains how to resolve it, and
/// <see cref="Url"/> (when set, <see cref="HasUrl"/> is true) is an official link the user
/// can open — the app only guides, it never installs anything.
/// </summary>
public sealed class PrereqItem
{
    public string Name { get; init; } = "";
    public string? Guidance { get; init; }
    public string? Url { get; init; }
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>
/// The direction of the mirror. <see cref="ThorToPc"/> is the default (scrcpy mirrors
/// the Thor on the PC, the existing flow); <see cref="PcToThor"/> is the reverse flow
/// (Sunshine on the PC + Moonlight on the Thor).
/// </summary>
public enum StreamDirection
{
    ThorToPc,
    PcToThor,
}

public partial class MainWindow
{
    // Direction toggle (Req 1.1). The two ToggleButtons are mutually exclusive in the
    // UI: picking one unchecks the other, and SelectDirection always leaves exactly one
    // checked. Changing direction while something is active stops the current direction
    // first (Req 1.6 / 7). Starting either direction also ends the other at the UI level
    // (see StartForwardAsync/StartReverseAsync), so the two sentidos never run at once.

    private void ToggleThorToPc_Click(object? sender, RoutedEventArgs e) =>
        SelectDirection(StreamDirection.ThorToPc);

    private void TogglePcToThor_Click(object? sender, RoutedEventArgs e) =>
        SelectDirection(StreamDirection.PcToThor);

    /// <summary>
    /// Switches the active direction and keeps the two toggles mutually exclusive. If a
    /// stream is running when the direction changes, it is stopped first (Req 1.6 / 7).
    /// </summary>
    private void SelectDirection(StreamDirection direction)
    {
        // Keep the toggle pair exclusive regardless of which one was clicked (a
        // ToggleButton would otherwise allow unchecking itself or checking both).
        ToggleThorToPc.IsChecked = direction == StreamDirection.ThorToPc;
        TogglePcToThor.IsChecked = direction == StreamDirection.PcToThor;

        // Clicking the already-active toggle is a no-op: the lines above restore its
        // checked state so it never ends up unchecked, and we don't tear down a running
        // stream unnecessarily. Only an actual direction change proceeds past here.
        if (direction == _direction)
            return;

        _direction = direction;

        // Req 1.6 / 7: changing direction ends any stream that is currently running and
        // resets the UI to the idle state for the newly selected direction. StopStreaming
        // clears the status, hides the Stop button, and re-labels the action button.
        if (BtnStopStream.IsVisible)
        {
            StopStreaming();
            return;
        }

        // No stream was running: still drop any stale status (e.g. a leftover error from
        // a failed attempt in the previous direction) so the new direction starts clean.
        StreamStatus.Text = "Inativo";
        StreamStatus.Foreground = SecondaryBrush;
        UpdateActionAvailability();

        if (direction == StreamDirection.PcToThor)
        {
            // Nice-to-have: surface what's pending as soon as the user picks PC→Thor, so
            // the guided list is visible without having to click the action first. Fire
            // and forget — it must not block the toggle (Req 2.4/6.2).
            _ = RefreshPrerequisitesAsync();
        }
        else
        {
            // Back to the default direction: the prereq list and the best-effort advisory
            // banner are PC→Thor-only. Hide both and restore the scrcpy hint in the center.
            SetPrereqPanelVisible(false);
            WarningBanner.IsVisible = false;
            WarningText.Text = "";
        }
    }

    /// <summary>
    /// Reverse-direction action (Req 1.2/1.3): ensures Sunshine is up and opens Moonlight
    /// on the Thor. Shows the "medindo" state while the attempt is in flight, then active
    /// on success or the error message on failure (Req 1.4/9.2).
    /// </summary>
    private async Task StartReverseAsync()
    {
        if (_reverseStreaming.IsRunning) return;

        // Req 1.5: unavailable without a connected Thor.
        if (!_device.IsConnected)
        {
            StreamStatus.Text = "Conecte o Thor para usar o PC no Thor.";
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        // Req 7.2: starting one direction ends the other. The reverse orchestrator also
        // stops scrcpy before it starts, but stop the forward through its own service
        // here too so the UI state stays consistent regardless of ordering.
        if (_streaming.IsRunning)
            _streaming.Stop();

        BtnStream.IsEnabled = false;
        StreamStatus.Text = "Verificando pré-requisitos...";
        StreamStatus.Foreground = SecondaryBrush;

        // Req 2.5 / 1.4: re-check prerequisites on every attempt and gate ONLY on the
        // blocking ones. RefreshPrerequisitesAsync renders the guided checklist for the
        // blocking items (each with guidance and an official link when present —
        // Req 2.3/2.4/6.2) and the advisory banner for best-effort warnings (Req 5.2/6.2),
        // returning the count of blocking-missing items. The app only orients; it never
        // installs. StartAsync re-checks too (and also blocks only on blocking items),
        // but gating here lets us show the per-item guidance instead of a flat error.
        var blockingMissing = await RefreshPrerequisitesAsync();
        if (blockingMissing > 0)
        {
            BtnStream.IsEnabled = true;
            StreamStatus.Text = "Pré-requisitos pendentes";
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        // No blocking prerequisite missing: hide the guided checklist and proceed. A
        // best-effort warning (e.g. likely software encode) may still be showing in the
        // advisory banner — we keep it visible so the user understands the performance
        // caveat while the stream runs (Req 5.2). The PC IP (hotspot gateway) is
        // guidance-only for the official Moonlight app (can't auto-connect via intent);
        // omit it here until the gateway is surfaced.
        SetPrereqPanelVisible(false);

        var result = await _reverseStreaming.StartAsync();
        BtnStream.IsEnabled = true;

        if (!result.IsSuccess)
        {
            // Req 1.4 / 9.2: surface the specific error and leave the action available so
            // the user can resolve and retry. A late prerequisite failure (something
            // changed between the gate and StartAsync) re-renders the guided list;
            // anything else gets a generic error.
            if (result.Error!.Code == "REVERSE_PREREQUISITES_MISSING")
            {
                await RefreshPrerequisitesAsync();
                StreamStatus.Text = "Pré-requisitos pendentes";
            }
            else
            {
                ConnMessage.Text = result.Error!.Message;
                StreamStatus.Text = "Erro";
            }
            StreamStatus.Foreground = DangerBrush;
            return;
        }

        BtnStream.IsVisible = false;
        BtnStopStream.IsVisible = true;
        BtnSunshineWebUi.IsVisible = true;
        StreamStatus.Text = "Ativo (PC no Thor via Moonlight)";
        StreamStatus.Foreground = SuccessBrush;
    }

    /// <summary>
    /// Collection backing the guided prerequisite list (<c>PrereqList</c>). Holds only the
    /// not-ok items for the PC→Thor direction.
    /// </summary>
    private readonly ObservableCollection<PrereqItem> _prereqItems = [];

    /// <summary>
    /// Re-checks prerequisites (fresh every call, Req 2.5) and splits the not-ok items
    /// into two buckets by <see cref="Prerequisite.IsBlocking"/>:
    /// <list type="bullet">
    ///   <item><description><b>Blocking</b> (Sunshine/Moonlight installed, Thor connected):
    ///   these go into the guided checklist (<c>PrereqList</c>) and must be resolved to
    ///   start, so only these count toward the gate the caller receives.</description></item>
    ///   <item><description><b>Best-effort</b> (hardware encoder, Wayland capture, Sunshine
    ///   already running): these NEVER block (Req 5/6). A not-ok encoder shows the
    ///   software-encode performance warning in the advisory banner (Req 5.2); the stream
    ///   is still allowed to start.</description></item>
    /// </list>
    /// Shows the guided panel only when a blocking item is missing and the direction is
    /// PC→Thor. Returns the number of <b>blocking</b> missing prerequisites so callers
    /// gate only on those.
    /// </summary>
    private async Task<int> RefreshPrerequisitesAsync()
    {
        // Bind the list once; cheap to re-assign and keeps wiring self-contained here.
        if (!ReferenceEquals(PrereqList.ItemsSource, _prereqItems))
            PrereqList.ItemsSource = _prereqItems;

        var prereqs = await _reverseStreaming.CheckPrerequisitesAsync(default);
        var notOk = prereqs.Where(p => !p.Ok).ToList();

        // Blocking → guided checklist; best-effort → advisory banner. Keeping them apart
        // means the user is never told to "resolve" a best-effort warning as if it blocks.
        var blocking = notOk.Where(p => p.IsBlocking).ToList();
        var warnings = notOk.Where(p => !p.IsBlocking).ToList();

        _prereqItems.Clear();
        foreach (var p in blocking)
            _prereqItems.Add(new PrereqItem { Name = p.Name, Guidance = p.Guidance, Url = p.Url });

        // The guided list is PC→Thor-only; never show it in the default direction even if
        // a stale refresh completes after the user switched back.
        var show = blocking.Count > 0 && _direction == StreamDirection.PcToThor;
        SetPrereqPanelVisible(show);

        ShowWarnings(warnings);

        return blocking.Count;
    }

    /// <summary>
    /// Renders the best-effort advisory banner (Req 5.2 / 6.2). When any best-effort
    /// prerequisite is not-ok — most importantly the hardware encoder, meaning Sunshine
    /// may fall back to software encode — the amber banner shows the guidance so the user
    /// understands the performance caveat. The banner never blocks the stream and is only
    /// shown in the PC→Thor direction. Clears when there is nothing to warn about.
    /// </summary>
    private void ShowWarnings(IReadOnlyList<Prerequisite> warnings)
    {
        if (warnings.Count == 0 || _direction != StreamDirection.PcToThor)
        {
            WarningBanner.IsVisible = false;
            WarningText.Text = "";
            return;
        }

        // Prefer each item's own guidance text; fall back to its name when none is set.
        var text = string.Join("\n\n",
            warnings.Select(p => string.IsNullOrWhiteSpace(p.Guidance) ? p.Name : p.Guidance));

        WarningText.Text = text;
        WarningBanner.IsVisible = true;
    }

    /// <summary>
    /// Toggles the guided prerequisite panel and the scrcpy hint so exactly one owns the
    /// center content area at a time. The hint (Thor→PC) is the default; the panel
    /// (PC→Thor) takes over only while prerequisites are pending.
    /// </summary>
    private void SetPrereqPanelVisible(bool visible)
    {
        PrereqPanel.IsVisible = visible;
        ScrcpyHint.IsVisible = !visible;
    }

    /// <summary>
    /// Opens a prerequisite's official link in the OS default browser (Req 2.3/2.4/6.2).
    /// Uses Avalonia's <see cref="TopLevel.Launcher"/> when available and falls back to a
    /// shell-executed process, so it works across platforms. The app only guides to the
    /// download/Play Store page — it never installs anything.
    /// </summary>
    /// <summary>Sunshine Web UI address, shown as a link while the reverse stream runs.</summary>
    private const string SunshineWebUiUrl = "https://localhost:47990";

    private void PrereqLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url })
            _ = OpenUrlAsync(url);
    }

    /// <summary>
    /// Opens the Sunshine Web UI (parear / PIN / configurar). Shown only while the
    /// PC→Thor stream is active, so the user can reach the panel without hunting for it.
    /// </summary>
    private void BtnSunshineWebUi_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenUrlAsync(SunshineWebUiUrl);

    /// <summary>
    /// Opens a URL in the OS default browser. Prefers Avalonia's launcher and falls back
    /// to a shell-executed process. Best-effort: a launch failure is non-fatal.
    /// </summary>
    private async Task OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(uri);
                return;
            }
        }
        catch
        {
            // Fall through to the process-based launch below.
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Best-effort: opening the browser is a convenience, so a failure is non-fatal.
        }
    }

    /// <summary>
    /// Reflects backend reverse-stream status changes on the UI (Req 3.3/9.1). Already
    /// marshaled onto the Dispatcher by the caller.
    /// </summary>
    private void OnReverseStateChanged(ReverseStreamStatus status)
    {
        switch (status.State)
        {
            case ReverseStreamState.Active:
                BtnStream.IsVisible = false;
                BtnStopStream.IsVisible = true;
                BtnSunshineWebUi.IsVisible = true;
                StreamStatus.Text = "Ativo (PC no Thor via Moonlight)";
                StreamStatus.Foreground = SuccessBrush;
                break;

            case ReverseStreamState.Error:
                BtnStopStream.IsVisible = false;
                BtnSunshineWebUi.IsVisible = false;
                StreamStatus.Text = status.Reason ?? "Erro no streaming reverso.";
                StreamStatus.Foreground = DangerBrush;
                UpdateActionAvailability();
                break;

            case ReverseStreamState.Inactive:
            case ReverseStreamState.Unavailable:
            case ReverseStreamState.Checking:
            default:
                BtnStopStream.IsVisible = false;
                BtnSunshineWebUi.IsVisible = false;
                StreamStatus.Text = status.Reason ?? "Inativo";
                StreamStatus.Foreground = SecondaryBrush;
                UpdateActionAvailability();
                break;
        }
    }

    /// <summary>
    /// Enables/disables the action button and labels it for the current direction. For
    /// PC→Thor the action is unavailable without a connected Thor (Req 1.5); Thor→PC
    /// keeps its existing always-available behavior.
    /// </summary>
    private void UpdateActionAvailability()
    {
        // Only restore the start button when no stream is active.
        if (!BtnStopStream.IsVisible)
            BtnStream.IsVisible = true;

        if (_direction == StreamDirection.PcToThor)
        {
            BtnStream.Content = "Usar PC no Thor";
            var connected = _device is not null && _device.IsConnected;
            BtnStream.IsEnabled = connected;
            BtnStream.SetValue(ToolTip.TipProperty,
                connected ? null : "Conecte o Thor para usar o PC no Thor.");
        }
        else
        {
            BtnStream.Content = "Espelhar Tela";
            BtnStream.IsEnabled = true;
            BtnStream.SetValue(ToolTip.TipProperty, null);
        }
    }
}
