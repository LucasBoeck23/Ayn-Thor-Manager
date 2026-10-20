using System.Runtime.InteropServices;

namespace AynThorManager.Infrastructure.Mount;

internal static class LibFuseInterop
{
    private const string LibFuse = "libfuse3";

    private static readonly string[] LibFuseCandidates =
    [
        "libfuse3.so.4",
        "libfuse3.so.3",
        "libfuse3.so.2",
        "libfuse3.so",
    ];

    static LibFuseInterop()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibFuseInterop).Assembly, (name, assembly, searchPath) =>
        {
            if (name != LibFuse)
                return IntPtr.Zero;

            foreach (var candidate in LibFuseCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                    return handle;
            }

            return IntPtr.Zero;
        });
    }

    internal static readonly nuint OperationsSize = (nuint)Marshal.SizeOf<FuseOperations>();

    [DllImport(LibFuse, EntryPoint = "fuse_main_real")]
    private static extern int fuse_main_real(
        int argc,
        IntPtr argv,
        ref FuseOperations op,
        nuint opSize,
        IntPtr privateData);

    internal static int FuseMain(string[] argv, ref FuseOperations op, nuint opSize, IntPtr privateData)
    {
        ArgumentNullException.ThrowIfNull(argv);

        var argptrs = new IntPtr[argv.Length + 1];
        var vector = IntPtr.Zero;
        try
        {
            for (var i = 0; i < argv.Length; i++)
                argptrs[i] = Marshal.StringToCoTaskMemUTF8(argv[i]);
            argptrs[argv.Length] = IntPtr.Zero;

            vector = Marshal.AllocCoTaskMem(IntPtr.Size * argptrs.Length);
            Marshal.Copy(argptrs, 0, vector, argptrs.Length);

            return fuse_main_real(argv.Length, vector, ref op, opSize, privateData);
        }
        finally
        {
            if (vector != IntPtr.Zero)
                Marshal.FreeCoTaskMem(vector);
            foreach (var ptr in argptrs)
                if (ptr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptr);
        }
    }

    internal static int Guard(Func<int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        try
        {
            return callback();
        }
        catch (FuseException fuse)
        {
            return -Math.Abs(fuse.Errno);
        }
        catch (OperationCanceledException)
        {
            return -FuseErrno.EINTR;
        }
        catch
        {
            return -FuseErrno.EIO;
        }
    }
}

internal sealed class FuseException : Exception
{
    public int Errno { get; }

    public FuseException(int errno, string? message = null)
        : base(message ?? $"FUSE operation failed with errno {errno}.")
    {
        Errno = errno == 0 ? FuseErrno.EIO : Math.Abs(errno);
    }
}

internal static class FuseErrno
{
    public const int ENOENT = 2;
    public const int EINTR = 4;
    public const int EIO = 5;
    public const int EACCES = 13;
    public const int EEXIST = 17;
    public const int EINVAL = 22;
}

internal static class FuseMode
{
    public const uint S_IFREG = 0x8000;
    public const uint S_IFDIR = 0x4000;
    public const uint S_IFLNK = 0xA000;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FuseStat
{
    public ulong st_dev;
    public ulong st_ino;
    public ulong st_nlink;

    public uint st_mode;

    public uint st_uid;
    public uint st_gid;
    private readonly int _pad0;
    public ulong st_rdev;

    public long st_size;

    public long st_blksize;

    public long st_blocks;

    public long st_atime;
    public long st_atime_nsec;

    public long st_mtime;
    public long st_mtime_nsec;

    public long st_ctime;
    public long st_ctime_nsec;

    private readonly long _reserved0;
    private readonly long _reserved1;
    private readonly long _reserved2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FuseFileInfo
{
    public int flags;

    private uint _bitfield;

    public ulong fh;

    public ulong lock_owner;

    public uint poll_events;

    private readonly uint _pad0;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseFillDir(
    IntPtr buf,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    IntPtr stbuf,
    long offset,
    int flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseGetattr(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseStat stat,
    IntPtr fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseReaddir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    FuseFillDir filler,
    long offset,
    IntPtr fi,
    int flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseOpen(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRead(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    nuint size,
    long offset,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseWrite(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    nuint size,
    long offset,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseCreate(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint mode,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseTruncate(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    long size,
    IntPtr fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseFlush(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRelease(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseUnlink(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseMkdir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint mode);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRmdir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRename(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string to,
    uint flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseChmod(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint mode,
    IntPtr fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseChown(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint uid,
    uint gid,
    IntPtr fi);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseUtimens(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr tv,
    IntPtr fi);

[StructLayout(LayoutKind.Sequential)]
internal struct FuseOperations
{
    public FuseGetattr? getattr;
    public IntPtr readlink;
    public IntPtr mknod;
    public FuseMkdir? mkdir;
    public FuseUnlink? unlink;
    public FuseRmdir? rmdir;
    public IntPtr symlink;
    public FuseRename? rename;
    public IntPtr link;
    public FuseChmod? chmod;
    public FuseChown? chown;
    public FuseTruncate? truncate;
    public FuseOpen? open;
    public FuseRead? read;
    public FuseWrite? write;
    public IntPtr statfs;
    public FuseFlush? flush;
    public FuseRelease? release;
    public IntPtr fsync;
    public IntPtr setxattr;
    public IntPtr getxattr;
    public IntPtr listxattr;
    public IntPtr removexattr;
    public IntPtr opendir;
    public FuseReaddir? readdir;
    public IntPtr releasedir;
    public IntPtr fsyncdir;
    public IntPtr init;
    public IntPtr destroy;
    public IntPtr access;
    public FuseCreate? create;
    public IntPtr lock_;
    public FuseUtimens? utimens;
    public IntPtr bmap;
    public IntPtr ioctl;
    public IntPtr poll;
    public IntPtr write_buf;
    public IntPtr read_buf;
    public IntPtr flock;
    public IntPtr fallocate;
    public IntPtr copy_file_range;
    public IntPtr lseek;
}

internal sealed class FuseOperationsHandle : IDisposable
{
    private readonly object _operations;
    private GCHandle _handle;
    private int _disposed;

    public FuseOperationsHandle(FuseOperations operations)
    {
        _operations = operations;
        _handle = GCHandle.Alloc(_operations, GCHandleType.Normal);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_handle.IsAllocated)
            _handle.Free();

        GC.KeepAlive(_operations);
    }
}
