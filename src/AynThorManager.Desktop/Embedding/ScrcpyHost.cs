using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AynThorManager.Desktop.Embedding;

/// <summary>
/// A native host area into which the scrcpy window is reparented. Avalonia
/// manages its position, size, and clipping within the layout, so the embedded
/// mirror behaves like any other control in the window.
/// </summary>
public sealed class ScrcpyHost : NativeControlHost
{
    private IPlatformHandle? _handle;

    public nint HostHandle => _handle?.Handle ?? nint.Zero;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _handle = base.CreateNativeControlCore(parent);
        return _handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _handle = null;
        base.DestroyNativeControlCore(control);
    }
}
