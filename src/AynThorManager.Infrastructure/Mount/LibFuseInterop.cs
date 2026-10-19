using System.Runtime.InteropServices;

namespace AynThorManager.Infrastructure.Mount;

/// <summary>
/// The lowest-level FUSE binding: a thin P/Invoke layer over the system <c>libfuse</c>
/// (libfuse3, the Debian <c>fuse3</c> package — the only system dependency of this spec).
/// It exposes the high-level libfuse API — the <c>fuse_operations</c> struct of
/// function-pointer callbacks plus <c>fuse_main</c> — so the translator built on top
/// (<c>ThorFuseFilesystem</c>, task 3.2) can speak file operations instead of raw kernel
/// protocol.
///
/// Two invariants this layer guarantees (design "Correctness Properties", Property 6):
/// <list type="number">
///   <item><description>
///     <b>Delegates stay alive.</b> The managed callbacks handed to native code are held
///     through <see cref="FuseOperationsHandle"/> (which pins a <see cref="GCHandle"/> on
///     the delegate set) for as long as the mount runs, so the garbage collector can never
///     reclaim a delegate that the kernel still holds a function pointer to.
///   </description></item>
///   <item><description>
///     <b>No exception reaches the kernel.</b> Every callback is invoked through
///     <see cref="Guard"/>, which catches everything and turns any failure into a negative
///     errno (<c>-errno</c>) return value. A managed exception propagating into native
///     libfuse would corrupt the FUSE session or crash the process and tear down the
///     mount; instead the failing operation reports an error and the mount stays alive.
///   </description></item>
/// </list>
///
/// This file defines only the plumbing — the native structs, the delegate signatures, the
/// errno codes, and the <c>fuse_main</c> import. It carries no filesystem logic of its own;
/// <c>ThorFuseFilesystem</c> fills in the callbacks and <c>ThorFuseMount</c> drives the
/// loop on a dedicated thread.
/// </summary>
internal static class LibFuseInterop
{
    /// <summary>
    /// Logical name used by the <see cref="DllImport"/> below. It is NOT a real file on
    /// disk: a <see cref="NativeLibrary.SetDllImportResolver"/> (registered in the static
    /// constructor) maps it to whatever libfuse3 soname is actually installed. This matters
    /// because the soname changed across releases — <c>fuse3</c> 3.17 ships
    /// <c>libfuse3.so.4</c>, older ones ship <c>libfuse3.so.3</c>, and the unversioned
    /// <c>libfuse3.so</c> only comes with the <c>-dev</c> package. A fixed name would break
    /// on whichever distro does not match it.
    /// </summary>
    private const string LibFuse = "libfuse3";

    /// <summary>
    /// The libfuse3 sonames to probe, in order, when resolving <see cref="LibFuse"/>:
    /// the current <c>.so.4</c> (fuse3 3.17+), the previous <c>.so.3</c>, the older
    /// <c>.so.2</c>, and finally the unversioned dev symlink. The first that loads wins.
    /// </summary>
    private static readonly string[] LibFuseCandidates =
    [
        "libfuse3.so.4",
        "libfuse3.so.3",
        "libfuse3.so.2",
        "libfuse3.so",
    ];

    /// <summary>
    /// Registers the resolver that maps the logical <see cref="LibFuse"/> name to the
    /// libfuse3 soname present on this machine, so the <c>fuse_main_real</c> P/Invoke binds
    /// regardless of the installed libfuse3 version.
    /// </summary>
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

    /// <summary>
    /// The ABI version of the <c>fuse_operations</c> struct this binding targets. It is
    /// passed to <c>fuse_main</c> as the struct size so libfuse marshals the struct with
    /// the layout it was compiled against — on a size mismatch libfuse would read past or
    /// short of our struct. Computed from the managed struct so it tracks any field change.
    /// </summary>
    internal static readonly nuint OperationsSize = (nuint)Marshal.SizeOf<FuseOperations>();

