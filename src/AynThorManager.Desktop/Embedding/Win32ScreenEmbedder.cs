using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AynThorManager.Desktop.Embedding;

/// <summary>
/// Embeds the scrcpy window on Windows by reparenting its HWND into the host
/// via SetParent, matching the behavior of the X11 embedder.
/// </summary>
public sealed class Win32ScreenEmbedder : IScreenEmbedder
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    public async Task<nint> WaitForWindowAsync(int processId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Refresh();
                if (proc.MainWindowHandle != nint.Zero)
                    return proc.MainWindowHandle;
            }
            catch (ArgumentException)
            {
                return nint.Zero; // process gone
            }

            await Task.Delay(100, ct);
        }

        return nint.Zero;
    }

    public void Embed(nint hostHandle, nint childWindow)
    {
        var style = GetWindowLong(childWindow, GWL_STYLE);
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME);
        style |= WS_CHILD;
        SetWindowLong(childWindow, GWL_STYLE, style);
        SetParent(childWindow, hostHandle);
    }

    public void Resize(nint childWindow, int width, int height)
    {
        if (childWindow == nint.Zero || width <= 0 || height <= 0) return;
        MoveWindow(childWindow, 0, 0, width, height, true);
    }
}
