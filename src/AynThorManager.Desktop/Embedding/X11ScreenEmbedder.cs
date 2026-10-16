using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AynThorManager.Desktop.Embedding;

/// <summary>
/// Embeds the scrcpy window on Linux by reparenting its X11 window into the
/// host via XReparentWindow. Works on native X11 sessions and on Wayland
/// through XWayland (both scrcpy and this app share the same X display).
/// </summary>
public sealed class X11ScreenEmbedder : IScreenEmbedder
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)] private static extern nint XOpenDisplay(string? display);
    [DllImport(LibX11)] private static extern int XReparentWindow(nint display, nint window, nint parent, int x, int y);
    [DllImport(LibX11)] private static extern int XMoveResizeWindow(nint display, nint window, int x, int y, uint width, uint height);
    [DllImport(LibX11)] private static extern int XMapWindow(nint display, nint window);
    [DllImport(LibX11)] private static extern int XFlush(nint display);
    [DllImport(LibX11)] private static extern int XSync(nint display, bool discard);
    [DllImport(LibX11)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(LibX11)] private static extern uint XQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint nchildren);
    [DllImport(LibX11)] private static extern int XFree(nint data);
    [DllImport(LibX11)] private static extern nint XGetWindowProperty(
        nint display, nint window, nint property, long offset, long length, bool delete,
        nint reqType, out nint actualType, out int actualFormat, out nint nitems, out nint bytesAfter, out nint prop);
    [DllImport(LibX11)] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);

    private nint _display;

    private nint Display
    {
        get
        {
            if (_display == nint.Zero)
            {
                _display = XOpenDisplay(null);
                if (_display == nint.Zero)
                    throw new InvalidOperationException(
                        "Nao foi possivel abrir o display X11. Em sessoes Wayland, o XWayland precisa estar ativo (DISPLAY definido).");
            }
            return _display;
        }
    }

    public async Task<nint> WaitForWindowAsync(int processId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var netWmPid = XInternAtom(Display, "_NET_WM_PID", false);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var window = FindWindowByPid(Display, XDefaultRootWindow(Display), netWmPid, (uint)processId);
            if (window != nint.Zero)
                return window;

            await Task.Delay(100, ct);
        }

        return nint.Zero;
    }

    private static nint FindWindowByPid(nint display, nint root, nint netWmPid, uint pid)
    {
        if (XQueryTree(display, root, out _, out _, out var children, out var nchildren) == 0 || children == nint.Zero)
            return nint.Zero;

        try
        {
            var size = nint.Size;
            for (var i = 0; i < nchildren; i++)
            {
                var child = Marshal.ReadIntPtr(children, i * size);

                if (GetWindowPid(display, child, netWmPid) == pid)
                    return child;

                var nested = FindWindowByPid(display, child, netWmPid, pid);
                if (nested != nint.Zero)
                    return nested;
            }
        }
        finally
        {
            XFree(children);
        }

        return nint.Zero;
    }

    private static uint GetWindowPid(nint display, nint window, nint netWmPid)
    {
        // XA_CARDINAL == 6
        var status = XGetWindowProperty(display, window, netWmPid, 0, 1, false, 6,
            out var actualType, out _, out var nitems, out _, out var prop);

        if (status != 0 || actualType == nint.Zero || nitems == nint.Zero || prop == nint.Zero)
        {
            if (prop != nint.Zero) XFree(prop);
            return 0;
        }

        var pid = (uint)Marshal.ReadInt32(prop);
        XFree(prop);
        return pid;
    }

    public void Embed(nint hostHandle, nint childWindow)
    {
        XReparentWindow(Display, childWindow, hostHandle, 0, 0);
        XMapWindow(Display, childWindow);
        XSync(Display, false);
        XFlush(Display);
    }

    public void Resize(nint childWindow, int width, int height)
    {
        if (childWindow == nint.Zero || width <= 0 || height <= 0) return;
        XMoveResizeWindow(Display, childWindow, 0, 0, (uint)width, (uint)height);
        XFlush(Display);
    }
}