    /// <summary>
    /// Runs the libfuse high-level event loop: parses the FUSE/mount arguments, mounts the
    /// filesystem, and dispatches kernel requests to the callbacks in
    /// <paramref name="op"/> until the filesystem is unmounted (then returns). This is the
    /// real symbol behind the <c>fuse_main</c> macro; the macro only supplies the compile-
    /// time version and struct size, which we pass explicitly.
    ///
    /// Blocking by design — <c>ThorFuseMount</c> calls it on a dedicated thread. A clean
    /// <c>fusermount -u</c> (the unmount path) makes this call return, which is how the
    /// loop is stopped without any in-process signal.
    /// </summary>
    /// <param name="argc">Argument count, as for a C <c>main</c> (argv[0] is the program name).</param>
    /// <param name="argv">Argument vector: program name, mount point, and any <c>-o</c> options.</param>
    /// <param name="op">Pointer to the (pinned) <see cref="FuseOperations"/> callback struct.</param>
    /// <param name="opSize">Size of the operations struct (<see cref="OperationsSize"/>).</param>
    /// <param name="privateData">Opaque pointer handed back to the <c>init</c> callback; may be <see cref="IntPtr.Zero"/>.</param>
    /// <returns>0 on a clean exit, non-zero when the mount could not be established.</returns>
    [DllImport(LibFuse, EntryPoint = "fuse_main_real")]
    private static extern int fuse_main_real(
        int argc,
        IntPtr argv,
        ref FuseOperations op,
        nuint opSize,
        IntPtr privateData);

    /// <summary>
    /// Managed entry point for <c>fuse_main_real</c>: marshals <paramref name="argv"/> to a
    /// native <c>char**</c> by hand and forwards the rest unchanged. The hand-marshalling is
    /// deliberate — the automatic <c>[MarshalAs(LPArray, LPUTF8Str)]</c> on a
    /// <c>string[]</c> is rejected by the runtime (a string array may only pair with
    /// LPStr/LPWStr/LPTStr), and even if it were allowed the marshaller frees the array when
    /// the call returns, whereas <c>fuse_main_real</c> blocks for the whole mount and keeps
    /// reading argv. Allocating the strings as UTF-8 and a NULL-terminated pointer array that
    /// live for the duration of the (blocking) call is both correct and the only accepted form.
    /// </summary>
    internal static int FuseMain(string[] argv, ref FuseOperations op, nuint opSize, IntPtr privateData)
    {
        ArgumentNullException.ThrowIfNull(argv);

        // Allocate one UTF-8 C string per argument, then a char*[argc+1] NULL-terminated
        // vector pointing at them (the trailing NULL is what argv-walkers expect).
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
            // Safe to free only after the blocking call returns (the mount is torn down by
            // then, so libfuse no longer reads argv).
            if (vector != IntPtr.Zero)
                Marshal.FreeCoTaskMem(vector);
            foreach (var ptr in argptrs)
                if (ptr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>
    /// Runs <paramref name="callback"/> and returns its errno-style result, swallowing any
    /// exception into <c>-EIO</c> (or the errno carried by a <see cref="FuseException"/>).
    /// This is the single choke point that keeps Property 6 true: a callback body can throw
    /// freely — a cancelled transfer, an ADB failure, a null reference — and the kernel
    /// still only ever sees a clean negative errno, never a managed exception unwinding
    /// through the native frame.
    ///
    /// By convention a FUSE callback returns 0 (or a non-negative byte count for
    /// read/write) on success and <c>-errno</c> on failure; <paramref name="callback"/>
    /// must already follow that convention for its success path.
    /// </summary>
    internal static int Guard(Func<int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        try
        {
            return callback();
        }
        catch (FuseException fuse)
        {
            // A caller that already knows the right errno (e.g. ENOENT for a missing path)
            // signals it explicitly; honour it.
            return -Math.Abs(fuse.Errno);
        }
        catch (OperationCanceledException)
        {
            // Unmount/shutdown cancelled an in-flight operation: report interrupted, never
            // let the cancellation unwind into libfuse.
            return -FuseErrno.EINTR;
        }
        catch
        {
            // Anything else (ADB failure mid read/write, I/O error, bug) becomes a generic
            // I/O error. The operation fails; the mount keeps running (design: Property 6).
            return -FuseErrno.EIO;
        }
    }
}

/// <summary>
/// Thrown inside a FUSE callback to report a specific errno to the kernel. Caught by
/// <see cref="LibFuseInterop.Guard"/> and converted to the matching <c>-errno</c> return
/// value, so callback code can fail with a precise reason (e.g.
/// <see cref="FuseErrno.ENOENT"/> for a missing path) without hand-rolling the sign and
/// return at every exit point.
/// </summary>
internal sealed class FuseException : Exception
{
    /// <summary>The positive errno to report; <see cref="LibFuseInterop.Guard"/> negates it.</summary>
    public int Errno { get; }

    public FuseException(int errno, string? message = null)
        : base(message ?? $"FUSE operation failed with errno {errno}.")
    {
        Errno = errno == 0 ? FuseErrno.EIO : Math.Abs(errno);
    }
}

/// <summary>
/// The subset of POSIX <c>errno</c> values the FUSE callbacks return as <c>-errno</c>.
/// Values are the Linux numeric codes (stable on the x86-64/arm64 Linux ABI the app runs
/// on). A callback returns <c>0</c> on success and <c>-&lt;one of these&gt;</c> on failure;
/// e.g. a missing path is <c>-ENOENT</c> and a failed ADB transfer is <c>-EIO</c>.
/// </summary>
internal static class FuseErrno
{
    /// <summary>Operation not permitted.</summary>
    public const int EPERM = 1;

    /// <summary>No such file or directory.</summary>
    public const int ENOENT = 2;

    /// <summary>Interrupted system call (used when an operation is cancelled).</summary>
    public const int EINTR = 4;

    /// <summary>Input/output error — the generic failure for an ADB operation that broke mid-flight.</summary>
    public const int EIO = 5;

    /// <summary>Bad file descriptor.</summary>
    public const int EBADF = 9;

    /// <summary>Permission denied.</summary>
    public const int EACCES = 13;

    /// <summary>File exists.</summary>
    public const int EEXIST = 17;

    /// <summary>Not a directory.</summary>
    public const int ENOTDIR = 20;

    /// <summary>Is a directory.</summary>
    public const int EISDIR = 21;

    /// <summary>Invalid argument.</summary>
    public const int EINVAL = 22;

    /// <summary>No space left on device.</summary>
    public const int ENOSPC = 28;

    /// <summary>Read-only file system.</summary>
    public const int EROFS = 30;

    /// <summary>Directory not empty.</summary>
    public const int ENOTEMPTY = 39;

    /// <summary>Function not implemented (for callbacks a filesystem chooses not to support).</summary>
    public const int ENOSYS = 38;
}

/// <summary>
/// POSIX file-mode bits used when a callback fills in a <see cref="FuseStat"/>: the type of
/// the node (<c>S_IFDIR</c>/<c>S_IFREG</c>/<c>S_IFLNK</c>) OR'd with the permission bits.
/// Kept here so <c>ThorFuseFilesystem</c>'s <c>getattr</c> can translate a
/// <c>ThorFileEntry</c> into the <c>st_mode</c> the kernel expects.
/// </summary>
internal static class FuseMode
{
    /// <summary>Mask for the file-type bits of <c>st_mode</c>.</summary>
    public const uint S_IFMT = 0xF000;

    /// <summary>Regular file type bit.</summary>
    public const uint S_IFREG = 0x8000;

    /// <summary>Directory type bit.</summary>
    public const uint S_IFDIR = 0x4000;

    /// <summary>Symbolic link type bit.</summary>
    public const uint S_IFLNK = 0xA000;
}

/// <summary>
/// Managed mirror of the native <c>struct stat</c> as libfuse passes it to <c>getattr</c>
/// (Linux <c>x86-64</c>/<c>arm64</c> layout). A callback writes the node's type+permissions
/// (<see cref="st_mode"/>), size (<see cref="st_size"/>) and timestamps so the kernel can
/// answer <c>stat()</c>. Only the fields the translator fills are named; the remaining
/// padding reproduces the C struct's size and offsets exactly so libfuse reads each field
/// from the right place.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FuseStat
{
    public ulong st_dev;
    public ulong st_ino;
    public ulong st_nlink;

    /// <summary>File type (<see cref="FuseMode"/>) OR'd with permission bits.</summary>
    public uint st_mode;

    public uint st_uid;
    public uint st_gid;
    private readonly int _pad0;
    public ulong st_rdev;

    /// <summary>Total size in bytes (regular files).</summary>
    public long st_size;

    /// <summary>Preferred I/O block size.</summary>
    public long st_blksize;

    /// <summary>Number of 512-byte blocks allocated.</summary>
    public long st_blocks;

    public long st_atime;
    public long st_atime_nsec;

    /// <summary>Last-modified time (seconds since the epoch).</summary>
    public long st_mtime;
    public long st_mtime_nsec;

    public long st_ctime;
    public long st_ctime_nsec;

    private readonly long _reserved0;
    private readonly long _reserved1;
    private readonly long _reserved2;
}

/// <summary>
/// Managed mirror of libfuse's <c>struct fuse_file_info</c>, passed to the open/read/write/
/// release family. The translator mainly reads <see cref="flags"/> (the <c>open(2)</c>
/// flags, to tell a read-only open from a writable one) and may stash a handle in
/// <see cref="fh"/>. The bit-packed options word and padding match the native layout so the
/// struct round-trips through libfuse unchanged.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FuseFileInfo
{
    /// <summary>The <c>open(2)</c> flags (O_RDONLY/O_WRONLY/O_RDWR, O_CREAT, O_TRUNC, ...).</summary>
    public int flags;

    /// <summary>Bit-packed libfuse flags (writepage, direct_io, keep_cache, ...); passed through untouched.</summary>
    private uint _bitfield;

    /// <summary>File handle the filesystem may set in <c>open</c>/<c>create</c> and read back later.</summary>
    public ulong fh;

    /// <summary>Lock owner id (unused by this filesystem).</summary>
    public ulong lock_owner;

    /// <summary>Request poll events (unused by this filesystem).</summary>
    public uint poll_events;

    private readonly uint _pad0;
}

/// <summary>
/// Delegate for libfuse's directory-fill callback (<c>fuse_fill_dir_t</c>). libfuse hands
/// this to <c>readdir</c>; the filesystem calls it once per child to add an entry to the
/// listing. <paramref name="buf"/> and <paramref name="stbuf"/> are opaque/optional from
/// the filesystem's side — passing <see cref="IntPtr.Zero"/> for <paramref name="stbuf"/>
/// and <c>0</c> for offset/flags is the simplest correct usage.
/// </summary>
/// <param name="buf">Opaque buffer supplied by libfuse; forwarded unchanged.</param>
/// <param name="name">The child entry name to add (null-terminated UTF-8).</param>
/// <param name="stbuf">Optional stat for the entry, or <see cref="IntPtr.Zero"/>.</param>
/// <param name="offset">Readdir offset; <c>0</c> for the simple (non-seeking) protocol.</param>
/// <param name="flags">Fill flags; <c>0</c> for the simple protocol.</param>
/// <returns>0 to continue, 1 when libfuse's buffer is full.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseFillDir(
    IntPtr buf,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    IntPtr stbuf,
    long offset,
    int flags);

// The callback delegate signatures below mirror the high-level libfuse3 operation
// prototypes. Each returns an int following the FUSE convention (0 / non-negative count on
// success, -errno on failure). ThorFuseFilesystem (task 3.2) supplies the bodies; every
// body is expected to run through LibFuseInterop.Guard so no exception escapes.

/// <summary><c>getattr(path, stat*, fi)</c> — fill the stat for a node.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseGetattr(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseStat stat,
    IntPtr fi);

/// <summary><c>readdir(path, buf, filler, offset, fi, flags)</c> — enumerate a directory.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseReaddir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    FuseFillDir filler,
    long offset,
    IntPtr fi,
    int flags);

/// <summary><c>open(path, fi)</c> — open an existing file.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseOpen(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

/// <summary><c>read(path, buf, size, offset, fi)</c> — read bytes; returns the count read or -errno.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRead(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    nuint size,
    long offset,
    ref FuseFileInfo fi);

/// <summary><c>write(path, buf, size, offset, fi)</c> — write bytes; returns the count written or -errno.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseWrite(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    IntPtr buf,
    nuint size,
    long offset,
    ref FuseFileInfo fi);

/// <summary><c>create(path, mode, fi)</c> — create and open a new file.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseCreate(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint mode,
    ref FuseFileInfo fi);

/// <summary><c>truncate(path, size, fi)</c> — set a file's length.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseTruncate(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    long size,
    IntPtr fi);

/// <summary><c>flush(path, fi)</c> — called on close(2); the point to push a dirty cache back.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseFlush(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

/// <summary><c>release(path, fi)</c> — last close of a handle; release resources. Its return value is ignored by the kernel.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRelease(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    ref FuseFileInfo fi);

/// <summary><c>unlink(path)</c> — delete a file.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseUnlink(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

/// <summary><c>mkdir(path, mode)</c> — create a directory.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseMkdir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
    uint mode);

/// <summary><c>rmdir(path)</c> — remove a directory.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRmdir(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

/// <summary><c>rename(from, to, flags)</c> — rename/move a node.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FuseRename(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string to,
    uint flags);

/// <summary>
/// Managed mirror of libfuse's high-level <c>struct fuse_operations</c>: a table of
/// function pointers the kernel calls for each filesystem operation. Only the operations
/// this filesystem implements are typed; every other slot is an <see cref="IntPtr"/> left
/// at <see cref="IntPtr.Zero"/> (a null slot tells libfuse the operation is unsupported and
/// it returns the right default errno on its own).
///
/// The field order matches the libfuse3 header exactly — libfuse indexes this struct by
/// offset, so a reordered or missing field would wire the wrong callback to an operation.
/// The unimplemented slots are reserved as <see cref="IntPtr"/> placeholders precisely to
/// hold those offsets. A struct value is pinned and kept alive for the mount's lifetime by
/// <see cref="FuseOperationsHandle"/>.
/// </summary>
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
    public IntPtr chmod;
    public IntPtr chown;
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
    public IntPtr utimens;
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

/// <summary>
/// Holds the set of callback delegates wired into a <see cref="FuseOperations"/> alive for
/// the entire lifetime of a mount. This is the mechanism behind the "delegates stay alive"
/// invariant (design: Property 6).
///
/// The operations struct is <b>not</b> blittable (it stores managed delegate references),
/// so it is passed to <c>fuse_main_real</c> by <c>ref</c> and the interop marshaller
/// converts each delegate to a native function pointer for the duration of the call. The
/// catch: the marshaller keeps the delegates alive only for that call, but libfuse keeps
/// the pointers for the whole mount and <c>fuse_main_real</c> blocks until unmount — so the
/// delegates must be rooted independently. This handle does exactly that: it keeps strong
/// references (and a strong <see cref="GCHandle"/>) to every delegate so the GC can never
/// collect a callback the kernel still points at. Build one from the populated struct
/// before calling <c>fuse_main_real</c> and <see cref="Dispose"/> it only after that call
/// has returned (the filesystem is then fully unmounted and no callback can fire again).
/// </summary>
internal sealed class FuseOperationsHandle : IDisposable
{
    // Strong references to the exact delegate instances stored in the operations struct.
    // Keeping the boxed struct rooted keeps every delegate field reachable.
    private readonly object _operations;
    private GCHandle _handle;
    private int _disposed;

    /// <summary>
    /// Roots <paramref name="operations"/> (and therefore the delegates it references) for
    /// the mount's lifetime with a strong <see cref="GCHandle"/>, so none of the callbacks
    /// can be collected while libfuse still holds function pointers to them.
    /// </summary>
    public FuseOperationsHandle(FuseOperations operations)
    {
        _operations = operations;
        _handle = GCHandle.Alloc(_operations, GCHandleType.Normal);
    }

    /// <summary>
    /// Releases the strong handle, allowing the operations struct and its delegates to be
    /// collected. Only safe once <c>fuse_main_real</c> has returned. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_handle.IsAllocated)
            _handle.Free();

        GC.KeepAlive(_operations);
    }
}
